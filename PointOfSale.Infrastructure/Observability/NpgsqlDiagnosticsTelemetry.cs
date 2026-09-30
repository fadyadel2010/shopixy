using System.Diagnostics.Metrics;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Process-wide, low-cardinality Npgsql and request lifecycle metrics.
/// </summary>
public sealed class NpgsqlDiagnosticsTelemetry : IDisposable
{
    public const string MeterName = "PointOfSale.Npgsql.Diagnostics";
    public const string MeterVersion = "1.0.0";

    private readonly Meter _meter = new(MeterName, MeterVersion);
    private readonly Counter<long> _totalRequests;
    private readonly Counter<long> _slowRequests;
    private readonly Counter<long> _verySlowRequests;
    private readonly Counter<long> _criticalRequests;
    private readonly Counter<long> _timeoutRequests;
    private readonly Counter<long> _cancelledRequests;
    private readonly Histogram<double> _requestDurationMs;
    private readonly Histogram<double> _connectionAcquisitionMs;
    private readonly Histogram<double> _commandExecutionMs;
    private readonly Histogram<double> _resultConsumptionMs;
    private readonly Histogram<double> _efMaterializationMs;
    private readonly Histogram<double> _cleanupMs;
    private readonly Histogram<double> _totalCommandDurationMs;
    private readonly Counter<long> _connectionAcquisitionTimeout;
    private readonly Counter<long> _connectionPoolExhausted;
    private readonly Counter<long> _commandTimeout;
    private readonly ObservableGauge<int> _activeConnections;
    private readonly ObservableGauge<int> _idleConnections;
    private readonly ObservableGauge<int> _executingCommands;

    private int _activeConnectionsValue;
    private int _idleConnectionsValue;
    private int _executingCommandsValue;

    public NpgsqlDiagnosticsTelemetry()
    {
        _totalRequests = _meter.CreateCounter<long>(
            "npgsql.diagnostics.requests.total", "{request}", "Total HTTP requests observed by request diagnostics");
        _slowRequests = _meter.CreateCounter<long>(
            "npgsql.diagnostics.requests.slow", "{request}", "Requests exceeding the configured slow threshold");
        _verySlowRequests = _meter.CreateCounter<long>(
            "npgsql.diagnostics.requests.very_slow", "{request}", "Requests exceeding five seconds");
        _criticalRequests = _meter.CreateCounter<long>(
            "npgsql.diagnostics.requests.critical", "{request}", "Requests exceeding thirty seconds");
        _timeoutRequests = _meter.CreateCounter<long>(
            "npgsql.diagnostics.requests.timeout", "{request}", "Timed out requests");
        _cancelledRequests = _meter.CreateCounter<long>(
            "npgsql.diagnostics.requests.cancelled", "{request}", "Cancelled requests");
        _requestDurationMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.request.duration_ms", "ms", "End-to-end HTTP request duration");
        _connectionAcquisitionMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.connection.acquisition_ms", "ms", "Npgsql connection acquisition duration");
        _commandExecutionMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.command.execution_ms", "ms", "Npgsql command duration through first result");
        _resultConsumptionMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.result.consumption_ms", "ms", "Result reader consumption duration");
        _efMaterializationMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.ef.materialization_ms", "ms", "EF materialization approximation");
        _cleanupMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.cleanup_ms", "ms", "Reader cleanup duration");
        _totalCommandDurationMs = _meter.CreateHistogram<double>(
            "npgsql.diagnostics.command.total_duration_ms", "ms", "Total Npgsql command lifecycle duration");
        _connectionAcquisitionTimeout = _meter.CreateCounter<long>(
            "npgsql.diagnostics.connection.acquisition_timeout", "{event}", "Connection acquisition failures");
        _connectionPoolExhausted = _meter.CreateCounter<long>(
            "npgsql.diagnostics.pool.exhausted", "{event}", "Observed connection pool exhaustion events");
        _commandTimeout = _meter.CreateCounter<long>(
            "npgsql.diagnostics.command.timeout", "{event}", "Cancelled Npgsql commands");
        _activeConnections = _meter.CreateObservableGauge(
            "npgsql.diagnostics.connections.active",
            () => Volatile.Read(ref _activeConnectionsValue),
            "{connection}",
            "Connections opened through instrumented DbContexts");
        _idleConnections = _meter.CreateObservableGauge(
            "npgsql.diagnostics.connections.idle",
            () => Volatile.Read(ref _idleConnectionsValue),
            "{connection}",
            "Provider-reported idle connections when available");
        _executingCommands = _meter.CreateObservableGauge(
            "npgsql.diagnostics.commands.executing",
            () => Volatile.Read(ref _executingCommandsValue),
            "{command}",
            "Commands currently tracked by instrumented DbContexts");
    }

    public void RecordRequest(
        double durationMs,
        bool isSlow,
        bool isVerySlow,
        bool isCritical,
        bool isTimeout,
        bool isCancelled)
    {
        _totalRequests.Add(1);
        _requestDurationMs.Record(durationMs);
        if (isSlow) _slowRequests.Add(1);
        if (isVerySlow) _verySlowRequests.Add(1);
        if (isCritical) _criticalRequests.Add(1);
        if (isTimeout) _timeoutRequests.Add(1);
        if (isCancelled) _cancelledRequests.Add(1);
    }

    public void RecordConnectionAcquisition(double durationMs) => _connectionAcquisitionMs.Record(durationMs);

    public void RecordConnectionAcquisitionTimeout() => _connectionAcquisitionTimeout.Add(1);

    public void RecordPoolExhausted() => _connectionPoolExhausted.Add(1);

    public void RecordCommandExecution(double durationMs) => _commandExecutionMs.Record(durationMs);

    public void RecordResultConsumption(double durationMs) => _resultConsumptionMs.Record(durationMs);

    public void RecordEFMaterialization(double durationMs) => _efMaterializationMs.Record(durationMs);

    public void RecordCleanup(double durationMs) => _cleanupMs.Record(durationMs);

    public void RecordTotalCommandDuration(double durationMs) => _totalCommandDurationMs.Record(durationMs);

    public void RecordCommandTimeout() => _commandTimeout.Add(1);

    public int ConnectionOpened() => Interlocked.Increment(ref _activeConnectionsValue);

    public int ConnectionClosed() => DecrementToZero(ref _activeConnectionsValue);

    public void SetIdleConnections(int value) =>
        Volatile.Write(ref _idleConnectionsValue, Math.Max(0, value));

    public int CommandStarted() => Interlocked.Increment(ref _executingCommandsValue);

    public int CommandCompleted() => DecrementToZero(ref _executingCommandsValue);

    public NpgsqlDiagnosticsSnapshot GetSnapshot() => new(
        Volatile.Read(ref _activeConnectionsValue),
        Volatile.Read(ref _idleConnectionsValue),
        Volatile.Read(ref _executingCommandsValue),
        DateTimeOffset.UtcNow);

    public void Dispose() => _meter.Dispose();

    private static int DecrementToZero(ref int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref value);
            if (current <= 0)
                return 0;

            var next = current - 1;
            if (Interlocked.CompareExchange(ref value, next, current) == current)
                return next;
        }
    }
}

public sealed record NpgsqlDiagnosticsSnapshot(
    int ActiveConnections,
    int IdleConnections,
    int ExecutingCommands,
    DateTimeOffset TimestampUtc);
