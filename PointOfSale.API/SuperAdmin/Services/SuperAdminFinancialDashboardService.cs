using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminFinancialDashboardService
{
    Task<SuperAdminFinancialOverviewDto> GetOverviewAsync(
        SuperAdminFinancialDashboardFilterDto? query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminFinancialTrendsDto> GetTrendsAsync(
        SuperAdminFinancialTrendsQueryDto? query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminPromoAnalyticsItemDto>> GetPromoAnalyticsAsync(
        SuperAdminPromoAnalyticsQueryDto? query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminVendorPerformanceItemDto>> GetVendorPerformanceAsync(
        SuperAdminVendorPerformanceQueryDto? query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminRefundAnalysisDto> GetRefundAnalysisAsync(
        SuperAdminRefundAnalysisQueryDto? query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminFinancialIntegrityCheckDto> GetIntegrityCheckAsync(
        SuperAdminFinancialIntegrityCheckQueryDto? query,
        CancellationToken cancellationToken = default);
}

public sealed partial class SuperAdminFinancialDashboardService : ISuperAdminFinancialDashboardService
{
    private const string DefaultCurrency = "EGP";
    private const string DefaultConsistencyModel =
        "Strong consistency applies to a single order flow. Aggregated dashboard metrics are eventually consistent.";
    private const string DefaultFreshnessNote =
        "Financial data may be slightly delayed due to processing.";
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinanceReconciliationReadService _reconciliationReadService;
    private readonly ICommerceHubDailyFinancialAggregateService _dailyFinancialAggregateService;
    private readonly ILogger<SuperAdminFinancialDashboardService> _logger;

    public SuperAdminFinancialDashboardService(
        ApplicationDbContext dbContext,
        ICommerceHubFinanceReconciliationReadService reconciliationReadService,
        ICommerceHubDailyFinancialAggregateService dailyFinancialAggregateService,
        ILogger<SuperAdminFinancialDashboardService> logger)
    {
        _dbContext = dbContext;
        _reconciliationReadService = reconciliationReadService;
        _dailyFinancialAggregateService = dailyFinancialAggregateService;
        _logger = logger;
    }

    public async Task<SuperAdminFinancialOverviewDto> GetOverviewAsync(
        SuperAdminFinancialDashboardFilterDto? query,
        CancellationToken cancellationToken = default)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var filter = await NormalizeFilterAsync(query, defaultToLast30Days: true, generatedAtUtc, cancellationToken);
        var reconciliationSnapshot = await _reconciliationReadService.GetSnapshotAsync(cancellationToken);
        if (filter.HasVendorFilter && filter.VendorIds.Count == 0)
        {
            return BuildEmptyOverview(filter, generatedAtUtc, reconciliationSnapshot);
        }

        var aggregateWindow = await TryGetAggregateWindowAsync(filter, cancellationToken);
        if (aggregateWindow != null)
        {
            _logger.LogInformation(
                "Super admin financial overview served from daily aggregates | FromDateUtc: {FromDateUtc} | ToDateUtc: {ToDateUtc}",
                filter.FromDateUtc,
                filter.ToDateUtc);

            return BuildOverviewFromAggregateWindow(filter, aggregateWindow, generatedAtUtc, reconciliationSnapshot);
        }

        var orderSummaryRows = await BuildOrderCommissionQuery(filter)
            .GroupBy(row => row.Currency)
            .Select(group => new CurrencyAggregateRow
            {
                Currency = group.Key,
                Amount = group.Sum(row => (decimal?)row.GrossCommissionAmount) ?? 0m,
                SecondaryAmount = group.Sum(row => (decimal?)row.OrderTotal) ?? 0m,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var orderPromotionRows = await BuildOrderPromotionQuery(filter)
            .GroupBy(row => row.Currency)
            .Select(group => new CurrencyAggregateRow
            {
                Currency = group.Key,
                Amount = group.Sum(row => (decimal?)row.PromoAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var refundSummaryRows = await BuildRefundMetricQuery(filter)
            .GroupBy(row => row.Currency)
            .Select(group => new CurrencyAggregateRow
            {
                Currency = group.Key,
                Amount = group.Sum(row => (decimal?)row.RefundAmount) ?? 0m,
                Count = group.Count(),
                DistinctCount = group.Select(row => row.SubOrderId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var refundCommissionRows = await BuildRefundCommissionQuery(filter)
            .GroupBy(row => row.Currency)
            .Select(group => new CurrencyAggregateRow
            {
                Currency = group.Key,
                Amount = group.Sum(row => (decimal?)row.CommissionAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var refundPromotionRows = await BuildRefundPromotionQuery(filter)
            .GroupBy(row => row.Currency)
            .Select(group => new CurrencyAggregateRow
            {
                Currency = group.Key,
                Amount = group.Sum(row => (decimal?)row.PromoAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var currencies = orderSummaryRows
            .Select(row => row.Currency)
            .Concat(orderPromotionRows.Select(row => row.Currency))
            .Concat(refundSummaryRows.Select(row => row.Currency))
            .Concat(refundCommissionRows.Select(row => row.Currency))
            .Concat(refundPromotionRows.Select(row => row.Currency))
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnsureSingleCurrency(currencies, "Super admin financial overview");

        var orderSummary = orderSummaryRows.SingleOrDefault();
        var refundSummary = refundSummaryRows.SingleOrDefault();
        var grossCommission = RoundCurrency(orderSummary?.Amount ?? 0m);
        var grossSales = RoundCurrency(orderSummary?.SecondaryAmount ?? 0m);
        var orderCount = orderSummary?.Count ?? 0;
        var promoCost = RoundCurrency((orderPromotionRows.SingleOrDefault()?.Amount ?? 0m) - (refundPromotionRows.SingleOrDefault()?.Amount ?? 0m));
        var refundImpact = RoundCurrency(refundCommissionRows.SingleOrDefault()?.Amount ?? 0m);
        var refundAmount = RoundCurrency(refundSummary?.Amount ?? 0m);
        var refundedOrdersCount = refundSummary?.DistinctCount ?? 0;
        var netCommission = RoundCurrency(grossCommission - promoCost);
        var netRevenue = RoundCurrency(netCommission - refundImpact);

        return new SuperAdminFinancialOverviewDto
        {
            FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
            ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            Currency = currencies.SingleOrDefault() ?? DefaultCurrency,
            BaseCurrencyCode = DefaultCurrency,
            ExchangeRateToBase = string.Equals(currencies.SingleOrDefault() ?? DefaultCurrency, DefaultCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : null,
            TotalGrossCommission = grossCommission,
            TotalPromoCost = promoCost,
            TotalNetCommission = netCommission,
            TotalRefundAmount = refundAmount,
            RefundImpactOnRevenue = refundImpact,
            NetRevenue = netRevenue,
            TotalOrdersCount = orderCount,
            AverageOrderValue = orderCount == 0 ? 0m : RoundCurrency(grossSales / orderCount),
            RefundedOrdersCount = refundedOrdersCount,
            RefundRate = orderCount == 0 ? 0m : RoundRatio(refundedOrdersCount / (decimal)orderCount),
            OrderScope = "SubOrder",
            AppliedVendorFilter = filter.VendorSearch,
            AppliedPromoCode = filter.PromoCode,
            DataSource = "LIVE_LEDGER",
            IsEventuallyConsistent = true,
            ConsistencyModel = DefaultConsistencyModel,
            DataFreshnessNote = DefaultFreshnessNote,
            GeneratedAtUtc = generatedAtUtc,
            AggregatedThroughUtc = filter.SnapshotBoundaryUtc,
            FinancialHealth = BuildFinancialHealth(reconciliationSnapshot),
            DataFreshness = BuildLiveDataFreshness(filter, generatedAtUtc),
            IntegrityPanel = BuildIntegrityPanel(reconciliationSnapshot)
        };
    }

    public async Task<SuperAdminFinancialTrendsDto> GetTrendsAsync(
        SuperAdminFinancialTrendsQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        query ??= new SuperAdminFinancialTrendsQueryDto();

        var generatedAtUtc = DateTime.UtcNow;
        var filter = await NormalizeFilterAsync(query, defaultToLast30Days: true, generatedAtUtc, cancellationToken);
        var buckets = InitializeTrendBuckets(filter, query.GroupBy);
        if (filter.HasVendorFilter && filter.VendorIds.Count == 0)
        {
            return new SuperAdminFinancialTrendsDto
            {
                FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
                ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
                SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
                Currency = DefaultCurrency,
                BaseCurrencyCode = DefaultCurrency,
                ExchangeRateToBase = 1m,
                GroupBy = query.GroupBy.ToString(),
                DataSource = "LIVE_LEDGER",
                IsEventuallyConsistent = true,
                ConsistencyModel = DefaultConsistencyModel,
                DataFreshnessNote = DefaultFreshnessNote,
                GeneratedAtUtc = generatedAtUtc,
                AggregatedThroughUtc = filter.SnapshotBoundaryUtc,
                DataFreshness = BuildLiveDataFreshness(filter, generatedAtUtc),
                Points = buckets.Values
                    .OrderBy(point => point.BucketStartUtc)
                    .Select(MapTrendPoint)
                    .ToList()
            };
        }

        var aggregateWindow = await TryGetAggregateWindowAsync(filter, cancellationToken);
        if (aggregateWindow != null)
        {
            _logger.LogInformation(
                "Super admin financial trends served from daily aggregates | FromDateUtc: {FromDateUtc} | ToDateUtc: {ToDateUtc} | GroupBy: {GroupBy}",
                filter.FromDateUtc,
                filter.ToDateUtc,
                query.GroupBy);

            return BuildTrendsFromAggregateWindow(filter, query.GroupBy, aggregateWindow, generatedAtUtc);
        }

        var grossRows = await GroupTemporalMetricsAsync(
            BuildOrderCommissionQuery(filter).Select(row => new TemporalMetricRow
            {
                OccurredAtUtc = row.CreatedAtUtc,
                Currency = row.Currency,
                Amount = row.GrossCommissionAmount,
                SecondaryAmount = 0m,
                Count = 1
            }),
            query.GroupBy,
            cancellationToken);

        var orderPromoRows = await GroupTemporalMetricsAsync(
            BuildOrderPromotionQuery(filter).Select(row => new TemporalMetricRow
            {
                OccurredAtUtc = row.CreatedAtUtc,
                Currency = row.Currency,
                Amount = row.PromoAmount,
                SecondaryAmount = 0m,
                Count = 0
            }),
            query.GroupBy,
            cancellationToken);

        var refundPromoRows = await GroupTemporalMetricsAsync(
            BuildRefundPromotionQuery(filter).Select(row => new TemporalMetricRow
            {
                OccurredAtUtc = row.CreatedAtUtc,
                Currency = row.Currency,
                Amount = row.PromoAmount,
                SecondaryAmount = 0m,
                Count = 0
            }),
            query.GroupBy,
            cancellationToken);

        var refundCommissionRows = await GroupTemporalMetricsAsync(
            BuildRefundCommissionQuery(filter).Select(row => new TemporalMetricRow
            {
                OccurredAtUtc = row.CreatedAtUtc,
                Currency = row.Currency,
                Amount = row.CommissionAmount,
                SecondaryAmount = 0m,
                Count = 0
            }),
            query.GroupBy,
            cancellationToken);

        var refundAmountRows = await GroupTemporalMetricsAsync(
            BuildRefundMetricQuery(filter).Select(row => new TemporalMetricRow
            {
                OccurredAtUtc = row.OccurredAtUtc,
                Currency = row.Currency,
                Amount = row.RefundAmount,
                SecondaryAmount = 0m,
                Count = 1
            }),
            query.GroupBy,
            cancellationToken);

        var currencies = grossRows
            .Select(row => row.Currency)
            .Concat(orderPromoRows.Select(row => row.Currency))
            .Concat(refundPromoRows.Select(row => row.Currency))
            .Concat(refundCommissionRows.Select(row => row.Currency))
            .Concat(refundAmountRows.Select(row => row.Currency))
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnsureSingleCurrency(currencies, "Super admin financial trends");

        foreach (var row in grossRows)
        {
            var bucket = buckets[ToBucketStartUtc(row)];
            bucket.GrossCommission += row.Amount;
            bucket.OrdersCount += row.Count;
        }

        foreach (var row in orderPromoRows)
        {
            buckets[ToBucketStartUtc(row)].PromoCost += row.Amount;
        }

        foreach (var row in refundPromoRows)
        {
            buckets[ToBucketStartUtc(row)].PromoCost -= row.Amount;
        }

        foreach (var row in refundCommissionRows)
        {
            buckets[ToBucketStartUtc(row)].CommissionReversals += row.Amount;
        }

        foreach (var row in refundAmountRows)
        {
            buckets[ToBucketStartUtc(row)].RefundAmount += row.Amount;
        }

        return new SuperAdminFinancialTrendsDto
        {
            FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
            ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            Currency = currencies.SingleOrDefault() ?? DefaultCurrency,
            BaseCurrencyCode = DefaultCurrency,
            ExchangeRateToBase = string.Equals(currencies.SingleOrDefault() ?? DefaultCurrency, DefaultCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : null,
            GroupBy = query.GroupBy.ToString(),
            DataSource = "LIVE_LEDGER",
            IsEventuallyConsistent = true,
            ConsistencyModel = DefaultConsistencyModel,
            DataFreshnessNote = DefaultFreshnessNote,
            GeneratedAtUtc = generatedAtUtc,
            AggregatedThroughUtc = filter.SnapshotBoundaryUtc,
            DataFreshness = BuildLiveDataFreshness(filter, generatedAtUtc),
            Points = buckets.Values
                .OrderBy(point => point.BucketStartUtc)
                .Select(MapTrendPoint)
                .ToList()
        };
    }

    public async Task<SuperAdminPagedResult<SuperAdminPromoAnalyticsItemDto>> GetPromoAnalyticsAsync(
        SuperAdminPromoAnalyticsQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        query ??= new SuperAdminPromoAnalyticsQueryDto();

        var filter = await NormalizeFilterAsync(query, defaultToLast30Days: true, DateTime.UtcNow, cancellationToken);
        if (filter.HasVendorFilter && filter.VendorIds.Count == 0)
        {
            return EmptyPagedResult<SuperAdminPromoAnalyticsItemDto>(query.Page, query.PageSize);
        }

        var usageRows = await BuildPromoUsageQuery(filter)
            .GroupBy(row => new
            {
                row.PromoCodeId,
                row.PromoCode,
                row.IsActive,
                row.Currency
            })
            .Select(group => new PromoAggregateRow
            {
                PromoCodeId = group.Key.PromoCodeId,
                PromoCode = group.Key.PromoCode,
                IsActive = group.Key.IsActive,
                Currency = group.Key.Currency,
                UsageCount = group.Select(row => row.MarketplaceOrderId).Distinct().Count(),
                LastUsedAtUtc = group.Max(row => (DateTime?)row.AppliedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var orderRevenueRows = await BuildOrderCommissionQuery(filter)
            .Where(row => row.PromoCode != null && row.PromoCode != string.Empty)
            .GroupBy(row => new { row.PromoCodeId, row.PromoCode, row.Currency })
            .Select(group => new PromoAggregateRow
            {
                PromoCodeId = group.Key.PromoCodeId,
                PromoCode = group.Key.PromoCode ?? string.Empty,
                Currency = group.Key.Currency,
                RevenueGenerated = group.Sum(row => (decimal?)row.GrossCommissionAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var orderPromoCostRows = await BuildOrderPromotionQuery(filter)
            .Where(row => row.PromoCode != null && row.PromoCode != string.Empty)
            .GroupBy(row => new { row.PromoCodeId, row.PromoCode, row.Currency })
            .Select(group => new PromoAggregateRow
            {
                PromoCodeId = group.Key.PromoCodeId,
                PromoCode = group.Key.PromoCode ?? string.Empty,
                Currency = group.Key.Currency,
                PromoCost = group.Sum(row => (decimal?)row.PromoAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var refundPromotionRows = await BuildRefundPromotionQuery(filter)
            .Where(row => row.PromoCode != null && row.PromoCode != string.Empty)
            .GroupBy(row => new { row.PromoCodeId, row.PromoCode, row.Currency })
            .Select(group => new PromoAggregateRow
            {
                PromoCodeId = group.Key.PromoCodeId,
                PromoCode = group.Key.PromoCode ?? string.Empty,
                Currency = group.Key.Currency,
                PromoReversal = group.Sum(row => (decimal?)row.PromoAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var refundCommissionRows = await BuildRefundCommissionQuery(filter)
            .Where(row => row.PromoCode != null && row.PromoCode != string.Empty)
            .GroupBy(row => new { row.PromoCodeId, row.PromoCode, row.Currency })
            .Select(group => new PromoAggregateRow
            {
                PromoCodeId = group.Key.PromoCodeId,
                PromoCode = group.Key.PromoCode ?? string.Empty,
                Currency = group.Key.Currency,
                CommissionReversals = group.Sum(row => (decimal?)row.CommissionAmount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var accumulators = new Dictionary<PromoAggregateKey, PromoMetricAccumulator>();
        MergePromoRows(accumulators, usageRows);
        MergePromoRows(accumulators, orderRevenueRows);
        MergePromoRows(accumulators, orderPromoCostRows);
        MergePromoRows(accumulators, refundPromotionRows);
        MergePromoRows(accumulators, refundCommissionRows);

        await PopulatePromoDefinitionsAsync(accumulators.Values, cancellationToken);

        var items = accumulators.Values
            .Select(accumulator => new SuperAdminPromoAnalyticsItemDto
            {
                PromoCodeId = accumulator.PromoCodeId,
                PromoCode = accumulator.PromoCode,
                IsActive = accumulator.IsActive,
                TotalUsageCount = accumulator.TotalUsageCount,
                TotalPromoCost = RoundCurrency(accumulator.TotalPromoCost - accumulator.PromoReversal),
                RevenueGenerated = RoundCurrency(accumulator.RevenueGenerated),
                CommissionReversals = RoundCurrency(accumulator.CommissionReversals),
                NetImpact = RoundCurrency(
                    accumulator.RevenueGenerated -
                    (accumulator.TotalPromoCost - accumulator.PromoReversal) -
                    accumulator.CommissionReversals),
                AverageDiscountPerUse = accumulator.TotalUsageCount == 0
                    ? 0m
                    : RoundCurrency((accumulator.TotalPromoCost - accumulator.PromoReversal) / accumulator.TotalUsageCount),
                ConversionRate = null,
                LastUsedAtUtc = accumulator.LastUsedAtUtc,
                Currency = accumulator.Currency
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.PromoCode))
            .ToList();

        var sortedItems = SortPromoItems(items, query).ToList();
        return ToPagedResult(sortedItems, query.Page, query.PageSize);
    }

    public async Task<SuperAdminPagedResult<SuperAdminVendorPerformanceItemDto>> GetVendorPerformanceAsync(
        SuperAdminVendorPerformanceQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        query ??= new SuperAdminVendorPerformanceQueryDto();

        var filter = await NormalizeFilterAsync(query, defaultToLast30Days: true, DateTime.UtcNow, cancellationToken);
        if (filter.HasVendorFilter && filter.VendorIds.Count == 0)
        {
            return EmptyPagedResult<SuperAdminVendorPerformanceItemDto>(query.Page, query.PageSize);
        }

        var orderRows = await BuildOrderCommissionQuery(filter)
            .GroupBy(row => new
            {
                row.VendorId,
                row.VendorName,
                row.VendorSlug,
                row.ContactEmail,
                row.Currency
            })
            .Select(group => new VendorAggregateRow
            {
                VendorId = group.Key.VendorId,
                VendorName = group.Key.VendorName,
                VendorSlug = group.Key.VendorSlug,
                ContactEmail = group.Key.ContactEmail,
                Currency = group.Key.Currency,
                OrdersCount = group.Count(),
                TotalSales = group.Sum(row => (decimal?)row.OrderTotal) ?? 0m,
                GrossCommission = group.Sum(row => (decimal?)row.GrossCommissionAmount) ?? 0m,
                LastOccurredAtUtc = group.Max(row => (DateTime?)row.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var orderPromoRows = await BuildOrderPromotionQuery(filter)
            .GroupBy(row => new
            {
                row.VendorId,
                row.VendorName,
                row.VendorSlug,
                row.ContactEmail,
                row.Currency
            })
            .Select(group => new VendorAggregateRow
            {
                VendorId = group.Key.VendorId,
                VendorName = group.Key.VendorName,
                VendorSlug = group.Key.VendorSlug,
                ContactEmail = group.Key.ContactEmail,
                Currency = group.Key.Currency,
                PromoCost = group.Sum(row => (decimal?)row.PromoAmount) ?? 0m,
                LastOccurredAtUtc = group.Max(row => (DateTime?)row.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var refundRows = await BuildRefundMetricQuery(filter)
            .GroupBy(row => new
            {
                row.VendorId,
                row.VendorName,
                row.VendorSlug,
                row.ContactEmail,
                row.Currency
            })
            .Select(group => new VendorAggregateRow
            {
                VendorId = group.Key.VendorId,
                VendorName = group.Key.VendorName,
                VendorSlug = group.Key.VendorSlug,
                ContactEmail = group.Key.ContactEmail,
                Currency = group.Key.Currency,
                RefundedOrdersCount = group.Select(row => row.SubOrderId).Distinct().Count(),
                TotalRefundAmount = group.Sum(row => (decimal?)row.RefundAmount) ?? 0m,
                LastOccurredAtUtc = group.Max(row => (DateTime?)row.OccurredAtUtc)
            })
            .ToListAsync(cancellationToken);

        var refundCommissionRows = await BuildRefundCommissionQuery(filter)
            .GroupBy(row => new
            {
                row.VendorId,
                row.VendorName,
                row.VendorSlug,
                row.ContactEmail,
                row.Currency
            })
            .Select(group => new VendorAggregateRow
            {
                VendorId = group.Key.VendorId,
                VendorName = group.Key.VendorName,
                VendorSlug = group.Key.VendorSlug,
                ContactEmail = group.Key.ContactEmail,
                Currency = group.Key.Currency,
                CommissionReversals = group.Sum(row => (decimal?)row.CommissionAmount) ?? 0m,
                LastOccurredAtUtc = group.Max(row => (DateTime?)row.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var refundPromotionRows = await BuildRefundPromotionQuery(filter)
            .GroupBy(row => new
            {
                row.VendorId,
                row.VendorName,
                row.VendorSlug,
                row.ContactEmail,
                row.Currency
            })
            .Select(group => new VendorAggregateRow
            {
                VendorId = group.Key.VendorId,
                VendorName = group.Key.VendorName,
                VendorSlug = group.Key.VendorSlug,
                ContactEmail = group.Key.ContactEmail,
                Currency = group.Key.Currency,
                PromoReversal = group.Sum(row => (decimal?)row.PromoAmount) ?? 0m,
                LastOccurredAtUtc = group.Max(row => (DateTime?)row.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        var accumulators = new Dictionary<VendorAggregateKey, VendorMetricAccumulator>();
        MergeVendorRows(accumulators, orderRows);
        MergeVendorRows(accumulators, orderPromoRows);
        MergeVendorRows(accumulators, refundRows);
        MergeVendorRows(accumulators, refundCommissionRows);
        MergeVendorRows(accumulators, refundPromotionRows);

        var items = accumulators.Values
            .Select(accumulator =>
            {
                var promoCost = RoundCurrency(accumulator.PromoCost - accumulator.PromoReversal);
                var refundRate = accumulator.OrdersCount == 0
                    ? 0m
                    : RoundRatio(accumulator.RefundedOrdersCount / (decimal)accumulator.OrdersCount);

                return new SuperAdminVendorPerformanceItemDto
                {
                    VendorId = accumulator.VendorId,
                    VendorName = accumulator.VendorName,
                    VendorSlug = accumulator.VendorSlug,
                    ContactEmail = accumulator.ContactEmail,
                    OrdersCount = accumulator.OrdersCount,
                    TotalSales = RoundCurrency(accumulator.TotalSales),
                    GrossCommission = RoundCurrency(accumulator.GrossCommission),
                    PromoCost = promoCost,
                    CommissionReversals = RoundCurrency(accumulator.CommissionReversals),
                    TotalRefundAmount = RoundCurrency(accumulator.TotalRefundAmount),
                    RefundedOrdersCount = accumulator.RefundedOrdersCount,
                    RefundRate = refundRate,
                    NetContribution = RoundCurrency(
                        accumulator.GrossCommission -
                        promoCost -
                        accumulator.CommissionReversals),
                    Currency = accumulator.Currency,
                    LastOrderAtUtc = accumulator.LastOccurredAtUtc
                };
            })
            .ToList();

        var sortedItems = SortVendorItems(items, query).ToList();
        return ToPagedResult(sortedItems, query.Page, query.PageSize);
    }

    public async Task<SuperAdminRefundAnalysisDto> GetRefundAnalysisAsync(
        SuperAdminRefundAnalysisQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        query ??= new SuperAdminRefundAnalysisQueryDto();

        var generatedAtUtc = DateTime.UtcNow;
        var filter = await NormalizeFilterAsync(query, defaultToLast30Days: true, generatedAtUtc, cancellationToken);
        var trendBuckets = InitializeRefundBuckets(filter, query.GroupBy);
        if (filter.HasVendorFilter && filter.VendorIds.Count == 0)
        {
            return new SuperAdminRefundAnalysisDto
            {
                FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
                ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
                SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
                Currency = DefaultCurrency,
                BaseCurrencyCode = DefaultCurrency,
                ExchangeRateToBase = 1m,
                GroupBy = query.GroupBy.ToString(),
                DataSource = "LIVE_LEDGER",
                IsEventuallyConsistent = true,
                ConsistencyModel = DefaultConsistencyModel,
                DataFreshnessNote = DefaultFreshnessNote,
                GeneratedAtUtc = generatedAtUtc,
                AggregatedThroughUtc = filter.SnapshotBoundaryUtc,
                DataFreshness = BuildLiveDataFreshness(filter, generatedAtUtc),
                Points = trendBuckets.Values.OrderBy(point => point.BucketStartUtc).Select(MapRefundPoint).ToList()
            };
        }

        var aggregateWindow = await TryGetAggregateWindowAsync(filter, cancellationToken);
        AggregateRefundSnapshot? aggregateSnapshot = null;
        if (aggregateWindow != null)
        {
            _logger.LogInformation(
                "Super admin financial refunds served from daily aggregates | FromDateUtc: {FromDateUtc} | ToDateUtc: {ToDateUtc} | GroupBy: {GroupBy}",
                filter.FromDateUtc,
                filter.ToDateUtc,
                query.GroupBy);

            aggregateSnapshot = BuildRefundSnapshotFromAggregateWindow(filter, query.GroupBy, aggregateWindow);
        }

        var orderCount = aggregateSnapshot?.OrdersCount ?? await BuildOrderCommissionQuery(filter).CountAsync(cancellationToken);

        List<CurrencyAggregateRow> refundSummaryRows;
        List<BucketAggregateRow> refundTrendRows;
        if (aggregateSnapshot == null)
        {
            refundSummaryRows = await BuildRefundMetricQuery(filter)
                .GroupBy(row => row.Currency)
                .Select(group => new CurrencyAggregateRow
                {
                    Currency = group.Key,
                    Amount = group.Sum(row => (decimal?)row.RefundAmount) ?? 0m,
                    SecondaryAmount = group.Sum(row => (decimal?)row.PromoDiscountReversalAmount) ?? 0m,
                    Count = group.Count(),
                    DistinctCount = group.Select(row => row.SubOrderId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            refundTrendRows = await GroupTemporalMetricsAsync(
                BuildRefundMetricQuery(filter).Select(row => new TemporalMetricRow
                {
                    OccurredAtUtc = row.OccurredAtUtc,
                    Currency = row.Currency,
                    Amount = row.RefundAmount,
                    SecondaryAmount = row.PromoDiscountReversalAmount,
                    Count = 1
                }),
                query.GroupBy,
                cancellationToken);
        }
        else
        {
            refundSummaryRows = new List<CurrencyAggregateRow>();
            refundTrendRows = new List<BucketAggregateRow>();
        }

        var topVendorRows = await BuildRefundMetricQuery(filter)
            .GroupBy(row => new
            {
                row.VendorId,
                row.VendorName,
                row.Currency
            })
            .Select(group => new RefundVendorAggregateRow
            {
                VendorId = group.Key.VendorId,
                VendorName = group.Key.VendorName,
                Currency = group.Key.Currency,
                RefundAmount = group.Sum(row => (decimal?)row.RefundAmount) ?? 0m,
                RefundCount = group.Count(),
                PromoReversalAmount = group.Sum(row => (decimal?)row.PromoDiscountReversalAmount) ?? 0m
            })
            .OrderByDescending(row => row.RefundAmount)
            .ThenBy(row => row.VendorName)
            .ThenBy(row => row.VendorId)
            .Take(5)
            .ToListAsync(cancellationToken);

        var topItemRows = await BuildRefundItemQuery(filter)
            .GroupBy(row => new
            {
                row.OrderItemId,
                row.SubOrderId,
                row.ProductName,
                row.VariantName,
                row.VendorName,
                row.Currency
            })
            .Select(group => new RefundItemAggregateRow
            {
                OrderItemId = group.Key.OrderItemId,
                SubOrderId = group.Key.SubOrderId,
                ProductName = group.Key.ProductName,
                VariantName = group.Key.VariantName,
                VendorName = group.Key.VendorName,
                Currency = group.Key.Currency,
                RefundAmount = group.Sum(row => (decimal?)row.RefundAmount) ?? 0m,
                RefundCount = group.Count(),
                RefundedQuantity = group.Sum(row => (int?)row.Quantity) ?? 0
            })
            .OrderByDescending(row => row.RefundAmount)
            .ThenBy(row => row.ProductName)
            .ThenBy(row => row.SubOrderId)
            .ThenBy(row => row.OrderItemId)
            .Take(8)
            .ToListAsync(cancellationToken);

        string currency;
        string baseCurrencyCode;
        decimal? exchangeRateToBase;
        IReadOnlyList<SuperAdminRefundTrendPointDto> points;
        int refundedOrdersCount;
        decimal refundRate;
        decimal totalRefundAmount;
        decimal totalPromoReversal;
        int totalRefundCount;
        string dataSource;
        DateTime? aggregatedThroughUtc;

        if (aggregateSnapshot == null)
        {
            var currencies = refundSummaryRows
                .Select(row => row.Currency)
                .Concat(refundTrendRows.Select(row => row.Currency))
                .Concat(topVendorRows.Select(row => row.Currency))
                .Concat(topItemRows.Select(row => row.Currency))
                .Where(currentCurrency => !string.IsNullOrWhiteSpace(currentCurrency))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            EnsureSingleCurrency(currencies, "Super admin refund analysis");

            foreach (var row in refundTrendRows)
            {
                var point = trendBuckets[ToBucketStartUtc(row)];
                point.RefundAmount += row.Amount;
                point.RefundCount += row.Count;
                point.PromoReversalAmount += row.SecondaryAmount;
            }

            points = trendBuckets.Values
                .OrderBy(point => point.BucketStartUtc)
                .Select(MapRefundPoint)
                .ToList();

            var refundSummary = refundSummaryRows.SingleOrDefault();
            refundedOrdersCount = refundSummary?.DistinctCount ?? 0;
            refundRate = orderCount == 0
                ? 0m
                : RoundRatio(refundedOrdersCount / (decimal)orderCount);
            totalRefundAmount = RoundCurrency(refundSummary?.Amount ?? 0m);
            totalPromoReversal = RoundCurrency(refundSummary?.SecondaryAmount ?? 0m);
            totalRefundCount = refundSummary?.Count ?? 0;
            currency = currencies.SingleOrDefault() ?? DefaultCurrency;
            baseCurrencyCode = DefaultCurrency;
            exchangeRateToBase = string.Equals(currency, DefaultCurrency, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : null;
            dataSource = "LIVE_LEDGER";
            aggregatedThroughUtc = filter.SnapshotBoundaryUtc;
        }
        else
        {
            points = aggregateSnapshot.Points;
            refundedOrdersCount = aggregateSnapshot.RefundedOrdersCount;
            refundRate = aggregateSnapshot.RefundRate;
            totalRefundAmount = aggregateSnapshot.TotalRefundAmount;
            totalPromoReversal = aggregateSnapshot.PromoReversalAmount;
            totalRefundCount = aggregateSnapshot.TotalRefundCount;
            currency = aggregateSnapshot.Currency;
            baseCurrencyCode = aggregateSnapshot.BaseCurrencyCode;
            exchangeRateToBase = aggregateSnapshot.ExchangeRateToBase;
            dataSource = "DAILY_AGGREGATE";
            aggregatedThroughUtc = aggregateSnapshot.AggregatedThroughUtc;
        }

        return new SuperAdminRefundAnalysisDto
        {
            FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
            ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            Currency = currency,
            BaseCurrencyCode = baseCurrencyCode,
            ExchangeRateToBase = exchangeRateToBase,
            GroupBy = query.GroupBy.ToString(),
            TotalRefundAmount = totalRefundAmount,
            TotalRefundCount = totalRefundCount,
            RefundedOrdersCount = refundedOrdersCount,
            RefundRate = refundRate,
            PromoReversalAmount = totalPromoReversal,
            DataSource = dataSource,
            IsEventuallyConsistent = true,
            ConsistencyModel = DefaultConsistencyModel,
            DataFreshnessNote = DefaultFreshnessNote,
            GeneratedAtUtc = generatedAtUtc,
            AggregatedThroughUtc = aggregatedThroughUtc,
            DataFreshness = dataSource == "DAILY_AGGREGATE" && aggregateWindow != null
                ? BuildAggregateDataFreshness(filter, aggregateWindow)
                : BuildLiveDataFreshness(filter, generatedAtUtc),
            Points = points,
            TopRefundedVendors = topVendorRows.Select(row => new SuperAdminRefundVendorItemDto
            {
                VendorId = row.VendorId,
                VendorName = row.VendorName,
                RefundAmount = RoundCurrency(row.RefundAmount),
                RefundCount = row.RefundCount,
                PromoReversalAmount = RoundCurrency(row.PromoReversalAmount),
                Currency = row.Currency
            }).ToList(),
            TopRefundedItems = topItemRows.Select(row => new SuperAdminRefundItemDto
            {
                OrderItemId = row.OrderItemId,
                SubOrderId = row.SubOrderId,
                ItemLabel = BuildItemLabel(row.ProductName, row.VariantName),
                VendorName = row.VendorName,
                RefundAmount = RoundCurrency(row.RefundAmount),
                RefundCount = row.RefundCount,
                RefundedQuantity = row.RefundedQuantity,
                Currency = row.Currency
            }).ToList(),
            Alerts = BuildRefundAlerts(points, refundRate, totalPromoReversal, totalRefundAmount)
        };
    }

    public async Task<SuperAdminFinancialIntegrityCheckDto> GetIntegrityCheckAsync(
        SuperAdminFinancialIntegrityCheckQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        query ??= new SuperAdminFinancialIntegrityCheckQueryDto();

        var generatedAtUtc = DateTime.UtcNow;
        var filter = await NormalizeFilterAsync(query, defaultToLast30Days: false, generatedAtUtc, cancellationToken);
        var isGlobalScope = !filter.HasDateFilter && !filter.HasVendorFilter && !filter.HasPromoFilter;
        if (filter.HasVendorFilter && filter.VendorIds.Count == 0)
        {
            return new SuperAdminFinancialIntegrityCheckDto
            {
                Status = "OK",
                ExecutedAtUtc = generatedAtUtc,
                SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
                ScopeDescription = $"No vendors matched '{filter.VendorSearch}', so no financial records were checked.",
                SupportsGlobalDebitCreditParity = false,
                DebitCreditParityStatus = "NOT_APPLICABLE",
                DebitCreditParityNote = "CommerceHub uses a bucketed append-only operational ledger. Global debit/credit equality is not a valid integrity signal for this model.",
                Issues = Array.Empty<SuperAdminFinancialIntegrityIssueDto>()
            };
        }

        var maxIssues = query.MaxIssues <= 0
            ? SuperAdminFinancialIntegrityCheckQueryDto.DefaultMaxIssues
            : Math.Min(query.MaxIssues, SuperAdminFinancialIntegrityCheckQueryDto.MaxAllowedIssues);

        var issues = new List<SuperAdminFinancialIntegrityIssueDto>();
        var orderMathCount = await BuildOrderMathMismatchQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildOrderMathMismatchQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "ORDER_MATH_MISMATCH",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var orderLedgerMismatchCount = await BuildOrderLedgerMismatchQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildOrderLedgerMismatchQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "ORDER_LEDGER_MISMATCH",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var promoUsageMismatchCount = await BuildPromoUsageMismatchQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildPromoUsageMismatchQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "WARNING",
                IssueType = "PROMO_USAGE_MISSING",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var promoUsageWithoutLedgerCount = await BuildPromoUsageWithoutLedgerQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildPromoUsageWithoutLedgerQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "PROMO_USAGE_WITHOUT_LEDGER",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var promoLedgerWithoutUsageCount = await BuildPromoLedgerWithoutUsageQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildPromoLedgerWithoutUsageQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "PROMO_LEDGER_WITHOUT_USAGE",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var duplicatePromoUsageCount = await BuildDuplicatePromoUsageQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildDuplicatePromoUsageQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "DUPLICATE_PROMO_USAGE",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var refundMathCount = await BuildRefundMathMismatchQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildRefundMathMismatchQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "REFUND_MATH_MISMATCH",
                ReferenceType = "Refund",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var refundLedgerMismatchCount = await BuildRefundLedgerMismatchQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildRefundLedgerMismatchQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "REFUND_LEDGER_MISMATCH",
                ReferenceType = "Refund",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var missingRefundReversalCount = await BuildMissingRefundReversalQuery(filter).CountAsync(cancellationToken);
        await AppendIssuesAsync(
            issues,
            BuildMissingRefundReversalQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "MISSING_REFUND_REVERSAL",
                ReferenceType = "Refund",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                ExpectedAmount = row.ExpectedAmount,
                ActualAmount = row.ActualAmount,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var orphanOrderCount = isGlobalScope || filter.HasDateFilter
            ? await BuildOrphanOrderLedgerQuery(filter).CountAsync(cancellationToken)
            : 0;
        await AppendIssuesAsync(
            issues,
            BuildOrphanOrderLedgerQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "ORPHAN_ORDER_LEDGER_ENTRY",
                ReferenceType = "Order",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var orphanRefundCount = isGlobalScope || filter.HasDateFilter
            ? await BuildOrphanRefundLedgerQuery(filter).CountAsync(cancellationToken)
            : 0;
        await AppendIssuesAsync(
            issues,
            BuildOrphanRefundLedgerQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "ORPHAN_REFUND_LEDGER_ENTRY",
                ReferenceType = "Refund",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var aggregateMismatchCount = !filter.HasVendorFilter && !filter.HasPromoFilter
            ? await BuildAggregateMismatchQuery(filter).CountAsync(cancellationToken)
            : 0;
        await AppendIssuesAsync(
            issues,
            BuildAggregateMismatchQuery(filter).OrderByDescending(row => row.OccurredAtUtc).ThenBy(row => row.ReferenceId),
            maxIssues,
            row => new SuperAdminFinancialIntegrityIssueDto
            {
                Severity = "ERROR",
                IssueType = "AGGREGATE_DRIFT",
                ReferenceType = "Aggregate",
                ReferenceId = row.ReferenceId,
                Summary = row.Summary,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc
            },
            cancellationToken);

        var orphanTransactionsCount = orphanOrderCount + orphanRefundCount;
        var orderFlowMismatchCount =
            orderMathCount +
            orderLedgerMismatchCount +
            promoUsageMismatchCount +
            promoUsageWithoutLedgerCount +
            promoLedgerWithoutUsageCount +
            duplicatePromoUsageCount;
        var refundFlowMismatchCount = refundMathCount + refundLedgerMismatchCount + missingRefundReversalCount;

        var scopeDescription = BuildIntegrityScopeDescription(filter, isGlobalScope);
        var executedAtUtc = DateTime.UtcNow;
        var missingIdempotencyKeyCount = 0;
        var missingCorrelationIdCount = 0;
        var duplicateEntryCount = 0;
        var missingPairCount = 0;
        var correlationIntegrityMismatchCount = 0;
        var payableMismatchCount = 0;
        var receivableMismatchCount = 0;
        var payoutFlowMismatchCount = 0;
        var adjustmentFlowMismatchCount = 0;
        var multiCurrencyReferenceCount = 0;
        var invalidEntityScopeCount = 0;

        if (isGlobalScope)
        {
            var snapshot = await _reconciliationReadService.GetSnapshotAsync(cancellationToken);
            executedAtUtc = snapshot.ExecutedAtUtc;
            missingIdempotencyKeyCount = snapshot.MissingIdempotencyKeyCount;
            missingCorrelationIdCount = snapshot.MissingCorrelationIdCount;
            duplicateEntryCount = snapshot.DuplicateEntryCount;
            missingPairCount = snapshot.MissingPairCount;
            correlationIntegrityMismatchCount = snapshot.CorrelationIntegrityMismatchCount;
            payableMismatchCount = snapshot.PayableMismatchCount;
            receivableMismatchCount = snapshot.ReceivableMismatchCount;
            payoutFlowMismatchCount = snapshot.PayoutFlowMismatchCount;
            adjustmentFlowMismatchCount = snapshot.AdjustmentFlowMismatchCount;
            multiCurrencyReferenceCount = snapshot.MultiCurrencyReferenceCount;
            invalidEntityScopeCount = snapshot.InvalidEntityScopeCount;
            orphanTransactionsCount = snapshot.OrphanTransactionsCount;
            orderFlowMismatchCount = snapshot.OrderFlowMismatchCount;
            refundFlowMismatchCount = snapshot.RefundFlowMismatchCount;
        }

        var totalIssueCount =
            orphanTransactionsCount +
            orderFlowMismatchCount +
            refundFlowMismatchCount +
            (isGlobalScope
                ? promoUsageWithoutLedgerCount +
                  promoLedgerWithoutUsageCount +
                  duplicatePromoUsageCount +
                  missingRefundReversalCount
                : 0) +
            payoutFlowMismatchCount +
            adjustmentFlowMismatchCount +
            payableMismatchCount +
            receivableMismatchCount +
            aggregateMismatchCount +
            correlationIntegrityMismatchCount +
            missingPairCount +
            duplicateEntryCount +
            missingIdempotencyKeyCount +
            missingCorrelationIdCount +
            multiCurrencyReferenceCount +
            invalidEntityScopeCount;

        return new SuperAdminFinancialIntegrityCheckDto
        {
            Status = totalIssueCount == 0 ? "OK" : "ISSUES_DETECTED",
            ExecutedAtUtc = executedAtUtc,
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            ScopeDescription = scopeDescription,
            SupportsGlobalDebitCreditParity = false,
            DebitCreditParityStatus = "NOT_APPLICABLE",
            DebitCreditParityNote = "CommerceHub uses an operational bucket ledger, so global debit and credit totals are not expected to balance like a full double-entry general ledger. Integrity is validated through reference-level order, refund, payout, receivable, and payable flow checks instead.",
            TotalIssueCount = totalIssueCount,
            OrphanTransactionsCount = orphanTransactionsCount,
            OrderFlowMismatchCount = orderFlowMismatchCount,
            RefundFlowMismatchCount = refundFlowMismatchCount,
            PayoutFlowMismatchCount = payoutFlowMismatchCount,
            AdjustmentFlowMismatchCount = adjustmentFlowMismatchCount,
            PayableMismatchCount = payableMismatchCount,
            ReceivableMismatchCount = receivableMismatchCount,
            PromoUsageWithoutLedgerCount = promoUsageWithoutLedgerCount,
            PromoLedgerWithoutUsageCount = promoLedgerWithoutUsageCount,
            DuplicatePromoUsageCount = duplicatePromoUsageCount,
            MissingRefundReversalCount = missingRefundReversalCount,
            CorrelationIntegrityMismatchCount = correlationIntegrityMismatchCount,
            MissingPairCount = missingPairCount,
            DuplicateEntryCount = duplicateEntryCount,
            MissingIdempotencyKeyCount = missingIdempotencyKeyCount,
            MissingCorrelationIdCount = missingCorrelationIdCount,
            MultiCurrencyReferenceCount = multiCurrencyReferenceCount,
            InvalidEntityScopeCount = invalidEntityScopeCount,
            AggregateMismatchCount = aggregateMismatchCount,
            Errors = GroupIntegrityIssues(issues, "ERROR"),
            Warnings = GroupIntegrityIssues(issues, "WARNING"),
            AffectedEntities = issues
                .Take(maxIssues)
                .Select(MapAffectedEntity)
                .ToList(),
            Issues = issues.Take(maxIssues).ToList()
        };
    }

    private IQueryable<OrderLedgerRow> BuildOrderCommissionQuery(DashboardFilterContext filter)
    {
        var query =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            join usage in _dbContext.PromoCodeUsages.AsNoTracking() on marketplaceOrder.Id equals usage.MarketplaceOrderId into usageJoin
            from usage in usageJoin.DefaultIfEmpty()
            join tenant in _dbContext.Tenants.AsNoTracking() on subOrder.StoreId equals tenant.Id into tenantJoin
            from tenant in tenantJoin.DefaultIfEmpty()
            where !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Commission
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
            select new OrderLedgerRow
            {
                ReferenceId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                VendorId = subOrder.StoreId,
                VendorName = tenant != null && tenant.Name != string.Empty ? tenant.Name : subOrder.StoreName,
                VendorSlug = tenant != null ? tenant.Slug : null,
                ContactEmail = tenant != null ? tenant.ContactEmail : null,
                PromoCodeId = usage != null ? usage.PromoCodeId : null,
                PromoCode = marketplaceOrder.PromoCode != null && marketplaceOrder.PromoCode != string.Empty
                    ? marketplaceOrder.PromoCode
                    : usage != null
                        ? usage.PromoCodeSnapshot
                        : null,
                Currency = transaction.Currency,
                CreatedAtUtc = transaction.CreatedAtUtc,
                OrderTotal = subOrder.Total,
                GrossCommissionAmount = transaction.Amount
            };

        return ApplyOrderFilters(query, filter);
    }

    private IQueryable<OrderPromotionRow> BuildOrderPromotionQuery(DashboardFilterContext filter)
    {
        var query =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            join usage in _dbContext.PromoCodeUsages.AsNoTracking() on marketplaceOrder.Id equals usage.MarketplaceOrderId into usageJoin
            from usage in usageJoin.DefaultIfEmpty()
            join tenant in _dbContext.Tenants.AsNoTracking() on subOrder.StoreId equals tenant.Id into tenantJoin
            from tenant in tenantJoin.DefaultIfEmpty()
            where !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
            select new OrderPromotionRow
            {
                ReferenceId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                VendorId = subOrder.StoreId,
                VendorName = tenant != null && tenant.Name != string.Empty ? tenant.Name : subOrder.StoreName,
                VendorSlug = tenant != null ? tenant.Slug : null,
                ContactEmail = tenant != null ? tenant.ContactEmail : null,
                PromoCodeId = usage != null ? usage.PromoCodeId : null,
                PromoCode = marketplaceOrder.PromoCode != null && marketplaceOrder.PromoCode != string.Empty
                    ? marketplaceOrder.PromoCode
                    : usage != null
                        ? usage.PromoCodeSnapshot
                        : null,
                Currency = transaction.Currency,
                CreatedAtUtc = transaction.CreatedAtUtc,
                PromoAmount = transaction.Amount
            };

        return ApplyOrderFilters(query, filter);
    }

    private IQueryable<RefundMetricRow> BuildRefundMetricQuery(DashboardFilterContext filter)
    {
        var query =
            from refund in _dbContext.CommerceHubRefunds.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            join usage in _dbContext.PromoCodeUsages.AsNoTracking() on marketplaceOrder.Id equals usage.MarketplaceOrderId into usageJoin
            from usage in usageJoin.DefaultIfEmpty()
            join tenant in _dbContext.Tenants.AsNoTracking() on subOrder.StoreId equals tenant.Id into tenantJoin
            from tenant in tenantJoin.DefaultIfEmpty()
            where !refund.IsDeleted
                  && !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && refund.Status == CommerceHubRefundStatus.Completed
            select new RefundMetricRow
            {
                RefundId = refund.Id,
                SubOrderId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                VendorId = subOrder.StoreId,
                VendorName = tenant != null && tenant.Name != string.Empty ? tenant.Name : subOrder.StoreName,
                VendorSlug = tenant != null ? tenant.Slug : null,
                ContactEmail = tenant != null ? tenant.ContactEmail : null,
                PromoCodeId = usage != null ? usage.PromoCodeId : null,
                PromoCode = marketplaceOrder.PromoCode != null && marketplaceOrder.PromoCode != string.Empty
                    ? marketplaceOrder.PromoCode
                    : usage != null
                        ? usage.PromoCodeSnapshot
                        : null,
                Currency = refund.Currency,
                RefundAmount = refund.Amount,
                PromoDiscountReversalAmount = refund.PromoDiscountReversalAmount,
                CommissionReversalAmount = refund.CommissionReversalAmount,
                OrderItemId = refund.OrderItemId,
                Quantity = refund.Quantity ?? 0,
                OccurredAtUtc = refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt
            };

        return ApplyRefundFilters(query, filter);
    }

    private IQueryable<RefundCommissionRow> BuildRefundCommissionQuery(DashboardFilterContext filter)
    {
        var query =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join refund in _dbContext.CommerceHubRefunds.AsNoTracking() on transaction.ReferenceId equals refund.Id
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            join usage in _dbContext.PromoCodeUsages.AsNoTracking() on marketplaceOrder.Id equals usage.MarketplaceOrderId into usageJoin
            from usage in usageJoin.DefaultIfEmpty()
            join tenant in _dbContext.Tenants.AsNoTracking() on subOrder.StoreId equals tenant.Id into tenantJoin
            from tenant in tenantJoin.DefaultIfEmpty()
            where !refund.IsDeleted
                  && !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && refund.Status == CommerceHubRefundStatus.Completed
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Commission
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund
            select new RefundCommissionRow
            {
                RefundId = refund.Id,
                SubOrderId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                VendorId = subOrder.StoreId,
                VendorName = tenant != null && tenant.Name != string.Empty ? tenant.Name : subOrder.StoreName,
                VendorSlug = tenant != null ? tenant.Slug : null,
                ContactEmail = tenant != null ? tenant.ContactEmail : null,
                PromoCodeId = usage != null ? usage.PromoCodeId : null,
                PromoCode = marketplaceOrder.PromoCode != null && marketplaceOrder.PromoCode != string.Empty
                    ? marketplaceOrder.PromoCode
                    : usage != null
                        ? usage.PromoCodeSnapshot
                        : null,
                Currency = transaction.Currency,
                CreatedAtUtc = transaction.CreatedAtUtc,
                CommissionAmount = transaction.Amount
            };

        return ApplyRefundLedgerFilters(query, filter);
    }

    private IQueryable<RefundPromotionRow> BuildRefundPromotionQuery(DashboardFilterContext filter)
    {
        var query =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join refund in _dbContext.CommerceHubRefunds.AsNoTracking() on transaction.ReferenceId equals refund.Id
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            join usage in _dbContext.PromoCodeUsages.AsNoTracking() on marketplaceOrder.Id equals usage.MarketplaceOrderId into usageJoin
            from usage in usageJoin.DefaultIfEmpty()
            join tenant in _dbContext.Tenants.AsNoTracking() on subOrder.StoreId equals tenant.Id into tenantJoin
            from tenant in tenantJoin.DefaultIfEmpty()
            where !refund.IsDeleted
                  && !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && refund.Status == CommerceHubRefundStatus.Completed
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund
            select new RefundPromotionRow
            {
                RefundId = refund.Id,
                SubOrderId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                VendorId = subOrder.StoreId,
                VendorName = tenant != null && tenant.Name != string.Empty ? tenant.Name : subOrder.StoreName,
                VendorSlug = tenant != null ? tenant.Slug : null,
                ContactEmail = tenant != null ? tenant.ContactEmail : null,
                PromoCodeId = usage != null ? usage.PromoCodeId : null,
                PromoCode = marketplaceOrder.PromoCode != null && marketplaceOrder.PromoCode != string.Empty
                    ? marketplaceOrder.PromoCode
                    : usage != null
                        ? usage.PromoCodeSnapshot
                        : null,
                Currency = transaction.Currency,
                CreatedAtUtc = transaction.CreatedAtUtc,
                PromoAmount = transaction.Amount
            };

        return ApplyRefundLedgerFilters(query, filter);
    }

    private IQueryable<PromoUsageRow> BuildPromoUsageQuery(DashboardFilterContext filter)
    {
        var financiallyRecognizedMarketplaceOrderIds =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
            where !subOrder.IsDeleted
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Commission
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
            select subOrder.MarketplaceOrderId;

        var query =
            from usage in _dbContext.PromoCodeUsages.AsNoTracking()
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on usage.MarketplaceOrderId equals marketplaceOrder.Id
            join promo in _dbContext.PromoCodes.AsNoTracking() on usage.PromoCodeId equals promo.Id into promoJoin
            from promo in promoJoin.DefaultIfEmpty()
            where !usage.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && marketplaceOrder.Status != OrderStatus.Cancelled
                  && marketplaceOrder.PaymentStatus != PaymentStatus.Failed
                  && marketplaceOrder.PaymentStatus != PaymentStatus.Rejected
                  && financiallyRecognizedMarketplaceOrderIds.Contains(usage.MarketplaceOrderId)
            select new PromoUsageRow
            {
                PromoCodeId = usage.PromoCodeId,
                PromoCode = promo != null && promo.Code != string.Empty
                    ? promo.Code
                    : usage.PromoCodeSnapshot,
                IsActive = promo != null && promo.IsActive && !promo.IsDeleted,
                MarketplaceOrderId = usage.MarketplaceOrderId,
                Currency = marketplaceOrder.Currency,
                AppliedAtUtc = usage.AppliedAtUtc
            };

        if (filter.HasVendorFilter)
        {
            if (filter.VendorIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            query = query.Where(row =>
                _dbContext.CommerceHubSubOrders.AsNoTracking().Any(subOrder =>
                    !subOrder.IsDeleted &&
                    subOrder.MarketplaceOrderId == row.MarketplaceOrderId &&
                    filter.VendorIds.Contains(subOrder.StoreId)));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.AppliedAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.AppliedAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private IQueryable<RefundItemQueryRow> BuildRefundItemQuery(DashboardFilterContext filter)
    {
        return
            from refund in BuildRefundMetricQuery(filter)
            join item in _dbContext.CommerceHubSubOrderItems.AsNoTracking() on refund.OrderItemId equals item.Id into itemJoin
            from item in itemJoin.DefaultIfEmpty()
            select new RefundItemQueryRow
            {
                OrderItemId = refund.OrderItemId,
                SubOrderId = refund.SubOrderId,
                ProductName = item != null ? item.ProductName : "Order item",
                VariantName = item != null ? item.VariantName : null,
                VendorName = refund.VendorName,
                Currency = refund.Currency,
                RefundAmount = refund.RefundAmount,
                Quantity = refund.Quantity
            };
    }

    private IQueryable<IntegrityIssueSeedRow> BuildOrderMathMismatchQuery(DashboardFilterContext filter)
    {
        return BuildIntegritySubOrderScope(filter)
            .Where(row =>
                row.PromoDiscountAmount > row.PlatformCommissionAmount ||
                row.FinalPlatformCommissionAmount != row.PlatformCommissionAmount - row.PromoDiscountAmount ||
                row.VendorEarningAmount != row.Total - row.PlatformCommissionAmount)
            .Select(row => new IntegrityIssueSeedRow
            {
                ReferenceId = row.SubOrderId,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc,
                ExpectedAmount = row.PlatformCommissionAmount - row.PromoDiscountAmount,
                ActualAmount = row.FinalPlatformCommissionAmount,
                Summary =
                    row.PromoDiscountAmount > row.PlatformCommissionAmount
                        ? "Promo discount exceeds gross platform commission."
                        : row.FinalPlatformCommissionAmount != row.PlatformCommissionAmount - row.PromoDiscountAmount
                            ? "Final platform commission does not equal gross commission minus promo discount."
                            : "Vendor earning does not reconcile to order total minus gross commission."
            });
    }

    private IQueryable<IntegrityIssueSeedRow> BuildOrderLedgerMismatchQuery(DashboardFilterContext filter)
    {
        return
            from row in BuildIntegritySubOrderScope(filter)
            let ledgerGrossCommission = _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                    transaction.ReferenceId == row.SubOrderId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            let ledgerPromoCost = _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                    transaction.ReferenceId == row.SubOrderId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            where ledgerGrossCommission != row.PlatformCommissionAmount ||
                  ledgerPromoCost != row.PromoDiscountAmount
            select new IntegrityIssueSeedRow
            {
                ReferenceId = row.SubOrderId,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc,
                ExpectedAmount = row.PlatformCommissionAmount,
                ActualAmount = ledgerGrossCommission,
                Summary = ledgerGrossCommission != row.PlatformCommissionAmount
                    ? "Platform commission ledger entries do not match the sub-order gross commission."
                    : "Platform promotion ledger entries do not match the sub-order promo discount."
            };
    }

    private IQueryable<IntegrityIssueSeedRow> BuildPromoUsageMismatchQuery(DashboardFilterContext filter)
    {
        var query =
            from marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking()
            where !marketplaceOrder.IsDeleted
                  && marketplaceOrder.PromoCode != null
                  && marketplaceOrder.PromoCode != string.Empty
                  && marketplaceOrder.PromoDiscountTotal > 0m
                  && (!filter.HasPromoFilter || marketplaceOrder.PromoCode == filter.PromoCode)
                  && _dbContext.CommerceHubSubOrders.AsNoTracking().Any(subOrder =>
                      !subOrder.IsDeleted &&
                      subOrder.MarketplaceOrderId == marketplaceOrder.Id &&
                      (!filter.HasVendorFilter || filter.VendorIds.Contains(subOrder.StoreId)))
                  && !_dbContext.PromoCodeUsages.AsNoTracking().Any(usage =>
                      !usage.IsDeleted &&
                      usage.MarketplaceOrderId == marketplaceOrder.Id)
            select new IntegrityIssueSeedRow
            {
                ReferenceId = marketplaceOrder.Id,
                Currency = marketplaceOrder.Currency,
                OccurredAtUtc = marketplaceOrder.CreatedAt,
                Summary = "Marketplace order carries promo discount totals but has no immutable promo usage record."
            };

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private IQueryable<IntegrityIssueSeedRow> BuildPromoUsageWithoutLedgerQuery(DashboardFilterContext filter)
    {
        var financiallyRecognizedMarketplaceOrderIds =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
            where !subOrder.IsDeleted
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Commission
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
            select subOrder.MarketplaceOrderId;

        var query =
            from usage in _dbContext.PromoCodeUsages.AsNoTracking()
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on usage.MarketplaceOrderId equals marketplaceOrder.Id
            where !usage.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && (
                      financiallyRecognizedMarketplaceOrderIds.Contains(usage.MarketplaceOrderId) ||
                      marketplaceOrder.Status == OrderStatus.Cancelled ||
                      marketplaceOrder.PaymentStatus == PaymentStatus.Failed ||
                      marketplaceOrder.PaymentStatus == PaymentStatus.Rejected)
                  && (!filter.HasPromoFilter ||
                      marketplaceOrder.PromoCode == filter.PromoCode ||
                      usage.PromoCodeSnapshot == filter.PromoCode)
                  && (!filter.HasVendorFilter ||
                      _dbContext.CommerceHubSubOrders.AsNoTracking().Any(subOrder =>
                          !subOrder.IsDeleted &&
                          subOrder.MarketplaceOrderId == marketplaceOrder.Id &&
                          filter.VendorIds.Contains(subOrder.StoreId)))
                  && (!filter.FromDateUtc.HasValue || usage.AppliedAtUtc >= filter.FromDateUtc.Value)
                  && (!filter.ToDateUtc.HasValue || usage.AppliedAtUtc <= filter.ToDateUtc.Value)
                  && !_dbContext.CommerceHubFinancialTransactions.AsNoTracking().Any(transaction =>
                      transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                      transaction.EntityId == null &&
                      transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                      transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                      transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                      _dbContext.CommerceHubSubOrders.AsNoTracking().Any(subOrder =>
                          !subOrder.IsDeleted &&
                          subOrder.Id == transaction.ReferenceId &&
                          subOrder.MarketplaceOrderId == marketplaceOrder.Id))
            group usage by new
            {
                usage.MarketplaceOrderId,
                marketplaceOrder.Currency
            }
            into grouped
            select new IntegrityIssueSeedRow
            {
                ReferenceId = grouped.Key.MarketplaceOrderId,
                Currency = grouped.Key.Currency,
                OccurredAtUtc = grouped.Max(row => (DateTime?)row.AppliedAtUtc),
                ExpectedAmount = grouped.Sum(row => (decimal?)row.DiscountAmount) ?? 0m,
                ActualAmount = 0m,
                Summary = "Promo usage exists but no platform promotion ledger debit was recorded for the marketplace order."
            };

        return query;
    }

    private IQueryable<IntegrityIssueSeedRow> BuildAggregateMismatchQuery(DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter || filter.HasPromoFilter)
        {
            return _dbContext.CommerceHubDailyFinancialAggregates
                .AsNoTracking()
                .Where(_ => false)
                .Select(_ => new IntegrityIssueSeedRow());
        }

        var fromDateUtc = filter.FromDateUtc?.Date;
        var toDateUtc = filter.ToDateUtc?.Date;

        return _dbContext.CommerceHubDailyFinancialAggregates
            .AsNoTracking()
            .Where(row =>
                !row.IsValid &&
                (!fromDateUtc.HasValue || row.DateUtc >= fromDateUtc.Value) &&
                (!toDateUtc.HasValue || row.DateUtc <= toDateUtc.Value))
            .Select(row => new IntegrityIssueSeedRow
            {
                ReferenceId = null,
                Currency = row.CurrencyCode,
                OccurredAtUtc = row.LastValidatedAtUtc ?? row.CalculatedAtUtc,
                Summary = row.ValidationError ?? "Aggregate row is invalid and should not be used for dashboard reads."
            });
    }

    private IQueryable<IntegrityIssueSeedRow> BuildPromoLedgerWithoutUsageQuery(DashboardFilterContext filter)
    {
        return
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            where !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                  && transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion
                  && transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                  && (!filter.HasVendorFilter || filter.VendorIds.Contains(subOrder.StoreId))
                  && (!filter.HasPromoFilter || marketplaceOrder.PromoCode == filter.PromoCode)
                  && (!filter.FromDateUtc.HasValue || transaction.CreatedAtUtc >= filter.FromDateUtc.Value)
                  && (!filter.ToDateUtc.HasValue || transaction.CreatedAtUtc <= filter.ToDateUtc.Value)
                  && !_dbContext.PromoCodeUsages.AsNoTracking().Any(usage =>
                      !usage.IsDeleted &&
                      usage.MarketplaceOrderId == marketplaceOrder.Id)
            group transaction by new
            {
                marketplaceOrder.Id,
                transaction.Currency
            }
            into grouped
            select new IntegrityIssueSeedRow
            {
                ReferenceId = grouped.Key.Id,
                Currency = grouped.Key.Currency,
                OccurredAtUtc = grouped.Max(row => (DateTime?)row.CreatedAtUtc),
                ExpectedAmount = grouped.Sum(row => (decimal?)row.Amount) ?? 0m,
                ActualAmount = 0m,
                Summary = "Platform promotion ledger entries exist but no immutable promo usage record is linked to the marketplace order."
            };
    }

    private IQueryable<IntegrityIssueSeedRow> BuildDuplicatePromoUsageQuery(DashboardFilterContext filter)
    {
        var query =
            from usage in _dbContext.PromoCodeUsages.AsNoTracking()
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on usage.MarketplaceOrderId equals marketplaceOrder.Id
            where !usage.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && (!filter.HasPromoFilter ||
                      marketplaceOrder.PromoCode == filter.PromoCode ||
                      usage.PromoCodeSnapshot == filter.PromoCode)
                  && (!filter.HasVendorFilter ||
                      _dbContext.CommerceHubSubOrders.AsNoTracking().Any(subOrder =>
                          !subOrder.IsDeleted &&
                          subOrder.MarketplaceOrderId == marketplaceOrder.Id &&
                          filter.VendorIds.Contains(subOrder.StoreId)))
                  && (!filter.FromDateUtc.HasValue || usage.AppliedAtUtc >= filter.FromDateUtc.Value)
                  && (!filter.ToDateUtc.HasValue || usage.AppliedAtUtc <= filter.ToDateUtc.Value)
            group usage by new
            {
                usage.MarketplaceOrderId,
                marketplaceOrder.Currency
            }
            into grouped
            where grouped.Count() > 1
            select new IntegrityIssueSeedRow
            {
                ReferenceId = grouped.Key.MarketplaceOrderId,
                Currency = grouped.Key.Currency,
                OccurredAtUtc = grouped.Max(row => (DateTime?)row.AppliedAtUtc),
                ExpectedAmount = 1m,
                ActualAmount = grouped.Count(),
                Summary = "Marketplace order has multiple active promo usage records. Only one promo usage record should exist per order."
            };

        return query;
    }

    private IQueryable<IntegrityIssueSeedRow> BuildRefundMathMismatchQuery(DashboardFilterContext filter)
    {
        return BuildIntegrityRefundScope(filter)
            .Where(row =>
                row.PromoDiscountReversalAmount < 0m ||
                row.CommissionReversalAmount + row.PromoDiscountReversalAmount != row.RefundAmount - row.VendorEarningReversalAmount ||
                row.PromoDiscountReversalAmount > row.RefundAmount - row.VendorEarningReversalAmount)
            .Select(row => new IntegrityIssueSeedRow
            {
                ReferenceId = row.RefundId,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc,
                ExpectedAmount = row.RefundAmount - row.VendorEarningReversalAmount,
                ActualAmount = row.CommissionReversalAmount + row.PromoDiscountReversalAmount,
                Summary = "Refund commission reversal plus promo reversal does not match the gross commission reversal implied by the refund."
            });
    }

    private IQueryable<IntegrityIssueSeedRow> BuildRefundLedgerMismatchQuery(DashboardFilterContext filter)
    {
        return
            from row in BuildIntegrityRefundScope(filter)
            let ledgerCommissionReversal = _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    transaction.ReferenceId == row.RefundId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            let ledgerPromoReversal = _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    transaction.ReferenceId == row.RefundId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            where ledgerCommissionReversal != row.CommissionReversalAmount ||
                  ledgerPromoReversal != row.PromoDiscountReversalAmount
            select new IntegrityIssueSeedRow
            {
                ReferenceId = row.RefundId,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc,
                ExpectedAmount = row.CommissionReversalAmount,
                ActualAmount = ledgerCommissionReversal,
                Summary = ledgerCommissionReversal != row.CommissionReversalAmount
                    ? "Platform commission reversal ledger entries do not match the refund."
                    : "Platform promotion reversal ledger entries do not match the refund."
            };
    }

    private IQueryable<IntegrityIssueSeedRow> BuildMissingRefundReversalQuery(DashboardFilterContext filter)
    {
        return
            from row in BuildIntegrityRefundScope(filter)
            let ledgerCommissionReversal = _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    transaction.ReferenceId == row.RefundId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            let ledgerPromoReversal = _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    transaction.ReferenceId == row.RefundId &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            where (row.CommissionReversalAmount > 0m && ledgerCommissionReversal <= 0m) ||
                  (row.PromoDiscountReversalAmount > 0m && ledgerPromoReversal <= 0m)
            select new IntegrityIssueSeedRow
            {
                ReferenceId = row.RefundId,
                Currency = row.Currency,
                OccurredAtUtc = row.OccurredAtUtc,
                ExpectedAmount =
                    (row.CommissionReversalAmount > 0m && ledgerCommissionReversal <= 0m
                        ? row.CommissionReversalAmount
                        : 0m) +
                    (row.PromoDiscountReversalAmount > 0m && ledgerPromoReversal <= 0m
                        ? row.PromoDiscountReversalAmount
                        : 0m),
                ActualAmount =
                    (row.CommissionReversalAmount > 0m && ledgerCommissionReversal <= 0m
                        ? ledgerCommissionReversal
                        : 0m) +
                    (row.PromoDiscountReversalAmount > 0m && ledgerPromoReversal <= 0m
                        ? ledgerPromoReversal
                        : 0m),
                Summary =
                    row.CommissionReversalAmount > 0m && ledgerCommissionReversal <= 0m &&
                    row.PromoDiscountReversalAmount > 0m && ledgerPromoReversal <= 0m
                        ? "Refund is missing both commission and promo reversal ledger entries."
                        : row.CommissionReversalAmount > 0m && ledgerCommissionReversal <= 0m
                            ? "Refund is missing the platform commission reversal ledger entry."
                            : "Refund is missing the platform promo reversal ledger entry."
            };
    }

    private IQueryable<IntegrityIssueSeedRow> BuildOrphanOrderLedgerQuery(DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter || filter.HasPromoFilter)
        {
            return _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(_ => false)
                .Select(_ => new IntegrityIssueSeedRow());
        }

        var query =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id into subOrderJoin
            from subOrder in subOrderJoin.DefaultIfEmpty()
            where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                  && (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission ||
                      transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion)
                  && subOrder == null
            select new IntegrityIssueSeedRow
            {
                ReferenceId = transaction.ReferenceId,
                Currency = transaction.Currency,
                OccurredAtUtc = transaction.CreatedAtUtc,
                Summary = "Ledger order reference points to a missing sub-order."
            };

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private IQueryable<IntegrityIssueSeedRow> BuildOrphanRefundLedgerQuery(DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter || filter.HasPromoFilter)
        {
            return _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(_ => false)
                .Select(_ => new IntegrityIssueSeedRow());
        }

        var query =
            from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
            join refund in _dbContext.CommerceHubRefunds.AsNoTracking() on transaction.ReferenceId equals refund.Id into refundJoin
            from refund in refundJoin.DefaultIfEmpty()
            where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                  && transaction.EntityId == null
                  && transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund
                  && (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission ||
                      transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion)
                  && refund == null
            select new IntegrityIssueSeedRow
            {
                ReferenceId = transaction.ReferenceId,
                Currency = transaction.Currency,
                OccurredAtUtc = transaction.CreatedAtUtc,
                Summary = "Ledger refund reference points to a missing refund record."
            };

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private IQueryable<IntegritySubOrderRow> BuildIntegritySubOrderScope(DashboardFilterContext filter)
    {
        var query =
            from subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking()
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            where !subOrder.IsDeleted && !marketplaceOrder.IsDeleted
            select new IntegritySubOrderRow
            {
                SubOrderId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                VendorId = subOrder.StoreId,
                PromoCode = marketplaceOrder.PromoCode,
                Currency = subOrder.Currency,
                Total = subOrder.Total,
                PlatformCommissionAmount = subOrder.PlatformCommissionAmount,
                FinalPlatformCommissionAmount = subOrder.FinalPlatformCommissionAmount,
                PromoDiscountAmount = subOrder.PromoDiscountAmount,
                VendorEarningAmount = subOrder.VendorEarningAmount,
                OccurredAtUtc = subOrder.CreatedAt
            };

        if (filter.HasVendorFilter)
        {
            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private IQueryable<IntegrityRefundRow> BuildIntegrityRefundScope(DashboardFilterContext filter)
    {
        var query =
            from refund in _dbContext.CommerceHubRefunds.AsNoTracking()
            join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
            join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
            where !refund.IsDeleted
                  && !subOrder.IsDeleted
                  && !marketplaceOrder.IsDeleted
                  && refund.Status == CommerceHubRefundStatus.Completed
            select new IntegrityRefundRow
            {
                RefundId = refund.Id,
                VendorId = subOrder.StoreId,
                PromoCode = marketplaceOrder.PromoCode,
                Currency = refund.Currency,
                RefundAmount = refund.Amount,
                VendorEarningReversalAmount = refund.VendorEarningReversalAmount,
                CommissionReversalAmount = refund.CommissionReversalAmount,
                PromoDiscountReversalAmount = refund.PromoDiscountReversalAmount,
                OccurredAtUtc = refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt
            };

        if (filter.HasVendorFilter)
        {
            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private async Task<DashboardFilterContext> NormalizeFilterAsync(
        SuperAdminFinancialDashboardFilterDto? query,
        bool defaultToLast30Days,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        query ??= new SuperAdminFinancialDashboardFilterDto();

        var snapshotBoundaryUtc = NormalizeSnapshotBoundary(query.SnapshotBoundaryUtc, generatedAtUtc);
        var (fromDateUtc, toDateUtc, hasDateFilter) = ResolveDateRange(query, defaultToLast30Days, snapshotBoundaryUtc);
        var vendorSearch = NormalizeSearchTerm(query.VendorSearch);
        var promoCode = NormalizePromoCode(query.PromoCode);
        var vendorIds = vendorSearch == null
            ? Array.Empty<Guid>()
            : await ResolveVendorIdsAsync(vendorSearch, cancellationToken);

        return new DashboardFilterContext(
            fromDateUtc,
            toDateUtc,
            snapshotBoundaryUtc,
            hasDateFilter,
            vendorSearch,
            promoCode,
            vendorIds);
    }

    private async Task<IReadOnlyList<Guid>> ResolveVendorIdsAsync(
        string vendorSearch,
        CancellationToken cancellationToken)
    {
        var escapedPattern = $"%{EscapeLikePattern(vendorSearch)}%";
        var isGuid = Guid.TryParse(vendorSearch, out var vendorId);

        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant =>
                tenant.DeletedAtUtc == null &&
                ((isGuid && tenant.Id == vendorId) ||
                 EF.Functions.ILike(tenant.Name, escapedPattern) ||
                 EF.Functions.ILike(tenant.Slug ?? string.Empty, escapedPattern) ||
                 EF.Functions.ILike(tenant.ContactEmail ?? string.Empty, escapedPattern)))
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
    }

    private static (DateTime? FromDateUtc, DateTime? ToDateUtc, bool HasDateFilter) ResolveDateRange(
        SuperAdminFinancialDashboardFilterDto query,
        bool defaultToLast30Days,
        DateTime snapshotBoundaryUtc)
    {
        var explicitFromDateUtc = query.FromDateUtc.HasValue
            ? NormalizeUtc(query.FromDateUtc)
            : NormalizeDateBoundary(query.FromDate, endOfDay: false);
        var explicitToDateUtc = query.ToDateUtc.HasValue
            ? NormalizeUtc(query.ToDateUtc)
            : NormalizeDateBoundary(query.ToDate, endOfDay: true);
        var hasExplicitDateFilter = explicitFromDateUtc.HasValue || explicitToDateUtc.HasValue;

        if (!hasExplicitDateFilter && !defaultToLast30Days)
        {
            return (null, null, false);
        }

        if (explicitFromDateUtc.HasValue && explicitToDateUtc.HasValue && explicitFromDateUtc > explicitToDateUtc)
        {
            (explicitFromDateUtc, explicitToDateUtc) = (explicitToDateUtc, explicitFromDateUtc);
        }

        if (!explicitFromDateUtc.HasValue && !explicitToDateUtc.HasValue)
        {
            var defaultEnd = snapshotBoundaryUtc;
            var defaultStart = DateTime.SpecifyKind(defaultEnd.Date.AddDays(-29), DateTimeKind.Utc);
            return (defaultStart, defaultEnd, true);
        }

        if (!explicitFromDateUtc.HasValue)
        {
            var computedTo = explicitToDateUtc!.Value;
            explicitFromDateUtc = DateTime.SpecifyKind(computedTo.Date.AddDays(-29), DateTimeKind.Utc);
        }

        if (!explicitToDateUtc.HasValue)
        {
            var computedFrom = explicitFromDateUtc!.Value;
            explicitToDateUtc = DateTime.SpecifyKind(computedFrom.Date.AddDays(30).AddTicks(-1), DateTimeKind.Utc);
        }

        if (explicitToDateUtc > snapshotBoundaryUtc)
        {
            explicitToDateUtc = snapshotBoundaryUtc;
        }

        if (explicitFromDateUtc > explicitToDateUtc)
        {
            explicitFromDateUtc = DateTime.SpecifyKind(explicitToDateUtc.Value.Date, DateTimeKind.Utc);
        }

        return (explicitFromDateUtc, explicitToDateUtc, true);
    }

    private static IQueryable<OrderLedgerRow> ApplyOrderFilters(
        IQueryable<OrderLedgerRow> query,
        DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter)
        {
            if (filter.VendorIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private static IQueryable<OrderPromotionRow> ApplyOrderFilters(
        IQueryable<OrderPromotionRow> query,
        DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter)
        {
            if (filter.VendorIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private static IQueryable<RefundMetricRow> ApplyRefundFilters(
        IQueryable<RefundMetricRow> query,
        DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter)
        {
            if (filter.VendorIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.OccurredAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private static IQueryable<RefundCommissionRow> ApplyRefundLedgerFilters(
        IQueryable<RefundCommissionRow> query,
        DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter)
        {
            if (filter.VendorIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private static IQueryable<RefundPromotionRow> ApplyRefundLedgerFilters(
        IQueryable<RefundPromotionRow> query,
        DashboardFilterContext filter)
    {
        if (filter.HasVendorFilter)
        {
            if (filter.VendorIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            query = query.Where(row => filter.VendorIds.Contains(row.VendorId));
        }

        if (filter.HasPromoFilter)
        {
            query = query.Where(row => row.PromoCode == filter.PromoCode);
        }

        if (filter.FromDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc >= filter.FromDateUtc.Value);
        }

        if (filter.ToDateUtc.HasValue)
        {
            query = query.Where(row => row.CreatedAtUtc <= filter.ToDateUtc.Value);
        }

        return query;
    }

    private static async Task<List<BucketAggregateRow>> GroupTemporalMetricsAsync(
        IQueryable<TemporalMetricRow> query,
        SuperAdminFinancialTrendGroupBy groupBy,
        CancellationToken cancellationToken)
    {
        return groupBy switch
        {
            SuperAdminFinancialTrendGroupBy.Month => await query
                .GroupBy(row => new
                {
                    row.OccurredAtUtc.Year,
                    row.OccurredAtUtc.Month,
                    row.Currency
                })
                .Select(group => new BucketAggregateRow
                {
                    Year = group.Key.Year,
                    Month = group.Key.Month,
                    Day = 1,
                    Currency = group.Key.Currency,
                    Amount = group.Sum(row => (decimal?)row.Amount) ?? 0m,
                    SecondaryAmount = group.Sum(row => (decimal?)row.SecondaryAmount) ?? 0m,
                    Count = group.Sum(row => (int?)row.Count) ?? 0
                })
                .ToListAsync(cancellationToken),
            _ => await query
                .GroupBy(row => new
                {
                    row.OccurredAtUtc.Year,
                    row.OccurredAtUtc.Month,
                    row.OccurredAtUtc.Day,
                    row.Currency
                })
                .Select(group => new BucketAggregateRow
                {
                    Year = group.Key.Year,
                    Month = group.Key.Month,
                    Day = group.Key.Day,
                    Currency = group.Key.Currency,
                    Amount = group.Sum(row => (decimal?)row.Amount) ?? 0m,
                    SecondaryAmount = group.Sum(row => (decimal?)row.SecondaryAmount) ?? 0m,
                    Count = group.Sum(row => (int?)row.Count) ?? 0
                })
                .ToListAsync(cancellationToken)
        };
    }

    private static Dictionary<DateTime, MutableTrendPoint> InitializeTrendBuckets(
        DashboardFilterContext filter,
        SuperAdminFinancialTrendGroupBy groupBy)
    {
        return EnumerateBuckets(filter, groupBy)
            .ToDictionary(
                bucket => bucket,
                bucket => new MutableTrendPoint
                {
                    BucketStartUtc = bucket
                });
    }

    private static Dictionary<DateTime, MutableRefundPoint> InitializeRefundBuckets(
        DashboardFilterContext filter,
        SuperAdminFinancialTrendGroupBy groupBy)
    {
        return EnumerateBuckets(filter, groupBy)
            .ToDictionary(
                bucket => bucket,
                bucket => new MutableRefundPoint
                {
                    BucketStartUtc = bucket
                });
    }

    private static IEnumerable<DateTime> EnumerateBuckets(
        DashboardFilterContext filter,
        SuperAdminFinancialTrendGroupBy groupBy)
    {
        var fromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart();
        var toDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd();

        if (groupBy == SuperAdminFinancialTrendGroupBy.Month)
        {
            var cursor = DateTime.SpecifyKind(new DateTime(fromDateUtc.Year, fromDateUtc.Month, 1), DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(new DateTime(toDateUtc.Year, toDateUtc.Month, 1), DateTimeKind.Utc);
            while (cursor <= end)
            {
                yield return cursor;
                cursor = DateTime.SpecifyKind(cursor.AddMonths(1), DateTimeKind.Utc);
            }

            yield break;
        }

        var current = fromDateUtc.Date;
        var finalDate = toDateUtc.Date;
        while (current <= finalDate)
        {
            yield return DateTime.SpecifyKind(current, DateTimeKind.Utc);
            current = current.AddDays(1);
        }
    }

    private static DateTime ToBucketStartUtc(BucketAggregateRow row) =>
        DateTime.SpecifyKind(new DateTime(row.Year, row.Month, row.Day <= 0 ? 1 : row.Day), DateTimeKind.Utc);

    private static SuperAdminFinancialTrendPointDto MapTrendPoint(MutableTrendPoint point)
    {
        var promoCost = RoundCurrency(point.PromoCost);
        var grossCommission = RoundCurrency(point.GrossCommission);
        var commissionReversals = RoundCurrency(point.CommissionReversals);

        return new SuperAdminFinancialTrendPointDto
        {
            BucketStartUtc = point.BucketStartUtc,
            GrossCommission = grossCommission,
            PromoCost = promoCost,
            NetCommission = RoundCurrency(grossCommission - promoCost),
            CommissionReversals = commissionReversals,
            NetRevenue = RoundCurrency(grossCommission - promoCost - commissionReversals),
            RefundAmount = RoundCurrency(point.RefundAmount),
            OrdersCount = point.OrdersCount
        };
    }

    private static SuperAdminRefundTrendPointDto MapRefundPoint(MutableRefundPoint point) =>
        new()
        {
            BucketStartUtc = point.BucketStartUtc,
            RefundAmount = RoundCurrency(point.RefundAmount),
            RefundCount = point.RefundCount,
            PromoReversalAmount = RoundCurrency(point.PromoReversalAmount)
        };

    private static void MergePromoRows(
        IDictionary<PromoAggregateKey, PromoMetricAccumulator> accumulators,
        IEnumerable<PromoAggregateRow> rows)
    {
        foreach (var row in rows)
        {
            var key = new PromoAggregateKey(row.PromoCodeId, row.PromoCode, row.Currency);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new PromoMetricAccumulator
                {
                    PromoCodeId = row.PromoCodeId,
                    PromoCode = row.PromoCode,
                    IsActive = row.IsActive,
                    Currency = row.Currency
                };
                accumulators[key] = accumulator;
            }

            accumulator.IsActive = accumulator.IsActive || row.IsActive;
            accumulator.TotalUsageCount += row.UsageCount;
            accumulator.TotalPromoCost += row.PromoCost;
            accumulator.PromoReversal += row.PromoReversal;
            accumulator.RevenueGenerated += row.RevenueGenerated;
            accumulator.CommissionReversals += row.CommissionReversals;
            accumulator.LastUsedAtUtc = Max(accumulator.LastUsedAtUtc, row.LastUsedAtUtc);
        }
    }

    private static void MergeVendorRows(
        IDictionary<VendorAggregateKey, VendorMetricAccumulator> accumulators,
        IEnumerable<VendorAggregateRow> rows)
    {
        foreach (var row in rows)
        {
            var key = new VendorAggregateKey(row.VendorId, row.Currency);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new VendorMetricAccumulator
                {
                    VendorId = row.VendorId,
                    VendorName = row.VendorName,
                    VendorSlug = row.VendorSlug,
                    ContactEmail = row.ContactEmail,
                    Currency = row.Currency
                };
                accumulators[key] = accumulator;
            }

            if (!string.IsNullOrWhiteSpace(row.VendorName))
            {
                accumulator.VendorName = row.VendorName;
            }

            accumulator.VendorSlug ??= row.VendorSlug;
            accumulator.ContactEmail ??= row.ContactEmail;
            accumulator.OrdersCount += row.OrdersCount;
            accumulator.TotalSales += row.TotalSales;
            accumulator.GrossCommission += row.GrossCommission;
            accumulator.PromoCost += row.PromoCost;
            accumulator.PromoReversal += row.PromoReversal;
            accumulator.CommissionReversals += row.CommissionReversals;
            accumulator.TotalRefundAmount += row.TotalRefundAmount;
            accumulator.RefundedOrdersCount += row.RefundedOrdersCount;
            accumulator.LastOccurredAtUtc = Max(accumulator.LastOccurredAtUtc, row.LastOccurredAtUtc);
        }
    }

    private async Task PopulatePromoDefinitionsAsync(
        IEnumerable<PromoMetricAccumulator> accumulators,
        CancellationToken cancellationToken)
    {
        var promoCodes = accumulators
            .Where(accumulator => !string.IsNullOrWhiteSpace(accumulator.PromoCode))
            .Select(accumulator => accumulator.PromoCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (promoCodes.Count == 0)
        {
            return;
        }

        var definitions = await _dbContext.PromoCodes
            .AsNoTracking()
            .Where(promoCode => promoCodes.Contains(promoCode.Code))
            .Select(promoCode => new
            {
                promoCode.Id,
                promoCode.Code,
                promoCode.IsActive,
                promoCode.IsDeleted
            })
            .ToListAsync(cancellationToken);

        var definitionLookup = definitions
            .GroupBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var accumulator in accumulators)
        {
            if (!definitionLookup.TryGetValue(accumulator.PromoCode, out var definition))
            {
                continue;
            }

            accumulator.PromoCodeId ??= definition.Id;
            accumulator.IsActive = definition.IsActive && !definition.IsDeleted;
        }
    }

    private static IEnumerable<SuperAdminPromoAnalyticsItemDto> SortPromoItems(
        IEnumerable<SuperAdminPromoAnalyticsItemDto> items,
        SuperAdminPromoAnalyticsQueryDto query)
    {
        return (query.SortBy, query.SortDirection) switch
        {
            (SuperAdminPromoAnalyticsSortField.PromoCode, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.PromoCode, _) =>
                items.OrderByDescending(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.UsageCount, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.TotalUsageCount)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.UsageCount, _) =>
                items.OrderByDescending(item => item.TotalUsageCount)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.PromoCost, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.TotalPromoCost)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.PromoCost, _) =>
                items.OrderByDescending(item => item.TotalPromoCost)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.RevenueGenerated, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.RevenueGenerated)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.RevenueGenerated, _) =>
                items.OrderByDescending(item => item.RevenueGenerated)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.LastUsedAt, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.LastUsedAtUtc)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.LastUsedAt, _) =>
                items.OrderByDescending(item => item.LastUsedAtUtc)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            (SuperAdminPromoAnalyticsSortField.NetImpact, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.NetImpact)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty),
            _ =>
                items.OrderByDescending(item => item.NetImpact)
                    .ThenBy(item => item.PromoCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.PromoCodeId ?? Guid.Empty)
        };
    }

    private static IEnumerable<SuperAdminVendorPerformanceItemDto> SortVendorItems(
        IEnumerable<SuperAdminVendorPerformanceItemDto> items,
        SuperAdminVendorPerformanceQueryDto query)
    {
        return (query.SortBy, query.SortDirection) switch
        {
            (SuperAdminVendorPerformanceSortField.VendorName, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.VendorName, _) =>
                items.OrderByDescending(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.TotalSales, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.TotalSales)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.TotalSales, _) =>
                items.OrderByDescending(item => item.TotalSales)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.GrossCommission, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.GrossCommission)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.GrossCommission, _) =>
                items.OrderByDescending(item => item.GrossCommission)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.PromoCost, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.PromoCost)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.PromoCost, _) =>
                items.OrderByDescending(item => item.PromoCost)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.RefundRate, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.RefundRate)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.RefundRate, _) =>
                items.OrderByDescending(item => item.RefundRate)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.TotalRefundAmount, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.TotalRefundAmount)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.TotalRefundAmount, _) =>
                items.OrderByDescending(item => item.TotalRefundAmount)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.OrdersCount, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.OrdersCount)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.OrdersCount, _) =>
                items.OrderByDescending(item => item.OrdersCount)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            (SuperAdminVendorPerformanceSortField.NetContribution, SuperAdminSortDirection.Asc) =>
                items.OrderBy(item => item.NetContribution)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId),
            _ =>
                items.OrderByDescending(item => item.NetContribution)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.VendorId)
        };
    }

    private static IReadOnlyList<SuperAdminRefundAlertDto> BuildRefundAlerts(
        IReadOnlyList<SuperAdminRefundTrendPointDto> points,
        decimal refundRate,
        decimal totalPromoReversalAmount,
        decimal totalRefundAmount)
    {
        var alerts = new List<SuperAdminRefundAlertDto>();

        if (refundRate >= 0.2m)
        {
            alerts.Add(new SuperAdminRefundAlertDto
            {
                Severity = "CRITICAL",
                AlertType = "REFUND_RATE_SPIKE",
                Message = $"Refund rate reached {refundRate:P1}, which is materially above expected steady-state levels."
            });
        }
        else if (refundRate >= 0.1m)
        {
            alerts.Add(new SuperAdminRefundAlertDto
            {
                Severity = "WARNING",
                AlertType = "REFUND_RATE_ELEVATED",
                Message = $"Refund rate is {refundRate:P1}. Review vendors and SKUs with concentrated reversal activity."
            });
        }

        var activePoints = points.Where(point => point.RefundAmount > 0m).ToList();
        if (activePoints.Count > 1)
        {
            var averageRefundAmount = activePoints.Average(point => point.RefundAmount);
            foreach (var point in activePoints
                         .Where(point => point.RefundAmount >= averageRefundAmount * 2m && point.RefundCount >= 2)
                         .OrderByDescending(point => point.RefundAmount)
                         .Take(3))
            {
                alerts.Add(new SuperAdminRefundAlertDto
                {
                    Severity = "WARNING",
                    AlertType = "REFUND_BUCKET_SPIKE",
                    BucketStartUtc = point.BucketStartUtc,
                    Message = $"Refund bucket at {point.BucketStartUtc:u} is {RoundCurrency(point.RefundAmount):0.00}, more than 2x the average active bucket."
                });
            }
        }

        if (totalRefundAmount > 0m && totalPromoReversalAmount / totalRefundAmount >= 0.15m)
        {
            alerts.Add(new SuperAdminRefundAlertDto
            {
                Severity = "INFO",
                AlertType = "PROMO_HEAVY_REFUNDS",
                Message = $"Promo reversals account for {RoundRatio(totalPromoReversalAmount / totalRefundAmount):P1} of refund value in the selected range."
            });
        }

        return alerts;
    }

    private static async Task AppendIssuesAsync<T>(
        IList<SuperAdminFinancialIntegrityIssueDto> issues,
        IQueryable<T> query,
        int maxIssues,
        Func<T, SuperAdminFinancialIntegrityIssueDto> map,
        CancellationToken cancellationToken)
    {
        var remaining = maxIssues - issues.Count;
        if (remaining <= 0)
        {
            return;
        }

        var rows = await query.Take(remaining).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            issues.Add(map(row));
        }
    }

    private static SuperAdminFinancialHealthDto BuildFinancialHealth(
        CommerceHubFinanceReconciliationResult reconciliationSnapshot) =>
        new()
        {
            Status = reconciliationSnapshot.LatestReportStatus,
            MismatchCount = reconciliationSnapshot.LatestReportMismatchCount,
            LastReportAtUtc = reconciliationSnapshot.LatestReportTimestampUtc
        };

    private static SuperAdminDataFreshnessDto BuildAggregateDataFreshness(
        DashboardFilterContext filter,
        CommerceHubDailyAggregateWindow aggregateWindow) =>
        new()
        {
            Source = "DAILY_AGGREGATE",
            LastUpdatedAtUtc = aggregateWindow.LatestCalculatedAtUtc ?? aggregateWindow.AggregatedThroughUtc,
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            IsValid = aggregateWindow.InvalidRowCount == 0,
            HasPendingJobs = aggregateWindow.HasPendingJobs
        };

    private static SuperAdminDataFreshnessDto BuildLiveDataFreshness(
        DashboardFilterContext filter,
        DateTime generatedAtUtc) =>
        new()
        {
            Source = "LIVE_LEDGER",
            LastUpdatedAtUtc = generatedAtUtc,
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            IsValid = true,
            HasPendingJobs = false
        };

    private static SuperAdminIntegrityPanelDto BuildIntegrityPanel(
        CommerceHubFinanceReconciliationResult reconciliationSnapshot)
    {
        var details = DeserializeIntegrityDetails(reconciliationSnapshot.LatestReportDetailsJson);
        return new SuperAdminIntegrityPanelDto
        {
            Status = reconciliationSnapshot.LatestReportStatus,
            MismatchCount = reconciliationSnapshot.LatestReportMismatchCount,
            Errors = details?.Errors?.Select(MapIssueGroup).ToList() ?? [],
            Warnings = details?.Warnings?.Select(MapIssueGroup).ToList() ?? [],
            AffectedEntities = details?.AffectedEntities?.Select(MapAffectedEntity).ToList() ?? []
        };
    }

    private static IReadOnlyList<SuperAdminIntegrityIssueGroupDto> GroupIntegrityIssues(
        IReadOnlyCollection<SuperAdminFinancialIntegrityIssueDto> issues,
        string severity)
    {
        return issues
            .Where(issue => string.Equals(issue.Severity, severity, StringComparison.OrdinalIgnoreCase))
            .GroupBy(issue => issue.IssueType)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => new SuperAdminIntegrityIssueGroupDto
            {
                IssueType = group.Key,
                Count = group.Count(),
                Summary = group.First().Summary
            })
            .ToList();
    }

    private static SuperAdminAffectedEntityDto MapAffectedEntity(
        SuperAdminFinancialIntegrityIssueDto issue) =>
        new()
        {
            Severity = issue.Severity,
            IssueType = issue.IssueType,
            ReferenceType = issue.ReferenceType,
            ReferenceId = issue.ReferenceId,
            Summary = issue.Summary,
            ExpectedAmount = issue.ExpectedAmount,
            ActualAmount = issue.ActualAmount,
            Currency = issue.Currency,
            OccurredAtUtc = issue.OccurredAtUtc
        };

    private static SuperAdminIntegrityIssueGroupDto MapIssueGroup(IntegrityIssueGroupPayload group) =>
        new()
        {
            IssueType = group.IssueType,
            Count = group.Count,
            Summary = group.Summary
        };

    private static SuperAdminAffectedEntityDto MapAffectedEntity(IntegrityAffectedEntityPayload entity) =>
        new()
        {
            Severity = entity.Severity,
            IssueType = entity.IssueType,
            ReferenceType = entity.ReferenceType,
            ReferenceId = entity.ReferenceId,
            Summary = entity.Summary,
            ExpectedAmount = entity.ExpectedAmount,
            ActualAmount = entity.ActualAmount,
            Currency = entity.Currency,
            OccurredAtUtc = entity.OccurredAtUtc
        };

    private static IntegrityDetailsPayload? DeserializeIntegrityDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<IntegrityDetailsPayload>(
                detailsJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
        catch
        {
            return null;
        }
    }

    private static SuperAdminFinancialOverviewDto BuildEmptyOverview(
        DashboardFilterContext filter,
        DateTime generatedAtUtc,
        CommerceHubFinanceReconciliationResult reconciliationSnapshot) =>
        new()
        {
            FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
            ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            Currency = DefaultCurrency,
            BaseCurrencyCode = DefaultCurrency,
            ExchangeRateToBase = 1m,
            OrderScope = "SubOrder",
            AppliedVendorFilter = filter.VendorSearch,
            AppliedPromoCode = filter.PromoCode,
            DataSource = "LIVE_LEDGER",
            IsEventuallyConsistent = true,
            ConsistencyModel = DefaultConsistencyModel,
            DataFreshnessNote = DefaultFreshnessNote,
            GeneratedAtUtc = generatedAtUtc,
            AggregatedThroughUtc = filter.SnapshotBoundaryUtc,
            FinancialHealth = BuildFinancialHealth(reconciliationSnapshot),
            DataFreshness = BuildLiveDataFreshness(filter, generatedAtUtc),
            IntegrityPanel = BuildIntegrityPanel(reconciliationSnapshot)
        };

    private static SuperAdminPagedResult<T> ToPagedResult<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize) =>
        new()
        {
            Items = items.Skip((Math.Max(1, page) - 1) * Math.Max(1, pageSize)).Take(Math.Max(1, pageSize)).ToList(),
            Page = Math.Max(1, page),
            PageSize = Math.Max(1, pageSize),
            TotalCount = items.Count,
            TotalPages = items.Count == 0 ? 0 : (int)Math.Ceiling(items.Count / (double)Math.Max(1, pageSize))
        };

    private static SuperAdminPagedResult<T> EmptyPagedResult<T>(int page, int pageSize) =>
        new()
        {
            Items = Array.Empty<T>(),
            Page = Math.Max(1, page),
            PageSize = Math.Max(1, pageSize),
            TotalCount = 0,
            TotalPages = 0
        };

    private static string BuildItemLabel(string productName, string? variantName) =>
        string.IsNullOrWhiteSpace(variantName) ? productName : $"{productName} / {variantName}";

    private static string BuildIntegrityScopeDescription(DashboardFilterContext filter, bool isGlobalScope)
    {
        if (isGlobalScope)
        {
            return "Full-ledger integrity snapshot with reconciliation counters, plus sample order, refund, promo, and orphan-reference inconsistencies.";
        }

        var scopeParts = new List<string>();
        if (filter.HasDateFilter)
        {
            scopeParts.Add($"dates {filter.FromDateUtc?.ToString("u")} to {filter.ToDateUtc?.ToString("u")}");
        }

        if (filter.HasVendorFilter)
        {
            scopeParts.Add($"vendor filter '{filter.VendorSearch}'");
        }

        if (filter.HasPromoFilter)
        {
            scopeParts.Add($"promo '{filter.PromoCode}'");
        }

        return $"Scoped integrity scan for {string.Join(", ", scopeParts)}. Global payable, receivable, payout, and duplicate-pair counters are only authoritative in the full-ledger snapshot.";
    }

    private static void EnsureSingleCurrency(
        IReadOnlyCollection<string> currencies,
        string scope)
    {
        foreach (var currency in currencies)
        {
            FinancialCurrencyGuard.RequireEgpCurrency(currency, $"super_admin.{scope}.currency", scope);
        }

        if (currencies.Count <= 1)
        {
            return;
        }

        throw new InvalidOperationException($"{scope} contains multiple currencies and cannot be represented as a single summary.");
    }

    private static string? NormalizeSearchTerm(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePromoCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _ => value.Value.ToUniversalTime()
        };
    }

    private static DateTime? NormalizeDateBoundary(DateTime? value, bool endOfDay)
    {
        var utcValue = NormalizeUtc(value);
        if (!utcValue.HasValue)
        {
            return null;
        }

        var date = utcValue.Value.Date;
        return endOfDay
            ? DateTime.SpecifyKind(date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
            : DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }

    private static DateTime GetDefaultRangeStart() =>
        DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-29), DateTimeKind.Utc);

    private static DateTime GetDefaultRangeEnd() =>
        DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

    private static DateTime? Max(DateTime? first, DateTime? second)
    {
        if (!first.HasValue)
        {
            return second;
        }

        if (!second.HasValue)
        {
            return first;
        }

        return first >= second ? first : second;
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundRatio(decimal amount) =>
        Math.Round(amount, 4, MidpointRounding.AwayFromZero);

    private static DateTime NormalizeSnapshotBoundary(DateTime? snapshotBoundaryUtc, DateTime fallbackUtc)
    {
        var value = snapshotBoundaryUtc ?? fallbackUtc;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime()
        };
    }

    private sealed record DashboardFilterContext(
        DateTime? FromDateUtc,
        DateTime? ToDateUtc,
        DateTime SnapshotBoundaryUtc,
        bool HasDateFilter,
        string? VendorSearch,
        string? PromoCode,
        IReadOnlyList<Guid> VendorIds)
    {
        public bool HasVendorFilter => VendorSearch != null;
        public bool HasPromoFilter => PromoCode != null;
    }

    private readonly record struct PromoAggregateKey(Guid? PromoCodeId, string PromoCode, string Currency);
    private readonly record struct VendorAggregateKey(Guid VendorId, string Currency);

    private sealed class CurrencyAggregateRow
    {
        public string Currency { get; init; } = DefaultCurrency;
        public decimal Amount { get; init; }
        public decimal SecondaryAmount { get; init; }
        public int Count { get; init; }
        public int DistinctCount { get; init; }
    }

    private sealed class TemporalMetricRow
    {
        public DateTime OccurredAtUtc { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public decimal Amount { get; init; }
        public decimal SecondaryAmount { get; init; }
        public int Count { get; init; }
    }

    private sealed class BucketAggregateRow
    {
        public int Year { get; init; }
        public int Month { get; init; }
        public int Day { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public decimal Amount { get; init; }
        public decimal SecondaryAmount { get; init; }
        public int Count { get; init; }
    }

    private sealed class MutableTrendPoint
    {
        public DateTime BucketStartUtc { get; init; }
        public decimal GrossCommission { get; set; }
        public decimal PromoCost { get; set; }
        public decimal CommissionReversals { get; set; }
        public decimal RefundAmount { get; set; }
        public int OrdersCount { get; set; }
    }

    private sealed class MutableRefundPoint
    {
        public DateTime BucketStartUtc { get; init; }
        public decimal RefundAmount { get; set; }
        public int RefundCount { get; set; }
        public decimal PromoReversalAmount { get; set; }
    }

    private sealed class OrderLedgerRow
    {
        public Guid ReferenceId { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public Guid? PromoCodeId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime CreatedAtUtc { get; init; }
        public decimal OrderTotal { get; init; }
        public decimal GrossCommissionAmount { get; init; }
    }

    private sealed class OrderPromotionRow
    {
        public Guid ReferenceId { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public Guid? PromoCodeId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime CreatedAtUtc { get; init; }
        public decimal PromoAmount { get; init; }
    }

    private sealed class RefundMetricRow
    {
        public Guid RefundId { get; init; }
        public Guid SubOrderId { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public Guid? PromoCodeId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public decimal RefundAmount { get; init; }
        public decimal PromoDiscountReversalAmount { get; init; }
        public decimal CommissionReversalAmount { get; init; }
        public Guid? OrderItemId { get; init; }
        public int Quantity { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    private sealed class RefundCommissionRow
    {
        public Guid RefundId { get; init; }
        public Guid SubOrderId { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public Guid? PromoCodeId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime CreatedAtUtc { get; init; }
        public decimal CommissionAmount { get; init; }
    }

    private sealed class RefundPromotionRow
    {
        public Guid RefundId { get; init; }
        public Guid SubOrderId { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public Guid? PromoCodeId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime CreatedAtUtc { get; init; }
        public decimal PromoAmount { get; init; }
    }

    private sealed class PromoUsageRow
    {
        public Guid PromoCodeId { get; init; }
        public string PromoCode { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime AppliedAtUtc { get; init; }
    }

    private sealed class PromoAggregateRow
    {
        public Guid? PromoCodeId { get; init; }
        public string PromoCode { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public int UsageCount { get; init; }
        public decimal PromoCost { get; init; }
        public decimal PromoReversal { get; init; }
        public decimal RevenueGenerated { get; init; }
        public decimal CommissionReversals { get; init; }
        public DateTime? LastUsedAtUtc { get; init; }
    }

    private sealed class PromoMetricAccumulator
    {
        public Guid? PromoCodeId { get; set; }
        public string PromoCode { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int TotalUsageCount { get; set; }
        public decimal TotalPromoCost { get; set; }
        public decimal PromoReversal { get; set; }
        public decimal RevenueGenerated { get; set; }
        public decimal CommissionReversals { get; set; }
        public DateTime? LastUsedAtUtc { get; set; }
        public string Currency { get; set; } = DefaultCurrency;
    }

    private sealed class VendorAggregateRow
    {
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public int OrdersCount { get; init; }
        public decimal TotalSales { get; init; }
        public decimal GrossCommission { get; init; }
        public decimal PromoCost { get; init; }
        public decimal PromoReversal { get; init; }
        public decimal CommissionReversals { get; init; }
        public decimal TotalRefundAmount { get; init; }
        public int RefundedOrdersCount { get; init; }
        public DateTime? LastOccurredAtUtc { get; init; }
    }

    private sealed class VendorMetricAccumulator
    {
        public Guid VendorId { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string? VendorSlug { get; set; }
        public string? ContactEmail { get; set; }
        public int OrdersCount { get; set; }
        public decimal TotalSales { get; set; }
        public decimal GrossCommission { get; set; }
        public decimal PromoCost { get; set; }
        public decimal PromoReversal { get; set; }
        public decimal CommissionReversals { get; set; }
        public decimal TotalRefundAmount { get; set; }
        public int RefundedOrdersCount { get; set; }
        public string Currency { get; set; } = DefaultCurrency;
        public DateTime? LastOccurredAtUtc { get; set; }
    }

    private sealed class RefundVendorAggregateRow
    {
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string Currency { get; init; } = DefaultCurrency;
        public decimal RefundAmount { get; init; }
        public int RefundCount { get; init; }
        public decimal PromoReversalAmount { get; init; }
    }

    private sealed class RefundItemQueryRow
    {
        public Guid? OrderItemId { get; init; }
        public Guid SubOrderId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string? VariantName { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string Currency { get; init; } = DefaultCurrency;
        public decimal RefundAmount { get; init; }
        public int Quantity { get; init; }
    }

    private sealed class RefundItemAggregateRow
    {
        public Guid? OrderItemId { get; init; }
        public Guid SubOrderId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string? VariantName { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string Currency { get; init; } = DefaultCurrency;
        public decimal RefundAmount { get; init; }
        public int RefundCount { get; init; }
        public int RefundedQuantity { get; init; }
    }

    private sealed class IntegrityIssueSeedRow
    {
        public Guid? ReferenceId { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime? OccurredAtUtc { get; init; }
        public decimal? ExpectedAmount { get; init; }
        public decimal? ActualAmount { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    private sealed class IntegritySubOrderRow
    {
        public Guid SubOrderId { get; init; }
        public Guid MarketplaceOrderId { get; init; }
        public Guid VendorId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public decimal Total { get; init; }
        public decimal PlatformCommissionAmount { get; init; }
        public decimal FinalPlatformCommissionAmount { get; init; }
        public decimal PromoDiscountAmount { get; init; }
        public decimal VendorEarningAmount { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    private sealed class IntegrityRefundRow
    {
        public Guid RefundId { get; init; }
        public Guid VendorId { get; init; }
        public string? PromoCode { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public decimal RefundAmount { get; init; }
        public decimal VendorEarningReversalAmount { get; init; }
        public decimal CommissionReversalAmount { get; init; }
        public decimal PromoDiscountReversalAmount { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    private sealed class IntegrityDetailsPayload
    {
        public IReadOnlyList<IntegrityIssueGroupPayload> Errors { get; init; } = [];
        public IReadOnlyList<IntegrityIssueGroupPayload> Warnings { get; init; } = [];
        public IReadOnlyList<IntegrityAffectedEntityPayload> AffectedEntities { get; init; } = [];
    }

    private sealed class IntegrityIssueGroupPayload
    {
        public string IssueType { get; init; } = string.Empty;
        public int Count { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    private sealed class IntegrityAffectedEntityPayload
    {
        public string Severity { get; init; } = "ERROR";
        public string IssueType { get; init; } = string.Empty;
        public string ReferenceType { get; init; } = string.Empty;
        public Guid? ReferenceId { get; init; }
        public string Summary { get; init; } = string.Empty;
        public decimal? ExpectedAmount { get; init; }
        public decimal? ActualAmount { get; init; }
        public string? Currency { get; init; }
        public DateTime? OccurredAtUtc { get; init; }
    }
}
