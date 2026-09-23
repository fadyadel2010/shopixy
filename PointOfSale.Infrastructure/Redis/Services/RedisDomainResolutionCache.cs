using Microsoft.Extensions.Options;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// Multi-instance host authority cache. A shared generation prevents a cold
/// lookup that overlaps an authority mutation from publishing stale data.
/// Refresh locking remains per host, so unrelated authorities never serialize.
/// </summary>
public sealed class RedisDomainResolutionCache : IDomainResolutionCache
{
    private const string CacheType = "domain-resolution";
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(10);

    private readonly IRedisJsonCacheService _cache;
    private readonly RedisConnectionFactory _connectionFactory;
    private readonly string _keyPrefix;
    private readonly string _generationKey;

    public RedisDomainResolutionCache(
        IRedisJsonCacheService cache,
        RedisConnectionFactory connectionFactory,
        IOptions<RedisSettings> settings)
    {
        _cache = cache;
        _connectionFactory = connectionFactory;
        var instance = settings.Value.InstanceName.Trim().TrimEnd(':');
        _keyPrefix = $"{instance}:authority:domain:v1:";
        _generationKey = $"{instance}:authority:domain:generation:v1";
    }

    public async ValueTask<DomainResolutionResult?> GetAsync(
        string normalizedHost,
        CancellationToken cancellationToken = default)
    {
        var result = await _cache.GetOwnedAsync<DomainResolutionResult>(
            BuildKey(normalizedHost),
            CacheType,
            _generationKey,
            cancellationToken: cancellationToken);

        return result == null ? null : WithCacheAttempted(result);
    }

    public async ValueTask SetAsync(
        string normalizedHost,
        DomainResolutionResult result,
        CancellationToken cancellationToken = default)
    {
        await _cache.GetOrCreateOwnedAsync(
            BuildKey(normalizedHost),
            CacheType,
            _generationKey,
            EntryTtl,
            _ => Task.FromResult(CachePopulationResult<DomainResolutionResult>.FromValue(result)),
            cancellationToken: cancellationToken);
    }

    public async ValueTask RemoveAsync(
        string normalizedHost,
        CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(BuildKey(normalizedHost), cancellationToken);
    }

    public async ValueTask<DomainResolutionResult> GetOrCreateAsync(
        string normalizedHost,
        Func<CancellationToken, ValueTask<DomainResolutionResult>> factory,
        CancellationToken cancellationToken = default)
    {
        var result = await _cache.GetOrCreateOwnedAsync(
            BuildKey(normalizedHost),
            CacheType,
            _generationKey,
            EntryTtl,
            async ct => CachePopulationResult<DomainResolutionResult>.FromValue(await factory(ct)),
            cancellationToken: cancellationToken);

        // RedisJsonCacheService invokes the factory directly when Redis is
        // disabled/unavailable, preserving the authoritative PostgreSQL path.
        return WithCacheAttempted(result ?? await factory(cancellationToken));
    }

    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        await _cache.AdvanceInvalidationGenerationAsync(_generationKey, cancellationToken);
    }

    private string BuildKey(string normalizedHost) =>
        $"{_keyPrefix}{normalizedHost.Trim().ToLowerInvariant()}";

    private DomainResolutionResult WithCacheAttempted(DomainResolutionResult result) => new()
    {
        NormalizedHost = result.NormalizedHost,
        Status = result.Status,
        CacheAttempted = _connectionFactory.IsEnabled,
        DomainId = result.DomainId,
        TenantId = result.TenantId,
        RedirectType = result.RedirectType,
        RedirectTargetNormalizedHost = result.RedirectTargetNormalizedHost
    };
}
