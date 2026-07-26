using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for storefront product read operations.
/// 
/// All operations are:
/// - Tenant-scoped
/// - Read-only
/// - Cache-aware (lazy population)
/// </summary>
public interface IStorefrontProductService
{
    /// <summary>
    /// Gets a single product by slug for storefront display.
    /// 
    /// Flow:
    /// 1. Check Redis cache
    /// 2. Fallback to MongoDB
    /// 3. Populate cache on miss (fire-and-forget)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="slug">Product URL slug</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ProductReadDto if found, null if not found or filtered out</returns>
    Task<ProductReadDto?> GetProductBySlugAsync(
        Guid tenantId, 
        string slug,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single product by ID for storefront display.
    /// Used by wishlist and other features that reference products by ID.
    /// 
    /// Flow:
    /// 1. Check Redis cache
    /// 2. Fallback to MongoDB
    /// 3. Populate cache on miss (fire-and-forget)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="productId">Product GUID</param>
    /// <param name="affiliateModeOnly">If true, only returns IsAffiliateEnabled=true products</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ProductReadDto if found, null if not found or filtered out</returns>
    Task<ProductReadDto?> GetProductByIdAsync(
        Guid tenantId, 
        Guid productId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    Task<StorefrontVariantResolutionResponse?> ResolveProductVariantAsync(
        Guid tenantId,
        Guid productId,
        StorefrontVariantResolutionRequest request,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated product list for storefront.
    /// 
    /// Flow:
    /// 1. Check Redis cache (by query hash)
    /// 2. Fallback to MongoDB
    /// 3. Populate cache on miss (fire-and-forget)
    /// 
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    Task<PagedResult<ProductListItemDto>> GetProductListAsync(
        Guid tenantId,
        ProductListQuery query,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);
}
