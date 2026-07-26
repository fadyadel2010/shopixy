using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

/// <summary>
/// OpenTelemetry-compatible metrics for CommissionService cache and database operations.
/// </summary>
public sealed class CommissionServiceTelemetry
{
    public const string MeterName = "PointOfSale.CommerceHub.Commission";
    
    private readonly Meter _meter;
    private readonly object _sync = new();
    private long _cacheHits;
    private long _cacheMisses;
    private readonly double _cacheDurationSeconds;

    public Counter<long> CacheHits { get; }
    public Counter<long> CacheMisses { get; }
    public Histogram<double> DbQueryLatency { get; }

    public CommissionServiceTelemetry(TimeSpan cacheDuration)
    {
        _meter = new Meter(MeterName, "1.0.0");
        _cacheDurationSeconds = cacheDuration.TotalSeconds;

        CacheHits = _meter.CreateCounter<long>(
            "commission_rules.cache_hits",
            "count",
            "Total number of commission rules cache hits.");

        CacheMisses = _meter.CreateCounter<long>(
            "commission_rules.cache_misses",
            "count",
            "Total number of commission rules cache misses (DB queries).");

        DbQueryLatency = _meter.CreateHistogram<double>(
            "commission_rules.db_query_latency",
            "milliseconds",
            "Latency of commission rules database queries.");

        _meter.CreateObservableGauge(
            "commission_rules.cache_duration",
            () => new Measurement<double>(_cacheDurationSeconds),
            "seconds",
            "Configured cache duration for active commission rules.");

        _meter.CreateObservableGauge(
            "commission_rules.cache_hit_rate",
            () => new Measurement<double>(GetCacheHitRate()),
            "ratio",
            "Cache hit rate (hits / total requests).");
    }

    public void RecordCacheHit()
    {
        CacheHits.Add(1);
        lock (_sync)
        {
            _cacheHits++;
        }
    }

    public void RecordCacheMiss()
    {
        CacheMisses.Add(1);
        lock (_sync)
        {
            _cacheMisses++;
        }
    }

    public void RecordDbQueryLatency(double milliseconds)
    {
        DbQueryLatency.Record(milliseconds);
    }

    public IDisposable MeasureDbQuery()
    {
        return new DbQueryMeasurement(this);
    }

    private double GetCacheHitRate()
    {
        lock (_sync)
        {
            var total = _cacheHits + _cacheMisses;
            return total == 0 ? 0.0 : (double)_cacheHits / total;
        }
    }

    private sealed class DbQueryMeasurement : IDisposable
    {
        private readonly CommissionServiceTelemetry _telemetry;
        private readonly Stopwatch _stopwatch;

        public DbQueryMeasurement(CommissionServiceTelemetry telemetry)
        {
            _telemetry = telemetry;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _telemetry.RecordDbQueryLatency(_stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
