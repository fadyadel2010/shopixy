using System.Diagnostics.Metrics;

namespace PointOfSale.API.Services.Checkout;

public sealed class CheckoutLockTelemetry
{
    public const string MeterName = "PointOfSale.Checkout.Locks";

    private readonly Meter _meter;
    private long _attemptCount;
    private long _contentionCount;
    private long _timeoutCount;
    private long _infrastructureFailureCount;
    private long _lastWarnedAttemptCount;

    public Counter<long> AcquisitionCount { get; }
    public Counter<long> ContentionCount { get; }
    public Counter<long> TimeoutCount { get; }
    public Counter<long> InfrastructureFailureCount { get; }
    public Histogram<double> AcquisitionDurationMs { get; }
    public Histogram<double> WaitDurationMs { get; }

    public CheckoutLockTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        AcquisitionCount = _meter.CreateCounter<long>(
            "checkout.lock.acquisition_total",
            "count",
            "Checkout lock acquisitions that completed successfully.");
        ContentionCount = _meter.CreateCounter<long>(
            "checkout.lock.contention_total",
            "count",
            "Checkout lock acquisition attempts rejected due to contention.");
        TimeoutCount = _meter.CreateCounter<long>(
            "checkout.lock.timeout_total",
            "count",
            "Checkout executions that exceeded the maximum lock duration.");
        InfrastructureFailureCount = _meter.CreateCounter<long>(
            "lock_infrastructure_failure_total",
            "count",
            "Checkout lock operations that failed because Redis or lock infrastructure was unavailable.");
        AcquisitionDurationMs = _meter.CreateHistogram<double>(
            "checkout.lock.acquisition_ms",
            "ms",
            "Elapsed time to acquire the checkout/cart lock lease.");
        WaitDurationMs = _meter.CreateHistogram<double>(
            "checkout.lock.wait_ms",
            "ms",
            "Elapsed time spent waiting on a checkout lock acquisition attempt.");

        _meter.CreateObservableGauge<double>(
            "checkout.lock.contention_rate",
            () => CurrentContentionRate,
            "ratio",
            "Contention rate for checkout lock acquisition attempts.");
    }

    public double CurrentContentionRate
    {
        get
        {
            var attempts = Volatile.Read(ref _attemptCount);
            if (attempts <= 0)
            {
                return 0d;
            }

            return (double)Volatile.Read(ref _contentionCount) / attempts;
        }
    }

    public long TimeoutTotal => Volatile.Read(ref _timeoutCount);

    public long InfrastructureFailureTotal => Volatile.Read(ref _infrastructureFailureCount);

    public long AttemptTotal => Volatile.Read(ref _attemptCount);

    public void RecordAcquisitionAttempt(TimeSpan elapsed, bool acquired)
    {
        var waitMs = Math.Max(0d, elapsed.TotalMilliseconds);
        WaitDurationMs.Record(waitMs);
        Interlocked.Increment(ref _attemptCount);

        if (acquired)
        {
            AcquisitionCount.Add(1);
            AcquisitionDurationMs.Record(waitMs);
            return;
        }

        Interlocked.Increment(ref _contentionCount);
        ContentionCount.Add(1);
    }

    public void RecordExecutionTimeout()
    {
        Interlocked.Increment(ref _timeoutCount);
        TimeoutCount.Add(1);
    }

    public void RecordInfrastructureFailure()
    {
        Interlocked.Increment(ref _infrastructureFailureCount);
        InfrastructureFailureCount.Add(1);
    }

    public bool ShouldWarnHighContention(double threshold, int minimumAttempts)
    {
        var attempts = Volatile.Read(ref _attemptCount);
        if (attempts < minimumAttempts || CurrentContentionRate < threshold)
        {
            return false;
        }

        var previousWarnedAt = Volatile.Read(ref _lastWarnedAttemptCount);
        if (attempts - previousWarnedAt < minimumAttempts)
        {
            return false;
        }

        Interlocked.Exchange(ref _lastWarnedAttemptCount, attempts);
        return true;
    }
}
