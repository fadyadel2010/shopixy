using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class PaymobOperationalTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Payments.Paymob";

    public static readonly IReadOnlyCollection<string> RequiredMetricNames = new[]
    {
        "paymob_payment_create_attempt_total",
        "paymob_payment_create_success_total",
        "paymob_provider_failure_total",
        "paymob_webhook_received_total",
        "paymob_replay_rejected_total",
        "paymob_duplicate_rejected_total",
        "paymob_runtime_admitted_total",
        "paymob_runtime_rejected_total",
        "paymob_refund_received_total",
        "paymob_void_received_total",
        "paymob_capture_received_total",
        "paymob_background_recovery_total",
        "paymob_retry_total",
        "paymob_provider_request_latency_ms"
    };

    private readonly Meter _meter;
    private readonly Counter<long> _paymentCreateAttempt;
    private readonly Counter<long> _paymentCreateSuccess;
    private readonly Counter<long> _providerFailure;
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _replayRejected;
    private readonly Counter<long> _duplicateRejected;
    private readonly Counter<long> _runtimeAdmitted;
    private readonly Counter<long> _runtimeRejected;
    private readonly Counter<long> _refundReceived;
    private readonly Counter<long> _voidReceived;
    private readonly Counter<long> _captureReceived;
    private readonly Counter<long> _backgroundRecovery;
    private readonly Counter<long> _retry;
    private readonly Histogram<double> _providerRequestLatency;

    public PaymobOperationalTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _paymentCreateAttempt = _meter.CreateCounter<long>("paymob_payment_create_attempt_total");
        _paymentCreateSuccess = _meter.CreateCounter<long>("paymob_payment_create_success_total");
        _providerFailure = _meter.CreateCounter<long>("paymob_provider_failure_total");
        _webhookReceived = _meter.CreateCounter<long>("paymob_webhook_received_total");
        _replayRejected = _meter.CreateCounter<long>("paymob_replay_rejected_total");
        _duplicateRejected = _meter.CreateCounter<long>("paymob_duplicate_rejected_total");
        _runtimeAdmitted = _meter.CreateCounter<long>("paymob_runtime_admitted_total");
        _runtimeRejected = _meter.CreateCounter<long>("paymob_runtime_rejected_total");
        _refundReceived = _meter.CreateCounter<long>("paymob_refund_received_total");
        _voidReceived = _meter.CreateCounter<long>("paymob_void_received_total");
        _captureReceived = _meter.CreateCounter<long>("paymob_capture_received_total");
        _backgroundRecovery = _meter.CreateCounter<long>("paymob_background_recovery_total");
        _retry = _meter.CreateCounter<long>("paymob_retry_total");
        _providerRequestLatency = _meter.CreateHistogram<double>("paymob_provider_request_latency_ms");
    }

    public void RecordPaymentCreateAttempt(Guid tenantId, string mode) =>
        _paymentCreateAttempt.Add(1, Tags(tenantId, ("mode", mode)));

    public void RecordPaymentCreateSuccess(Guid tenantId, string mode) =>
        _paymentCreateSuccess.Add(1, Tags(tenantId, ("mode", mode)));

    public void RecordProviderFailure(Guid tenantId, string operation, string reasonCode) =>
        _providerFailure.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

    public void RecordWebhookReceived(Guid tenantId, string eventType) =>
        _webhookReceived.Add(1, Tags(tenantId, ("event_type", eventType)));

    public void RecordReplayRejected(Guid tenantId, string reasonCode) =>
        _replayRejected.Add(1, Tags(tenantId, ("reason_code", reasonCode)));

    public void RecordDuplicateRejected(Guid tenantId, string source) =>
        _duplicateRejected.Add(1, Tags(tenantId, ("source", source)));

    public void RecordRuntimeAdmitted(Guid tenantId, string source) =>
        _runtimeAdmitted.Add(1, Tags(tenantId, ("source", source)));

    public void RecordRuntimeRejected(Guid tenantId, string source, string reasonCode) =>
        _runtimeRejected.Add(1, Tags(tenantId, ("source", source), ("reason_code", reasonCode)));

    public void RecordRefundReceived(Guid tenantId, string source) =>
        _refundReceived.Add(1, Tags(tenantId, ("source", source)));

    public void RecordVoidReceived(Guid tenantId, string source) =>
        _voidReceived.Add(1, Tags(tenantId, ("source", source)));

    public void RecordCaptureReceived(Guid tenantId, string source) =>
        _captureReceived.Add(1, Tags(tenantId, ("source", source)));

    public void RecordBackgroundRecovery(Guid tenantId, string reasonCode) =>
        _backgroundRecovery.Add(1, Tags(tenantId, ("reason_code", reasonCode)));

    public void RecordRetry(Guid tenantId, string operation, string reasonCode) =>
        _retry.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

    public void RecordProviderRequestLatency(string operation, TimeSpan duration) =>
        _providerRequestLatency.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("operation", operation));

    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?>[] Tags(
        Guid tenantId,
        params (string Key, string Value)[] values)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("tenant_bucket", BucketTenant(tenantId))
        };

        tags.AddRange(values.Select(value => new KeyValuePair<string, object?>(value.Key, value.Value)));
        return tags.ToArray();
    }

    private static string BucketTenant(Guid tenantId) =>
        tenantId == Guid.Empty ? "unknown" : tenantId.ToString("N")[..8];
}
