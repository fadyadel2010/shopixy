using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Interface for analytics hot storage (Redis).
/// 
/// PURPOSE:
/// - Realtime counters and metrics
/// - Recent events for live dashboards
/// - TTL-based automatic cleanup
/// 
/// NOT FOR:
/// - Historical analytics (use MongoDB)
/// - Long-term storage (TTL expires data)
/// </summary>
public interface IAnalyticsHotStoreService
{
    /// <summary>
    /// Stores an analytics event in hot storage.
    /// TTL: 24 hours by default.
    /// </summary>
    /// <param name="analyticsEvent">The event to store</param>
    /// <param name="ttl">Optional custom TTL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if stored successfully</returns>
    Task<bool> StoreEventAsync(
        AnalyticsEvent analyticsEvent,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an event has already been processed (for idempotency).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="eventId">Event ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if event was already processed</returns>
    Task<bool> IsEventProcessedAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an event as processed (for idempotency).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="eventId">Event ID</param>
    /// <param name="ttl">How long to remember this event ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task MarkEventProcessedAsync(
        Guid tenantId,
        Guid eventId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments a realtime counter (e.g., page views per hour).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="counterName">Counter name (e.g., "pageviews")</param>
    /// <param name="timeWindow">Time window for the counter</param>
    /// <param name="increment">Amount to increment (default 1)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New counter value</returns>
    Task<long> IncrementCounterAsync(
        Guid tenantId,
        string counterName,
        DateTime timeWindow,
        long increment = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent events for a tenant (for live dashboard).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="eventType">Optional filter by event type</param>
    /// <param name="limit">Maximum events to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of recent events</returns>
    Task<List<AnalyticsEvent>> GetRecentEventsAsync(
        Guid tenantId,
        AnalyticsEventType? eventType = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Increments a page view counter for a specific path.
    /// </summary>
    Task<long> IncrementPageViewCounterAsync(
        Guid tenantId,
        string pagePath,
        DateTime timeWindow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments a product view counter.
    /// </summary>
    Task<long> IncrementProductViewCounterAsync(
        Guid tenantId,
        Guid productId,
        DateTime timeWindow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments an add-to-cart counter.
    /// </summary>
    Task<long> IncrementAddToCartCounterAsync(
        Guid tenantId,
        Guid productId,
        DateTime timeWindow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top viewed pages.
    /// </summary>
    Task<List<TopPageStats>> GetTopPagesAsync(
        Guid tenantId,
        int windowMinutes,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top viewed products.
    /// </summary>
    Task<List<TopProductStats>> GetTopViewedProductsAsync(
        Guid tenantId,
        int windowMinutes,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top added-to-cart products.
    /// </summary>
    Task<List<TopProductStats>> GetTopAddedToCartProductsAsync(
        Guid tenantId,
        int windowMinutes,
        int limit = 10,
        CancellationToken cancellationToken = default);
}
