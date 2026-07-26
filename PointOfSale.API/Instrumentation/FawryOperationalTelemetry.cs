using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class FawryOperationalTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Payments.Fawry";

    public static readonly IReadOnlyCollection<string> RequiredMetricNames = new[]
    {
        "fawry_payment_create_attempt_total",
        "fawry_webhook_received_total",
        "fawry_webhook_accepted_total",
        "fawry_webhook_rejected_total",
        "fawry_replay_detected_total",
        "fawry_duplicate_detected_total",
        "fawry_runtime_accepted_total",
        "fawry_runtime_rejected_total",
        "fawry_application_failure_total",
        "fawry_provider_failure_total",
        "fawry_refund_request_total",
        "fawry_capture_request_total",
        "fawry_void_request_total",
        "fawry_inquiry_request_total",
        "fawry_advanced_operation_failure_total"
    };

    private readonly Meter _meter;
    private readonly Counter<long> _paymentCreateAttempt;
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _webhookAccepted;
    private readonly Counter<long> _webhookRejected;
    private readonly Counter<long> _replayDetected;
    private readonly Counter<long> _duplicateDetected;
    private readonly Counter<long> _runtimeAccepted;
    private readonly Counter<long> _runtimeRejected;
    private readonly Counter<long> _applicationFailure;
    private readonly Counter<long> _providerFailure;
    private readonly Counter<long> _refundRequest;
    private readonly Counter<long> _captureRequest;
    private readonly Counter<long> _voidRequest;
    private readonly Counter<long> _inquiryRequest;
    private readonly Counter<long> _advancedOperationFailure;

    public FawryOperationalTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _paymentCreateAttempt = _meter.CreateCounter<long>("fawry_payment_create_attempt_total");
        _webhookReceived = _meter.CreateCounter<long>("fawry_webhook_received_total");
        _webhookAccepted = _meter.CreateCounter<long>("fawry_webhook_accepted_total");
        _webhookRejected = _meter.CreateCounter<long>("fawry_webhook_rejected_total");
        _replayDetected = _meter.CreateCounter<long>("fawry_replay_detected_total");
        _duplicateDetected = _meter.CreateCounter<long>("fawry_duplicate_detected_total");
        _runtimeAccepted = _meter.CreateCounter<long>("fawry_runtime_accepted_total");
        _runtimeRejected = _meter.CreateCounter<long>("fawry_runtime_rejected_total");
        _applicationFailure = _meter.CreateCounter<long>("fawry_application_failure_total");
        _providerFailure = _meter.CreateCounter<long>("fawry_provider_failure_total");
        _refundRequest = _meter.CreateCounter<long>("fawry_refund_request_total");
        _captureRequest = _meter.CreateCounter<long>("fawry_capture_request_total");
        _voidRequest = _meter.CreateCounter<long>("fawry_void_request_total");
        _inquiryRequest = _meter.CreateCounter<long>("fawry_inquiry_request_total");
        _advancedOperationFailure = _meter.CreateCounter<long>("fawry_advanced_operation_failure_total");
    }

    public void RecordPaymentCreateAttempt(Guid tenantId, string mode) =>
        _paymentCreateAttempt.Add(1, Tags(tenantId, ("mode", mode)));

    public void RecordWebhookReceived(Guid tenantId, string eventType) =>
        _webhookReceived.Add(1, Tags(tenantId, ("event_type", eventType)));

    public void RecordWebhookAccepted(Guid tenantId, string eventType) =>
        _webhookAccepted.Add(1, Tags(tenantId, ("event_type", eventType)));

    public void RecordWebhookRejected(Guid tenantId, string reasonCode) =>
        _webhookRejected.Add(1, Tags(tenantId, ("reason_code", reasonCode)));

    public void RecordReplayDetected(Guid tenantId, string source) =>
        _replayDetected.Add(1, Tags(tenantId, ("source", source)));

    public void RecordDuplicateDetected(Guid tenantId, string source) =>
        _duplicateDetected.Add(1, Tags(tenantId, ("source", source)));

    public void RecordRuntimeAccepted(Guid tenantId, string source) =>
        _runtimeAccepted.Add(1, Tags(tenantId, ("source", source)));

    public void RecordRuntimeRejected(Guid tenantId, string source, string reasonCode) =>
        _runtimeRejected.Add(1, Tags(tenantId, ("source", source), ("reason_code", reasonCode)));

    public void RecordApplicationFailure(Guid tenantId, string operation, string reasonCode) =>
        _applicationFailure.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

    public void RecordProviderFailure(Guid tenantId, string operation, string reasonCode) =>
        _providerFailure.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

    public void RecordRefundRequest(Guid tenantId, string source) =>
        _refundRequest.Add(1, Tags(tenantId, ("source", source)));

    public void RecordCaptureRequest(Guid tenantId, string source) =>
        _captureRequest.Add(1, Tags(tenantId, ("source", source)));

    public void RecordVoidRequest(Guid tenantId, string source) =>
        _voidRequest.Add(1, Tags(tenantId, ("source", source)));

    public void RecordInquiryRequest(Guid tenantId, string source) =>
        _inquiryRequest.Add(1, Tags(tenantId, ("source", source)));

    public void RecordAdvancedOperationFailure(Guid tenantId, string operation, string reasonCode) =>
        _advancedOperationFailure.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

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
