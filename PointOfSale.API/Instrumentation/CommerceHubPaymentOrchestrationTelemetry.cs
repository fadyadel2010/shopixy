using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class CommerceHubPaymentOrchestrationTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.CommerceHub.PaymentOrchestration";
    public const string PaymentExecutionStarted = nameof(PaymentExecutionStarted);
    public const string PaymentExecutionSucceeded = nameof(PaymentExecutionSucceeded);
    public const string PaymentExecutionFailed = nameof(PaymentExecutionFailed);
    public const string ProviderSelected = nameof(ProviderSelected);
    public const string WebhookReceived = nameof(WebhookReceived);
    public const string PaymentCompletionStarted = nameof(PaymentCompletionStarted);
    public const string PaymentCompletionSucceeded = nameof(PaymentCompletionSucceeded);
    public const string PaymentCompletionFailed = nameof(PaymentCompletionFailed);
    public const string OrderMarkedPaid = nameof(OrderMarkedPaid);
    public const string VendorEarningsCreated = nameof(VendorEarningsCreated);
    public const string SettlementCreated = nameof(SettlementCreated);
    public const string InventoryFinalized = nameof(InventoryFinalized);
    public const string OutboxPublished = nameof(OutboxPublished);

    private readonly Meter _meter;
    private readonly Counter<long> _paymentExecutionStarted;
    private readonly Counter<long> _paymentExecutionSucceeded;
    private readonly Counter<long> _paymentExecutionFailed;
    private readonly Counter<long> _providerSelected;
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _paymentCompletionStarted;
    private readonly Counter<long> _paymentCompletionSucceeded;
    private readonly Counter<long> _paymentCompletionFailed;
    private readonly Counter<long> _orderMarkedPaid;
    private readonly Counter<long> _vendorEarningsCreated;
    private readonly Counter<long> _settlementCreated;
    private readonly Counter<long> _inventoryFinalized;
    private readonly Counter<long> _outboxPublished;
    private readonly Histogram<double> _completionDurationMs;

    public CommerceHubPaymentOrchestrationTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _paymentExecutionStarted = _meter.CreateCounter<long>("commercehub_payment_execution_started_total");
        _paymentExecutionSucceeded = _meter.CreateCounter<long>("commercehub_payment_execution_succeeded_total");
        _paymentExecutionFailed = _meter.CreateCounter<long>("commercehub_payment_execution_failed_total");
        _providerSelected = _meter.CreateCounter<long>("commercehub_payment_provider_selected_total");
        _webhookReceived = _meter.CreateCounter<long>("commercehub_payment_webhook_received_total");
        _paymentCompletionStarted = _meter.CreateCounter<long>("commercehub_payment_completion_started_total");
        _paymentCompletionSucceeded = _meter.CreateCounter<long>("commercehub_payment_completion_succeeded_total");
        _paymentCompletionFailed = _meter.CreateCounter<long>("commercehub_payment_completion_failed_total");
        _orderMarkedPaid = _meter.CreateCounter<long>("commercehub_order_marked_paid_total");
        _vendorEarningsCreated = _meter.CreateCounter<long>("commercehub_vendor_earnings_created_total");
        _settlementCreated = _meter.CreateCounter<long>("commercehub_settlement_created_total");
        _inventoryFinalized = _meter.CreateCounter<long>("commercehub_inventory_finalized_total");
        _outboxPublished = _meter.CreateCounter<long>("commercehub_payment_completion_outbox_published_total");
        _completionDurationMs = _meter.CreateHistogram<double>("commercehub_payment_completion_duration_ms");
    }

    public void RecordPaymentExecutionStarted(string provider) =>
        _paymentExecutionStarted.Add(
            1,
            new KeyValuePair<string, object?>("event", PaymentExecutionStarted),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)));

    public void RecordPaymentExecutionSucceeded(string provider, string status) =>
        _paymentExecutionSucceeded.Add(
            1,
            new KeyValuePair<string, object?>("event", PaymentExecutionSucceeded),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("status", NormalizeDimension(status, "unknown")));

    public void RecordPaymentExecutionFailed(string provider, string reasonCode) =>
        _paymentExecutionFailed.Add(
            1,
            new KeyValuePair<string, object?>("event", PaymentExecutionFailed),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("reason_code", NormalizeDimension(reasonCode, "UNKNOWN")));

    public void RecordProviderSelected(string provider) =>
        _providerSelected.Add(
            1,
            new KeyValuePair<string, object?>("event", ProviderSelected),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)));

    public void RecordWebhookReceived(string provider) =>
        _webhookReceived.Add(
            1,
            new KeyValuePair<string, object?>("event", WebhookReceived),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)));

    public void RecordPaymentCompletionStarted(string provider) =>
        _paymentCompletionStarted.Add(1, Tags(PaymentCompletionStarted, provider));

    public void RecordPaymentCompletionSucceeded(string provider, double durationMs)
    {
        _paymentCompletionSucceeded.Add(1, Tags(PaymentCompletionSucceeded, provider));
        _completionDurationMs.Record(
            durationMs,
            new KeyValuePair<string, object?>("event", "CompletionDuration"),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)));
    }

    public void RecordPaymentCompletionFailed(string provider, string reasonCode) =>
        _paymentCompletionFailed.Add(
            1,
            new KeyValuePair<string, object?>("event", PaymentCompletionFailed),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider)),
            new KeyValuePair<string, object?>("reason_code", NormalizeDimension(reasonCode, "UNKNOWN")));

    public void RecordOrderMarkedPaid(string provider) => _orderMarkedPaid.Add(1, Tags(OrderMarkedPaid, provider));

    public void RecordVendorEarningsCreated(string provider, int count) =>
        _vendorEarningsCreated.Add(count, Tags(VendorEarningsCreated, provider));

    public void RecordSettlementCreated(string provider, int count) =>
        _settlementCreated.Add(count, Tags(SettlementCreated, provider));

    public void RecordInventoryFinalized(string provider, int reservationCount) =>
        _inventoryFinalized.Add(reservationCount, Tags(InventoryFinalized, provider));

    public void RecordOutboxPublished(string provider, int eventCount) =>
        _outboxPublished.Add(eventCount, Tags(OutboxPublished, provider));

    public void Dispose() => _meter.Dispose();

    private static string NormalizeProvider(string? provider) =>
        NormalizeDimension(provider, "unknown").ToUpperInvariant();

    private static KeyValuePair<string, object?>[] Tags(string eventName, string provider) =>
        new[]
        {
            new KeyValuePair<string, object?>("event", eventName),
            new KeyValuePair<string, object?>("provider", NormalizeProvider(provider))
        };

    private static string NormalizeDimension(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }
}
