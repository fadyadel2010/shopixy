using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Interface for promotion cache operations.
/// </summary>
public interface IPromotionCacheService
{
    /// <summary>
    /// Invalidates all promotion-related cache keys for a tenant.
    /// Called when promotions are created, updated, or deleted.
    /// </summary>
    Task InvalidatePromotionCacheAsync(Guid tenantId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invalidates product-level promotion cache (when product's promotion changes).
    /// </summary>
    Task InvalidateProductPromotionCacheAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invalidates category-level promotion cache (when category's promotion changes).
    /// </summary>
    Task InvalidateCategoryPromotionCacheAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Redis-based promotion cache service.
/// 
/// ARCHITECTURE:
/// - Promotions are cached with short TTL (90 seconds) for accuracy
/// - Cache is invalidated on any promotion event
/// - Product list caches are also invalidated when promotions change
///   (because product cards display promotion badges)
/// </summary>
public class PromotionCacheService : IPromotionCacheService
{
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger<PromotionCacheService> _logger;

    public PromotionCacheService(
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> settings,
        ILogger<PromotionCacheService> logger)
    {
        _redisFactory = redisFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvalidatePromotionCacheAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                _logger.LogDebug(
                    "Cache SKIP (no connection) | TenantId: {TenantId} | Operation: InvalidatePromotionCache",
                    tenantId);
                return;
            }

            var server = _redisFactory.GetServer();
            if (server == null)
            {
                _logger.LogWarning(
                    "Cache SKIP (no server) | TenantId: {TenantId} | Operation: InvalidatePromotionCache",
                    tenantId);
                return;
            }

            // Invalidate all promotion-related keys
            var promotionsPattern = CacheKeyBuilder.PromotionsPatternKey(_settings.InstanceName, tenantId);
            
            // Also invalidate product list caches (they contain promotion badge data)
            var productsListPattern = CacheKeyBuilder.ProductsPatternKey(_settings.InstanceName, tenantId);
            
            // Also invalidate category tree cache (it contains promotion banner data)
            var categoryTreeKey = CacheKeyBuilder.CategoriesTreeKey(_settings.InstanceName, tenantId);
            
            var keysToDelete = new List<RedisKey>();
            
            // Always delete the category tree cache (single key, not a pattern)
            keysToDelete.Add(categoryTreeKey);
            
            // Get all promotion keys
            await foreach (var key in server.KeysAsync(pattern: promotionsPattern))
            {
                keysToDelete.Add(key);
            }
            
            // Get all product list keys (they need refresh to show updated badges)
            await foreach (var key in server.KeysAsync(pattern: productsListPattern))
            {
                keysToDelete.Add(key);
            }
            
            if (keysToDelete.Count > 0)
            {
                await database.KeyDeleteAsync(keysToDelete.ToArray());
                _logger.LogInformation(
                    "Cache invalidated | TenantId: {TenantId} | KeysDeleted: {Count}",
                    tenantId, keysToDelete.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache invalidation failed | TenantId: {TenantId} | Operation: InvalidatePromotionCache",
                tenantId);
            // Don't throw - cache failures should not break the flow
        }
    }

    /// <inheritdoc />
    public async Task InvalidateProductPromotionCacheAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                return;
            }

            // Invalidate product-specific cache keys
            var productByIdKey = CacheKeyBuilder.ProductByIdKey(_settings.InstanceName, tenantId, productId);
            
            await database.KeyDeleteAsync(productByIdKey);
            
            // Also invalidate all promotions cache
            await InvalidatePromotionCacheAsync(tenantId, cancellationToken);

            _logger.LogDebug(
                "Product promotion cache invalidated | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, productId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Product promotion cache invalidation failed | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, productId);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateCategoryPromotionCacheAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _redisFactory.GetDatabase();
            if (database == null)
            {
                return;
            }

            // Invalidate category-specific cache keys
            var categoryKey = CacheKeyBuilder.PromotionsByCategoryKey(_settings.InstanceName, tenantId, categoryId);
            var categoryTreeKey = CacheKeyBuilder.CategoriesTreeKey(_settings.InstanceName, tenantId);
            
            await database.KeyDeleteAsync(new RedisKey[] { categoryKey, categoryTreeKey });
            
            // Also invalidate all promotions cache
            await InvalidatePromotionCacheAsync(tenantId, cancellationToken);

            _logger.LogDebug(
                "Category promotion cache invalidated | TenantId: {TenantId} | CategoryId: {CategoryId}",
                tenantId, categoryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Category promotion cache invalidation failed | TenantId: {TenantId} | CategoryId: {CategoryId}",
                tenantId, categoryId);
        }
    }
}
