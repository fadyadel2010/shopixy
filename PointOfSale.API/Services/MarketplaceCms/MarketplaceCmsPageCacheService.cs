using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.MarketplaceCms;

public interface IMarketplaceCmsPageCacheService
{
    Task<IReadOnlyList<MarketplaceCmsPageDto>?> GetPublishedPagesAsync(CancellationToken cancellationToken = default);

    Task SetPublishedPagesAsync(
        IReadOnlyList<MarketplaceCmsPageDto> pages,
        CancellationToken cancellationToken = default);

    Task InvalidatePublishedPagesAsync(CancellationToken cancellationToken = default);
}

public sealed class MarketplaceCmsPageCacheService : IMarketplaceCmsPageCacheService
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<MarketplaceCmsPageCacheService> _logger;
    private readonly TimeSpan _ttl;

    public MarketplaceCmsPageCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> redisSettings,
        IOptions<MarketplaceCmsPageCacheOptions> options,
        ILogger<MarketplaceCmsPageCacheService> logger)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
        _logger = logger;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.PublishedTtlMinutes));
    }

    public async Task<IReadOnlyList<MarketplaceCmsPageDto>?> GetPublishedPagesAsync(
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();

        try
        {
            return await _cache.GetAsync<IReadOnlyList<MarketplaceCmsPageDto>>(
                cacheKey,
                "marketplace-cms-pages",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace CMS cache read failed | CacheKey: {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task SetPublishedPagesAsync(
        IReadOnlyList<MarketplaceCmsPageDto> pages,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        var lastUpdatedAtUtc = pages.Count == 0
            ? (DateTime?)null
            : pages.Max(page => page.UpdatedAtUtc);

        try
        {
            await _cache.SetAsync(
                cacheKey,
                "marketplace-cms-pages",
                pages,
                _ttl,
                lastUpdatedAtUtc,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace CMS cache write failed | CacheKey: {CacheKey}", cacheKey);
        }
    }

    public async Task InvalidatePublishedPagesAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();

        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace CMS cache invalidation failed | CacheKey: {CacheKey}", cacheKey);
        }
    }

    private string GetCacheKey()
    {
        return CacheKeyBuilder.MarketplaceCmsPagesKey(_redisSettings.InstanceName);
    }
}
