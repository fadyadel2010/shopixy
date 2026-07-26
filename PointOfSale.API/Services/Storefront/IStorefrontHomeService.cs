using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for storefront homepage section read operations.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 2.5 - Homepage Sections):
/// - All operations are READ-ONLY - no writes, no events
/// - All operations are tenant-scoped via X-Tenant-Id
/// - Cache-first approach - Redis is the primary data source
/// - MongoDB supplies projection candidates that are SQL-revalidated before public return
/// - Elasticsearch is used for best sellers (popularity data)
/// - Has NO dependency on Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// 
/// Cache Behavior:
/// - Redis is REQUIRED for performance (homepage is read-heavy)
/// - Redis failures are graceful (logged as Warning, continue to MongoDB)
/// - Cache miss → Query data source → Populate cache → Return
/// - All cache operations are fire-and-forget
/// 
/// Homepage Sections:
/// - Featured Products: IsFeatured == true (curated selection)
/// - New Arrivals: CreatedAtUtc DESC (latest products)
/// - Best Sellers: Elasticsearch popularity candidates with MongoDB degraded candidates, SQL-revalidated before return
/// - Homepage Categories: Top-level categories (ParentId == null)
/// </summary>
public interface IStorefrontHomeService
{
    /// <summary>
    /// Gets featured products for homepage display.
    /// 
    /// Flow:
    /// 1. Check Redis cache (key: tenant:{tenantId}:home:featured-products)
    /// 2. Cache HIT → Return cached data
    /// 3. Cache MISS → Query MongoDB (IsFeatured == true, IsActive == true, IsOnlineVisible == true)
    /// 4. Populate cache (fire-and-forget, TTL: 30 minutes)
    /// 5. Return products
    /// 
    /// Rules:
    /// - IsFeatured == true
    /// - IsActive == true
    /// - IsOnlineVisible == true
    /// - Limit: Configurable (default: 12)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of featured products</returns>
    Task<List<ProductListItemDto>> GetFeaturedProductsAsync(
        Guid tenantId,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets new arrival products for homepage display.
    /// 
    /// Flow:
    /// 1. Check Redis cache (key: tenant:{tenantId}:home:new-arrivals)
    /// 2. Cache HIT → Return cached data
    /// 3. Cache MISS → Query MongoDB (sort by CreatedAtUtc DESC)
    /// 4. Populate cache (fire-and-forget, TTL: 15 minutes)
    /// 5. Return products
    /// 
    /// Rules:
    /// - IsActive == true
    /// - IsOnlineVisible == true
    /// - Sorted by CreatedAtUtc DESC
    /// - Limit: Configurable (default: 12)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of new arrival products</returns>
    Task<List<ProductListItemDto>> GetNewArrivalsAsync(
        Guid tenantId,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets best-selling products for homepage display.
    /// 
    /// Flow:
    /// 1. Check Redis cache (key: tenant:{tenantId}:home:best-sellers)
    /// 2. Cache HIT → Return cached data
    /// 3. Cache MISS → Query Elasticsearch (popularity/order data)
    /// 4. Elasticsearch failure → Fallback to MongoDB (most recent products)
    /// 5. Populate cache (fire-and-forget, TTL: 15 minutes)
    /// 6. Return products
    /// 
    /// Primary Source: Elasticsearch (orders/popularity index)
    /// Fallback: MongoDB (most recently created products)
    /// 
    /// Rules:
    /// - TenantId filter REQUIRED
    /// - IsActive == true
    /// - IsOnlineVisible == true
    /// - Limit: Configurable (default: 12)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of best-selling products</returns>
    Task<List<ProductListItemDto>> GetBestSellersAsync(
        Guid tenantId,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets homepage products in the exact order requested by ProductId.
    /// Intended for schema-driven homepage sections with curated product IDs.
    /// </summary>
    Task<List<ProductListItemDto>> GetProductsByIdsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> productIds,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top-level categories for homepage navigation.
    /// 
    /// Flow:
    /// 1. Check Redis cache (key: tenant:{tenantId}:home:categories)
    /// 2. Cache HIT → Return cached data
    /// 3. Cache MISS → Query MongoDB (ParentId == null)
    /// 4. Populate cache (fire-and-forget, TTL: 60 minutes)
    /// 5. Return categories
    /// 
    /// Rules:
    /// - ParentId == null (top-level only)
    /// - IsActive == true
    /// - IsOnlineVisible == true
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of top-level categories</returns>
    Task<List<CategoryReadDto>> GetHomepageCategoriesAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
