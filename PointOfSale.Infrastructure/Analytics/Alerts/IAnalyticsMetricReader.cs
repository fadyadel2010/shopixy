using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// Service for reading analytics metrics from Redis counters.
/// 
/// DATA SOURCE:
/// - Redis counters written by AnalyticsEventConsumer (Phase 1)
/// - Sliding time windows ONLY
/// - NO MongoDB reads
/// - NO recomputation from raw events
/// 
/// METRICS:
/// - Simple: Direct counter reads (PageViews, AddToCart, etc.)
/// - Derived: Computed from multiple counters (ConversionRate, CartAbandonmentRate)
/// </summary>
public interface IAnalyticsMetricReader
{
    /// <summary>
    /// Gets the current value of a metric for a tenant over a time window.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="metric">The metric to read</param>
    /// <param name="windowMinutes">Time window in minutes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The metric value (count or percentage)</returns>
    Task<decimal> GetMetricValueAsync(
        Guid tenantId,
        AnalyticsMetric metric,
        int windowMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple metrics for a tenant at once.
    /// More efficient than multiple individual calls.
    /// </summary>
    Task<Dictionary<AnalyticsMetric, decimal>> GetMetricValuesAsync(
        Guid tenantId,
        IEnumerable<AnalyticsMetric> metrics,
        int windowMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a raw counter value from Redis.
    /// </summary>
    Task<long> GetCounterValueAsync(
        Guid tenantId,
        string counterName,
        int windowMinutes,
        CancellationToken cancellationToken = default);
}
