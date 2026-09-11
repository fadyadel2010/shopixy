using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryInflowAuthorityCertificationTests
{
    [Fact]
    public async Task ForgedSourceType_CannotCreateTreasuryInflow()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, revenue) = await SeedCashAccountsAsync(db, tenantId);
        var posting = CreatePostingService(db);

        var exception = await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildInflow(
                tenantId,
                cash.Id,
                revenue.Id,
                100m,
                "WebhookEvidence",
                Guid.NewGuid(),
                $"FORGED:{Guid.NewGuid():N}")));

        Assert.Contains("certified payment or accounting SQL authority", exception.Message);
        Assert.Equal(0m, cash.CurrentBalance);
    }

    [Fact]
    public async Task PaymentReplay_ConvergesAndCannotExceedAuthoritativeAmount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, revenue) = await SeedCashAccountsAsync(db, tenantId);
        var warehouse = new Warehouse
        {
            TenantId = tenantId,
            Name = "T2 Warehouse",
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        var invoice = new SalesInvoice
        {
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            InvoiceNumber = "T2-001",
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 100m
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);
        var request = BuildInflow(
            tenantId,
            cash.Id,
            revenue.Id,
            100m,
            "SalesInvoice",
            invoice.Id,
            $"SALE:{tenantId}:{invoice.Id}");

        var first = await posting.PostJournalAsync(request);
        var replay = await posting.PostJournalAsync(request);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(100m, cash.CurrentBalance);

        await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildInflow(
                tenantId,
                cash.Id,
                revenue.Id,
                1m,
                "SalesInvoice",
                invoice.Id,
                $"SALE-INFLATE:{tenantId}:{invoice.Id}")));
    }

    [Fact]
    public async Task CustomerPayment_AuthorizesTreasuryInflowWithinPersistedAmount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, receivable) = await SeedCashAccountsAsync(db, tenantId);
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Treasury authority customer",
        };
        var payment = new CustomerPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            PaymentDate = DateTime.UtcNow,
            Amount = 100m,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Cash,
            CashAmount = 100m,
            IdempotencyKey = $"customer-payment-{Guid.NewGuid():N}",
        };
        db.Customers.Add(customer);
        db.CustomerPayments.Add(payment);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);
        var request = BuildInflow(
            tenantId,
            cash.Id,
            receivable.Id,
            payment.Amount,
            "CustomerPayment",
            payment.Id,
            $"CUSTOMER_PAYMENT:{tenantId}:{payment.Id}");

        var first = await posting.PostJournalAsync(request);
        var replay = await posting.PostJournalAsync(request);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(100m, cash.CurrentBalance);

        await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildInflow(
                tenantId,
                cash.Id,
                receivable.Id,
                1m,
                "CustomerPayment",
                payment.Id,
                $"CUSTOMER_PAYMENT-INFLATE:{tenantId}:{payment.Id}")));
    }

    [Fact]
    public async Task ReversedSale_CanBeRepostedWithinCurrentAuthoritativeAmount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, revenue) = await SeedCashAccountsAsync(db, tenantId);
        var warehouse = new Warehouse
        {
            TenantId = tenantId,
            Name = "T2 Rewrite Warehouse",
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        var invoice = new SalesInvoice
        {
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            InvoiceNumber = "T2-REWRITE",
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 100m
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);
        var original = await posting.PostJournalAsync(BuildInflow(
            tenantId,
            cash.Id,
            revenue.Id,
            100m,
            "SalesInvoice",
            invoice.Id,
            $"SALE-ORIGINAL:{tenantId}:{invoice.Id}"));

        await posting.ReverseJournalAsync(tenantId, original.Id, "Draft sale rewrite");
        invoice.TotalAmount = 120m;
        invoice.PaidAmount = 120m;
        await db.SaveChangesAsync();

        await posting.PostJournalAsync(BuildInflow(
            tenantId,
            cash.Id,
            revenue.Id,
            120m,
            "SalesInvoice",
            invoice.Id,
            $"SALE-REPLACEMENT:{tenantId}:{invoice.Id}"));

        Assert.Equal(120m, cash.CurrentBalance);
    }

    [Fact]
    public async Task ForeignTenantPayment_CannotCreateLocalCash()
    {
        var localTenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, revenue) = await SeedCashAccountsAsync(db, localTenantId);
        var foreignWarehouse = new Warehouse
        {
            TenantId = foreignTenantId,
            Name = "Foreign Warehouse",
            IsActive = true
        };
        db.Warehouses.Add(foreignWarehouse);
        var foreignInvoice = new SalesInvoice
        {
            TenantId = foreignTenantId,
            WarehouseId = foreignWarehouse.Id,
            InvoiceNumber = "T2-FOREIGN",
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 100m
        };
        db.SalesInvoices.Add(foreignInvoice);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);

        await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildInflow(
                localTenantId,
                cash.Id,
                revenue.Id,
                100m,
                "SalesInvoice",
                foreignInvoice.Id,
                $"FOREIGN:{foreignInvoice.Id}")));
    }

    [Fact]
    public void RuntimeSource_MustFreezeManualRecoveryAndLineageBoundaries()
    {
        var solution = GetSolutionDirectory();
        var posting = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.Infrastructure", "Accounting", "FinancialPostingService.cs"));
        var operations = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.Infrastructure", "Treasury", "TreasuryOperationService.cs"));
        var transfers = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.Infrastructure", "Accounting", "TreasuryTransferService.cs"));
        var treasuryController = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.API", "Controllers", "TreasuryController.cs"));
        var cashboxController = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.API", "Controllers", "CashboxController.cs"));
        var adminAccounting = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.API", "Controllers", "AdminAccountingController.cs"));

        Assert.Contains("ValidateTreasuryInflowAuthorityAsync", posting);
        Assert.Contains("previouslyPostedIncrease + netCashIncrease", posting);
        Assert.Contains("\"CustomerPayment\" => await _db.CustomerPayments", posting);
        Assert.Contains("certified payment or accounting SQL authority", posting);
        Assert.Contains("Direct treasury cash-in is disabled", operations);
        Assert.Contains("RequireTreasuryTypeAsync", transfers);
        Assert.Contains("TREASURY_INFLOW_WORKFLOW_REQUIRED", treasuryController);
        Assert.Contains("OPENING_FLOAT_TRANSFER_REQUIRED", cashboxController);
        Assert.Contains("OPENING_BALANCE_AUTHORITY_REQUIRED", cashboxController);
        Assert.Contains("SETTLEMENT_AUTHORITY_REQUIRED", adminAccounting);
        Assert.Contains("DepositId is required for replay-safe deposit lineage", adminAccounting);
    }

    private static FinancialPostingService CreatePostingService(ApplicationDbContext db) =>
        new(db, NullLogger<FinancialPostingService>.Instance);

    private static PostingRequest BuildInflow(
        Guid tenantId,
        Guid cashAccountId,
        Guid revenueAccountId,
        decimal amount,
        string sourceType,
        Guid sourceId,
        string idempotencyKey) =>
        new()
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = sourceType,
            SourceId = sourceId,
            Description = "Treasury inflow certification",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                PostingLine.Debit(cashAccountId, amount),
                PostingLine.Credit(revenueAccountId, amount)
            ]
        };

    private static async Task<(Account Cash, Account Revenue)> SeedCashAccountsAsync(
        ApplicationDbContext db,
        Guid tenantId)
    {
        var cash = Account.Create(tenantId, "1010", "Cash", AccountType.Asset);
        var revenue = Account.Create(tenantId, "4010", "Revenue", AccountType.Revenue);
        db.LedgerAccounts.AddRange(cash, revenue);
        db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(tenantId, cash.Id, "Cash"));
        await db.SaveChangesAsync();
        return (cash, revenue);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TreasuryInflowAuthority_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string GetSolutionDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
