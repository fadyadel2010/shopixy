using PointOfSale.Core.Analytics;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for managing per-product pixel conversion value overrides.
/// Phase M2: Per-Product Pixel Override.
/// </summary>
public interface IProductPixelOverrideService
{
    /// <summary>
    /// Gets a specific override for a product.
    /// </summary>
    Task<ProductPixelOverride?> GetOverrideAsync(Guid tenantId, Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Gets all active overrides for a tenant (cached).
    /// Used by the resolver for O(1) bulk lookups.
    /// </summary>
    Task<Dictionary<Guid, ProductPixelOverride>> GetOverridesForTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Saves (creates or updates) an override.
    /// Invalidates cache.
    /// </summary>
    Task SaveOverrideAsync(ProductPixelOverride overrideEntity, CancellationToken ct = default);

    /// <summary>
    /// Deletes an override.
    /// Invalidates cache.
    /// </summary>
    Task DeleteOverrideAsync(Guid tenantId, Guid productId, CancellationToken ct = default);
}
