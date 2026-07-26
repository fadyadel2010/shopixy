using PointOfSale.Core.Analytics.Attribution;

namespace PointOfSale.Infrastructure.Attribution;

/// <summary>
/// Interface for Attribution Context storage operations.
/// 
/// STORAGE STRATEGY:
/// - Redis (Hot): Real-time lookup during checkout, TTL-based
/// - MongoDB (Cold): Append-only historical record
/// 
/// OPERATIONS:
/// - Store: Called on PageView with UTM params
/// - Get First Touch: Returns earliest context for session
/// - Get Last Touch: Returns latest context for session
/// </summary>
public interface IAttributionContextStore
{
    /// <summary>
    /// Stores an attribution context for a session.
    /// Called when a PageView event contains UTM parameters.
    /// </summary>
    Task StoreContextAsync(
        AttributionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the first touch attribution context for a session.
    /// Returns the earliest captured context for the given session.
    /// </summary>
    Task<AttributionContext?> GetFirstTouchContextAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last touch attribution context for a session.
    /// Returns the most recently captured context for the given session.
    /// </summary>
    Task<AttributionContext?> GetLastTouchContextAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all attribution contexts for a session (for debugging/analysis).
    /// Ordered by CapturedAtUtc ascending.
    /// </summary>
    Task<List<AttributionContext>> GetAllContextsForSessionAsync(
        Guid tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
