using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Product cache service implementation using Redis.
/// 
/// PRODUCTION HARDENING (Phase 12) — CACHE SAFETY RULES
/// 
/// ⚠️ CRITICAL INVARIANT: Stock data in cached products is DISPLAY ONLY.
/// 
/// WHAT IS CACHED:
/// ✅ Product metadata (name, description, images, category)
/// ✅ Variant structure (attributes, SKUs, prices)
/// ✅ Stock values (for display hints like "In Stock" badges)
/// 
/// WHAT IS NEVER AUTHORITATIVE FROM CACHE:
/// ❌ Real-time available quantity for cart validation
/// ❌ Stock reservation decisions
/// ❌ Checkout availability checks
/// 
/// AUTHORITATIVE STOCK SOURCES:
/// - Cart validation: MongoDB projections (ProductDocument.Variants[].TotalStock)
/// - Checkout reservation: PostgreSQL (ProductVariantStocks.Quantity - ReservedQuantity)
/// 
/// WHY THIS IS SAFE:
/// 1. Cart always validates against MongoDB (not cache) before adding items
/// 2. Checkout ALWAYS uses PostgreSQL for atomic reservation
/// 3. Cache invalidation happens on every ProductVariantStockChangedEvent
/// 4. Short TTL (5-15min) limits staleness window for display
/// 5. UI shows "limited stock" messaging that doesn't guarantee availability
/// 
/// Key Features:
/// - Tenant-isolated cache keys
/// - Graceful failure handling (cache is optional)
/// - Structured logging for observability
/// - Configurable TTLs
/// - Event-driven cache invalidation
/// 
/// All operations follow the pattern:
/// 1. Check if Redis is available
/// 2. Build tenant-scoped key
/// 3. Execute operation
/// 4. Log result
/// 5. Return gracefully on any failure
/// </summary>
public class ProductCacheService : IProductCacheService
{
    private readonly RedisConnectionFactory _connectionFactory;
    private readonly RedisSettings _settings;
    private readonly ILogger<ProductCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProductCacheService(
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings,
        ILogger<ProductCacheService> logger)
    {
        _connectionFactory = connectionFactory;
        _settings = settings.Value;
        _logger = logger;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public bool IsAvailable => _connectionFactory.IsEnabled && _connectionFactory.IsConnected;

    /// <inheritdoc />
    public async Task<ProductDocument?> GetProductAsync(
        Guid tenantId, 
        Guid productId, 
        CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _logger.LogDebug("Cache SKIP (no connection) | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId, productId);
            return null;
        }

        var key = CacheKeyBuilder.ProductKey(_settings.InstanceName, tenantId, productId);

        try
        {
            var value = await database.StringGetAsync(key);
            
            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache MISS | Key: {Key} | TenantId: {TenantId} | ProductId: {ProductId}",
                    key, tenantId, productId);
                return null;
            }

            var product = JsonSerializer.Deserialize<ProductDocument>(value!, _jsonOptions);
            
            _logger.LogDebug("Cache HIT | Key: {Key} | TenantId: {TenantId} | ProductId: {ProductId}",
                key, tenantId, productId);
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "Cache GET failed | Key: {Key} | TenantId: {TenantId} | ProductId: {ProductId}",
                key, tenantId, productId);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> SetProductAsync(ProductDocument product, CancellationToken cancellationToken = default)
    {
        var ttl = TimeSpan.FromMinutes(_settings.ProductCacheTtlMinutes);
        return SetProductAsync(product, ttl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetProductAsync(
        ProductDocument product, 
        TimeSpan ttl, 
        CancellationToken cancellationToken = default)
    {
        if (product == null) return false;
        
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            _logger.LogDebug("Cache SKIP (no connection) | TenantId: {TenantId} | ProductId: {ProductId}",
                product.TenantId, product.ProductId);
            return false;
        }

        var key = CacheKeyBuilder.ProductKey(_settings.InstanceName, product.TenantId, product.ProductId);

        try
        {
            var json = JsonSerializer.Serialize(product, _jsonOptions);
            var success = await database.StringSetAsync(key, json, ttl);

            if (success)
            {
                _logger.LogDebug(
                    "Cache SET | Key: {Key} | TenantId: {TenantId} | ProductId: {ProductId} | TTL: {TtlMinutes}min",
                    key, product.TenantId, product.ProductId, ttl.TotalMinutes);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache SET failed | Key: {Key} | TenantId: {TenantId} | ProductId: {ProductId}",
                key, product.TenantId, product.ProductId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InvalidateProductAsync(
        Guid tenantId, 
        Guid productId, 
        string? slug = null,
        CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            return true; // Nothing to invalidate if no connection
        }

        var keys = new List<RedisKey>
        {
            CacheKeyBuilder.ProductKey(_settings.InstanceName, tenantId, productId),
            CacheKeyBuilder.ProductByIdKey(_settings.InstanceName, tenantId, productId),
            $"{CacheKeyBuilder.ProductByIdKey(_settings.InstanceName, tenantId, productId)}:affiliate"
        };

        if (!string.IsNullOrWhiteSpace(slug))
        {
            keys.Add(CacheKeyBuilder.ProductBySlugKey(_settings.InstanceName, tenantId, slug));
            keys.Add(CacheKeyBuilder.ProductBySlugKey(_settings.InstanceName, tenantId, $"{slug}:affiliate"));
        }

        try
        {
            var deletedCount = await database.KeyDeleteAsync(keys.ToArray());
            
            _logger.LogInformation(
                "Cache INVALIDATE | TenantId: {TenantId} | ProductId: {ProductId} | Slug: {Slug} | KeysRequested: {KeyCount} | KeysDeleted: {DeletedCount}",
                tenantId, productId, slug, keys.Count, deletedCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache INVALIDATE failed | TenantId: {TenantId} | ProductId: {ProductId} | Slug: {Slug}",
                tenantId, productId, slug);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InvalidateAllProductsAsync(
        Guid tenantId, 
        CancellationToken cancellationToken = default)
    {
        var database = _connectionFactory.GetDatabase();
        if (database == null)
        {
            return true; // Nothing to invalidate if no connection
        }

        try
        {
            RedisKey[] exactKeys =
            {
                (RedisKey)CacheKeyBuilder.ProductsAllKey(_settings.InstanceName, tenantId),
                (RedisKey)CacheKeyBuilder.HomeFeaturedProductsKey(_settings.InstanceName, tenantId),
                (RedisKey)CacheKeyBuilder.HomeNewArrivalsKey(_settings.InstanceName, tenantId),
                (RedisKey)CacheKeyBuilder.HomeBestSellersKey(_settings.InstanceName, tenantId),
            };

            long deletedCount = await database.KeyDeleteAsync(exactKeys);

            var server = _connectionFactory.GetServer();
            if (server != null)
            {
                var patterns = new[]
                {
                    CacheKeyBuilder.ProductsPatternKey(_settings.InstanceName, tenantId),
                    $"{_settings.InstanceName}:tenant:{tenantId:N}:products:list:*",
                };

                foreach (var pattern in patterns)
                {
                    var patternKeys = new List<RedisKey>();
                    await foreach (var key in server.KeysAsync(database.Database, pattern))
                    {
                        patternKeys.Add(key);
                    }

                    if (patternKeys.Count == 0)
                    {
                        continue;
                    }

                    deletedCount += await database.KeyDeleteAsync(patternKeys.ToArray());
                }
            }
            
            _logger.LogInformation(
                "Cache INVALIDATE ALL | TenantId: {TenantId} | DeletedKeys: {DeletedCount}",
                tenantId, deletedCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache INVALIDATE ALL failed | TenantId: {TenantId}",
                tenantId);
            return false;
        }
    }
}
