using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Redis.Services;

/// <summary>
/// OpenTelemetry-compatible metrics for Redis cache performance.
/// </summary>
public sealed class RedisPerformanceTelemetry
{
    public const string MeterName = "PointOfSale.Redis.Performance";

    private readonly Meter _meter;

    public Counter<long> CacheHits { get; }
    public Counter<long> CacheMisses { get; }
    public Counter<long> CacheCorruptions { get; }
    public Histogram<double> CacheRefreshDurationMs { get; }
    public Histogram<double> ProjectionLagMs { get; }
    public Histogram<double> ReadLatencyMs { get; }
    public Histogram<double> WriteLatencyMs { get; }
    public Counter<long> PatternInvalidations { get; }

    public RedisPerformanceTelemetry()
    {
        _meter = new Meter(MeterName, "2.0.0");
        CacheHits = _meter.CreateCounter<long>("redis.cache.hits", "count", "Redis cache hits");
        CacheMisses = _meter.CreateCounter<long>("redis.cache.misses", "count", "Redis cache misses");
        CacheCorruptions = _meter.CreateCounter<long>("redis.cache.corruptions", "count", "Redis cache deserialization failures");
        CacheRefreshDurationMs = _meter.CreateHistogram<double>("redis.cache.refresh_ms", "ms", "Redis cache refresh duration");
        ProjectionLagMs = _meter.CreateHistogram<double>("redis.cache.projection_lag_ms", "ms", "Read-model projection lag at cache population time");
        ReadLatencyMs = _meter.CreateHistogram<double>("redis.cache.read_latency_ms", "ms", "Redis cache read latency");
        WriteLatencyMs = _meter.CreateHistogram<double>("redis.cache.write_latency_ms", "ms", "Redis cache write latency");
        PatternInvalidations = _meter.CreateCounter<long>("redis.cache.pattern_invalidations", "count", "Redis cache pattern-based invalidations");
    }

    public void RecordHit(string cacheType) => CacheHits.Add(1, CreateTags(cacheType));

    public void RecordMiss(string cacheType) => CacheMisses.Add(1, CreateTags(cacheType));

    public void RecordCorruption(string cacheType) => CacheCorruptions.Add(1, CreateTags(cacheType));

    public void RecordRefresh(string cacheType, TimeSpan duration) =>
        CacheRefreshDurationMs.Record(duration.TotalMilliseconds, CreateTags(cacheType));

    public void RecordProjectionLag(string cacheType, TimeSpan lag) =>
        ProjectionLagMs.Record(Math.Max(0, lag.TotalMilliseconds), CreateTags(cacheType));

    public void RecordReadLatency(string cacheType, TimeSpan latency) =>
        ReadLatencyMs.Record(latency.TotalMilliseconds, CreateTags(cacheType));

    public void RecordWriteLatency(string cacheType, TimeSpan latency) =>
        WriteLatencyMs.Record(latency.TotalMilliseconds, CreateTags(cacheType));

    public void RecordPatternInvalidation(string pattern) =>
        PatternInvalidations.Add(
            1,
            new KeyValuePair<string, object?>("pattern_family", NormalizePatternFamily(pattern)));

    private static KeyValuePair<string, object?>[] CreateTags(string cacheType)
    {
        return
        [
            new KeyValuePair<string, object?>("cache_type", NormalizeCacheType(cacheType))
        ];
    }

    private static string NormalizeCacheType(string cacheType)
    {
        var normalized = PointOfSale.Infrastructure.Instrumentation.OperationalTelemetryDimensions.NormalizeTagValue(cacheType);

        if (normalized.Contains("homepage", StringComparison.Ordinal))
            return "homepage";
        if (normalized.Contains("cms", StringComparison.Ordinal))
            return "cms";
        if (normalized.Contains("footer", StringComparison.Ordinal))
            return "footer";
        if (normalized.Contains("translation", StringComparison.Ordinal))
            return "translation";
        if (normalized.Contains("product", StringComparison.Ordinal))
            return "product";
        if (normalized.Contains("category", StringComparison.Ordinal))
            return "category";
        if (normalized.Contains("order", StringComparison.Ordinal))
            return "order";
        if (normalized.Contains("payment", StringComparison.Ordinal))
            return "payment";
        if (normalized.Contains("shipping", StringComparison.Ordinal))
            return "shipping";
        if (normalized.Contains("promotion", StringComparison.Ordinal))
            return "promotion";

        return normalized is "unknown" or "invalid" ? normalized : "other";
    }

    private static string NormalizePatternFamily(string pattern)
    {
        var normalized = PointOfSale.Infrastructure.Instrumentation.OperationalTelemetryDimensions.NormalizeTagValue(pattern);

        if (normalized.Contains("homepage", StringComparison.Ordinal))
            return "homepage";
        if (normalized.Contains("cms", StringComparison.Ordinal))
            return "cms";
        if (normalized.Contains("footer", StringComparison.Ordinal))
            return "footer";
        if (normalized.Contains("marketplace", StringComparison.Ordinal))
            return "marketplace";
        if (normalized.Contains("product", StringComparison.Ordinal))
            return "product";
        if (normalized.Contains("order", StringComparison.Ordinal))
            return "order";
        if (normalized.Contains("tenant", StringComparison.Ordinal))
            return "tenant";

        return normalized is "unknown" or "invalid" ? normalized : "other";
    }
}
