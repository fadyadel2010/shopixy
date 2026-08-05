using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// The single invalidation owner for the Storefront wishlist and wishlist-count
/// cache entries. Projection convergence is the only trigger for invalidation.
/// </summary>
public interface IStorefrontWishlistCacheInvalidator
{
    Task InvalidateAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}

public sealed class StorefrontWishlistCacheInvalidator : IStorefrontWishlistCacheInvalidator
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _settings;

    public StorefrontWishlistCacheInvalidator(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> settings)
    {
        _cache = cache;
        _settings = settings.Value;
    }

    public async Task InvalidateAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        await _cache.AdvanceInvalidationGenerationAsync(
            CacheKeyBuilder.StorefrontWishlistInvalidationGenerationKey(
                _settings.InstanceName,
                tenantId,
                customerId),
            cancellationToken);

        await _cache.RemoveManyAsync(
            [
                CacheKeyBuilder.StorefrontWishlistKey(_settings.InstanceName, tenantId, customerId),
                CacheKeyBuilder.StorefrontWishlistCountKey(_settings.InstanceName, tenantId, customerId)
            ],
            cancellationToken);
    }
}
