using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Instrumentation;

/// <summary>
/// OpenTelemetry-compatible metrics for reservation health and lifecycle outcomes.
/// </summary>
public sealed class InventoryReservationTelemetry
{
    public const string MeterName = "PointOfSale.Inventory.Reservations";

    private readonly Meter _meter;
    private readonly object _sync = new();
    private double _committedQuantityTotal;
    private double _releasedQuantityTotal;
    private double _totalReservedQuantity;
    private long _staleReservationCount;
    private double _staleReservedQuantity;
    private double _commitReleaseRatio;

    public Counter<double> CommittedQuantityTotal { get; }
    public Counter<double> ReleasedQuantityTotal { get; }

    public InventoryReservationTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        CommittedQuantityTotal = _meter.CreateCounter<double>(
            "inventory.reservations.committed_quantity_total",
            "units",
            "Total quantity committed from reservations.");
        ReleasedQuantityTotal = _meter.CreateCounter<double>(
            "inventory.reservations.released_quantity_total",
            "units",
            "Total quantity released from reservations.");

        _meter.CreateObservableGauge(
            "inventory.reservations.total_reserved_quantity",
            () => new Measurement<double>(GetTotalReservedQuantity()),
            "units",
            "Current reserved quantity across product variant stock.");
        _meter.CreateObservableGauge(
            "inventory.reservations.stale_count",
            () => new Measurement<long>(GetStaleReservationCount()),
            "count",
            "Current count of reservations older than the configured threshold.");
        _meter.CreateObservableGauge(
            "inventory.reservations.stale_reserved_quantity",
            () => new Measurement<double>(GetStaleReservedQuantity()),
            "units",
            "Current reserved quantity locked in stale reservations.");
        _meter.CreateObservableGauge(
            "inventory.reservations.commit_release_ratio",
            () => new Measurement<double>(GetCommitReleaseRatio()),
            "ratio",
            "Committed quantity divided by released quantity.");
    }

    public void RecordCommit(decimal quantity, string source)
    {
        var value = NormalizeQuantity(quantity);
        CommittedQuantityTotal.Add(
            value,
            new KeyValuePair<string, object?>("source", NormalizeSource(source)));

        lock (_sync)
        {
            _committedQuantityTotal += value;
            _commitReleaseRatio = ComputeRatio();
        }
    }

    public void RecordRelease(decimal quantity, string source)
    {
        var value = NormalizeQuantity(quantity);
        ReleasedQuantityTotal.Add(
            value,
            new KeyValuePair<string, object?>("source", NormalizeSource(source)));

        lock (_sync)
        {
            _releasedQuantityTotal += value;
            _commitReleaseRatio = ComputeRatio();
        }
    }

    public double UpdateSnapshot(
        decimal totalReservedQuantity,
        int staleReservationCount,
        decimal staleReservedQuantity)
    {
        lock (_sync)
        {
            _totalReservedQuantity = (double)totalReservedQuantity;
            _staleReservationCount = staleReservationCount;
            _staleReservedQuantity = (double)staleReservedQuantity;
            _commitReleaseRatio = ComputeRatio();
            return _commitReleaseRatio;
        }
    }

    private double GetTotalReservedQuantity()
    {
        lock (_sync)
        {
            return _totalReservedQuantity;
        }
    }

    private long GetStaleReservationCount()
    {
        lock (_sync)
        {
            return _staleReservationCount;
        }
    }

    private double GetStaleReservedQuantity()
    {
        lock (_sync)
        {
            return _staleReservedQuantity;
        }
    }

    private double GetCommitReleaseRatio()
    {
        lock (_sync)
        {
            return _commitReleaseRatio;
        }
    }

    private static double NormalizeQuantity(decimal quantity)
    {
        // Return absolute value without coercing zeros to 1
        // This ensures accurate metrics - zero quantities should be recorded as zero
        return Math.Abs((double)quantity);
    }

    private static string NormalizeSource(string? source) =>
        OperationalTelemetryDimensions.NormalizeTagValue(source);

    private double ComputeRatio()
    {
        if (_releasedQuantityTotal <= 0d)
        {
            return _committedQuantityTotal <= 0d ? 1d : _committedQuantityTotal;
        }

        return _committedQuantityTotal / _releasedQuantityTotal;
    }
}
