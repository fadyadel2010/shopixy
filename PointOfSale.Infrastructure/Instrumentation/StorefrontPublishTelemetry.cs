using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

public sealed class StorefrontPublishTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Storefront.Publish";

    private readonly Meter _meter;

    public Counter<long> PublishSuccessTotal { get; }
    public Counter<long> PublishFailureTotal { get; }
    public Counter<long> AdminFakeVersionAttemptTotal { get; }
    public Counter<long> AdminRollbackInvalidTotal { get; }
    public Histogram<double> ProjectionLagSeconds { get; }

    public StorefrontPublishTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        PublishSuccessTotal = _meter.CreateCounter<long>(
            "publish_success_total",
            unit: "count",
            description: "Number of successful storefront publish stages and projection completions.");

        PublishFailureTotal = _meter.CreateCounter<long>(
            "publish_failure_total",
            unit: "count",
            description: "Number of failed storefront publish stages and projection attempts.");

        AdminFakeVersionAttemptTotal = _meter.CreateCounter<long>(
            "admin_fake_version_attempt_total",
            unit: "count",
            description: "Rollback requests rejected because the requested version did not exist for the tenant.");

        AdminRollbackInvalidTotal = _meter.CreateCounter<long>(
            "admin_rollback_invalid_total",
            unit: "count",
            description: "Rollback requests rejected because the request was invalid, unsafe, or could not be satisfied.");

        ProjectionLagSeconds = _meter.CreateHistogram<double>(
            "projection_lag_seconds",
            unit: "seconds",
            description: "Lag between the source storefront publish event timestamp and projection completion.");
    }

    private static string NormalizeTag(string? value) =>
        OperationalTelemetryDimensions.NormalizeTagValue(value);

    public void RecordPublishSuccess(string action, string stage)
    {
        PublishSuccessTotal.Add(
            1,
            new KeyValuePair<string, object?>("action", NormalizeTag(action)),
            new KeyValuePair<string, object?>("stage", NormalizeTag(stage)));
    }

    public void RecordPublishFailure(string action, string stage, string reason)
    {
        PublishFailureTotal.Add(
            1,
            new KeyValuePair<string, object?>("action", NormalizeTag(action)),
            new KeyValuePair<string, object?>("stage", NormalizeTag(stage)),
            new KeyValuePair<string, object?>("reason", NormalizeTag(reason)));
    }

    public void RecordFakeVersionAttempt(Guid tenantId)
    {
        AdminFakeVersionAttemptTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId));
    }

    public void RecordInvalidRollback(Guid tenantId, string reason)
    {
        AdminRollbackInvalidTotal.Add(
            1,
            OperationalTelemetryDimensions.CreateTenantBucketTag(tenantId),
            new KeyValuePair<string, object?>("reason", NormalizeTag(reason)));
    }

    public void RecordProjectionLag(double seconds, string eventType)
    {
        ProjectionLagSeconds.Record(
            Math.Max(0d, seconds),
            new KeyValuePair<string, object?>("event_type", NormalizeTag(eventType)));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
