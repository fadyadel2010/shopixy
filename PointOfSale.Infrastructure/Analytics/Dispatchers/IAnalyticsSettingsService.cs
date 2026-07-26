using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

/// <summary>
/// Service for managing tenant analytics provider settings.
/// 
/// RESPONSIBILITIES:
/// - Get settings for a tenant (with Redis caching)
/// - Save settings (with encryption for secrets)
/// - Invalidate cache on settings change
/// 
/// STORAGE:
/// - PostgreSQL: Source of truth (encrypted secrets)
/// - Redis: Cache for fast reads (5-minute TTL)
/// </summary>
public interface IAnalyticsSettingsService
{
    /// <summary>
    /// Gets analytics settings for a tenant.
    /// Returns null if tenant has no analytics configured.
    /// </summary>
    Task<TenantAnalyticsSettings?> GetSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves analytics settings for a tenant.
    /// Secrets are encrypted before storing.
    /// </summary>
    Task SaveSettingsAsync(
        TenantAnalyticsSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a specific provider for a tenant.
    /// </summary>
    Task SetProviderEnabledAsync(
        Guid tenantId,
        AnalyticsProvider provider,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached settings for a tenant.
    /// Called after settings update.
    /// </summary>
    Task InvalidateCacheAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
