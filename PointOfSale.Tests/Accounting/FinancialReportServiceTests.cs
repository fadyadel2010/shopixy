using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Accounting;

public sealed class FinancialReportServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();

    public FinancialReportServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options, new TestTenantContext(_tenantId));
    }

    [Fact]
    public async Task GetCashFlowAsync_IncludesSeededAccountsReceivableCodeInWorkingCapital()
    {
        var receivable = Account.Create(
            _tenantId,
            AccountCodes.AccountsReceivable,
            "Accounts Receivable",
            AccountType.Asset);
        var revenue = Account.Create(
            _tenantId,
            AccountCodes.PosSales,
            "POS Sales",
            AccountType.Revenue);

        _db.LedgerAccounts.AddRange(receivable, revenue);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(
            _db,
            NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(
            _db,
            NullLogger<FinancialReportService>.Instance);
        var fromDate = new DateTime(2026, 07, 01);
        var toDate = new DateTime(2026, 07, 31);

        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(4),
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Credit sale creates receivable",
            IdempotencyKey = $"REPORT-CF-AR:{_tenantId}:{Guid.NewGuid():N}",
            Lines =
            [
                PostingLine.Debit(receivable.Id, 250m),
                PostingLine.Credit(revenue.Id, 250m)
            ]
        });

        _db.ChangeTracker.Clear();
        Assert.Equal(1, await _db.JournalEntries.CountAsync());
        Assert.Equal(2, await _db.LedgerEntries.CountAsync());

        var report = await reportService.GetCashFlowAsync(_tenantId, fromDate, toDate);

        var receivableAdjustment = Assert.Single(
            report.WorkingCapitalChanges,
            adjustment => adjustment.Description == "Change in Accounts Receivable");
        Assert.Equal($"{AccountCodes.AccountsReceivable}/{AccountCodes.MarketplaceVendorReceivable}", receivableAdjustment.AccountCode);
        Assert.Equal(-250m, receivableAdjustment.Amount);
        Assert.Equal(250m, report.NetProfit);
        Assert.Equal(0m, report.OperatingCashFlow);
        Assert.True(report.Reconciles);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_IncludesCurrentEarningsInEquity()
    {
        var cash = Account.Create(_tenantId, AccountCodes.CashboxMain, "Cashbox", AccountType.Asset);
        var revenue = Account.Create(_tenantId, AccountCodes.PosSales, "POS Sales", AccountType.Revenue);
        _db.LedgerAccounts.AddRange(cash, revenue);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var journalDate = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = journalDate,
            SourceType = "SalesInvoice",
            SourceId = Guid.NewGuid(),
            Description = "Current period sale",
            IdempotencyKey = $"REPORT-BS-EARNINGS:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(cash.Id, 125m),
                PostingLine.Credit(revenue.Id, 125m)
            ]
        });

        var report = await reportService.GetBalanceSheetAsync(_tenantId, journalDate.Date.AddDays(1).AddTicks(-1));

        var currentEarnings = Assert.Single(report.EquityLines, line => line.AccountCode == "3999");
        Assert.Equal(125m, currentEarnings.Balance);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public async Task GetCashFlowAsync_TreatsClearingAsCashEquivalentAndEquityAsFinancing()
    {
        var clearing = Account.Create(_tenantId, AccountCodes.VisaClearing, "Visa Clearing", AccountType.Asset);
        var equity = Account.Create(_tenantId, AccountCodes.OpeningBalanceEquity, "Opening Equity", AccountType.Equity);
        _db.LedgerAccounts.AddRange(clearing, equity);
        await _db.SaveChangesAsync();

        var posting = new FinancialPostingService(_db, NullLogger<FinancialPostingService>.Instance);
        var reportService = new FinancialReportService(_db, NullLogger<FinancialReportService>.Instance);
        var fromDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = _tenantId,
            JournalDate = fromDate.AddDays(1),
            SourceType = "OpeningBalance",
            SourceId = Guid.NewGuid(),
            Description = "Owner contribution held in clearing",
            IdempotencyKey = $"REPORT-CF-EQUITY:{_tenantId}",
            Lines =
            [
                PostingLine.Debit(clearing.Id, 100m),
                PostingLine.Credit(equity.Id, 100m)
            ]
        });

        var report = await reportService.GetCashFlowAsync(_tenantId, fromDate, toDate);

        Assert.Equal(100m, report.ClosingCashBalance);
        Assert.Equal(100m, Assert.Single(report.FinancingActivities).Amount);
        Assert.True(report.Reconciles);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
