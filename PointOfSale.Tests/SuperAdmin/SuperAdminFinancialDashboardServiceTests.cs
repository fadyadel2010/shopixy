using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class SuperAdminFinancialDashboardServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StubCommerceHubDailyFinancialAggregateService _aggregateService;
    private readonly SuperAdminFinancialDashboardService _service;

    public SuperAdminFinancialDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _aggregateService = new StubCommerceHubDailyFinancialAggregateService();
        _service = new SuperAdminFinancialDashboardService(
            _dbContext,
            new StubCommerceHubFinanceReconciliationReadService(),
            _aggregateService,
            NullLogger<SuperAdminFinancialDashboardService>.Instance);
    }

    [Fact]
    public async Task DashboardQueries_ShouldReconcileOverviewTrendsPromosVendorsAndRefunds()
    {
        var scenario = await SeedHealthyScenarioAsync();

        var overview = await _service.GetOverviewAsync(new SuperAdminFinancialDashboardFilterDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc
        });

        Assert.Equal("EGP", overview.Currency);
        Assert.Equal(35m, overview.TotalGrossCommission);
        Assert.Equal(2.4m, overview.TotalPromoCost);
        Assert.Equal(32.6m, overview.TotalNetCommission);
        Assert.Equal(77m, overview.TotalRefundAmount);
        Assert.Equal(7m, overview.RefundImpactOnRevenue);
        Assert.Equal(25.6m, overview.NetRevenue);
        Assert.Equal(3, overview.TotalOrdersCount);
        Assert.Equal(128.33m, overview.AverageOrderValue);
        Assert.Equal(2, overview.RefundedOrdersCount);
        Assert.Equal(0.6667m, overview.RefundRate);
        Assert.Equal("SubOrder", overview.OrderScope);

        var trends = await _service.GetTrendsAsync(new SuperAdminFinancialTrendsQueryDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc,
            GroupBy = SuperAdminFinancialTrendGroupBy.Day
        });

        Assert.Equal("EGP", trends.Currency);
        Assert.Equal("Day", trends.GroupBy);
        Assert.Equal(35m, trends.Points.Sum(point => point.GrossCommission));
        Assert.Equal(2.4m, trends.Points.Sum(point => point.PromoCost));
        Assert.Equal(7m, trends.Points.Sum(point => point.CommissionReversals));
        Assert.Equal(25.6m, trends.Points.Sum(point => point.NetRevenue));
        Assert.Equal(77m, trends.Points.Sum(point => point.RefundAmount));
        Assert.Equal(3, trends.Points.Sum(point => point.OrdersCount));

        var promos = await _service.GetPromoAnalyticsAsync(new SuperAdminPromoAnalyticsQueryDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc,
            Page = 1,
            PageSize = 10,
            SortBy = SuperAdminPromoAnalyticsSortField.NetImpact,
            SortDirection = SuperAdminSortDirection.Desc
        });

        Assert.Equal(1, promos.TotalCount);
        var promo = Assert.Single(promos.Items);
        Assert.Equal(scenario.PromoCode, promo.PromoCode);
        Assert.Equal(1, promo.TotalUsageCount);
        Assert.Equal(2.4m, promo.TotalPromoCost);
        Assert.Equal(10m, promo.RevenueGenerated);
        Assert.Equal(2m, promo.CommissionReversals);
        Assert.Equal(5.6m, promo.NetImpact);
        Assert.Equal(2.4m, promo.AverageDiscountPerUse);

        var vendors = await _service.GetVendorPerformanceAsync(new SuperAdminVendorPerformanceQueryDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc,
            Page = 1,
            PageSize = 10,
            SortBy = SuperAdminVendorPerformanceSortField.NetContribution,
            SortDirection = SuperAdminSortDirection.Desc
        });

        Assert.Equal(2, vendors.TotalCount);
        Assert.Equal(scenario.VendorBetaId, vendors.Items[0].VendorId);
        Assert.Equal(20m, vendors.Items[0].NetContribution);

        var vendorAlpha = Assert.Single(vendors.Items.Where(item => item.VendorId == scenario.VendorAlphaId));
        Assert.Equal(2, vendorAlpha.OrdersCount);
        Assert.Equal(165m, vendorAlpha.TotalSales);
        Assert.Equal(15m, vendorAlpha.GrossCommission);
        Assert.Equal(2.4m, vendorAlpha.PromoCost);
        Assert.Equal(7m, vendorAlpha.CommissionReversals);
        Assert.Equal(77m, vendorAlpha.TotalRefundAmount);
        Assert.Equal(2, vendorAlpha.RefundedOrdersCount);
        Assert.Equal(1m, vendorAlpha.RefundRate);
        Assert.Equal(5.6m, vendorAlpha.NetContribution);

        var refunds = await _service.GetRefundAnalysisAsync(new SuperAdminRefundAnalysisQueryDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc,
            GroupBy = SuperAdminFinancialTrendGroupBy.Day
        });

        Assert.Equal("EGP", refunds.Currency);
        Assert.Equal("Day", refunds.GroupBy);
        Assert.Equal(77m, refunds.TotalRefundAmount);
        Assert.Equal(2, refunds.TotalRefundCount);
        Assert.Equal(2, refunds.RefundedOrdersCount);
        Assert.Equal(0.6667m, refunds.RefundRate);
        Assert.Equal(0.6m, refunds.PromoReversalAmount);
        Assert.Equal(77m, refunds.Points.Sum(point => point.RefundAmount));
        Assert.Equal(2, refunds.Points.Sum(point => point.RefundCount));
        Assert.Single(refunds.TopRefundedVendors);
        Assert.Equal(scenario.VendorAlphaId, refunds.TopRefundedVendors[0].VendorId);
        Assert.Equal(77m, refunds.TopRefundedVendors[0].RefundAmount);
        Assert.Equal(scenario.FullRefundItemId, refunds.TopRefundedItems[0].OrderItemId);
        Assert.Contains(refunds.Alerts, alert => alert.AlertType == "REFUND_RATE_SPIKE");
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldApplyPromoCodeFilterCaseInsensitively()
    {
        var scenario = await SeedHealthyScenarioAsync();

        var overview = await _service.GetOverviewAsync(new SuperAdminFinancialDashboardFilterDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc,
            PromoCode = scenario.PromoCode.ToLowerInvariant()
        });

        Assert.Equal(10m, overview.TotalGrossCommission);
        Assert.Equal(2.4m, overview.TotalPromoCost);
        Assert.Equal(7.6m, overview.TotalNetCommission);
        Assert.Equal(22m, overview.TotalRefundAmount);
        Assert.Equal(2m, overview.RefundImpactOnRevenue);
        Assert.Equal(5.6m, overview.NetRevenue);
        Assert.Equal(1, overview.TotalOrdersCount);
        Assert.Equal(110m, overview.AverageOrderValue);
        Assert.Equal(1, overview.RefundedOrdersCount);
        Assert.Equal(1m, overview.RefundRate);
        Assert.Equal(scenario.PromoCode, overview.AppliedPromoCode);
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldUseDailyAggregatesWhenFreshAndUnfiltered()
    {
        var fromDateUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 3, 2, 23, 59, 59, DateTimeKind.Utc);
        var calculatedAtUtc = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);

        _aggregateService.Window = new CommerceHubDailyAggregateWindow(
            new List<CommerceHubDailyFinancialAggregate>
            {
                new()
                {
                    DateUtc = fromDateUtc.Date,
                    CurrencyCode = "EGP",
                    BaseCurrencyCode = "EGP",
                    ExchangeRateToBase = 1m,
                    GrossSales = 150m,
                    GrossCommission = 15m,
                    PromoCost = 2m,
                    RefundAmount = 20m,
                    RefundImpactOnRevenue = 3m,
                    OrdersCount = 2,
                    RefundCount = 1,
                    RefundedOrdersCount = 1,
                    AggregatedThroughUtc = calculatedAtUtc,
                    CalculatedAtUtc = calculatedAtUtc
                },
                new()
                {
                    DateUtc = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    CurrencyCode = "EGP",
                    BaseCurrencyCode = "EGP",
                    ExchangeRateToBase = 1m,
                    GrossSales = 50m,
                    GrossCommission = 5m,
                    PromoCost = 0m,
                    RefundAmount = 0m,
                    RefundImpactOnRevenue = 0m,
                    OrdersCount = 1,
                    RefundCount = 0,
                    RefundedOrdersCount = 0,
                    AggregatedThroughUtc = calculatedAtUtc,
                    CalculatedAtUtc = calculatedAtUtc
                }
            },
            CoversRequestedWindow: true,
            IsFresh: true,
            LatestCalculatedAtUtc: calculatedAtUtc,
            AggregatedThroughUtc: calculatedAtUtc,
            BaseCurrencyCode: "EGP");

        var overview = await _service.GetOverviewAsync(new SuperAdminFinancialDashboardFilterDto
        {
            FromDateUtc = fromDateUtc,
            ToDateUtc = toDateUtc
        });

        Assert.Equal("DAILY_AGGREGATE", overview.DataSource);
        Assert.Equal("EGP", overview.Currency);
        Assert.Equal("EGP", overview.BaseCurrencyCode);
        Assert.Equal(20m, overview.TotalGrossCommission);
        Assert.Equal(2m, overview.TotalPromoCost);
        Assert.Equal(18m, overview.TotalNetCommission);
        Assert.Equal(20m, overview.TotalRefundAmount);
        Assert.Equal(3m, overview.RefundImpactOnRevenue);
        Assert.Equal(15m, overview.NetRevenue);
        Assert.Equal(3, overview.TotalOrdersCount);
        Assert.Equal(66.67m, overview.AverageOrderValue);
        Assert.Equal(1, overview.RefundedOrdersCount);
        Assert.Equal(0.3333m, overview.RefundRate);
        Assert.Equal(calculatedAtUtc, overview.AggregatedThroughUtc);
    }

    [Fact]
    public async Task GetPromoAnalyticsAsync_ShouldExcludeUnrecognizedFailedPromoOrders()
    {
        var scenario = await SeedHealthyScenarioAsync();

        var failedOrder = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: _dbContext.CommerceHubCustomers.Select(customer => customer.Id).First(),
            orderNumber: "CH-FIN-FAILED-1004",
            createdAtUtc: new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc),
            totalAmount: 75m,
            subtotal: 70m,
            shippingTotal: 5m,
            promoCode: scenario.PromoCode,
            promoDiscountTotal: 2m);
        failedOrder.Status = OrderStatus.Cancelled;
        failedOrder.PaymentStatus = PaymentStatus.Failed;

        _dbContext.CommerceHubMarketplaceOrders.Add(failedOrder);
        _dbContext.PromoCodeUsages.Add(new PromoCodeUsage
        {
            Id = Guid.NewGuid(),
            PromoCodeId = _dbContext.PromoCodes.Single().Id,
            MarketplaceOrderId = failedOrder.Id,
            PromoCodeSnapshot = scenario.PromoCode,
            DiscountAmount = 2m,
            EligibleBaseCommissionAmount = 7m,
            AppliedAtUtc = failedOrder.CreatedAt,
            CreatedAt = failedOrder.CreatedAt
        });

        await SaveCommerceHubFixtureAsync();

        var promos = await _service.GetPromoAnalyticsAsync(new SuperAdminPromoAnalyticsQueryDto
        {
            FromDateUtc = scenario.FromDateUtc,
            ToDateUtc = scenario.ToDateUtc,
            PromoCode = scenario.PromoCode,
            Page = 1,
            PageSize = 10,
            SortBy = SuperAdminPromoAnalyticsSortField.UsageCount,
            SortDirection = SuperAdminSortDirection.Desc
        });

        var promo = Assert.Single(promos.Items);
        Assert.Equal(1, promo.TotalUsageCount);
        Assert.Equal(10m, promo.RevenueGenerated);
        Assert.Equal(2.4m, promo.TotalPromoCost);
    }

    [Fact]
    public async Task GetIntegrityCheckAsync_ShouldDetectScopedFinancialInconsistencies()
    {
        var scenario = await SeedBrokenScenarioAsync();

        var integrity = await _service.GetIntegrityCheckAsync(new SuperAdminFinancialIntegrityCheckQueryDto
        {
            PromoCode = scenario.PromoCode.ToLowerInvariant(),
            MaxIssues = 10
        });

        Assert.Equal("ISSUES_DETECTED", integrity.Status);
        Assert.False(integrity.SupportsGlobalDebitCreditParity);
        Assert.Equal("NOT_APPLICABLE", integrity.DebitCreditParityStatus);
        Assert.Contains("promo 'BROKEN1'", integrity.ScopeDescription, StringComparison.Ordinal);
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "ORDER_MATH_MISMATCH");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "ORDER_LEDGER_MISMATCH");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "PROMO_USAGE_MISSING");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "PROMO_LEDGER_WITHOUT_USAGE");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "REFUND_MATH_MISMATCH");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "REFUND_LEDGER_MISMATCH");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "MISSING_REFUND_REVERSAL");
        Assert.Equal(0, integrity.OrphanTransactionsCount);
        Assert.Equal(0, integrity.PromoUsageWithoutLedgerCount);
        Assert.Equal(1, integrity.PromoLedgerWithoutUsageCount);
        Assert.Equal(0, integrity.DuplicatePromoUsageCount);
        Assert.Equal(1, integrity.MissingRefundReversalCount);
        Assert.Equal(4, integrity.OrderFlowMismatchCount);
        Assert.Equal(3, integrity.RefundFlowMismatchCount);
        Assert.Equal(7, integrity.TotalIssueCount);
    }

    [Fact]
    public async Task GetIntegrityCheckAsync_GlobalScope_ShouldKeepLiveCountersAlignedWithSampledIssues()
    {
        await SeedBrokenScenarioAsync();

        var integrity = await _service.GetIntegrityCheckAsync(new SuperAdminFinancialIntegrityCheckQueryDto
        {
            MaxIssues = 50
        });

        Assert.Equal("ISSUES_DETECTED", integrity.Status);
        Assert.NotEmpty(integrity.Issues);
        Assert.True(integrity.OrderFlowMismatchCount > 0);
        Assert.True(integrity.TotalIssueCount > 0);
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "ORDER_MATH_MISMATCH");
    }

    [Fact]
    public async Task GetIntegrityCheckAsync_ShouldNotRequireLedgerForPendingCodSiblingOfPartiallyRefundedOrder()
    {
        var vendorId = Guid.NewGuid();
        var createdAtUtc = new DateTime(2026, 3, 22, 12, 0, 0, DateTimeKind.Utc);
        var customer = CreateCustomer("pending-cod-dashboard@example.com", "Pending COD Customer");
        var order = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: customer.Id,
            orderNumber: "CH-DASH-PARTIAL-COD",
            createdAtUtc: createdAtUtc,
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m);
        order.PaymentStatus = PaymentStatus.PartiallyRefunded;

        var pendingCodSubOrder = CreateSubOrder(
            id: Guid.NewGuid(),
            order: order,
            vendorId: vendorId,
            vendorName: "Pending COD Dashboard Vendor",
            orderNumber: "CH-DASH-PARTIAL-COD-B",
            createdAtUtc: createdAtUtc,
            total: 110m,
            subtotal: 100m,
            shipping: 10m,
            grossCommission: 10m,
            finalCommission: 10m,
            promoDiscount: 0m,
            vendorEarning: 100m);
        pendingCodSubOrder.Status = OrderStatus.Pending;
        pendingCodSubOrder.PaymentStatus = PaymentStatus.CashOnDelivery;
        pendingCodSubOrder.PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery;
        pendingCodSubOrder.PaymentMethodCode = "COD";
        pendingCodSubOrder.PaymentMethodName = "Cash on Delivery";

        _dbContext.Tenants.Add(CreateTenant(vendorId, "Pending COD Dashboard Vendor", "pending-cod-dashboard"));
        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(pendingCodSubOrder);
        await SaveCommerceHubFixtureAsync();

        var integrity = await _service.GetIntegrityCheckAsync(new SuperAdminFinancialIntegrityCheckQueryDto
        {
            FromDateUtc = createdAtUtc.Date,
            ToDateUtc = createdAtUtc.Date.AddDays(1).AddTicks(-1),
            MaxIssues = 10
        });

        Assert.Equal(0, integrity.OrderFlowMismatchCount);
        Assert.DoesNotContain(integrity.Issues, issue =>
            issue.IssueType == "ORDER_LEDGER_MISMATCH" && issue.ReferenceId == pendingCodSubOrder.Id);
    }

    [Fact]
    public async Task GetIntegrityCheckAsync_ShouldDetectDuplicatePromoUsageWithoutLedger()
    {
        var vendorId = Guid.NewGuid();
        var promoId = Guid.NewGuid();
        var fromDateUtc = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 3, 22, 23, 59, 59, DateTimeKind.Utc);

        _dbContext.Tenants.Add(CreateTenant(vendorId, "Promo Guard Vendor", "promo-guard"));

        var customer = CreateCustomer("duplicate-promo@example.com", "Duplicate Promo Customer");
        var order = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: customer.Id,
            orderNumber: "CH-DUP-3001",
            createdAtUtc: new DateTime(2026, 3, 22, 10, 0, 0, DateTimeKind.Utc),
            totalAmount: 88m,
            subtotal: 80m,
            shippingTotal: 8m,
            promoCode: "DUPLICATE1",
            promoDiscountTotal: 2m);
        var subOrder = CreateSubOrder(
            id: Guid.NewGuid(),
            order: order,
            vendorId: vendorId,
            vendorName: "Promo Guard Vendor",
            orderNumber: "CH-DUP-3001-A",
            createdAtUtc: order.CreatedAt,
            total: 88m,
            subtotal: 80m,
            shipping: 8m,
            grossCommission: 8m,
            finalCommission: 8m,
            promoDiscount: 0m,
            vendorEarning: 80m);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.PromoCodes.Add(new PromoCode
        {
            Id = promoId,
            Code = "DUPLICATE1",
            DiscountType = PromoDiscountType.Fixed,
            DiscountValue = 2m,
            IsActive = true,
            UsedCount = 2,
            CreatedAt = order.CreatedAt
        });
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        _dbContext.PromoCodeUsages.AddRange(
            new PromoCodeUsage
            {
                Id = Guid.NewGuid(),
                PromoCodeId = promoId,
                MarketplaceOrderId = order.Id,
                CustomerId = customer.Id,
                PromoCodeSnapshot = "DUPLICATE1",
                DiscountAmount = 2m,
                EligibleBaseCommissionAmount = 8m,
                AppliedAtUtc = order.CreatedAt,
                CreatedAt = order.CreatedAt
            },
            new PromoCodeUsage
            {
                Id = Guid.NewGuid(),
                PromoCodeId = promoId,
                MarketplaceOrderId = order.Id,
                CustomerId = customer.Id,
                PromoCodeSnapshot = "DUPLICATE1",
                DiscountAmount = 2m,
                EligibleBaseCommissionAmount = 8m,
                AppliedAtUtc = order.CreatedAt.AddMinutes(1),
                CreatedAt = order.CreatedAt.AddMinutes(1)
            });

        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            8m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            order.CreatedAt,
            "Duplicate promo order commission");

        await SaveCommerceHubFixtureAsync();

        var integrity = await _service.GetIntegrityCheckAsync(new SuperAdminFinancialIntegrityCheckQueryDto
        {
            FromDateUtc = fromDateUtc,
            ToDateUtc = toDateUtc,
            PromoCode = "duplicate1",
            MaxIssues = 10
        });

        Assert.Equal("ISSUES_DETECTED", integrity.Status);
        Assert.Equal(2, integrity.TotalIssueCount);
        Assert.Equal(2, integrity.OrderFlowMismatchCount);
        Assert.Equal(1, integrity.PromoUsageWithoutLedgerCount);
        Assert.Equal(0, integrity.PromoLedgerWithoutUsageCount);
        Assert.Equal(1, integrity.DuplicatePromoUsageCount);
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "PROMO_USAGE_WITHOUT_LEDGER");
        Assert.Contains(integrity.Issues, issue => issue.IssueType == "DUPLICATE_PROMO_USAGE");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task<HealthyDashboardScenario> SeedHealthyScenarioAsync()
    {
        var vendorAlphaId = Guid.NewGuid();
        var vendorBetaId = Guid.NewGuid();
        var promoId = Guid.NewGuid();
        var promoCode = "SPRING26";
        var fromDateUtc = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 3, 14, 23, 59, 59, DateTimeKind.Utc);

        _dbContext.Tenants.AddRange(
            CreateTenant(vendorAlphaId, "Vendor Alpha", "vendor-alpha"),
            CreateTenant(vendorBetaId, "Vendor Beta", "vendor-beta"));

        var customerAlpha = CreateCustomer("alpha-finance@example.com", "Vendor Alpha Customer");
        var customerBeta = CreateCustomer("beta-finance@example.com", "Vendor Beta Customer");
        var customerGamma = CreateCustomer("gamma-finance@example.com", "Vendor Gamma Customer");

        var promoDefinition = new PromoCode
        {
            Id = promoId,
            Code = promoCode,
            DiscountType = PromoDiscountType.Fixed,
            DiscountValue = 3m,
            IsActive = true,
            UsedCount = 1,
            CreatedAt = fromDateUtc
        };

        var promoOrder = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: customerAlpha.Id,
            orderNumber: "CH-FIN-1001",
            createdAtUtc: new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc),
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m,
            promoCode: promoCode,
            promoDiscountTotal: 3m);
        var promoSubOrder = CreateSubOrder(
            id: Guid.NewGuid(),
            order: promoOrder,
            vendorId: vendorAlphaId,
            vendorName: "Vendor Alpha",
            orderNumber: "CH-FIN-1001-A",
            createdAtUtc: promoOrder.CreatedAt,
            total: 110m,
            subtotal: 100m,
            shipping: 10m,
            grossCommission: 10m,
            finalCommission: 7m,
            promoDiscount: 3m,
            vendorEarning: 100m);
        var promoItem = CreateSubOrderItem(
            id: Guid.NewGuid(),
            subOrder: promoSubOrder,
            productName: "Promo Jacket",
            variantName: "Blue / M",
            quantity: 2,
            unitPrice: 50m,
            total: 100m,
            grossCommission: 10m,
            finalCommission: 7m,
            promoDiscount: 3m);
        var promoRefund = CreateRefund(
            id: Guid.NewGuid(),
            vendorId: vendorAlphaId,
            subOrder: promoSubOrder,
            orderItemId: promoItem.Id,
            quantity: 1,
            createdAtUtc: new DateTime(2026, 3, 13, 14, 0, 0, DateTimeKind.Utc),
            amount: 22m,
            vendorEarningReversal: 19.4m,
            commissionReversal: 2m,
            promoReversal: 0.6m,
            isFullRefund: false);
        var promoUsage = new PromoCodeUsage
        {
            Id = Guid.NewGuid(),
            PromoCodeId = promoId,
            MarketplaceOrderId = promoOrder.Id,
            CustomerId = customerAlpha.Id,
            PromoCodeSnapshot = promoCode,
            DiscountAmount = 3m,
            EligibleBaseCommissionAmount = 10m,
            AppliedAtUtc = promoOrder.CreatedAt,
            CreatedAt = promoOrder.CreatedAt
        };

        var standardOrder = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: customerBeta.Id,
            orderNumber: "CH-FIN-1002",
            createdAtUtc: new DateTime(2026, 3, 11, 10, 0, 0, DateTimeKind.Utc),
            totalAmount: 220m,
            subtotal: 200m,
            shippingTotal: 20m);
        var standardSubOrder = CreateSubOrder(
            id: Guid.NewGuid(),
            order: standardOrder,
            vendorId: vendorBetaId,
            vendorName: "Vendor Beta",
            orderNumber: "CH-FIN-1002-A",
            createdAtUtc: standardOrder.CreatedAt,
            total: 220m,
            subtotal: 200m,
            shipping: 20m,
            grossCommission: 20m,
            finalCommission: 20m,
            promoDiscount: 0m,
            vendorEarning: 200m);

        var fullRefundOrder = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: customerGamma.Id,
            orderNumber: "CH-FIN-1003",
            createdAtUtc: new DateTime(2026, 3, 12, 11, 0, 0, DateTimeKind.Utc),
            totalAmount: 55m,
            subtotal: 50m,
            shippingTotal: 5m);
        var fullRefundSubOrder = CreateSubOrder(
            id: Guid.NewGuid(),
            order: fullRefundOrder,
            vendorId: vendorAlphaId,
            vendorName: "Vendor Alpha",
            orderNumber: "CH-FIN-1003-A",
            createdAtUtc: fullRefundOrder.CreatedAt,
            total: 55m,
            subtotal: 50m,
            shipping: 5m,
            grossCommission: 5m,
            finalCommission: 5m,
            promoDiscount: 0m,
            vendorEarning: 50m);
        var fullRefundItem = CreateSubOrderItem(
            id: Guid.NewGuid(),
            subOrder: fullRefundSubOrder,
            productName: "Vendor Alpha Basic",
            variantName: "Standard",
            quantity: 1,
            unitPrice: 50m,
            total: 50m,
            grossCommission: 5m,
            finalCommission: 5m,
            promoDiscount: 0m);
        var fullRefund = CreateRefund(
            id: Guid.NewGuid(),
            vendorId: vendorAlphaId,
            subOrder: fullRefundSubOrder,
            orderItemId: fullRefundItem.Id,
            quantity: 1,
            createdAtUtc: new DateTime(2026, 3, 14, 16, 0, 0, DateTimeKind.Utc),
            amount: 55m,
            vendorEarningReversal: 50m,
            commissionReversal: 5m,
            promoReversal: 0m,
            isFullRefund: true);

        _dbContext.CommerceHubCustomers.AddRange(customerAlpha, customerBeta, customerGamma);
        _dbContext.PromoCodes.Add(promoDefinition);
        _dbContext.CommerceHubMarketplaceOrders.AddRange(promoOrder, standardOrder, fullRefundOrder);
        _dbContext.CommerceHubSubOrders.AddRange(promoSubOrder, standardSubOrder, fullRefundSubOrder);
        _dbContext.CommerceHubSubOrderItems.AddRange(promoItem, fullRefundItem);
        _dbContext.CommerceHubRefunds.AddRange(promoRefund, fullRefund);
        _dbContext.PromoCodeUsages.Add(promoUsage);

        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            10m,
            CommerceHubFinancialReferenceType.Order,
            promoSubOrder.Id,
            promoOrder.CreatedAt,
            "Promo order gross commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Debit,
            3m,
            CommerceHubFinancialReferenceType.Order,
            promoSubOrder.Id,
            promoOrder.CreatedAt.AddMinutes(1),
            "Promo order platform discount");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            20m,
            CommerceHubFinancialReferenceType.Order,
            standardSubOrder.Id,
            standardOrder.CreatedAt,
            "Vendor beta gross commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            5m,
            CommerceHubFinancialReferenceType.Order,
            fullRefundSubOrder.Id,
            fullRefundOrder.CreatedAt,
            "Vendor alpha second commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            2m,
            CommerceHubFinancialReferenceType.Refund,
            promoRefund.Id,
            promoRefund.ProcessedAtUtc ?? promoRefund.CreatedAt,
            "Promo refund commission reversal");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Credit,
            0.6m,
            CommerceHubFinancialReferenceType.Refund,
            promoRefund.Id,
            (promoRefund.ProcessedAtUtc ?? promoRefund.CreatedAt).AddMinutes(1),
            "Promo refund reversal");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            5m,
            CommerceHubFinancialReferenceType.Refund,
            fullRefund.Id,
            fullRefund.ProcessedAtUtc ?? fullRefund.CreatedAt,
            "Full refund commission reversal");

        await SaveCommerceHubFixtureAsync();

        return new HealthyDashboardScenario(
            fromDateUtc,
            toDateUtc,
            promoCode,
            vendorAlphaId,
            vendorBetaId,
            fullRefundItem.Id);
    }

    private async Task<BrokenDashboardScenario> SeedBrokenScenarioAsync()
    {
        var vendorId = Guid.NewGuid();
        var promoCode = "BROKEN1";
        var fromDateUtc = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);
        var toDateUtc = new DateTime(2026, 3, 21, 23, 59, 59, DateTimeKind.Utc);

        _dbContext.Tenants.Add(CreateTenant(vendorId, "Broken Vendor", "broken-vendor"));

        var customer = CreateCustomer("broken-finance@example.com", "Broken Finance Customer");
        var order = CreateMarketplaceOrder(
            id: Guid.NewGuid(),
            customerId: customer.Id,
            orderNumber: "CH-BROKEN-2001",
            createdAtUtc: new DateTime(2026, 3, 20, 9, 0, 0, DateTimeKind.Utc),
            totalAmount: 110m,
            subtotal: 100m,
            shippingTotal: 10m,
            promoCode: promoCode,
            promoDiscountTotal: 3m);
        var subOrder = CreateSubOrder(
            id: Guid.NewGuid(),
            order: order,
            vendorId: vendorId,
            vendorName: "Broken Vendor",
            orderNumber: "CH-BROKEN-2001-A",
            createdAtUtc: order.CreatedAt,
            total: 110m,
            subtotal: 100m,
            shipping: 10m,
            grossCommission: 10m,
            finalCommission: 8m,
            promoDiscount: 3m,
            vendorEarning: 100m);
        var refund = CreateRefund(
            id: Guid.NewGuid(),
            vendorId: vendorId,
            subOrder: subOrder,
            orderItemId: null,
            quantity: null,
            createdAtUtc: new DateTime(2026, 3, 21, 13, 0, 0, DateTimeKind.Utc),
            amount: 20m,
            vendorEarningReversal: 17m,
            commissionReversal: 2m,
            promoReversal: 0.5m,
            isFullRefund: false);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubSubOrders.Add(subOrder);
        _dbContext.CommerceHubRefunds.Add(refund);

        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Credit,
            9m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            order.CreatedAt,
            "Broken order commission");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Promotion,
            CommerceHubFinancialTransactionDirection.Debit,
            2m,
            CommerceHubFinancialReferenceType.Order,
            subOrder.Id,
            order.CreatedAt.AddMinutes(1),
            "Broken order promo funding");
        AddPlatformTransaction(
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1.5m,
            CommerceHubFinancialReferenceType.Refund,
            refund.Id,
            refund.ProcessedAtUtc ?? refund.CreatedAt,
            "Broken refund commission reversal");

        await SaveCommerceHubFixtureAsync();

        return new BrokenDashboardScenario(fromDateUtc, toDateUtc, promoCode);
    }

    private void AddPlatformTransaction(
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

    private static Tenant CreateTenant(Guid id, string name, string slug) =>
        new()
        {
            Id = id,
            Name = name,
            Slug = slug,
            ContactEmail = $"{slug}@vendors.test",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static CommerceHubCustomer CreateCustomer(string email, string name) =>
        CommerceHubCustomer.Create(email, name, "01000000000", "hash");

    private static CommerceHubMarketplaceOrder CreateMarketplaceOrder(
        Guid id,
        Guid customerId,
        string orderNumber,
        DateTime createdAtUtc,
        decimal totalAmount,
        decimal subtotal,
        decimal shippingTotal,
        string? promoCode = null,
        decimal promoDiscountTotal = 0m) =>
        new()
        {
            Id = id,
            CustomerId = customerId,
            OrderNumber = orderNumber,
            FullName = "Finance Dashboard Customer",
            PhoneNumber = "01000000000",
            AddressLine = "1 Finance Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            Subtotal = subtotal,
            ShippingTotal = shippingTotal,
            TotalAmount = totalAmount,
            PromoCode = promoCode,
            PromoDiscountTotal = promoDiscountTotal,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            CreatedAt = createdAtUtc
        };

    private static CommerceHubSubOrder CreateSubOrder(
        Guid id,
        CommerceHubMarketplaceOrder order,
        Guid vendorId,
        string vendorName,
        string orderNumber,
        DateTime createdAtUtc,
        decimal total,
        decimal subtotal,
        decimal shipping,
        decimal grossCommission,
        decimal finalCommission,
        decimal promoDiscount,
        decimal vendorEarning) =>
        new()
        {
            Id = id,
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = orderNumber,
            StoreId = vendorId,
            StoreName = vendorName,
            Currency = "EGP",
            Subtotal = subtotal,
            Shipping = shipping,
            PaymentMethod = CommerceHubPaymentMethod.Online,
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            PlatformCommissionAmount = grossCommission,
            FinalPlatformCommissionAmount = finalCommission,
            PromoDiscountAmount = promoDiscount,
            VendorEarningAmount = vendorEarning,
            PlatformCommissionRate = total == 0m ? 0m : Math.Round(grossCommission / total, 6, MidpointRounding.AwayFromZero),
            Total = total,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled,
            CreatedAt = createdAtUtc
        };

    private static CommerceHubSubOrderItem CreateSubOrderItem(
        Guid id,
        CommerceHubSubOrder subOrder,
        string productName,
        string? variantName,
        int quantity,
        decimal unitPrice,
        decimal total,
        decimal grossCommission,
        decimal finalCommission,
        decimal promoDiscount) =>
        new()
        {
            Id = id,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            ProductName = productName,
            VariantName = variantName,
            Quantity = quantity,
            UnitPrice = unitPrice,
            PlatformCommissionRate = total == 0m ? 0m : Math.Round(grossCommission / total, 6, MidpointRounding.AwayFromZero),
            PlatformCommissionAmount = grossCommission,
            FinalPlatformCommissionAmount = finalCommission,
            PromoDiscountAmount = promoDiscount,
            Total = total,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = subOrder.CreatedAt.AddDays(1),
            CreatedAt = subOrder.CreatedAt
        };

    private static CommerceHubRefund CreateRefund(
        Guid id,
        Guid vendorId,
        CommerceHubSubOrder subOrder,
        Guid? orderItemId,
        int? quantity,
        DateTime createdAtUtc,
        decimal amount,
        decimal vendorEarningReversal,
        decimal commissionReversal,
        decimal promoReversal,
        bool isFullRefund) =>
        new()
        {
            Id = id,
            TenantId = vendorId,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            OrderItemId = orderItemId,
            Quantity = quantity,
            Amount = amount,
            VendorEarningReversalAmount = vendorEarningReversal,
            CommissionReversalAmount = commissionReversal,
            PromoDiscountReversalAmount = promoReversal,
            Currency = "EGP",
            Status = CommerceHubRefundStatus.Completed,
            Reason = isFullRefund ? "Full refund" : "Partial refund",
            IdempotencyKey = $"refund:{id:N}",
            IsFullRefund = isFullRefund,
            IsPostPayoutAdjustment = false,
            ProcessedAtUtc = createdAtUtc,
            CreatedAt = createdAtUtc
        };

    private sealed class StubCommerceHubFinanceReconciliationReadService : ICommerceHubFinanceReconciliationReadService
    {
        public Task<CommerceHubFinanceReconciliationResult> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new CommerceHubFinanceReconciliationResult(
                    Status: "OK",
                    PlatformNetBalance: 0m,
                    VendorsNetBalance: 0m,
                    TotalTransactions: 0,
                    MissingIdempotencyKeyCount: 0,
                    MissingCorrelationIdCount: 0,
                    DuplicateIdempotencyKeyCount: 0,
                    DuplicateReferenceConstraintCount: 0,
                    MissingPairCount: 0,
                    DuplicateEntryCount: 0,
                    CorrelationIntegrityMismatchCount: 0,
                    PayableMismatchCount: 0,
                    ReceivableMismatchCount: 0,
                    OrphanTransactionsCount: 0,
                    OrderFlowMismatchCount: 0,
                    RefundFlowMismatchCount: 0,
                    PayoutFlowMismatchCount: 0,
                    AdjustmentFlowMismatchCount: 0,
                    MultiCurrencyReferenceCount: 0,
                    InvalidEntityScopeCount: 0,
                    ExecutedAtUtc: new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc)));
        }
    }

    private sealed class StubCommerceHubDailyFinancialAggregateService : ICommerceHubDailyFinancialAggregateService
    {
        public CommerceHubDailyAggregateWindow Window { get; set; } =
            new(
                Array.Empty<CommerceHubDailyFinancialAggregate>(),
                CoversRequestedWindow: false,
                IsFresh: false,
                LatestCalculatedAtUtc: null,
                AggregatedThroughUtc: null,
                BaseCurrencyCode: "EGP");

        public Task<CommerceHubDailyAggregateWindow> GetWindowAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            DateTime? snapshotBoundaryUtc = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Window);
        }

        public Task<IReadOnlyList<CommerceHubDailyFinancialAggregate>> RecalculateWindowAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            DateTime? snapshotBoundaryUtc = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CommerceHubDailyFinancialAggregate>>(Window.Rows);
        }

        public Task RefreshWindowAsync(
            DateTime fromDateUtc,
            DateTime toDateUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record HealthyDashboardScenario(
        DateTime FromDateUtc,
        DateTime ToDateUtc,
        string PromoCode,
        Guid VendorAlphaId,
        Guid VendorBetaId,
        Guid FullRefundItemId);

    private sealed record BrokenDashboardScenario(
        DateTime FromDateUtc,
        DateTime ToDateUtc,
        string PromoCode);
}
