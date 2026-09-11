using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubFinancialDashboardServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubFinancialDashboardService _dashboardService;

    public CommerceHubFinancialDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        var walletService = new CommerceHubWalletService(_dbContext);
        var debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        _dashboardService = new CommerceHubFinancialDashboardService(_dbContext, walletService, debtService);
    }

    [Fact]
    public async Task GetPlatformOverviewAsync_ShouldSummarizeLedgerOnlyMetrics()
    {
        var vendorId = Guid.NewGuid();
        var orderReference = Guid.NewGuid();
        var refundReference = Guid.NewGuid();
        var settlementReference = Guid.NewGuid();

        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Dashboard Vendor",
            IsActive = true,
            Currency = "EGP"
        });

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            120m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 0, 0, DateTimeKind.Utc),
            description: "Order payment");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 0, 5, DateTimeKind.Utc),
            description: "Vendor order proceeds");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            20m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 5, 0, DateTimeKind.Utc),
            description: "Commission capture");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialTransactionDirection.Debit,
            90m,
            CommerceHubFinancialReferenceType.Payout,
            Guid.NewGuid(),
            createdAtUtc: new DateTime(2026, 3, 24, 8, 10, 0, DateTimeKind.Utc),
            description: "Vendor payout");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            30m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 15, 0, DateTimeKind.Utc),
            description: "Customer refund");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            25m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 15, 5, DateTimeKind.Utc),
            description: "Vendor refund impact");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            5m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 20, 0, DateTimeKind.Utc),
            description: "Commission reversal");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            12m,
            CommerceHubFinancialReferenceType.Settlement,
            settlementReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 24, 0, DateTimeKind.Utc),
            description: "Vendor debt repayment");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            12m,
            CommerceHubFinancialReferenceType.Settlement,
            settlementReference,
            createdAtUtc: new DateTime(2026, 3, 24, 8, 25, 0, DateTimeKind.Utc),
            description: "Settlement cash receipt");

        await _dbContext.SaveChangesAsync();

        var overview = await _dashboardService.GetPlatformOverviewAsync();

        Assert.Equal(12m, overview.CashBalance);
        Assert.Equal(0m, overview.ReceivablesBalance);
        Assert.Equal(120m, overview.PayablesBalance);
        Assert.Equal(-108m, overview.NetPosition);
        Assert.Equal(12m, overview.PlatformBalance);
        Assert.Equal(20m, overview.TotalCommissions);
        Assert.Equal(5m, overview.CommissionReversals);
        Assert.Equal(30m, overview.TotalRefunds);
        Assert.Equal(90m, overview.TotalPayouts);
        Assert.Equal(15m, overview.NetProfit);
        Assert.Equal("EGP", overview.Currency);
        Assert.Equal(6, overview.TransactionsCount);
        Assert.Equal(new DateTime(2026, 3, 24, 8, 25, 0, DateTimeKind.Utc), overview.LastTransactionAtUtc);
        Assert.Contains(overview.Breakdown, item => item.TransactionType == "Commission" && item.NetAmount == 15m);
        Assert.Equal(6, overview.RecentTransactions.Count);
        Assert.Contains(overview.RecentTransactions, item => item.FinancialBucket == "Cash");
        Assert.Contains(overview.RecentTransactions, item => item.FinancialBucket == "Payable");
    }

    [Fact]
    public async Task GetVendorDashboardAsync_ShouldExcludeOnlineCommissionFromDebtAndDeriveActivity()
    {
        var vendorId = Guid.NewGuid();
        var otherVendorId = Guid.NewGuid();
        var orderReference = Guid.NewGuid();
        var refundReference = Guid.NewGuid();
        var otherOrderReference = Guid.NewGuid();
        var settlementReference = Guid.NewGuid();

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 24, 9, 0, 0, DateTimeKind.Utc),
            "Platform order cash");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            82m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 24, 9, 0, 10, DateTimeKind.Utc),
            "Vendor earnings");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            18m,
            CommerceHubFinancialReferenceType.Order,
            orderReference,
            new DateTime(2026, 3, 24, 9, 0, 20, DateTimeKind.Utc),
            "Commission capture");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            40m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 24, 10, 0, 0, DateTimeKind.Utc),
            "Customer refund");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            34m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 24, 10, 0, 10, DateTimeKind.Utc),
            "Vendor refund impact",
            CommerceHubFinancialBucket.Receivable);

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            6m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 24, 10, 0, 20, DateTimeKind.Utc),
            "Commission reversal");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialTransactionDirection.Debit,
            20m,
            CommerceHubFinancialReferenceType.Payout,
            Guid.NewGuid(),
            new DateTime(2026, 3, 24, 11, 0, 0, DateTimeKind.Utc),
            "Vendor payout");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            5m,
            CommerceHubFinancialReferenceType.Settlement,
            settlementReference,
            new DateTime(2026, 3, 24, 11, 2, 0, DateTimeKind.Utc),
            "Vendor debt settlement");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialTransactionDirection.Credit,
            5m,
            CommerceHubFinancialReferenceType.Settlement,
            settlementReference,
            new DateTime(2026, 3, 24, 11, 2, 5, DateTimeKind.Utc),
            "Platform settlement receipt");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            75m,
            CommerceHubFinancialReferenceType.Order,
            otherOrderReference,
            new DateTime(2026, 3, 24, 11, 5, 0, DateTimeKind.Utc),
            "Other vendor order");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            otherVendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            63m,
            CommerceHubFinancialReferenceType.Order,
            otherOrderReference,
            new DateTime(2026, 3, 24, 11, 5, 10, DateTimeKind.Utc),
            "Other vendor earnings");

        SeedTransaction(
            CommerceHubFinancialEntityType.Platform,
            null,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            12m,
            CommerceHubFinancialReferenceType.Order,
            otherOrderReference,
            new DateTime(2026, 3, 24, 11, 5, 20, DateTimeKind.Utc),
            "Other vendor commission");

        await _dbContext.SaveChangesAsync();

        var dashboard = await _dashboardService.GetVendorDashboardAsync(vendorId);

        Assert.Equal(vendorId, dashboard.VendorId);
        Assert.Equal(33m, dashboard.CurrentBalance);
        Assert.Equal(0m, dashboard.AmountOwedByVendor);
        Assert.Equal("ACTIVE", dashboard.DebtStatus);
        Assert.False(dashboard.IsAtRisk);
        Assert.False(dashboard.IsSuspendedFromNewOrders);
        Assert.False(dashboard.IsGracePeriodActive);
        Assert.False(dashboard.IsNearThreshold);
        Assert.Equal(1000m, dashboard.HardLimitAmount);
        Assert.Equal(3, dashboard.GracePeriodDays);
        Assert.Null(dashboard.ThresholdCrossedAtUtc);
        Assert.Null(dashboard.GraceDeadlineUtc);
        Assert.Equal(0m, dashboard.MinimumPaymentToRestoreOrdering);
        Assert.Equal(5m, dashboard.TotalSettlementPaymentsToPlatform);
        Assert.Equal(82m, dashboard.TotalEarnings);
        Assert.Equal(12m, dashboard.TotalCommissions);
        Assert.Equal(18m, dashboard.GrossCommissions);
        Assert.Equal(6m, dashboard.CommissionReversals);
        Assert.Equal(34m, dashboard.TotalRefunds);
        Assert.Equal(20m, dashboard.TotalPayouts);
        Assert.Equal("EGP", dashboard.Currency);
        Assert.Equal(4, dashboard.TransactionsCount);
        Assert.Single(dashboard.RecentSettlements);
        Assert.Equal("SettlementPayment", dashboard.RecentSettlements[0].SettlementType);
        Assert.Equal(5m, dashboard.RecentSettlements[0].Amount);
        Assert.Equal(settlementReference, dashboard.RecentSettlements[0].SettlementId);
        Assert.Single(dashboard.RecentRefundImpacts);
        Assert.Equal(refundReference, dashboard.RecentRefundImpacts[0].RefundId);
        Assert.Equal("DebtIncrease", dashboard.RecentRefundImpacts[0].ImpactType);
        Assert.Equal(34m, dashboard.RecentRefundImpacts[0].SignedDebtAmount);
    }

    [Fact]
    public async Task GetVendorStatementAsync_ShouldApplyFiltersAndPaginate()
    {
        var vendorId = Guid.NewGuid();
        var orderOne = Guid.NewGuid();
        var payoutReference = Guid.NewGuid();
        var orderTwo = Guid.NewGuid();
        var refundReference = Guid.NewGuid();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            100m,
            CommerceHubFinancialReferenceType.Order,
            orderOne,
            new DateTime(2026, 3, 24, 8, 0, 0, DateTimeKind.Utc),
            "First order");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Payout,
            CommerceHubFinancialTransactionDirection.Debit,
            30m,
            CommerceHubFinancialReferenceType.Payout,
            payoutReference,
            new DateTime(2026, 3, 24, 8, 30, 0, DateTimeKind.Utc),
            "Vendor payout");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.OrderPayment,
            CommerceHubFinancialTransactionDirection.Credit,
            20m,
            CommerceHubFinancialReferenceType.Order,
            orderTwo,
            new DateTime(2026, 3, 24, 9, 0, 0, DateTimeKind.Utc),
            "Second order");

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Refund,
            CommerceHubFinancialTransactionDirection.Debit,
            10m,
            CommerceHubFinancialReferenceType.Refund,
            refundReference,
            new DateTime(2026, 3, 24, 9, 30, 0, DateTimeKind.Utc),
            "Filtered refund");

        await _dbContext.SaveChangesAsync();

        var statement = await _dashboardService.GetVendorStatementAsync(
            vendorId,
            new CommerceHubFinanceStatementQueryDto
            {
                FromDateUtc = new DateTime(2026, 3, 24, 7, 45, 0, DateTimeKind.Utc),
                ToDateUtc = new DateTime(2026, 3, 24, 9, 15, 0, DateTimeKind.Utc),
                TransactionTypes = new List<CommerceHubFinancialTransactionType>
                {
                    CommerceHubFinancialTransactionType.OrderPayment,
                    CommerceHubFinancialTransactionType.Payout
                },
                Page = 1,
                PageSize = 10
            });

        Assert.Equal("Vendor", statement.EntityType);
        Assert.Equal(vendorId, statement.EntityId);
        Assert.Equal(80m, statement.CurrentBalance);
        Assert.Equal(120m, statement.FilteredCredits);
        Assert.Equal(30m, statement.FilteredDebits);
        Assert.Equal(0m, statement.OpeningBalance);
        Assert.Equal(90m, statement.ClosingBalance);
        Assert.Equal(1, statement.Page);
        Assert.Equal(10, statement.PageSize);
        Assert.Equal(3, statement.TotalCount);
        Assert.False(statement.HasMore);
        Assert.Equal(new[] { "OrderPayment", "Payout" }, statement.AppliedTransactionTypes);
        Assert.Equal(3, statement.Transactions.Count);

        Assert.Collection(
            statement.Transactions,
            first =>
            {
                Assert.Equal("OrderPayment", first.TransactionType);
                Assert.Equal(20m, first.Amount);
                Assert.Equal(90m, first.RunningBalance);
                Assert.Equal($"Order:{orderTwo:N}", first.CorrelationId);
            },
            second =>
            {
                Assert.Equal("Payout", second.TransactionType);
                Assert.Equal(-30m, second.SignedAmount);
                Assert.Equal(70m, second.RunningBalance);
                Assert.Equal($"Payout:{payoutReference:N}", second.CorrelationId);
            },
            third =>
            {
                Assert.Equal("OrderPayment", third.TransactionType);
                Assert.Equal(100m, third.Amount);
                Assert.Equal(100m, third.RunningBalance);
                Assert.Equal($"Order:{orderOne:N}", third.CorrelationId);
            });
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private void SeedTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        Guid? referenceId = null,
        DateTime? createdAtUtc = null,
        string? description = null,
        CommerceHubFinancialBucket? financialBucketOverride = null)
    {
        _dbContext.CommerceHubFinancialTransactions.Add(
            CommerceHubFinancialTransaction.Create(
                entityType,
                entityId,
                transactionType,
                direction,
                amount,
                "EGP",
                referenceType,
                referenceId ?? Guid.NewGuid(),
                description ?? $"{transactionType} {direction}",
                createdAtUtc: createdAtUtc,
                financialBucketOverride: financialBucketOverride));
    }
}
