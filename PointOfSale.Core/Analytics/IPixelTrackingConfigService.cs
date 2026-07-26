using PointOfSale.Core.Analytics;

namespace PointOfSale.Core.Analytics;

/// <summary>
/// Service for managing Pixel Tracking Configuration.
/// Handles storage, caching, and retrieval.
/// </summary>
public interface IPixelTrackingConfigService
{
    /// <summary>
    /// Gets the configuration for a tenant.
    /// Returns default (disabled) config if none exists.
    /// </summary>
    Task<PixelTrackingConfig> GetConfigAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Updates the configuration for a tenant.
    /// </summary>
    Task UpdateConfigAsync(PixelTrackingConfig config, CancellationToken ct = default);
}
