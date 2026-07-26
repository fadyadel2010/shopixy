using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Treasury;

namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryCashAuthorityCertificationTests
{
    [Fact]
    public async Task ReversalHistory_MustRetainOriginalAndReversingCashLineage()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        var cash = Account.Create(
            tenantId,
            "1010",
            "Cash",
            AccountType.Asset,
            preventNegative: true);
        var revenue = Account.Create(
            tenantId,
            "4010",
            "Revenue",
            AccountType.Revenue);
        db.LedgerAccounts.AddRange(cash, revenue);
        db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(
            tenantId,
            cash.Id,
            "Cash"));
        var warehouse = new PointOfSale.Core.Entities.Warehouse
        {
            TenantId = tenantId,
            Name = "T1 Warehouse",
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        var invoice = new PointOfSale.Core.Entities.SalesInvoice
        {
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            InvoiceNumber = "T1-CASH-001",
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 100m
        };
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var posting = new FinancialPostingService(
            db,
            NullLogger<FinancialPostingService>.Instance);
        var original = await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = tenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "SalesInvoice",
            SourceId = invoice.Id,
            Description = "Cash received",
            IdempotencyKey = $"TREASURY-CASH:{tenantId}:{Guid.NewGuid()}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 100m),
                PostingLine.Credit(revenue.Id, 100m)
            ]
        });

        await posting.ReverseJournalAsync(
            tenantId,
            original.Id,
            "Certification reversal");

        var readService = new TreasuryReadService(
            db,
            NullLogger<TreasuryReadService>.Instance);
        var movements = await readService.GetMovementsAsync(
            tenantId,
            cash.AccountCode,
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date.AddDays(1));
        var balance = await readService.GetAccountBalanceAsync(
            tenantId,
            cash.AccountCode);

        Assert.Equal(2, movements.Count);
        Assert.Equal(balance, movements.Sum(item => item.Debit - item.Credit));
        Assert.Equal(0m, balance);
    }

    [Fact]
    public async Task DirectLedgerBalanceMutation_MustFailOutsidePostingAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var cash = Account.Create(tenantId, "1010", "Cash", AccountType.Asset);
        db.LedgerAccounts.Add(cash);
        await db.SaveChangesAsync();

        cash.ApplyEntry(10m, 0m);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());

        Assert.Contains(
            "financial posting authority",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrossTenantTreasuryMapping_MustNotExposeForeignLedgerBalance()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = CreateDbContext();

        var foreignCash = Account.Create(
            tenantB,
            "1010",
            "Foreign Cash",
            AccountType.Asset);
        db.LedgerAccounts.Add(foreignCash);
        db.TreasuryAccounts.Add(TreasuryAccount.CreateCashbox(
            tenantA,
            foreignCash.Id,
            "Forged Mapping"));
        await db.SaveChangesAsync();

        var readService = new TreasuryReadService(
            db,
            NullLogger<TreasuryReadService>.Instance);
        var accounts = await readService.GetTreasuryAccountsAsync(tenantA);

        Assert.Empty(accounts);
    }

    [Fact]
    public async Task NonTreasuryLedgerAccountCode_MustNotBecomeCashReadAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var revenue = Account.Create(
            tenantId,
            "4010",
            "Revenue",
            AccountType.Revenue);
        db.LedgerAccounts.Add(revenue);
        await db.SaveChangesAsync();

        var readService = new TreasuryReadService(
            db,
            NullLogger<TreasuryReadService>.Instance);

        var balance = await readService.GetAccountBalanceAsync(
            tenantId,
            revenue.AccountCode);
        var movements = await readService.GetMovementsAsync(
            tenantId,
            revenue.AccountCode,
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(0m, balance);
        Assert.Empty(movements);
    }

    [Fact]
    public void RuntimeSource_MustFreezeLedgerAndTenantAuthorityBoundaries()
    {
        var solution = GetSolutionDirectory();
        var readService = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Treasury",
            "TreasuryReadService.cs"));
        var postingService = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialPostingService.cs"));
        var dbContext = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs"));
        var financialReports = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialReportService.cs"));
        var dashboard = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.API",
            "Controllers",
            "DashboardController.cs"));

        Assert.Contains("JournalStatus.Reversed", readService, StringComparison.Ordinal);
        Assert.Contains("account.Account.TenantId == tenantId", readService, StringComparison.Ordinal);
        Assert.Contains("LedgerMutationScope.Authorize()", postingService, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", postingService, StringComparison.Ordinal);
        Assert.Contains("GuardTreasuryCashAuthority", dbContext, StringComparison.Ordinal);
        Assert.Contains("financial posting authority", dbContext, StringComparison.Ordinal);
        Assert.Contains("JournalStatus.Reversed", financialReports, StringComparison.Ordinal);
        Assert.Contains("JournalStatus.Reversed", dashboard, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TreasuryCashAuthority_{Guid.NewGuid():N}")
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
