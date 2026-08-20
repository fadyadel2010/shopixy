using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1B test doubles: an in-memory IMetaFeedCache that mirrors the real
/// wrapper semantics (tenant+token+channel keys, TTL, best-effort
/// invalidation) and a failure-injection variant for Redis-outage isolation
/// tests. FEED-2: keys carry the channel segment so Meta/Google isolation is
/// testable.
/// </summary>
public sealed class InMemoryMetaFeedCache : IMetaFeedCache
{
    private readonly Dictionary<string, MetaFeedCachePayload> _store = new();
    private readonly TimeSpan _ttl;

    public int GetCalls { get; private set; }
    public int SetCalls { get; private set; }
    public int InvalidateCalls { get; private set; }
    public List<string> SetKeys { get; } = new();
    public List<string> InvalidatedKeys { get; } = new();

    public InMemoryMetaFeedCache(int ttlMinutes = 5)
    {
        _ttl = TimeSpan.FromMinutes(ttlMinutes);
    }

    public string BuildKey(Guid tenantId, Guid tokenId, string channel = ProductFeedToken.MetaChannel) =>
        $"pos:tenant:{tenantId:N}:feed:{channel}:{tokenId:N}:v1";

    public TimeSpan GetTtl() => _ttl;

    public Task<(MetaFeedCacheLookupOutcome Outcome, MetaFeedCachePayload? Payload)> TryGetAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        GetCalls++;
        var key = BuildKey(tenantId, tokenId, channel);
        return _store.TryGetValue(key, out var payload)
            ? Task.FromResult((MetaFeedCacheLookupOutcome.Hit, (MetaFeedCachePayload?)payload))
            : Task.FromResult((MetaFeedCacheLookupOutcome.Miss, (MetaFeedCachePayload?)null));
    }

    public Task SetAsync(
        Guid tenantId,
        Guid tokenId,
        MetaFeedCachePayload payload,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        SetCalls++;
        var key = BuildKey(tenantId, tokenId, channel);
        SetKeys.Add(key);
        _store[key] = payload;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        InvalidateCalls++;
        var key = BuildKey(tenantId, tokenId, channel);
        InvalidatedKeys.Add(key);
        _store.Remove(key);
        return Task.CompletedTask;
    }

    /// <summary>Test hook: plant a cached payload as if a prior request had cached it.</summary>
    public void Seed(Guid tenantId, Guid tokenId, MetaFeedCachePayload payload, string channel = ProductFeedToken.MetaChannel) =>
        _store[BuildKey(tenantId, tokenId, channel)] = payload;

    public bool Contains(Guid tenantId, Guid tokenId, string channel = ProductFeedToken.MetaChannel) =>
        _store.ContainsKey(BuildKey(tenantId, tokenId, channel));
}

/// <summary>
/// Simulates a total Redis outage: every operation throws. Feed-1B requires
/// the feed path to survive this and serve from SQL.
/// </summary>
public sealed class ThrowingMetaFeedCache : IMetaFeedCache
{
    public int GetAttempts { get; private set; }

    public string BuildKey(Guid tenantId, Guid tokenId, string channel = ProductFeedToken.MetaChannel) =>
        $"pos:tenant:{tenantId:N}:feed:{channel}:{tokenId:N}:v1";

    public TimeSpan GetTtl() => TimeSpan.FromMinutes(5);

    public Task<(MetaFeedCacheLookupOutcome Outcome, MetaFeedCachePayload? Payload)> TryGetAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        GetAttempts++;
        throw new TimeoutException("Simulated Redis outage (read)");
    }

    public Task SetAsync(
        Guid tenantId,
        Guid tokenId,
        MetaFeedCachePayload payload,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
        => throw new TimeoutException("Simulated Redis outage (write)");

    public Task InvalidateAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
        => throw new TimeoutException("Simulated Redis outage (invalidate)");
}

/// <summary>
/// Shared construction helper for the hardened feed service stack in tests.
/// </summary>
public static class FeedTestFactory
{
    public static MetaFeedTelemetry CreateTelemetry() => new();

    public static MetaFeedCache CreateRealCacheOver(
        PointOfSale.Infrastructure.Redis.Services.IRedisJsonCacheService redisCache,
        MetaFeedTelemetry telemetry,
        string instanceName = "pos",
        int ttlMinutes = 5)
    {
        var settings = Options.Create(new RedisSettings
        {
            InstanceName = instanceName,
            MetaFeedCacheTtlMinutes = ttlMinutes
        });
        return new MetaFeedCache(redisCache, settings, telemetry, NullLogger<MetaFeedCache>.Instance);
    }
}
