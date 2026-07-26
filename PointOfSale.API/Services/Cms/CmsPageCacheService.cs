using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Cms;

public interface ICmsPageCacheService
{
    Task<IReadOnlyList<CmsPageDto>?> GetPublishedScopeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task SetPublishedScopeAsync(
        Guid tenantId,
        IReadOnlyList<CmsPageDto> pages,
        CancellationToken cancellationToken = default);

    Task InvalidatePublishedScopeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public sealed class CmsPageCacheService : ICmsPageCacheService
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<CmsPageCacheService> _logger;
    private readonly TimeSpan _ttl;

    public CmsPageCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> redisSettings,
        IOptions<CmsPageCacheOptions> options,
        ILogger<CmsPageCacheService> logger)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
        _logger = logger;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.PublishedTtlMinutes));
    }

    public async Task<IReadOnlyList<CmsPageDto>?> GetPublishedScopeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetPublishedScopeCacheKey(tenantId);

        try
        {
            return await _cache.GetAsync<IReadOnlyList<CmsPageDto>>(
                cacheKey,
                "cms-pages",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CMS page cache read failed | CacheKey: {CacheKey} | TenantId: {TenantId}",
                cacheKey,
                tenantId);

            return null;
        }
    }

    public async Task SetPublishedScopeAsync(
        Guid tenantId,
        IReadOnlyList<CmsPageDto> pages,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetPublishedScopeCacheKey(tenantId);
        var lastUpdatedAtUtc = pages.Count == 0
            ? (DateTime?)null
            : pages.Max(page => page.UpdatedAtUtc);

        try
        {
            await _cache.SetAsync(
                cacheKey,
                "cms-pages",
                pages,
                _ttl,
                lastUpdatedAtUtc,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CMS page cache write failed | CacheKey: {CacheKey} | TenantId: {TenantId}",
                cacheKey,
                tenantId);
        }
    }

    public async Task InvalidatePublishedScopeAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetPublishedScopeCacheKey(tenantId);

        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CMS page cache invalidation failed | CacheKey: {CacheKey} | TenantId: {TenantId}",
                cacheKey,
                tenantId);
        }
    }

    private string GetPublishedScopeCacheKey(Guid tenantId)
    {
        return CacheKeyBuilder.CmsPagesScopeKey(_redisSettings.InstanceName, tenantId);
    }
}
