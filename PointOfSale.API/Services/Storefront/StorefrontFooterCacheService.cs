using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontFooterCacheService
{
    Task<StorefrontFooterDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task SetAsync(
        Guid tenantId,
        StorefrontFooterDto footer,
        DateTime? lastUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class StorefrontFooterCacheService : IStorefrontFooterCacheService
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _redisSettings;

    public StorefrontFooterCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> redisSettings)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
    }

    public Task<StorefrontFooterDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _cache.GetAsync<StorefrontFooterDto>(
            GetCacheKey(tenantId),
            "storefront-footer",
            cancellationToken: cancellationToken);
    }

    public Task SetAsync(
        Guid tenantId,
        StorefrontFooterDto footer,
        DateTime? lastUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return _cache.SetAsync(
            GetCacheKey(tenantId),
            "storefront-footer",
            footer,
            TimeSpan.FromMinutes(Math.Max(1, _redisSettings.FooterCacheTtlMinutes)),
            lastUpdatedAtUtc,
            cancellationToken: cancellationToken);
    }

    public Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(GetCacheKey(tenantId), cancellationToken);
    }

    private string GetCacheKey(Guid tenantId)
    {
        return CacheKeyBuilder.StorefrontFooterKey(_redisSettings.InstanceName, tenantId);
    }
}
