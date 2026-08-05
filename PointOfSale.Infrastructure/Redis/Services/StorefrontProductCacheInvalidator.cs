using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// The single invalidation owner for the live Storefront product Redis keyspace.
/// Product, list, and legacy homepage read services remain the mutation owners for
/// their own entries; event consumers and repair tools may only request invalidation
/// through this boundary.
/// </summary>
public interface IStorefrontProductCacheInvalidator
{
    Task InvalidateProductAsync(
        Guid tenantId,
        Guid productId,
        string? slug = null,
        bool invalidateHomepage = true,
        CancellationToken cancellationToken = default);

    Task InvalidateTenantAsync(
        Guid tenantId,
        bool invalidateHomepage = true,
        CancellationToken cancellationToken = default);
}

public sealed class StorefrontProductCacheInvalidator : IStorefrontProductCacheInvalidator
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _settings;

    public StorefrontProductCacheInvalidator(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> settings)
    {
        _cache = cache;
        _settings = settings.Value;
    }

    public async Task InvalidateProductAsync(
        Guid tenantId,
        Guid productId,
        string? slug = null,
        bool invalidateHomepage = true,
        CancellationToken cancellationToken = default)
    {
        var keys = new List<string>
        {
            CacheKeyBuilder.ProductByIdKey(_settings.InstanceName, tenantId, productId),
            $"{CacheKeyBuilder.ProductByIdKey(_settings.InstanceName, tenantId, productId)}:affiliate"
        };

        if (!string.IsNullOrWhiteSpace(slug))
        {
            keys.Add(CacheKeyBuilder.ProductBySlugKey(_settings.InstanceName, tenantId, slug));
            keys.Add(CacheKeyBuilder.ProductBySlugKey(_settings.InstanceName, tenantId, $"{slug}:affiliate"));
        }

        await _cache.RemoveManyAsync(keys, cancellationToken);
        await InvalidateTenantCollectionsAsync(tenantId, invalidateHomepage, cancellationToken);
    }

    public Task InvalidateTenantAsync(
        Guid tenantId,
        bool invalidateHomepage = true,
        CancellationToken cancellationToken = default)
        => InvalidateTenantCollectionsAsync(tenantId, invalidateHomepage, cancellationToken);

    private async Task InvalidateTenantCollectionsAsync(
        Guid tenantId,
        bool invalidateHomepage,
        CancellationToken cancellationToken)
    {
        await _cache.AdvanceInvalidationGenerationAsync(
            CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey(_settings.InstanceName, tenantId),
            cancellationToken);

        await _cache.RemoveByPatternAsync(
            CacheKeyBuilder.ProductsPatternKey(_settings.InstanceName, tenantId),
            cancellationToken);

        if (invalidateHomepage)
        {
            await _cache.RemoveByPatternAsync(
                CacheKeyBuilder.HomePatternKey(_settings.InstanceName, tenantId),
                cancellationToken);
        }
    }
}
