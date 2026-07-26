using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

public sealed class AnalyticsReliabilityTelemetry
{
    public const string MeterName = "PointOfSale.Analytics.Reliability";
    private readonly Meter _meter;
    private long _enqueueSuccessCount;
    private long _enqueueFailureCount;
    private long _storageSuccessCount;
    private long _storageFailureCount;

    public Counter<long> EnqueueSuccessTotal { get; }
    public Counter<long> EnqueueFailureTotal { get; }
    public Counter<long> StorageSuccessTotal { get; }
    public Counter<long> StorageFailureTotal { get; }
    public Counter<long> PayloadRejectedTotal { get; }
    public Histogram<long> PayloadSizeBytes { get; }

    public AnalyticsReliabilityTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        EnqueueSuccessTotal = _meter.CreateCounter<long>(
            "analytics.enqueue.success_total",
            "count",
            "Analytics events successfully written to the durable outbox.");
        EnqueueFailureTotal = _meter.CreateCounter<long>(
            "analytics.enqueue.failure_total",
            "count",
            "Analytics events rejected because the durable outbox write failed.");
        StorageSuccessTotal = _meter.CreateCounter<long>(
            "analytics.storage.success_total",
            "count",
            "Analytics events successfully persisted to at least one analytics store.");
        StorageFailureTotal = _meter.CreateCounter<long>(
            "analytics.storage.failure_total",
            "count",
            "Analytics events that could not be persisted to any analytics store.");
        PayloadRejectedTotal = _meter.CreateCounter<long>(
            "analytics_payload_rejected_total",
            "count",
            "Analytics events rejected before enqueue because payload bounds or structure were unsafe.");
        PayloadSizeBytes = _meter.CreateHistogram<long>(
            "analytics_payload_size_bytes",
            "bytes",
            "Serialized analytics payload sizes measured before enqueue.");

        _meter.CreateObservableGauge<double>(
            "analytics.enqueue.failure_rate",
            () => CalculateRate(_enqueueFailureCount, _enqueueSuccessCount + _enqueueFailureCount),
            "ratio",
            "Failure rate for analytics outbox writes.");

        _meter.CreateObservableGauge<double>(
            "analytics.storage.failure_rate",
            () => CalculateRate(_storageFailureCount, _storageSuccessCount + _storageFailureCount),
            "ratio",
            "Failure rate for analytics event persistence.");
    }

    public void RecordEnqueueSuccess(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _enqueueSuccessCount, count);
        EnqueueSuccessTotal.Add(count);
    }

    public void RecordEnqueueFailure(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _enqueueFailureCount, count);
        EnqueueFailureTotal.Add(count);
    }

    public void RecordStorageSuccess(string eventType)
    {
        Interlocked.Increment(ref _storageSuccessCount);
        StorageSuccessTotal.Add(1, new KeyValuePair<string, object?>("event_type", NormalizeReason(eventType)));
    }

    public void RecordStorageFailure(string eventType, string reason)
    {
        Interlocked.Increment(ref _storageFailureCount);
        StorageFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("event_type", NormalizeReason(eventType)),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));
    }

    public void RecordPayloadRejected(Guid tenantId, string reason)
    {
        PayloadRejectedTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeReason(reason)));
    }

    public void RecordPayloadSize(Guid tenantId, long sizeBytes, string outcome)
    {
        if (sizeBytes < 0)
        {
            sizeBytes = 0;
        }

        PayloadSizeBytes.Record(
            sizeBytes,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("outcome", NormalizeReason(outcome)));
    }

    private static double CalculateRate(long failures, long total)
    {
        if (total <= 0)
        {
            return 0d;
        }

        return (double)failures / total;
    }

    private static string NormalizeReason(string? reason) =>
        OperationalTelemetryDimensions.NormalizeTagValue(reason);
}
