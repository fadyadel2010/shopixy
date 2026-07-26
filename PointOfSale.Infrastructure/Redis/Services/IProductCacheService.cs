using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Abstraction for product cache operations.
/// All operations are tenant-scoped by design.
/// 
/// Error Handling:
/// - All methods return gracefully on failure (null for gets, false for operations)
/// - Cache is an optimization, not a dependency
/// - Callers should fall back to database on cache miss/failure
/// </summary>
public interface IProductCacheService
{
    /// <summary>
    /// Gets a product from cache.
    /// Returns null on cache miss or failure.
    /// </summary>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="productId">The product ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached product or null</returns>
    Task<ProductDocument?> GetProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a product in cache with default TTL.
    /// Fails silently if Redis is unavailable.
    /// </summary>
    /// <param name="product">The product document to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if cached successfully, false otherwise</returns>
    Task<bool> SetProductAsync(ProductDocument product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a product in cache with custom TTL.
    /// </summary>
    Task<bool> SetProductAsync(ProductDocument product, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific product from cache (invalidation).
    /// </summary>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="productId">The product ID</param>
    /// <param name="slug">Optional product slug for slug-based cache invalidation.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if removed or didn't exist, false on failure</returns>
    Task<bool> InvalidateProductAsync(Guid tenantId, Guid productId, string? slug = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all product-related cache for a tenant.
    /// Used when product list changes (create, delete, bulk updates).
    /// </summary>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false on failure</returns>
    Task<bool> InvalidateAllProductsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Redis cache is available
    /// </summary>
    bool IsAvailable { get; }
}
