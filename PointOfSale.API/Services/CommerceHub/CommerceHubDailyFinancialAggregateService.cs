using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubDailyFinancialAggregateService
{
    Task<CommerceHubDailyAggregateWindow> GetWindowAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime? snapshotBoundaryUtc = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommerceHubDailyFinancialAggregate>> RecalculateWindowAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime? snapshotBoundaryUtc = null,
        CancellationToken cancellationToken = default);

    Task RefreshWindowAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default);
}

public sealed record CommerceHubDailyAggregateWindow(
    IReadOnlyList<CommerceHubDailyFinancialAggregate> Rows,
    bool CoversRequestedWindow,
    bool IsFresh,
    DateTime? LatestCalculatedAtUtc,
    DateTime? AggregatedThroughUtc,
    string BaseCurrencyCode)
{
    public int InvalidRowCount { get; init; }
    public bool HasPendingJobs { get; init; }
    public DateTime SnapshotBoundaryUtc { get; init; }
    public DateTime? LastValidatedAtUtc { get; init; }
}

public sealed class CommerceHubDailyFinancialAggregateService : ICommerceHubDailyFinancialAggregateService
{
    public const string DefaultBaseCurrencyCode = "EGP";
    private static readonly TimeSpan AggregateFreshnessThreshold = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubDailyFinancialAggregateService> _logger;

    public CommerceHubDailyFinancialAggregateService(
        ApplicationDbContext dbContext,
        ILogger<CommerceHubDailyFinancialAggregateService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CommerceHubDailyAggregateWindow> GetWindowAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime? snapshotBoundaryUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (fromDate, toDate) = NormalizeDateRange(fromDateUtc, toDateUtc);
        var snapshotBoundary = NormalizeSnapshotBoundary(snapshotBoundaryUtc);

        var rows = await _dbContext.CommerceHubDailyFinancialAggregates
            .AsNoTracking()
            .Where(row => row.DateUtc >= fromDate && row.DateUtc <= toDate)
            .OrderBy(row => row.DateUtc)
            .ThenBy(row => row.CurrencyCode)
            .ToListAsync(cancellationToken);

        var expectedDays = (int)(toDate - fromDate).TotalDays + 1;
        var baseRows = rows
            .Where(row => string.Equals(row.CurrencyCode, DefaultBaseCurrencyCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var coversRequestedWindow = baseRows
            .Select(row => row.DateUtc.Date)
            .Distinct()
            .Count() == expectedDays;

        var latestCalculatedAtUtc = rows.Count == 0
            ? null
            : rows.Max(row => (DateTime?)row.CalculatedAtUtc);
        var oldestCalculatedAtUtc = rows.Count == 0
            ? null
            : rows.Min(row => (DateTime?)row.CalculatedAtUtc);
        var aggregatedThroughUtc = rows.Count == 0
            ? null
            : rows.Min(row => (DateTime?)row.AggregatedThroughUtc);
        var lastValidatedAtUtc = rows.Count == 0
            ? null
            : rows.Min(row => row.LastValidatedAtUtc);
        var invalidRowCount = rows.Count(row => !row.IsValid);
        var hasPendingJobs = aggregatedThroughUtc.HasValue &&
                             await HasPendingJobsAsync(
                                 aggregatedThroughUtc.Value,
                                 snapshotBoundary,
                                 cancellationToken);
        var isFresh =
            coversRequestedWindow &&
            invalidRowCount == 0 &&
            !hasPendingJobs &&
            oldestCalculatedAtUtc.HasValue &&
            snapshotBoundary - oldestCalculatedAtUtc.Value <= AggregateFreshnessThreshold;

        return new CommerceHubDailyAggregateWindow(
            rows,
            coversRequestedWindow,
            isFresh,
            latestCalculatedAtUtc,
            aggregatedThroughUtc,
            DefaultBaseCurrencyCode)
        {
            InvalidRowCount = invalidRowCount,
            HasPendingJobs = hasPendingJobs,
            SnapshotBoundaryUtc = snapshotBoundary,
            LastValidatedAtUtc = lastValidatedAtUtc
        };
    }

    public async Task<IReadOnlyList<CommerceHubDailyFinancialAggregate>> RecalculateWindowAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime? snapshotBoundaryUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (fromDate, toDate) = NormalizeDateRange(fromDateUtc, toDateUtc);
        var snapshotBoundary = NormalizeSnapshotBoundary(snapshotBoundaryUtc);
        var aggregateMap = await BuildAggregateMapAsync(
            fromDate,
            toDate,
            snapshotBoundary,
            snapshotBoundary,
            cancellationToken);

        return aggregateMap.Values
            .OrderBy(row => row.DateUtc)
            .ThenBy(row => row.CurrencyCode)
            .ToList();
    }

    public async Task RefreshWindowAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        CancellationToken cancellationToken = default)
    {
        var (fromDate, toDate) = NormalizeDateRange(fromDateUtc, toDateUtc);
        var calculatedAtUtc = DateTime.UtcNow;
        var aggregateMap = await BuildAggregateMapAsync(
            fromDate,
            toDate,
            calculatedAtUtc,
            calculatedAtUtc,
            cancellationToken);

        var existingRows = await _dbContext.CommerceHubDailyFinancialAggregates
            .Where(row => row.DateUtc >= fromDate && row.DateUtc <= toDate)
            .ToListAsync(cancellationToken);

        var existingMap = existingRows.ToDictionary(
            row => CreateKey(row.DateUtc, row.CurrencyCode),
            row => row,
            StringComparer.OrdinalIgnoreCase);

        foreach (var aggregate in aggregateMap.Values)
        {
            var key = CreateKey(aggregate.DateUtc, aggregate.CurrencyCode);
            if (existingMap.TryGetValue(key, out var existing))
            {
                CopyAggregateValues(aggregate, existing);
            }
            else
            {
                _dbContext.CommerceHubDailyFinancialAggregates.Add(aggregate);
            }
        }

        foreach (var existing in existingRows)
        {
            var key = CreateKey(existing.DateUtc, existing.CurrencyCode);
            if (!aggregateMap.ContainsKey(key))
            {
                _dbContext.CommerceHubDailyFinancialAggregates.Remove(existing);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Refreshed CommerceHub daily financial aggregates | FromDateUtc: {FromDateUtc} | ToDateUtc: {ToDateUtc} | Rows: {RowCount}",
            fromDate,
            toDate,
            aggregateMap.Count);
    }

    private async Task<Dictionary<string, CommerceHubDailyFinancialAggregate>> BuildAggregateMapAsync(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime snapshotBoundaryUtc,
        DateTime calculatedAtUtc,
        CancellationToken cancellationToken)
    {
        var windowStartUtc = fromDateUtc;
        var windowEndExclusiveUtc = toDateUtc.AddDays(1);
        var aggregateMap = InitializeBaseCurrencyRows(fromDateUtc, toDateUtc, snapshotBoundaryUtc, calculatedAtUtc);

        var orderCommissionRows = await (
                from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
                join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
                where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                      && transaction.EntityId == null
                      && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                      && transaction.TransactionType == CommerceHubFinancialTransactionType.Commission
                      && transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                      && !subOrder.IsDeleted
                      && !marketplaceOrder.IsDeleted
                      && transaction.CreatedAtUtc >= windowStartUtc
                      && transaction.CreatedAtUtc < windowEndExclusiveUtc
                      && transaction.CreatedAtUtc <= snapshotBoundaryUtc
                group new { transaction, subOrder } by new
                {
                    DateUtc = transaction.CreatedAtUtc.Date,
                    transaction.Currency
                }
                into grouped
                select new AggregateRow
                {
                    DateUtc = grouped.Key.DateUtc,
                    CurrencyCode = grouped.Key.Currency,
                    GrossSales = grouped.Sum(row => (decimal?)row.subOrder.Total) ?? 0m,
                    GrossCommission = grouped.Sum(row => (decimal?)row.transaction.Amount) ?? 0m,
                    OrdersCount = grouped.Count()
                })
            .ToListAsync(cancellationToken);

        var orderPromotionRows = await (
                from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id
                join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
                where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                      && transaction.EntityId == null
                      && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                      && transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion
                      && transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                      && !subOrder.IsDeleted
                      && !marketplaceOrder.IsDeleted
                      && transaction.CreatedAtUtc >= windowStartUtc
                      && transaction.CreatedAtUtc < windowEndExclusiveUtc
                      && transaction.CreatedAtUtc <= snapshotBoundaryUtc
                group transaction by new
                {
                    DateUtc = transaction.CreatedAtUtc.Date,
                    transaction.Currency
                }
                into grouped
                select new AggregateRow
                {
                    DateUtc = grouped.Key.DateUtc,
                    CurrencyCode = grouped.Key.Currency,
                    PromoCost = grouped.Sum(row => (decimal?)row.Amount) ?? 0m
                })
            .ToListAsync(cancellationToken);

        var refundCommissionRows = await (
                from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                join refund in _dbContext.CommerceHubRefunds.AsNoTracking() on transaction.ReferenceId equals refund.Id
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
                join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
                where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                      && transaction.EntityId == null
                      && transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund
                      && transaction.TransactionType == CommerceHubFinancialTransactionType.Commission
                      && transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                      && refund.Status == CommerceHubRefundStatus.Completed
                      && !refund.IsDeleted
                      && !subOrder.IsDeleted
                      && !marketplaceOrder.IsDeleted
                      && transaction.CreatedAtUtc >= windowStartUtc
                      && transaction.CreatedAtUtc < windowEndExclusiveUtc
                      && transaction.CreatedAtUtc <= snapshotBoundaryUtc
                group transaction by new
                {
                    DateUtc = transaction.CreatedAtUtc.Date,
                    transaction.Currency
                }
                into grouped
                select new AggregateRow
                {
                    DateUtc = grouped.Key.DateUtc,
                    CurrencyCode = grouped.Key.Currency,
                    RefundImpactOnRevenue = grouped.Sum(row => (decimal?)row.Amount) ?? 0m
                })
            .ToListAsync(cancellationToken);

        var refundPromotionRows = await (
                from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                join refund in _dbContext.CommerceHubRefunds.AsNoTracking() on transaction.ReferenceId equals refund.Id
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
                join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
                where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                      && transaction.EntityId == null
                      && transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund
                      && transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion
                      && transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                      && refund.Status == CommerceHubRefundStatus.Completed
                      && !refund.IsDeleted
                      && !subOrder.IsDeleted
                      && !marketplaceOrder.IsDeleted
                      && transaction.CreatedAtUtc >= windowStartUtc
                      && transaction.CreatedAtUtc < windowEndExclusiveUtc
                      && transaction.CreatedAtUtc <= snapshotBoundaryUtc
                group transaction by new
                {
                    DateUtc = transaction.CreatedAtUtc.Date,
                    transaction.Currency
                }
                into grouped
                select new AggregateRow
                {
                    DateUtc = grouped.Key.DateUtc,
                    CurrencyCode = grouped.Key.Currency,
                    PromoReversalAmount = grouped.Sum(row => (decimal?)row.Amount) ?? 0m
                })
            .ToListAsync(cancellationToken);

        var refundMetricRows = await (
                from refund in _dbContext.CommerceHubRefunds.AsNoTracking()
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on refund.SubOrderId equals subOrder.Id
                join marketplaceOrder in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking() on subOrder.MarketplaceOrderId equals marketplaceOrder.Id
                let effectiveOccurredAt = refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt
                where refund.Status == CommerceHubRefundStatus.Completed
                      && !refund.IsDeleted
                      && !subOrder.IsDeleted
                      && !marketplaceOrder.IsDeleted
                      && effectiveOccurredAt >= windowStartUtc
                      && effectiveOccurredAt < windowEndExclusiveUtc
                      && effectiveOccurredAt <= snapshotBoundaryUtc
                group refund by new
                {
                    DateUtc = effectiveOccurredAt.Date,
                    refund.Currency
                }
                into grouped
                select new AggregateRow
                {
                    DateUtc = grouped.Key.DateUtc,
                    CurrencyCode = grouped.Key.Currency,
                    RefundAmount = grouped.Sum(row => (decimal?)row.Amount) ?? 0m,
                    RefundCount = grouped.Count(),
                    RefundedOrdersCount = grouped.Select(row => row.SubOrderId).Distinct().Count()
                })
            .ToListAsync(cancellationToken);

        MergeAggregateRows(aggregateMap, orderCommissionRows);
        MergeAggregateRows(aggregateMap, orderPromotionRows);
        MergeAggregateRows(aggregateMap, refundCommissionRows);
        MergeAggregateRows(aggregateMap, refundPromotionRows);
        MergeAggregateRows(aggregateMap, refundMetricRows);

        foreach (var row in aggregateMap.Values)
        {
            row.BaseCurrencyCode = DefaultBaseCurrencyCode;
            row.AggregatedThroughUtc = snapshotBoundaryUtc;
            row.CalculatedAtUtc = calculatedAtUtc;
            row.IsValid = true;
            row.LastValidatedAtUtc = calculatedAtUtc;
            row.ValidationError = null;

            row.PromoCost = RoundCurrency(Math.Max(0m, row.PromoCost - row.PromoReversalAmount));
            row.NetRevenue = RoundCurrency(row.GrossCommission - row.PromoCost - row.RefundImpactOnRevenue);

            var exchangeRateToBase = ResolveExchangeRateToBase(row.CurrencyCode, row.BaseCurrencyCode);
            row.ExchangeRateToBase = exchangeRateToBase;
            row.GrossSalesBase = NormalizeToBase(row.GrossSales, exchangeRateToBase);
            row.GrossCommissionBase = NormalizeToBase(row.GrossCommission, exchangeRateToBase);
            row.PromoCostBase = NormalizeToBase(row.PromoCost, exchangeRateToBase);
            row.PromoReversalAmountBase = NormalizeToBase(row.PromoReversalAmount, exchangeRateToBase);
            row.RefundAmountBase = NormalizeToBase(row.RefundAmount, exchangeRateToBase);
            row.RefundImpactOnRevenueBase = NormalizeToBase(row.RefundImpactOnRevenue, exchangeRateToBase);
            row.NetRevenueBase = NormalizeToBase(row.NetRevenue, exchangeRateToBase);
        }

        return aggregateMap;
    }

    private async Task<bool> HasPendingJobsAsync(
        DateTime aggregatedThroughUtc,
        DateTime snapshotBoundaryUtc,
        CancellationToken cancellationToken)
    {
        if (aggregatedThroughUtc >= snapshotBoundaryUtc)
        {
            return false;
        }

        var hasPendingLedgerTransactions = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .AnyAsync(
                transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.CreatedAtUtc > aggregatedThroughUtc &&
                    transaction.CreatedAtUtc <= snapshotBoundaryUtc &&
                    (
                        (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                         transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                         transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                        (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                         transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                         transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                        (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                         transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                         transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                        (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                         transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                         transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)),
                cancellationToken);

        if (hasPendingLedgerTransactions)
        {
            return true;
        }

        return await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .AnyAsync(
                refund =>
                    refund.Status == CommerceHubRefundStatus.Completed &&
                    !refund.IsDeleted &&
                    (refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt) > aggregatedThroughUtc &&
                    (refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt) <= snapshotBoundaryUtc,
                cancellationToken);
    }

    private static Dictionary<string, CommerceHubDailyFinancialAggregate> InitializeBaseCurrencyRows(
        DateTime fromDateUtc,
        DateTime toDateUtc,
        DateTime aggregatedThroughUtc,
        DateTime calculatedAtUtc)
    {
        var rows = new Dictionary<string, CommerceHubDailyFinancialAggregate>(StringComparer.OrdinalIgnoreCase);

        for (var date = fromDateUtc; date <= toDateUtc; date = date.AddDays(1))
        {
            var normalizedDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            var row = new CommerceHubDailyFinancialAggregate
            {
                DateUtc = normalizedDate,
                CurrencyCode = DefaultBaseCurrencyCode,
                BaseCurrencyCode = DefaultBaseCurrencyCode,
                ExchangeRateToBase = 1m,
                AggregatedThroughUtc = aggregatedThroughUtc,
                CalculatedAtUtc = calculatedAtUtc,
                IsValid = true,
                LastValidatedAtUtc = calculatedAtUtc,
                ValidationError = null
            };

            rows[CreateKey(row.DateUtc, row.CurrencyCode)] = row;
        }

        return rows;
    }

    private static void MergeAggregateRows(
        IDictionary<string, CommerceHubDailyFinancialAggregate> aggregates,
        IEnumerable<AggregateRow> rows)
    {
        foreach (var row in rows)
        {
            var currencyCode = FinancialCurrencyGuard.RequireEgpCurrency(
                row.CurrencyCode,
                "commercehub.daily_financial_aggregate.currency",
                $"date={row.DateUtc:yyyy-MM-dd}");
            var key = CreateKey(row.DateUtc, currencyCode);
            if (!aggregates.TryGetValue(key, out var aggregate))
            {
                aggregate = new CommerceHubDailyFinancialAggregate
                {
                    DateUtc = row.DateUtc,
                    CurrencyCode = currencyCode,
                    BaseCurrencyCode = DefaultBaseCurrencyCode
                };
                aggregates[key] = aggregate;
            }

            aggregate.GrossSales = RoundCurrency(aggregate.GrossSales + row.GrossSales);
            aggregate.GrossCommission = RoundCurrency(aggregate.GrossCommission + row.GrossCommission);
            aggregate.PromoCost = RoundCurrency(aggregate.PromoCost + row.PromoCost);
            aggregate.PromoReversalAmount = RoundCurrency(aggregate.PromoReversalAmount + row.PromoReversalAmount);
            aggregate.RefundAmount = RoundCurrency(aggregate.RefundAmount + row.RefundAmount);
            aggregate.RefundImpactOnRevenue = RoundCurrency(aggregate.RefundImpactOnRevenue + row.RefundImpactOnRevenue);
            aggregate.OrdersCount += row.OrdersCount;
            aggregate.RefundCount += row.RefundCount;
            aggregate.RefundedOrdersCount += row.RefundedOrdersCount;
        }
    }

    private static void CopyAggregateValues(
        CommerceHubDailyFinancialAggregate source,
        CommerceHubDailyFinancialAggregate target)
    {
        target.BaseCurrencyCode = source.BaseCurrencyCode;
        target.ExchangeRateToBase = source.ExchangeRateToBase;
        target.GrossSales = source.GrossSales;
        target.GrossCommission = source.GrossCommission;
        target.PromoCost = source.PromoCost;
        target.PromoReversalAmount = source.PromoReversalAmount;
        target.RefundAmount = source.RefundAmount;
        target.RefundImpactOnRevenue = source.RefundImpactOnRevenue;
        target.NetRevenue = source.NetRevenue;
        target.GrossSalesBase = source.GrossSalesBase;
        target.GrossCommissionBase = source.GrossCommissionBase;
        target.PromoCostBase = source.PromoCostBase;
        target.PromoReversalAmountBase = source.PromoReversalAmountBase;
        target.RefundAmountBase = source.RefundAmountBase;
        target.RefundImpactOnRevenueBase = source.RefundImpactOnRevenueBase;
        target.NetRevenueBase = source.NetRevenueBase;
        target.OrdersCount = source.OrdersCount;
        target.RefundCount = source.RefundCount;
        target.RefundedOrdersCount = source.RefundedOrdersCount;
        target.AggregatedThroughUtc = source.AggregatedThroughUtc;
        target.CalculatedAtUtc = source.CalculatedAtUtc;
        target.IsValid = source.IsValid;
        target.LastValidatedAtUtc = source.LastValidatedAtUtc;
        target.ValidationError = source.ValidationError;
    }

    private static (DateTime FromDateUtc, DateTime ToDateUtc) NormalizeDateRange(DateTime fromDateUtc, DateTime toDateUtc)
    {
        var normalizedFrom = DateTime.SpecifyKind(fromDateUtc.Date, DateTimeKind.Utc);
        var normalizedTo = DateTime.SpecifyKind(toDateUtc.Date, DateTimeKind.Utc);

        return normalizedFrom <= normalizedTo
            ? (normalizedFrom, normalizedTo)
            : (normalizedTo, normalizedFrom);
    }

    private static DateTime NormalizeSnapshotBoundary(DateTime? snapshotBoundaryUtc)
    {
        var value = snapshotBoundaryUtc ?? DateTime.UtcNow;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime()
        };
    }

    private static string CreateKey(DateTime dateUtc, string currencyCode) =>
        $"{dateUtc:yyyy-MM-dd}|{currencyCode}";

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static decimal? ResolveExchangeRateToBase(string currencyCode, string baseCurrencyCode) =>
        string.Equals(currencyCode, baseCurrencyCode, StringComparison.OrdinalIgnoreCase)
            ? 1m
            : null;

    private static decimal? NormalizeToBase(decimal amount, decimal? exchangeRateToBase) =>
        exchangeRateToBase.HasValue
            ? RoundCurrency(amount * exchangeRateToBase.Value)
            : null;

    private sealed class AggregateRow
    {
        public DateTime DateUtc { get; init; }
        public string CurrencyCode { get; init; } = DefaultBaseCurrencyCode;
        public decimal GrossSales { get; init; }
        public decimal GrossCommission { get; init; }
        public decimal PromoCost { get; init; }
        public decimal PromoReversalAmount { get; init; }
        public decimal RefundAmount { get; init; }
        public decimal RefundImpactOnRevenue { get; init; }
        public int OrdersCount { get; init; }
        public int RefundCount { get; init; }
        public int RefundedOrdersCount { get; init; }
    }
}

public sealed class CommerceHubDailyFinancialAggregateRefreshService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private const int InitialBackfillDays = 365;
    private const int RollingRefreshDays = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubDailyFinancialAggregateRefreshService> _logger;

    public CommerceHubDailyFinancialAggregateRefreshService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubDailyFinancialAggregateRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[FinanceAggregates] Daily financial aggregate refresh service started");

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RefreshAsync(InitialBackfillDays, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, stoppingToken);
                await RefreshAsync(RollingRefreshDays, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FinanceAggregates] Aggregate refresh service failed unexpectedly");
        }

        _logger.LogInformation("[FinanceAggregates] Daily financial aggregate refresh service stopped");
    }

    private async Task RefreshAsync(int lookbackDays, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var aggregateService = scope.ServiceProvider.GetRequiredService<ICommerceHubDailyFinancialAggregateService>();

        var todayUtc = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var fromDateUtc = todayUtc.AddDays(-(lookbackDays - 1));

        await aggregateService.RefreshWindowAsync(fromDateUtc, todayUtc, cancellationToken);
    }
}
