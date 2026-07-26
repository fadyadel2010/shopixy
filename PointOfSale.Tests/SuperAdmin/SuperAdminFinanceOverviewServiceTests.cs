using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class SuperAdminFinanceOverviewServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly SuperAdminFinanceOverviewService _service;

    public SuperAdminFinanceOverviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        var walletService = new CommerceHubWalletService(_dbContext);
        var debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var dashboardService = new CommerceHubFinancialDashboardService(_dbContext, walletService, debtService);
        var reconciliationService = new CommerceHubFinanceReconciliationReadService(_dbContext, debtService);

        _service = new SuperAdminFinanceOverviewService(
            _dbContext,
            dashboardService,
            reconciliationService);
    }

    [Fact]
    public async Task GetCashflowAndProfitAndLossAsync_ShouldUseCommerceHubLedgerOnly()
    {
        var orderReference = Guid.NewGuid();
        var payoutReference = Guid.NewGuid();
        var refundReference = Guid.NewGuid();
        var settlementReference = Guid.NewGuid();

        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            120m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 25, 8, 0, 0, DateTimeKind.Utc),
            "Online payment");
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            20m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 25, 8, 1, 0, DateTimeKind.Utc),
            "Commission capture");
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            15m,
            CommerceHubFinancialReferenceType.Settlement,
            settlementReference,
            new DateTime(2026, 3, 25, 8, 2, 0, DateTimeKind.Utc),
            "Vendor debt repayment");
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            30m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 25, 8, 3, 0, DateTimeKind.Utc),
            "Customer refund");
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            5m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 25, 8, 4, 0, DateTimeKind.Utc),
            "Commission reversal");
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Debit,
            60m,
            CommerceHubFinancialReferenceType.Payout,
            payoutReference,
            new DateTime(2026, 3, 25, 8, 5, 0, DateTimeKind.Utc),
            "Vendor payout cash movement");

        await _dbContext.SaveChangesAsync();

        var query = new SuperAdminFinanceReportQueryDto
        {
            FromDateUtc = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc),
            ToDateUtc = new DateTime(2026, 3, 25, 23, 59, 59, DateTimeKind.Utc)
        };

        var cashflow = await _service.GetCashflowAsync(query);
        var pnl = await _service.GetProfitAndLossAsync(query);

        Assert.Equal(120m, cashflow.OnlinePaymentsIn);
        Assert.Equal(15m, cashflow.VendorSettlementsIn);
        Assert.Equal(135m, cashflow.CashInTotal);
        Assert.Equal(30m, cashflow.RefundsOut);
        Assert.Equal(60m, cashflow.VendorPayoutsOut);
        Assert.Equal(90m, cashflow.CashOutTotal);
        Assert.Equal(45m, cashflow.NetCashFlow);
        Assert.Equal("EGP", cashflow.Currency);
        Assert.Single(cashflow.Series);
        Assert.Equal(new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc), cashflow.Series[0].DateUtc);
        Assert.Equal(135m, cashflow.Series[0].CashInTotal);
        Assert.Equal(90m, cashflow.Series[0].CashOutTotal);
        Assert.Equal(45m, cashflow.Series[0].NetCashFlow);

        Assert.Equal(20m, pnl.Revenue);
        Assert.Equal(5m, pnl.RefundLosses);
        Assert.Equal(15m, pnl.NetProfit);
        Assert.Equal("EGP", pnl.Currency);
    }

    [Fact]
    public async Task GetCashflowAsync_ShouldReturnDailySeriesIncludingZeroDays()
    {
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            50m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid(),
            new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc),
            "Day one order");
        SeedPlatformTransaction(
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            10m,
            CommerceHubFinancialReferenceType.Refund,
            Guid.NewGuid(),
            new DateTime(2026, 3, 22, 12, 0, 0, DateTimeKind.Utc),
            "Day three refund");

        await _dbContext.SaveChangesAsync();

        var cashflow = await _service.GetCashflowAsync(new SuperAdminFinanceReportQueryDto
        {
            FromDateUtc = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            ToDateUtc = new DateTime(2026, 3, 22, 23, 59, 59, DateTimeKind.Utc)
        });

        Assert.Equal(3, cashflow.Series.Count);
        Assert.Collection(
            cashflow.Series,
            first =>
            {
                Assert.Equal(new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc), first.DateUtc);
                Assert.Equal(50m, first.CashInTotal);
                Assert.Equal(0m, first.CashOutTotal);
            },
            second =>
            {
                Assert.Equal(new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc), second.DateUtc);
                Assert.Equal(0m, second.CashInTotal);
                Assert.Equal(0m, second.CashOutTotal);
            },
            third =>
            {
                Assert.Equal(new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc), third.DateUtc);
                Assert.Equal(0m, third.CashInTotal);
                Assert.Equal(10m, third.CashOutTotal);
                Assert.Equal(-10m, third.NetCashFlow);
            });
    }

    [Fact]
    public async Task GetReconciliationAsync_ShouldExposeExpandedCounters()
    {
        var result = await _service.GetReconciliationAsync();

        Assert.Equal("OK", result.Status);
        Assert.Equal(0, result.TotalTransactions);
        Assert.Equal(0, result.CorrelationIntegrityMismatchCount);
        Assert.Equal(0, result.PayableMismatchCount);
        Assert.Equal(0, result.ReceivableMismatchCount);
        Assert.Equal(0, result.MissingPairCount);
        Assert.Equal(0, result.DuplicateEntryCount);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private void SeedPlatformTransaction(
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        DateTime createdAtUtc,
        string description)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                transactionType,
                direction,
                amount,
                "EGP",
                referenceType,
                referenceId,
                description,
                createdAtUtc: createdAtUtc));
    }
}
