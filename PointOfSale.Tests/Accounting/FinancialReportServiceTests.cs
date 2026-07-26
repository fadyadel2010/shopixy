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

    public void Dispose()
    {
        _db.Dispose();
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
