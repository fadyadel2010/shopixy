using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

/// <summary>
/// FEED-1B: OpenTelemetry-compatible metrics for the public product feeds.
/// Follows the repository's per-domain Meter convention (see
/// RedisPerformanceTelemetry / CommerceReliabilityTelemetry) — no new metrics
/// framework is introduced. FEED-2 added the Google channel on the SAME meter
/// (one fixed low-cardinality channel dimension value per feed channel).
///
/// CARDINALITY RULES:
/// - Labels are limited to channel ("meta" / "google"), request result
///   (success/empty/error/rate_limited), and low-cardinality failure reasons.
/// - Never labeled by raw token, token id, tenant id, product/variant id, or URL.
/// </summary>
public sealed class MetaFeedTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Commerce.Feeds";

    public const string ChannelMeta = "meta";
    public const string ChannelGoogle = "google";
    public const string ChannelTikTok = "tiktok";
    public const string ChannelSnapchat = "snapchat";

    public const string ResultSuccess = "success";
    public const string ResultEmpty = "empty";
    public const string ResultError = "error";
    public const string ResultRateLimited = "rate_limited";

    private readonly Meter _meter;

    public Counter<long> FeedRequests { get; }
    public Counter<long> CacheHits { get; }
    public Counter<long> CacheMisses { get; }
    public Counter<long> CacheErrors { get; }
    public Counter<long> GenerationFailures { get; }
    public Counter<long> RateLimited { get; }
    public Counter<long> ItemCapTruncations { get; }
    public Histogram<double> GenerationDurationMs { get; }
    public Histogram<long> ResponseSizeBytes { get; }

    public MetaFeedTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        FeedRequests = _meter.CreateCounter<long>(
            "feed_request",
            unit: "count",
            description: "Public product feed requests by channel and result.");
        CacheHits = _meter.CreateCounter<long>(
            "feed_cache_hit",
            unit: "count",
            description: "Feed requests served from the rendered-XML cache.");
        CacheMisses = _meter.CreateCounter<long>(
            "feed_cache_miss",
            unit: "count",
            description: "Feed requests that regenerated the feed from SQL.");
        CacheErrors = _meter.CreateCounter<long>(
            "feed_cache_error",
            unit: "count",
            description: "Feed cache operations that failed and were bypassed (cache is never an availability dependency).");
        GenerationFailures = _meter.CreateCounter<long>(
            "feed_generation_failure",
            unit: "count",
            description: "Feed generation attempts that did not produce a valid feed, by reason category.");
        RateLimited = _meter.CreateCounter<long>(
            "feed_rate_limited",
            unit: "count",
            description: "Feed requests rejected by the PublicFeedRateLimit policy.");
        ItemCapTruncations = _meter.CreateCounter<long>(
            "feed_item_cap_truncation",
            unit: "count",
            description: "Feeds whose deterministic output was capped at the configured item limit.");
        GenerationDurationMs = _meter.CreateHistogram<double>(
            "feed_generation_duration_ms",
            unit: "ms",
            description: "Cold-path feed generation duration (projection + serialization), excluding cache reads.");
        ResponseSizeBytes = _meter.CreateHistogram<long>(
            "feed_response_size_bytes",
            unit: "bytes",
            description: "UTF-8 size of the rendered feed XML returned to the caller.");
    }

    public void RecordRequest(string result, string channel = ChannelMeta) =>
        FeedRequests.Add(1, ChannelTag(channel), new KeyValuePair<string, object?>("result", result));

    public void RecordCacheHit(string channel = ChannelMeta) => CacheHits.Add(1, ChannelTag(channel));

    public void RecordCacheMiss(string channel = ChannelMeta) => CacheMisses.Add(1, ChannelTag(channel));

    public void RecordCacheError(string operation, string channel = ChannelMeta) =>
        CacheErrors.Add(1, ChannelTag(channel), new KeyValuePair<string, object?>("operation", operation));

    public void RecordGenerationFailure(string reason, string channel = ChannelMeta) =>
        GenerationFailures.Add(1, ChannelTag(channel), new KeyValuePair<string, object?>("reason", reason));

    public void RecordRateLimited(string channel = ChannelMeta) => RateLimited.Add(1, ChannelTag(channel));

    public void RecordItemCapTruncation(string channel = ChannelMeta) => ItemCapTruncations.Add(1, ChannelTag(channel));

    public void RecordGenerationDuration(TimeSpan duration, string channel = ChannelMeta) =>
        GenerationDurationMs.Record(duration.TotalMilliseconds, ChannelTag(channel));

    public void RecordResponseSize(long bytes, string channel = ChannelMeta) =>
        ResponseSizeBytes.Record(Math.Max(0, bytes), ChannelTag(channel));

    private static KeyValuePair<string, object?> ChannelTag(string channel) =>
        new("channel", channel);

    public void Dispose() => _meter.Dispose();
}
