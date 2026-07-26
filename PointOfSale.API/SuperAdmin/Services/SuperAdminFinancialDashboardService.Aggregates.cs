using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities.CommerceHubFinance;

namespace PointOfSale.API.SuperAdmin.Services;

public sealed partial class SuperAdminFinancialDashboardService
{
    private async Task<CommerceHubDailyAggregateWindow?> TryGetAggregateWindowAsync(
        DashboardFilterContext filter,
        CancellationToken cancellationToken)
    {
        if (!ShouldUseDailyAggregates(filter))
        {
            return null;
        }

        var fromDateUtc = DateTime.SpecifyKind((filter.FromDateUtc ?? GetDefaultRangeStart()).Date, DateTimeKind.Utc);
        var toDateUtc = DateTime.SpecifyKind((filter.ToDateUtc ?? GetDefaultRangeEnd()).Date, DateTimeKind.Utc);
        var aggregateWindow = await _dailyFinancialAggregateService.GetWindowAsync(
            fromDateUtc,
            toDateUtc,
            filter.SnapshotBoundaryUtc,
            cancellationToken);

        if (!aggregateWindow.CoversRequestedWindow)
        {
            _logger.LogDebug(
                "Daily aggregates do not fully cover the requested financial dashboard window | FromDateUtc: {FromDateUtc} | ToDateUtc: {ToDateUtc}",
                fromDateUtc,
                toDateUtc);
            return null;
        }

        if (!aggregateWindow.IsFresh)
        {
            _logger.LogDebug(
                "Daily aggregates are not safe for the requested financial dashboard window | FromDateUtc: {FromDateUtc} | ToDateUtc: {ToDateUtc} | LatestCalculatedAtUtc: {LatestCalculatedAtUtc} | InvalidRowCount: {InvalidRowCount} | HasPendingJobs: {HasPendingJobs}",
                fromDateUtc,
                toDateUtc,
                aggregateWindow.LatestCalculatedAtUtc,
                aggregateWindow.InvalidRowCount,
                aggregateWindow.HasPendingJobs);
            return null;
        }

        var unsupportedCurrencyCount = aggregateWindow.Rows.Count(row =>
            !string.Equals(row.CurrencyCode, aggregateWindow.BaseCurrencyCode, StringComparison.OrdinalIgnoreCase) &&
            !row.ExchangeRateToBase.HasValue);

        if (unsupportedCurrencyCount > 0)
        {
            _logger.LogWarning(
                "Daily aggregates contain non-base currencies without exchange-rate normalization; falling back to live ledger queries | UnsupportedCurrencyRows: {UnsupportedCurrencyRows}",
                unsupportedCurrencyCount);
            return null;
        }

        return aggregateWindow;
    }

    private static bool ShouldUseDailyAggregates(DashboardFilterContext filter) =>
        !filter.HasVendorFilter && !filter.HasPromoFilter;

    private static SuperAdminFinancialOverviewDto BuildOverviewFromAggregateWindow(
        DashboardFilterContext filter,
        CommerceHubDailyAggregateWindow aggregateWindow,
        DateTime generatedAtUtc,
        CommerceHubFinanceReconciliationResult reconciliationSnapshot)
    {
        var projection = ResolveAggregateProjection(aggregateWindow);
        var grossSales = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectGrossSales(row)));
        var grossCommission = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectGrossCommission(row)));
        var promoCost = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectPromoCost(row)));
        var refundAmount = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectRefundAmount(row)));
        var refundImpact = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectRefundImpact(row)));
        var orderCount = aggregateWindow.Rows.Sum(row => row.OrdersCount);
        var refundedOrdersCount = aggregateWindow.Rows.Sum(row => row.RefundedOrdersCount);
        var netCommission = RoundCurrency(grossCommission - promoCost);
        var netRevenue = RoundCurrency(grossCommission - promoCost - refundImpact);

        return new SuperAdminFinancialOverviewDto
        {
            FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
            ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            Currency = projection.Currency,
            BaseCurrencyCode = aggregateWindow.BaseCurrencyCode,
            ExchangeRateToBase = projection.ExchangeRateToBase,
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
            DataSource = "DAILY_AGGREGATE",
            IsEventuallyConsistent = true,
            ConsistencyModel = DefaultConsistencyModel,
            DataFreshnessNote = DefaultFreshnessNote,
            GeneratedAtUtc = generatedAtUtc,
            AggregatedThroughUtc = aggregateWindow.AggregatedThroughUtc,
            FinancialHealth = BuildFinancialHealth(reconciliationSnapshot),
            DataFreshness = BuildAggregateDataFreshness(filter, aggregateWindow),
            IntegrityPanel = BuildIntegrityPanel(reconciliationSnapshot)
        };
    }

    private static SuperAdminFinancialTrendsDto BuildTrendsFromAggregateWindow(
        DashboardFilterContext filter,
        SuperAdminFinancialTrendGroupBy groupBy,
        CommerceHubDailyAggregateWindow aggregateWindow,
        DateTime generatedAtUtc)
    {
        var projection = ResolveAggregateProjection(aggregateWindow);
        var buckets = InitializeTrendBuckets(filter, groupBy);

        foreach (var row in aggregateWindow.Rows)
        {
            var bucket = buckets[ToBucketStartUtc(row.DateUtc, groupBy)];
            bucket.GrossCommission += projection.SelectGrossCommission(row);
            bucket.PromoCost += projection.SelectPromoCost(row);
            bucket.CommissionReversals += projection.SelectRefundImpact(row);
            bucket.RefundAmount += projection.SelectRefundAmount(row);
            bucket.OrdersCount += row.OrdersCount;
        }

        return new SuperAdminFinancialTrendsDto
        {
            FromDateUtc = filter.FromDateUtc ?? GetDefaultRangeStart(),
            ToDateUtc = filter.ToDateUtc ?? GetDefaultRangeEnd(),
            SnapshotBoundaryUtc = filter.SnapshotBoundaryUtc,
            Currency = projection.Currency,
            BaseCurrencyCode = aggregateWindow.BaseCurrencyCode,
            ExchangeRateToBase = projection.ExchangeRateToBase,
            GroupBy = groupBy.ToString(),
            DataSource = "DAILY_AGGREGATE",
            IsEventuallyConsistent = true,
            ConsistencyModel = DefaultConsistencyModel,
            DataFreshnessNote = DefaultFreshnessNote,
            GeneratedAtUtc = generatedAtUtc,
            AggregatedThroughUtc = aggregateWindow.AggregatedThroughUtc,
            DataFreshness = BuildAggregateDataFreshness(filter, aggregateWindow),
            Points = buckets.Values
                .OrderBy(point => point.BucketStartUtc)
                .Select(MapTrendPoint)
                .ToList()
        };
    }

    private static AggregateRefundSnapshot BuildRefundSnapshotFromAggregateWindow(
        DashboardFilterContext filter,
        SuperAdminFinancialTrendGroupBy groupBy,
        CommerceHubDailyAggregateWindow aggregateWindow)
    {
        var projection = ResolveAggregateProjection(aggregateWindow);
        var buckets = InitializeRefundBuckets(filter, groupBy);

        foreach (var row in aggregateWindow.Rows)
        {
            var point = buckets[ToBucketStartUtc(row.DateUtc, groupBy)];
            point.RefundAmount += projection.SelectRefundAmount(row);
            point.RefundCount += row.RefundCount;
            point.PromoReversalAmount += projection.SelectPromoReversal(row);
        }

        var orderCount = aggregateWindow.Rows.Sum(row => row.OrdersCount);
        var refundedOrdersCount = aggregateWindow.Rows.Sum(row => row.RefundedOrdersCount);

        return new AggregateRefundSnapshot
        {
            Currency = projection.Currency,
            BaseCurrencyCode = aggregateWindow.BaseCurrencyCode,
            ExchangeRateToBase = projection.ExchangeRateToBase,
            OrdersCount = orderCount,
            TotalRefundAmount = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectRefundAmount(row))),
            TotalRefundCount = aggregateWindow.Rows.Sum(row => row.RefundCount),
            RefundedOrdersCount = refundedOrdersCount,
            RefundRate = orderCount == 0 ? 0m : RoundRatio(refundedOrdersCount / (decimal)orderCount),
            PromoReversalAmount = RoundCurrency(aggregateWindow.Rows.Sum(row => projection.SelectPromoReversal(row))),
            Points = buckets.Values
                .OrderBy(point => point.BucketStartUtc)
                .Select(MapRefundPoint)
                .ToList(),
            AggregatedThroughUtc = aggregateWindow.AggregatedThroughUtc
        };
    }

    private static AggregateProjection ResolveAggregateProjection(CommerceHubDailyAggregateWindow aggregateWindow)
    {
        var operationalRows = aggregateWindow.Rows
            .Where(IsOperationalAggregateRow)
            .ToList();
        var operationalCurrencies = operationalRows
            .Select(row => row.CurrencyCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (operationalCurrencies.Count <= 1)
        {
            var currency = operationalCurrencies.SingleOrDefault() ?? aggregateWindow.BaseCurrencyCode;
            var exchangeRateToBase = string.Equals(currency, aggregateWindow.BaseCurrencyCode, StringComparison.OrdinalIgnoreCase)
                ? 1m
                : ResolveAggregateExchangeRate(operationalRows, currency);

            return new AggregateProjection
            {
                Currency = currency,
                ExchangeRateToBase = exchangeRateToBase,
                SelectGrossSales = row => row.GrossSales,
                SelectGrossCommission = row => row.GrossCommission,
                SelectPromoCost = row => row.PromoCost,
                SelectPromoReversal = row => row.PromoReversalAmount,
                SelectRefundAmount = row => row.RefundAmount,
                SelectRefundImpact = row => row.RefundImpactOnRevenue
            };
        }

        return new AggregateProjection
        {
            Currency = aggregateWindow.BaseCurrencyCode,
            ExchangeRateToBase = 1m,
            SelectGrossSales = row => row.GrossSalesBase ?? 0m,
            SelectGrossCommission = row => row.GrossCommissionBase ?? 0m,
            SelectPromoCost = row => row.PromoCostBase ?? 0m,
            SelectPromoReversal = row => row.PromoReversalAmountBase ?? 0m,
            SelectRefundAmount = row => row.RefundAmountBase ?? 0m,
            SelectRefundImpact = row => row.RefundImpactOnRevenueBase ?? 0m
        };
    }

    private static bool IsOperationalAggregateRow(CommerceHubDailyFinancialAggregate row) =>
        row.GrossSales != 0m ||
        row.GrossCommission != 0m ||
        row.PromoCost != 0m ||
        row.PromoReversalAmount != 0m ||
        row.RefundAmount != 0m ||
        row.RefundImpactOnRevenue != 0m ||
        row.NetRevenue != 0m ||
        row.OrdersCount != 0 ||
        row.RefundCount != 0 ||
        row.RefundedOrdersCount != 0;

    private static decimal? ResolveAggregateExchangeRate(
        IEnumerable<CommerceHubDailyFinancialAggregate> rows,
        string currency)
    {
        var distinctRates = rows
            .Where(row =>
                string.Equals(row.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase) &&
                row.ExchangeRateToBase.HasValue)
            .Select(row => row.ExchangeRateToBase!.Value)
            .Distinct()
            .Take(2)
            .ToList();

        return distinctRates.Count == 1 ? distinctRates[0] : null;
    }

    private static DateTime ToBucketStartUtc(DateTime dateUtc, SuperAdminFinancialTrendGroupBy groupBy) =>
        groupBy == SuperAdminFinancialTrendGroupBy.Month
            ? DateTime.SpecifyKind(new DateTime(dateUtc.Year, dateUtc.Month, 1), DateTimeKind.Utc)
            : DateTime.SpecifyKind(dateUtc.Date, DateTimeKind.Utc);

    private sealed class AggregateProjection
    {
        public string Currency { get; init; } = DefaultCurrency;
        public decimal? ExchangeRateToBase { get; init; }
        public Func<CommerceHubDailyFinancialAggregate, decimal> SelectGrossSales { get; init; } = _ => 0m;
        public Func<CommerceHubDailyFinancialAggregate, decimal> SelectGrossCommission { get; init; } = _ => 0m;
        public Func<CommerceHubDailyFinancialAggregate, decimal> SelectPromoCost { get; init; } = _ => 0m;
        public Func<CommerceHubDailyFinancialAggregate, decimal> SelectPromoReversal { get; init; } = _ => 0m;
        public Func<CommerceHubDailyFinancialAggregate, decimal> SelectRefundAmount { get; init; } = _ => 0m;
        public Func<CommerceHubDailyFinancialAggregate, decimal> SelectRefundImpact { get; init; } = _ => 0m;
    }

    private sealed class AggregateRefundSnapshot
    {
        public string Currency { get; init; } = DefaultCurrency;
        public string BaseCurrencyCode { get; init; } = DefaultCurrency;
        public decimal? ExchangeRateToBase { get; init; }
        public int OrdersCount { get; init; }
        public decimal TotalRefundAmount { get; init; }
        public int TotalRefundCount { get; init; }
        public int RefundedOrdersCount { get; init; }
        public decimal RefundRate { get; init; }
        public decimal PromoReversalAmount { get; init; }
        public IReadOnlyList<SuperAdminRefundTrendPointDto> Points { get; init; } = Array.Empty<SuperAdminRefundTrendPointDto>();
        public DateTime? AggregatedThroughUtc { get; init; }
    }
}
