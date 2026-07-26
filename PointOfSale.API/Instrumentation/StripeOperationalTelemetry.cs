using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class StripeOperationalTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Payments.Stripe";

    public static readonly IReadOnlyCollection<string> RequiredMetricNames = new[]
    {
        "stripe_payment_created_total",
        "stripe_payment_succeeded_total",
        "stripe_payment_failed_total",
        "stripe_webhook_received_total",
        "stripe_webhook_duplicate_total",
        "stripe_replay_prevented_total",
        "stripe_refund_success_total",
        "stripe_refund_failure_total",
        "stripe_capture_success_total",
        "stripe_capture_failure_total",
        "stripe_runtime_decision_total",
        "stripe_payment_duration_ms",
        "stripe_provider_latency_ms",
        "stripe_webhook_processing_ms",
        "stripe_webhook_replay_total",
        "stripe_webhook_failures_total",
        "stripe_provider_availability_total",
        "stripe_provider_retries_total",
        "stripe_refund_latency_ms"
    };

    private readonly Meter _meter;
    private readonly Counter<long> _paymentCreated;
    private readonly Counter<long> _paymentSucceeded;
    private readonly Counter<long> _paymentFailed;
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _webhookDuplicate;
    private readonly Counter<long> _replayPrevented;
    private readonly Counter<long> _refundSuccess;
    private readonly Counter<long> _refundFailure;
    private readonly Counter<long> _captureSuccess;
    private readonly Counter<long> _captureFailure;
    private readonly Counter<long> _runtimeDecision;
    private readonly Histogram<double> _paymentDuration;
    private readonly Histogram<double> _providerLatency;
    private readonly Histogram<double> _webhookProcessing;
    private readonly Counter<long> _webhookReplay;
    private readonly Counter<long> _webhookFailures;
    private readonly Counter<long> _providerAvailability;
    private readonly Counter<long> _providerRetries;
    private readonly Histogram<double> _refundLatency;

    public StripeOperationalTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _paymentCreated = _meter.CreateCounter<long>("stripe_payment_created_total");
        _paymentSucceeded = _meter.CreateCounter<long>("stripe_payment_succeeded_total");
        _paymentFailed = _meter.CreateCounter<long>("stripe_payment_failed_total");
        _webhookReceived = _meter.CreateCounter<long>("stripe_webhook_received_total");
        _webhookDuplicate = _meter.CreateCounter<long>("stripe_webhook_duplicate_total");
        _replayPrevented = _meter.CreateCounter<long>("stripe_replay_prevented_total");
        _refundSuccess = _meter.CreateCounter<long>("stripe_refund_success_total");
        _refundFailure = _meter.CreateCounter<long>("stripe_refund_failure_total");
        _captureSuccess = _meter.CreateCounter<long>("stripe_capture_success_total");
        _captureFailure = _meter.CreateCounter<long>("stripe_capture_failure_total");
        _runtimeDecision = _meter.CreateCounter<long>("stripe_runtime_decision_total");
        _paymentDuration = _meter.CreateHistogram<double>("stripe_payment_duration_ms", unit: "ms");
        _providerLatency = _meter.CreateHistogram<double>("stripe_provider_latency_ms", unit: "ms");
        _webhookProcessing = _meter.CreateHistogram<double>("stripe_webhook_processing_ms", unit: "ms");
        _webhookReplay = _meter.CreateCounter<long>("stripe_webhook_replay_total");
        _webhookFailures = _meter.CreateCounter<long>("stripe_webhook_failures_total");
        _providerAvailability = _meter.CreateCounter<long>("stripe_provider_availability_total");
        _providerRetries = _meter.CreateCounter<long>("stripe_provider_retries_total");
        _refundLatency = _meter.CreateHistogram<double>("stripe_refund_latency_ms", unit: "ms");
    }

    public void RecordPaymentCreated(Guid tenantId, string mode) =>
        _paymentCreated.Add(1, Tags(tenantId, ("mode", mode)));

    public void RecordPaymentSucceeded(Guid tenantId, string source) =>
        _paymentSucceeded.Add(1, Tags(tenantId, ("source", source)));

    public void RecordPaymentFailed(Guid tenantId, string source, string reasonCode) =>
        _paymentFailed.Add(1, Tags(tenantId, ("source", source), ("reason_code", reasonCode)));

    public void RecordWebhookReceived(Guid tenantId, string eventType) =>
        _webhookReceived.Add(1, Tags(tenantId, ("event_type", eventType)));

    public void RecordWebhookDuplicate(Guid tenantId, string eventType) =>
        _webhookDuplicate.Add(1, Tags(tenantId, ("event_type", eventType)));

    public void RecordReplayPrevented(Guid tenantId, string reasonCode) =>
        _replayPrevented.Add(1, Tags(tenantId, ("reason_code", reasonCode)));

    public void RecordRefundSuccess(Guid tenantId, string source) =>
        _refundSuccess.Add(1, Tags(tenantId, ("source", source)));

    public void RecordRefundFailure(Guid tenantId, string source, string reasonCode) =>
        _refundFailure.Add(1, Tags(tenantId, ("source", source), ("reason_code", reasonCode)));

    public void RecordCaptureSuccess(Guid tenantId, string source) =>
        _captureSuccess.Add(1, Tags(tenantId, ("source", source)));

    public void RecordCaptureFailure(Guid tenantId, string source, string reasonCode) =>
        _captureFailure.Add(1, Tags(tenantId, ("source", source), ("reason_code", reasonCode)));

    public void RecordRuntimeDecision(Guid tenantId, string decisionStatus) =>
        _runtimeDecision.Add(1, Tags(tenantId, ("decision_status", decisionStatus)));

    public void RecordPaymentDuration(Guid tenantId, string operation, TimeSpan elapsed, string outcome) =>
        _paymentDuration.Record(elapsed.TotalMilliseconds, Tags(tenantId, ("operation", operation), ("outcome", outcome)));

    public void RecordProviderLatency(Guid tenantId, string operation, TimeSpan elapsed, string outcome) =>
        _providerLatency.Record(elapsed.TotalMilliseconds, Tags(tenantId, ("operation", operation), ("outcome", outcome)));

    public void RecordWebhookProcessing(Guid tenantId, string eventType, TimeSpan elapsed, string outcome) =>
        _webhookProcessing.Record(elapsed.TotalMilliseconds, Tags(tenantId, ("event_type", eventType), ("outcome", outcome)));

    public void RecordWebhookReplay(Guid tenantId, string eventType, string reasonCode) =>
        _webhookReplay.Add(1, Tags(tenantId, ("event_type", eventType), ("reason_code", reasonCode)));

    public void RecordWebhookFailure(Guid tenantId, string eventType, string reasonCode) =>
        _webhookFailures.Add(1, Tags(tenantId, ("event_type", eventType), ("reason_code", reasonCode)));

    public void RecordProviderAvailability(Guid tenantId, string operation, bool available, string reasonCode) =>
        _providerAvailability.Add(1, Tags(tenantId, ("operation", operation), ("available", available ? "true" : "false"), ("reason_code", reasonCode)));

    public void RecordProviderRetry(Guid tenantId, string operation, string reasonCode) =>
        _providerRetries.Add(1, Tags(tenantId, ("operation", operation), ("reason_code", reasonCode)));

    public void RecordRefundLatency(Guid tenantId, string source, TimeSpan elapsed, string outcome) =>
        _refundLatency.Record(elapsed.TotalMilliseconds, Tags(tenantId, ("source", source), ("outcome", outcome)));

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
