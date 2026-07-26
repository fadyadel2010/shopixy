using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class SuperAdminPayoutOrchestrationTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.SuperAdmin.PayoutOrchestration";
    public const string PayoutExecutionStarted = nameof(PayoutExecutionStarted);
    public const string PayoutExecutionSucceeded = nameof(PayoutExecutionSucceeded);
    public const string PayoutExecutionFailed = nameof(PayoutExecutionFailed);

    private readonly Meter _meter;
    private readonly Counter<long> _executionStarted;
    private readonly Counter<long> _executionSucceeded;
    private readonly Counter<long> _executionFailed;

    public SuperAdminPayoutOrchestrationTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _executionStarted = _meter.CreateCounter<long>("super_admin_payout_execution_started_total");
        _executionSucceeded = _meter.CreateCounter<long>("super_admin_payout_execution_succeeded_total");
        _executionFailed = _meter.CreateCounter<long>("super_admin_payout_execution_failed_total");
    }

    public void RecordExecutionStarted() =>
        _executionStarted.Add(1, new KeyValuePair<string, object?>("event", PayoutExecutionStarted));

    public void RecordExecutionSucceeded(string status) =>
        _executionSucceeded.Add(
            1,
            new KeyValuePair<string, object?>("event", PayoutExecutionSucceeded),
            new KeyValuePair<string, object?>("status", status));

    public void RecordExecutionFailed(string reasonCode) =>
        _executionFailed.Add(
            1,
            new KeyValuePair<string, object?>("event", PayoutExecutionFailed),
            new KeyValuePair<string, object?>("reason_code", reasonCode));

    public void Dispose() => _meter.Dispose();
}
