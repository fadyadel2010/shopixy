namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Store config cache service interface.
/// Phase 7.1: Theme & Store Config System
/// 
/// Caching Strategy:
/// - TTL: 30 minutes
/// - Invalidate on admin update
/// - Graceful degradation (cache miss → DB)
/// 
/// Note: Uses string serialization to avoid cross-project DTO dependencies.
/// Serialization/deserialization is handled by the consumer.
/// </summary>
public interface IStoreConfigCacheService
{
    /// <summary>
    /// Whether Redis is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets store config from cache as JSON string.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached store config JSON or null if not found</returns>
    Task<string?> GetStoreConfigJsonAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets store config in cache with default TTL (30 minutes).
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="configJson">Store config as JSON string</param>
    /// <param name="themeId">Theme ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if cached successfully</returns>
    Task<bool> SetStoreConfigJsonAsync(
        Guid tenantId,
        string configJson,
        string themeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates store config cache for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if invalidated successfully</returns>
    Task<bool> InvalidateStoreConfigAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
