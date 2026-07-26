using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// OpenTelemetry-compatible metrics for Elasticsearch search performance.
/// 
/// PHASE: Elasticsearch Search &amp; Discovery — Observability
/// 
/// Meter: PointOfSale.Elasticsearch.Search
/// 
/// Metrics:
/// - elasticsearch.search.latency_ms (histogram, by search_type)
/// - elasticsearch.search.total (counter, by search_type)
/// - elasticsearch.search.zero_results (counter, by search_type)
/// - elasticsearch.search.errors (counter, by search_type)
/// - elasticsearch.index.operations (counter, by operation)
/// - elasticsearch.index.latency_ms (histogram, by operation)
/// - elasticsearch.index.errors (counter, by operation)
/// 
/// Dashboard Queries (Prometheus/Grafana):
/// - P99 search latency: histogram_quantile(0.99, elasticsearch_search_latency_ms)
/// - Zero-result rate: rate(elasticsearch_search_zero_results) / rate(elasticsearch_search_total)
/// - Index lag: rate(elasticsearch_index_operations{operation="index"})
/// </summary>
public sealed class SearchTelemetry
{
    public const string MeterName = "PointOfSale.Elasticsearch.Search";

    private readonly Meter _meter;

    // Search metrics
    public Histogram<double> SearchLatencyMs { get; }
    public Counter<long> SearchTotal { get; }
    public Counter<long> SearchZeroResults { get; }
    public Counter<long> SearchErrors { get; }

    // Index metrics
    public Counter<long> IndexOperations { get; }
    public Histogram<double> IndexLatencyMs { get; }
    public Counter<long> IndexErrors { get; }

    public SearchTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        SearchLatencyMs = _meter.CreateHistogram<double>(
            "elasticsearch.search.latency_ms", "ms", "Elasticsearch search latency");
        SearchTotal = _meter.CreateCounter<long>(
            "elasticsearch.search.total", "count", "Total Elasticsearch searches");
        SearchZeroResults = _meter.CreateCounter<long>(
            "elasticsearch.search.zero_results", "count", "Elasticsearch searches with zero results");
        SearchErrors = _meter.CreateCounter<long>(
            "elasticsearch.search.errors", "count", "Elasticsearch search errors");
        IndexOperations = _meter.CreateCounter<long>(
            "elasticsearch.index.operations", "count", "Elasticsearch index operations");
        IndexLatencyMs = _meter.CreateHistogram<double>(
            "elasticsearch.index.latency_ms", "ms", "Elasticsearch index operation latency");
        IndexErrors = _meter.CreateCounter<long>(
            "elasticsearch.index.errors", "count", "Elasticsearch index errors");
    }

    /// <summary>
    /// Records a completed search operation.
    /// </summary>
    public void RecordSearch(string searchType, double latencyMs, long resultCount, bool isSuccess)
    {
        var tags = CreateTags(searchType);

        SearchTotal.Add(1, tags);
        SearchLatencyMs.Record(latencyMs, tags);

        if (isSuccess && resultCount == 0)
        {
            SearchZeroResults.Add(1, tags);
        }

        if (!isSuccess)
        {
            SearchErrors.Add(1, tags);
        }
    }

    /// <summary>
    /// Records an index operation (index/delete/reindex).
    /// </summary>
    public void RecordIndexOperation(string operation, double latencyMs, bool isSuccess)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("operation", NormalizeTag(operation))
        };

        IndexOperations.Add(1, tags);
        IndexLatencyMs.Record(latencyMs, tags);

        if (!isSuccess)
        {
            IndexErrors.Add(1, tags);
        }
    }

    private static KeyValuePair<string, object?>[] CreateTags(string searchType)
    {
        return
        [
            new KeyValuePair<string, object?>("search_type", NormalizeTag(searchType))
        ];
    }

    private static string NormalizeTag(string? value) =>
        PointOfSale.Infrastructure.Instrumentation.OperationalTelemetryDimensions.NormalizeTagValue(value);
}
