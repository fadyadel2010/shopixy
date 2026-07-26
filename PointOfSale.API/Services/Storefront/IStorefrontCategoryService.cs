using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for storefront category read operations.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 2.4):
/// - All operations are READ-ONLY - no writes, no events
/// - All operations are tenant-scoped via X-Tenant-Id
/// - Has NO dependency on Products, Inventory, Pricing, or Stock
/// - Makes NO cross-service calls
/// - Is fully self-contained within the Read Side
/// 
/// Cache Behavior:
/// - Redis for categories is optional (low volatility data)
/// - Redis failures are graceful (logged as Warning, never fail request)
/// - MongoDB is the read-model source for storefront reads
/// </summary>
public interface IStorefrontCategoryService
{
    /// <summary>
    /// Gets all visible categories for a tenant as a hierarchical tree.
    /// 
    /// Flow:
    /// 1. Check Redis cache
    /// 2. Fallback to MongoDB
    /// 3. Build tree in-memory
    /// 4. Populate cache on miss (best-effort)
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of CategoryTreeDto (root categories with children)</returns>
    Task<List<CategoryTreeDto>> GetCategoryTreeAsync(
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single category by its SEO-friendly slug.
    /// 
    /// Flow:
    /// 1. Check Redis cache
    /// 2. Fallback to MongoDB
    /// 3. Populate cache on miss (best-effort)
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="slug">SEO-friendly URL slug</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>CategoryReadDto if found, null if not found</returns>
    Task<CategoryReadDto?> GetCategoryBySlugAsync(
        Guid tenantId,
        string slug,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all category-related caches for a tenant.
    /// 
    /// This should be called:
    /// - After category sync operations
    /// - After structural fixes to category data
    /// - When category data is updated in MongoDB
    /// 
    /// Cache keys invalidated:
    /// - Category tree cache
    /// - Individual category by slug caches
    /// - Homepage categories cache
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <returns>Number of cache keys invalidated</returns>
    Task<int> InvalidateCategoryCacheAsync(
        Guid tenantId,
        string correlationId);
}
