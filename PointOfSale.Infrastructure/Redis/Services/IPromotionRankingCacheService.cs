using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Cache service for promotion ranking configuration.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// Caches PromotionRankingConfig per tenant in Redis for fast search query access.
/// This is read frequently during ES queries and should not hit the database.
/// 
/// Cache TTL: 5 minutes (short TTL because config changes should reflect quickly)
/// </summary>
public interface IPromotionRankingCacheService
{
    /// <summary>
    /// Gets cached promotion ranking config for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached config or null if not in cache</returns>
    Task<PromotionRankingConfig?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets promotion ranking config in cache.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="config">Config to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAsync(Guid tenantId, PromotionRankingConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached promotion ranking config for a tenant.
    /// Called when StoreConfig is updated.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
