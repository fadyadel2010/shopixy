using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Homepage;

public interface IHomepageSectionCacheService
{
    Task<IReadOnlyList<HomepageSectionDto>?> GetPublishedScopeAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task SetPublishedScopeAsync(
        Guid? tenantId,
        IReadOnlyList<HomepageSectionDto> sections,
        CancellationToken cancellationToken = default);

    Task InvalidatePublishedScopeAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

public sealed class HomepageSectionCacheService : IHomepageSectionCacheService
{
    private readonly IRedisJsonCacheService _cache;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<HomepageSectionCacheService> _logger;
    private readonly TimeSpan _ttl;

    public HomepageSectionCacheService(
        IRedisJsonCacheService cache,
        IOptions<RedisSettings> redisSettings,
        IOptions<HomepageSectionCacheOptions> options,
        ILogger<HomepageSectionCacheService> logger)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
        _logger = logger;
        _ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.PublishedTtlMinutes));
    }

    public async Task<IReadOnlyList<HomepageSectionDto>?> GetPublishedScopeAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetPublishedScopeCacheKey(tenantId);

        try
        {
            return await _cache.GetAsync<IReadOnlyList<HomepageSectionDto>>(
                cacheKey,
                "homepage-sections",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Homepage section cache read failed | CacheKey: {CacheKey} | TenantId: {TenantId}",
                cacheKey,
                tenantId);

            return null;
        }
    }

    public async Task SetPublishedScopeAsync(
        Guid? tenantId,
        IReadOnlyList<HomepageSectionDto> sections,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetPublishedScopeCacheKey(tenantId);
        var lastUpdatedAtUtc = sections.Count == 0
            ? (DateTime?)null
            : sections.Max(section => section.UpdatedAt);

        try
        {
            await _cache.SetAsync(
                cacheKey,
                "homepage-sections",
                sections,
                _ttl,
                lastUpdatedAtUtc,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Homepage section cache write failed | CacheKey: {CacheKey} | TenantId: {TenantId}",
                cacheKey,
                tenantId);
        }
    }

    public async Task InvalidatePublishedScopeAsync(
        Guid? tenantId,
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
                "Homepage section cache invalidation failed | CacheKey: {CacheKey} | TenantId: {TenantId}",
                cacheKey,
                tenantId);
        }
    }

    private string GetPublishedScopeCacheKey(Guid? tenantId)
    {
        return CacheKeyBuilder.HomepagePublishedScopeKey(_redisSettings.InstanceName, tenantId);
    }
}
