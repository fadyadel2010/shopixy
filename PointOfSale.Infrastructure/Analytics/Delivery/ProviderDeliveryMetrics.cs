using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryMetrics : IDisposable
{
    public const string MeterName = "PointOfSale.ProviderDelivery";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _circuitRecovered;
    private readonly Histogram<double> _latencyMs;
    private readonly ObservableGauge<long> _pending;
    private readonly ObservableGauge<long> _deadLettered;
    private long _pendingCount;
    private long _deadLetteredCount;

    public ProviderDeliveryMetrics()
    {
        _delivered = _meter.CreateCounter<long>("provider_delivery_delivered_total");
        _failed = _meter.CreateCounter<long>("provider_delivery_failed_total");
        _circuitRecovered = _meter.CreateCounter<long>("provider_delivery_circuit_recovered_total");
        _latencyMs = _meter.CreateHistogram<double>("provider_delivery_latency_ms");
        _pending = _meter.CreateObservableGauge("provider_delivery_pending", () => _pendingCount);
        _deadLettered = _meter.CreateObservableGauge("provider_delivery_dead_lettered", () => _deadLetteredCount);
    }

    public void RecordDelivery(string provider, TimeSpan latency)
    {
        _delivered.Add(1, new KeyValuePair<string, object?>("provider", provider));
        _latencyMs.Record(latency.TotalMilliseconds, new KeyValuePair<string, object?>("provider", provider));
    }

    public void RecordFailure(string provider, string failureClass, bool terminal)
    {
        _failed.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("failure_class", failureClass),
            new KeyValuePair<string, object?>("terminal", terminal));
    }

    public void SetPending(long count) => Interlocked.Exchange(ref _pendingCount, count);
    public void SetDeadLettered(long count) => Interlocked.Exchange(ref _deadLetteredCount, count);

    public void RecordCircuitRecovered(string provider)
    {
        _circuitRecovered.Add(1, new KeyValuePair<string, object?>("provider", provider));
    }

    public void Dispose() => _meter.Dispose();
}
