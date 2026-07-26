using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service for promotion-aware search ranking operations.
/// Phase: Shopify-Grade Promotion-Aware Search
/// 
/// Responsibilities:
/// - Fetch tenant-specific promotion ranking config (cached)
/// - Provide ranking config to Elasticsearch query builders
/// 
/// Cache Strategy:
/// - Redis cache with 5-minute TTL
/// - Fallback to database on cache miss
/// - Default config returned if all sources fail
/// </summary>
public interface IPromotionRankingService
{
    /// <summary>
    /// Gets promotion ranking configuration for a tenant.
    /// Uses Redis cache first, then database, then defaults.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Promotion ranking config (never null)</returns>
    Task<PromotionRankingConfig> GetConfigAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
