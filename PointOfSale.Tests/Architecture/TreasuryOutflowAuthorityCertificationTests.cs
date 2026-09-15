using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PaymentMethodEnum = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryOutflowAuthorityCertificationTests
{
    [Fact]
    public async Task ForgedSourceType_CannotReduceTreasuryCash()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, expense) = await SeedFundedCashAsync(db, tenantId, 200m);
        var posting = CreatePostingService(db);

        var exception = await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildOutflow(
                tenantId,
                cash.Id,
                expense.Id,
                50m,
                "ForgedPayout",
                Guid.NewGuid(),
                $"FORGED-OUTFLOW:{Guid.NewGuid():N}")));

        Assert.Contains("certified liability, refund, payout, or accounting SQL authority", exception.Message);
        Assert.Equal(200m, cash.CurrentBalance);
    }

    [Fact]
    public async Task LiabilityReplay_CannotReduceCashAboveAuthoritativeAmount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, payable) = await SeedFundedCashAsync(db, tenantId, 200m, AccountType.Liability);
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "T3 Supplier"
        };
        var payment = new SupplierPayment
        {
            TenantId = tenantId,
            SupplierId = supplier.Id,
            PaymentDate = DateTime.UtcNow,
            Amount = 100m,
            PaymentMethod = PaymentMethodEnum.Cash,
            IdempotencyKey = $"SUPPLIER:{Guid.NewGuid():N}"
        };
        db.Suppliers.Add(supplier);
        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);
        var request = BuildOutflow(
            tenantId,
            cash.Id,
            payable.Id,
            100m,
            "SupplierPayment",
            payment.Id,
            $"SUPPLIER_PAYMENT:{tenantId}:{payment.Id}");

        var first = await posting.PostJournalAsync(request);
        var replay = await posting.PostJournalAsync(request);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(100m, cash.CurrentBalance);

        await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildOutflow(
                tenantId,
                cash.Id,
                payable.Id,
                1m,
                "SupplierPayment",
                payment.Id,
                $"SUPPLIER_PAYMENT_DUPLICATE:{tenantId}:{payment.Id}")));
    }

    [Fact]
    public async Task ReversedLiabilityOutflow_CanBeRepostedWithinAuthoritativeAmount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, payable) = await SeedFundedCashAsync(db, tenantId, 200m, AccountType.Liability);
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "T3 Supplier"
        };
        var payment = new SupplierPayment
        {
            TenantId = tenantId,
            SupplierId = supplier.Id,
            PaymentDate = DateTime.UtcNow,
            Amount = 100m,
            PaymentMethod = PaymentMethodEnum.Cash,
            IdempotencyKey = $"SUPPLIER:{Guid.NewGuid():N}"
        };
        db.Suppliers.Add(supplier);
        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);
        var original = await posting.PostJournalAsync(BuildOutflow(
            tenantId,
            cash.Id,
            payable.Id,
            100m,
            "SupplierPayment",
            payment.Id,
            $"SUPPLIER_PAYMENT:{tenantId}:{payment.Id}:V1"));

        await posting.ReverseJournalAsync(tenantId, original.Id, "Correct payment allocation");
        var replacement = await posting.PostJournalAsync(BuildOutflow(
            tenantId,
            cash.Id,
            payable.Id,
            100m,
            "SupplierPayment",
            payment.Id,
            $"SUPPLIER_PAYMENT:{tenantId}:{payment.Id}:V2"));

        Assert.Equal(JournalStatus.Reversed, original.Status);
        Assert.Equal(JournalStatus.Posted, replacement.Status);
        Assert.Equal(100m, cash.CurrentBalance);
    }

    [Fact]
    public async Task ForeignTenantLiability_CannotReduceLocalCash()
    {
        var localTenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, payable) = await SeedFundedCashAsync(
            db,
            localTenantId,
            200m,
            AccountType.Liability);
        var foreignSupplier = new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = foreignTenantId,
            Name = "Foreign T3 Supplier"
        };
        var foreignPayment = new SupplierPayment
        {
            TenantId = foreignTenantId,
            SupplierId = foreignSupplier.Id,
            PaymentDate = DateTime.UtcNow,
            Amount = 50m,
            PaymentMethod = PaymentMethodEnum.Cash,
            IdempotencyKey = $"FOREIGN:{Guid.NewGuid():N}"
        };
        db.Suppliers.Add(foreignSupplier);
        db.SupplierPayments.Add(foreignPayment);
        await db.SaveChangesAsync();

        var posting = CreatePostingService(db);

        await Assert.ThrowsAsync<InvalidJournalException>(() =>
            posting.PostJournalAsync(BuildOutflow(
                localTenantId,
                cash.Id,
                payable.Id,
                50m,
                "SupplierPayment",
                foreignPayment.Id,
                $"FOREIGN-LIABILITY:{foreignPayment.Id}")));
    }

    [Fact]
    public async Task ForeignTenantCommerceHubOutbox_CannotAuthorizeLocalTreasuryOutflow()
    {
        var localTenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var (cash, expense) = await SeedFundedCashAsync(db, localTenantId, 200m);
        var transaction = CommerceHubFinancialTransaction.Create(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            50m,
            "EGP",
            CommerceHubFinancialReferenceType.Refund,
            Guid.NewGuid(),
            "Foreign CommerceHub refund",
            financialBucketOverride: CommerceHubFinancialBucket.Cash);
        var idempotencyKey = $"COMMERCEHUB_TX:{foreignTenantId:N}:{transaction.Id:N}";
        db.CommerceHubFinancialTransactions.Add(transaction);
        db.AccountingOutboxMessages.Add(AccountingOutboxMessage.Create(
            foreignTenantId,
            "CommerceHubLedgerPosting",
            transaction.Id,
            nameof(CommerceHubFinancialTransaction),
            "{}",
            idempotencyKey,
            sourceType: "CommerceHubFinancialTransaction"));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidJournalException>(() =>
            CreatePostingService(db).PostJournalAsync(BuildOutflow(
                localTenantId,
                cash.Id,
                expense.Id,
                50m,
                "CommerceHubFinancialTransaction",
                transaction.Id,
                idempotencyKey)));

        Assert.Contains("accounting SQL authority", exception.Message);
        Assert.Equal(200m, cash.CurrentBalance);
    }

    [Fact]
    public void RuntimeSource_MustFreezeOutflowAndManualSpendBoundaries()
    {
        var solution = GetSolutionDirectory();
        var posting = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.Infrastructure", "Accounting", "FinancialPostingService.cs"));
        var operations = File.ReadAllText(Path.Combine(
            solution, "PointOfSale.Infrastructure", "Treasury", "TreasuryOperationService.cs"));

        Assert.Contains("ValidateTreasuryOutflowAuthorityAsync", posting);
        Assert.Contains("previouslyPostedDecrease + netCashDecrease", posting);
        Assert.Contains("certified liability, refund, payout, or accounting SQL authority", posting);
        Assert.Contains("\"SupplierPayment\"", posting);
        Assert.Contains("\"Order_Refund\"", posting);
        Assert.Contains("\"CommerceHubFinancialTransaction\"", posting);
        Assert.Contains("\"AffiliateCommissionPaid\"", posting);
        Assert.Contains("Direct treasury expense posting is disabled", operations);
        Assert.Contains("Direct treasury cash-out is disabled", operations);
    }

    private static FinancialPostingService CreatePostingService(ApplicationDbContext db) =>
        new(db, NullLogger<FinancialPostingService>.Instance);

    private static PostingRequest BuildOutflow(
        Guid tenantId,
        Guid cashAccountId,
        Guid debitAccountId,
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
            Description = "Treasury outflow certification",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                PostingLine.Debit(debitAccountId, amount),
                PostingLine.Credit(cashAccountId, amount)
            ]
        };

    private static async Task<(Account Cash, Account Offset)> SeedFundedCashAsync(
        ApplicationDbContext db,
        Guid tenantId,
        decimal amount,
        AccountType offsetType = AccountType.Expense)
    {
        var cash = Account.Create(
            tenantId,
            "1010",
            "Cash",
            AccountType.Asset,
            preventNegative: true);
        var revenue = Account.Create(tenantId, "4010", "Revenue", AccountType.Revenue);
        var offset = Account.Create(
            tenantId,
            offsetType == AccountType.Liability ? "2010" : "6010",
            offsetType == AccountType.Liability ? "Payable" : "Expense",
            offsetType);
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "T3 Warehouse",
            IsActive = true
        };
        var invoice = new SalesInvoice
        {
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            InvoiceNumber = $"T3-{Guid.NewGuid():N}",
            Status = InvoiceStatus.Completed,
            TotalAmount = amount,
            PaidAmount = amount
        };

        db.LedgerAccounts.AddRange(cash, revenue, offset);
        db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(tenantId, cash.Id, "Cash"));
        db.Warehouses.Add(warehouse);
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        await CreatePostingService(db).PostJournalAsync(new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "SalesInvoice",
            SourceId = invoice.Id,
            Description = "Seed certified cash",
            IdempotencyKey = $"T3-SEED:{tenantId}:{invoice.Id}",
            Lines =
            [
                PostingLine.Debit(cash.Id, amount),
                PostingLine.Credit(revenue.Id, amount)
            ]
        });

        return (cash, offset);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TreasuryOutflowAuthority_{Guid.NewGuid():N}")
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
