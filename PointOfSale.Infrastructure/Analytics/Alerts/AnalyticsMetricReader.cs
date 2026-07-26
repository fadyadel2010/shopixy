using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Analytics;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// Redis-based implementation of IAnalyticsMetricReader.
/// 
/// Reads from Redis counters populated by AnalyticsEventConsumer (Phase 1).
/// Supports both direct counter reads and derived metrics.
/// </summary>
public class AnalyticsMetricReader : IAnalyticsMetricReader
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IStorefrontAnalyticsAuthorityReader _authorityReader;
    private readonly ILogger<AnalyticsMetricReader> _logger;
    private readonly string _instanceName;

    public AnalyticsMetricReader(
        IConnectionMultiplexer redis,
        IStorefrontAnalyticsAuthorityReader authorityReader,
        IOptions<RedisSettings> settings,
        ILogger<AnalyticsMetricReader> logger)
    {
        _redis = redis;
        _authorityReader = authorityReader;
        _instanceName = settings.Value.InstanceName;
        _logger = logger;
    }

    public async Task<decimal> GetMetricValueAsync(
        Guid tenantId,
        AnalyticsMetric metric,
        int windowMinutes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return metric switch
            {
                // Simple metrics - direct counter reads
                AnalyticsMetric.PageViews => await GetCounterValueAsync(tenantId, "pageview", windowMinutes, cancellationToken),
                AnalyticsMetric.ProductViews => await GetCounterValueAsync(tenantId, "productview", windowMinutes, cancellationToken),
                AnalyticsMetric.AddToCart => await GetCounterValueAsync(tenantId, "addtocart", windowMinutes, cancellationToken),
                AnalyticsMetric.BeginCheckout => await GetCounterValueAsync(tenantId, "begincheckout", windowMinutes, cancellationToken),
                AnalyticsMetric.PurchaseCompleted => await GetPurchaseCountAsync(tenantId, windowMinutes, cancellationToken),
                AnalyticsMetric.PaymentFailed => await GetCounterValueAsync(tenantId, "paymentfailed", windowMinutes, cancellationToken),

                // Derived metrics - computed from multiple counters
                AnalyticsMetric.ConversionRate => await CalculateConversionRateAsync(tenantId, windowMinutes, cancellationToken),
                AnalyticsMetric.CartAbandonmentRate => await CalculateCartAbandonmentRateAsync(tenantId, windowMinutes, cancellationToken),
                AnalyticsMetric.ProductToCartRate => await CalculateProductToCartRateAsync(tenantId, windowMinutes, cancellationToken),

                _ => 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Metrics] Failed to read metric | TenantId: {TenantId} | Metric: {Metric}",
                tenantId, metric);
            return 0;
        }
    }

    public async Task<Dictionary<AnalyticsMetric, decimal>> GetMetricValuesAsync(
        Guid tenantId,
        IEnumerable<AnalyticsMetric> metrics,
        int windowMinutes,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<AnalyticsMetric, decimal>();

        foreach (var metric in metrics)
        {
            results[metric] = await GetMetricValueAsync(tenantId, metric, windowMinutes, cancellationToken);
        }

        return results;
    }

    public async Task<long> GetCounterValueAsync(
        Guid tenantId,
        string counterName,
        int windowMinutes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var now = DateTime.UtcNow;
            long total = 0;

            // Sum up hourly buckets that fall within the window
            // Phase 1 stores counters with hourly granularity: yyyyMMddHH
            var windowStart = now.AddMinutes(-windowMinutes);

            // Get all hours that overlap with our window
            var currentHour = now;
            while (currentHour >= windowStart)
            {
                var windowKey = currentHour.ToString("yyyyMMddHH");
                var key = $"{_instanceName}:analytics:tenant:{tenantId:N}:counter:{counterName}:{windowKey}";

                var value = await db.StringGetAsync(key);
                if (value.HasValue && long.TryParse(value, out var count))
                {
                    total += count;
                }

                currentHour = currentHour.AddHours(-1);
            }

            return total;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Metrics] Failed to read counter | TenantId: {TenantId} | Counter: {Counter}",
                tenantId, counterName);
            return 0;
        }
    }

    #region Derived Metrics

    private async Task<decimal> CalculateConversionRateAsync(
        Guid tenantId,
        int windowMinutes,
        CancellationToken cancellationToken)
    {
        var pageViews = await GetCounterValueAsync(tenantId, "pageview", windowMinutes, cancellationToken);
        var purchases = await GetPurchaseCountAsync(tenantId, windowMinutes, cancellationToken);

        if (pageViews == 0) return 0;
        return Math.Round((decimal)purchases / pageViews * 100, 2);
    }

    private async Task<decimal> CalculateCartAbandonmentRateAsync(
        Guid tenantId,
        int windowMinutes,
        CancellationToken cancellationToken)
    {
        var checkouts = await GetCounterValueAsync(tenantId, "begincheckout", windowMinutes, cancellationToken);
        var purchases = await GetPurchaseCountAsync(tenantId, windowMinutes, cancellationToken);

        if (checkouts == 0) return 0;
        
        var abandoned = checkouts - purchases;
        if (abandoned < 0) abandoned = 0; // Edge case: more purchases than checkouts (data from previous period)
        
        return Math.Round((decimal)abandoned / checkouts * 100, 2);
    }

    private async Task<decimal> CalculateProductToCartRateAsync(
        Guid tenantId,
        int windowMinutes,
        CancellationToken cancellationToken)
    {
        var productViews = await GetCounterValueAsync(tenantId, "productview", windowMinutes, cancellationToken);
        var addToCarts = await GetCounterValueAsync(tenantId, "addtocart", windowMinutes, cancellationToken);

        if (productViews == 0) return 0;
        return Math.Round((decimal)addToCarts / productViews * 100, 2);
    }

    private async Task<long> GetPurchaseCountAsync(
        Guid tenantId,
        int windowMinutes,
        CancellationToken cancellationToken)
    {
        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.AddMinutes(-Math.Max(1, windowMinutes));
        var snapshot = await _authorityReader.GetCommerceSnapshotAsync(
            tenantId,
            startUtc,
            endUtc,
            cancellationToken);

        return snapshot.PurchaseCount;
    }

    #endregion
}
