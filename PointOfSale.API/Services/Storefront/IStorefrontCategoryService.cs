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
/// - SQL category lineage remains the result authority
/// </summary>
public interface IStorefrontCategoryService
{
    /// <summary>
    /// Gets all visible categories for a tenant as a hierarchical tree.
    /// 
    /// Flow:
    /// The tree is recomposed from the live SQL category lineage.
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
    /// The category is resolved from the live SQL category lineage.
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
    /// Resolves a category ID or slug and returns that live category plus all live descendants.
    /// PostgreSQL category lineage is the exclusive identity authority for catalog filtering.
    /// </summary>
    Task<List<Guid>> ResolveCategoryTreeIdsAsync(
        Guid tenantId,
        string categoryIdOrSlug,
        string correlationId,
        CancellationToken cancellationToken = default);

}
