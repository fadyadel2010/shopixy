using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.MongoDB.Services;

public enum AnalyticsEventStoreOutcome
{
    Failed = 0,
    Stored = 1,
    AlreadyExists = 2
}

/// <summary>
/// Interface for analytics cold storage (MongoDB).
/// 
/// PURPOSE:
/// - Historical analytics (append-only)
/// - Long-term storage for reporting
/// - Aggregation queries for dashboards
/// 
/// NOT FOR:
/// - Realtime counters (use Redis)
/// - Live dashboards (use Redis)
/// 
/// BEHAVIOR:
/// - GetEventsByTimeRangeAsync performs fail-fast validation on pagination parameters (skip, take)
/// - Invalid pagination parameters throw ArgumentOutOfRangeException before database operations
/// </summary>
public interface IAnalyticsColdStoreService
{
    /// <summary>
    /// Stores an analytics event in cold storage (append-only).
    /// </summary>
    /// <param name="analyticsEvent">The event to store</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if stored successfully</returns>
    Task<bool> StoreEventAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default);

    Task<AnalyticsEventStoreOutcome> StoreEventWithOutcomeAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an event already exists (for idempotency).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="eventId">Event ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if event already exists</returns>
    Task<bool> EventExistsAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events by tenant in a time range (for historical queries).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="startUtc">Start time (inclusive)</param>
    /// <param name="endUtc">End time (exclusive)</param>
    /// <param name="eventType">Optional filter by event type</param>
    /// <param name="skip">Pagination offset (must be non-negative)</param>
    /// <param name="take">Pagination limit (must be non-negative)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of events. Returns an empty list immediately without querying the store when take is 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when skip or take is negative</exception>
    Task<List<AnalyticsEvent>> GetEventsByTimeRangeAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AnalyticsEventType? eventType = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts events by type for a tenant in a time range.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="startUtc">Start time (inclusive)</param>
    /// <param name="endUtc">End time (exclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of event type to count</returns>
    Task<Dictionary<AnalyticsEventType, long>> CountEventsByTypeAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);
}
