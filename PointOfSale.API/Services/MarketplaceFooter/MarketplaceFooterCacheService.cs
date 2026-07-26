using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.MarketplaceFooter;

public interface IMarketplaceFooterCacheService
{
    Task<MarketplaceFooterDto?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(
        MarketplaceFooterDto footer,
        DateTime? lastUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public sealed class MarketplaceFooterCacheService : IMarketplaceFooterCacheService
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _redisSettings;

    public MarketplaceFooterCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> redisSettings)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
    }

    public Task<MarketplaceFooterDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        return _cache.GetAsync<MarketplaceFooterDto>(
            GetCacheKey(),
            "marketplace-footer",
            cancellationToken: cancellationToken);
    }

    public Task SetAsync(
        MarketplaceFooterDto footer,
        DateTime? lastUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return _cache.SetAsync(
            GetCacheKey(),
            "marketplace-footer",
            footer,
            TimeSpan.FromMinutes(Math.Max(1, _redisSettings.FooterCacheTtlMinutes)),
            lastUpdatedAtUtc,
            cancellationToken: cancellationToken);
    }

    public Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync(GetCacheKey(), cancellationToken);
    }

    private string GetCacheKey()
    {
        return CacheKeyBuilder.MarketplaceFooterKey(_redisSettings.InstanceName);
    }
}
