using Microsoft.Extensions.Options;
using PointOfSale.API.Instrumentation;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-1B: Cached value for a rendered public product feed. Only the final
/// XML plus safe diagnostics are cached — never entities, DbContext state, or
/// tokens. Shared by every feed channel (Feed-2).
/// </summary>
public sealed class MetaFeedCachePayload
{
    public string Xml { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public int ExcludedItemCount { get; set; }
    public bool Truncated { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

/// <summary>
/// FEED-1B: Result of a feed cache lookup. Error is distinguished from Miss
/// only for telemetry — both fall through to SQL generation.
/// </summary>
public enum MetaFeedCacheLookupOutcome
{
    Miss,
    Hit,
    Error
}

/// <summary>
/// FEED-1B/FEED-2: Rendered-XML cache contract. Channel-aware since Feed-2:
/// every method accepts the feed channel (default <c>meta</c>, so certified
/// Meta call sites and Meta cache keys stay exactly unchanged). Meta and
/// Google entries are keyed on distinct channel segments and can never collide.
/// </summary>
public interface IMetaFeedCache
{
    /// <summary>Cache key for the tenant+token+channel feed entry (never raw tokens).</summary>
    string BuildKey(Guid tenantId, Guid tokenId, string channel = ProductFeedToken.MetaChannel);

    /// <summary>Configured TTL; also the Cache-Control max-age source.</summary>
    TimeSpan GetTtl();

    Task<(MetaFeedCacheLookupOutcome Outcome, MetaFeedCachePayload? Payload)> TryGetAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        Guid tenantId,
        Guid tokenId,
        MetaFeedCachePayload payload,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default);

    /// <summary>Best-effort removal on revoke/rotate; failures never propagate.</summary>
    Task InvalidateAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// FEED-1B: Rendered-XML feed cache on top of the existing
/// <see cref="IRedisJsonCacheService"/> — no new Redis wrapper or connection
/// handling is introduced. FEED-2 added the channel dimension (keys, cache
/// type label, telemetry) with the Meta path byte-for-byte unchanged.
///
/// FAILURE ISOLATION (Feed-1B hard requirement):
/// - Redis unavailable / timeout / exception  -> treated as cache miss/bypass,
///   the feed is generated from SQL and served normally.
/// - Corrupted cached value                    -> treated as cache miss
///   (RedisJsonCacheService already deletes corrupt envelopes).
/// - Invalidation failure                     -> logged, never thrown; token
///   revocation stays effective because token validation always runs BEFORE
///   any cache lookup on the read path.
///
/// TTL: Redis.MetaFeedCacheTtlMinutes, clamped to the Feed-1B range (5–10 min).
/// The same TTL setting serves every channel — freshness bounds are a
/// platform-wide feed guarantee, not a per-channel one.
/// </summary>
public sealed class MetaFeedCache : IMetaFeedCache
{
    /// <summary>cache_type dimension for the shared Redis performance metrics (Meta channel).</summary>
    public const string CacheType = "meta-feed";

    private const int MinTtlMinutes = 5;
    private const int MaxTtlMinutes = 10;

    private readonly IRedisJsonCacheService _cacheService;
    private readonly RedisSettings _settings;
    private readonly MetaFeedTelemetry _telemetry;
    private readonly ILogger<MetaFeedCache> _logger;

    public MetaFeedCache(
        IRedisJsonCacheService cacheService,
        IOptions<RedisSettings> settings,
        MetaFeedTelemetry telemetry,
        ILogger<MetaFeedCache> logger)
    {
        _cacheService = cacheService;
        _settings = settings.Value;
        _telemetry = telemetry;
        _logger = logger;
    }

    public string BuildKey(Guid tenantId, Guid tokenId, string channel = ProductFeedToken.MetaChannel) =>
        CacheKeyBuilder.FeedKey(_settings.InstanceName, tenantId, tokenId, channel);

    public TimeSpan GetTtl() =>
        TimeSpan.FromMinutes(Math.Clamp(_settings.MetaFeedCacheTtlMinutes, MinTtlMinutes, MaxTtlMinutes));

    public async Task<(MetaFeedCacheLookupOutcome Outcome, MetaFeedCachePayload? Payload)> TryGetAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await _cacheService.GetAsync<MetaFeedCachePayload>(
                BuildKey(tenantId, tokenId, channel),
                CacheTypeFor(channel),
                cancellationToken: cancellationToken);

            if (payload == null || string.IsNullOrEmpty(payload.Xml))
            {
                // RedisJsonCacheService records misses/corruption itself; from the
                // feed's perspective a missing or empty entry is simply a miss.
                return (MetaFeedCacheLookupOutcome.Miss, null);
            }

            return (MetaFeedCacheLookupOutcome.Hit, payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cache outage must never become a feed outage.
            _telemetry.RecordCacheError("read", channel);
            _logger.LogWarning(
                ex,
                "Feed cache read failed; falling back to SQL generation | TenantId: {TenantId} | TokenId: {TokenId} | Channel: {Channel}",
                tenantId, tokenId, channel);
            return (MetaFeedCacheLookupOutcome.Error, null);
        }
    }

    public async Task SetAsync(
        Guid tenantId,
        Guid tokenId,
        MetaFeedCachePayload payload,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cacheService.SetAsync(
                BuildKey(tenantId, tokenId, channel),
                CacheTypeFor(channel),
                payload,
                GetTtl(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _telemetry.RecordCacheError("write", channel);
            _logger.LogWarning(
                ex,
                "Feed cache write failed; feed was still served | TenantId: {TenantId} | TokenId: {TokenId} | Channel: {Channel}",
                tenantId, tokenId, channel);
        }
    }

    public async Task InvalidateAsync(
        Guid tenantId,
        Guid tokenId,
        string channel = ProductFeedToken.MetaChannel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _cacheService.RemoveAsync(BuildKey(tenantId, tokenId, channel), cancellationToken);
            _logger.LogInformation(
                "Feed cache invalidated | TenantId: {TenantId} | TokenId: {TokenId} | Channel: {Channel}",
                tenantId, tokenId, channel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best effort: the entry expires by TTL, and revocation is enforced
            // on every request because token validation precedes cache lookup.
            _telemetry.RecordCacheError("invalidate", channel);
            _logger.LogWarning(
                ex,
                "Feed cache invalidation failed; entry will expire by TTL | TenantId: {TenantId} | TokenId: {TokenId} | Channel: {Channel}",
                tenantId, tokenId, channel);
        }
    }

    /// <summary>
    /// cache_type metric dimension per channel. Meta keeps the certified
    /// "meta-feed" label; Google adds one fixed low-cardinality value.
    /// </summary>
    private static string CacheTypeFor(string channel) =>
        channel == ProductFeedToken.MetaChannel ? CacheType : $"{channel}-feed";
}
