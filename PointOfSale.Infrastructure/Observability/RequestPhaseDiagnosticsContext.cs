using System.Diagnostics;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Request-local aggregation for public EF/Npgsql lifecycle segments. AsyncLocal
/// keeps concurrent requests isolated without placing request state in singleton telemetry.
/// </summary>
public sealed class RequestPhaseDiagnosticsContext
{
    private static readonly AsyncLocal<RequestPhaseDiagnosticsContext?> CurrentContext = new();

    private readonly object _gate = new();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long? _endpointStartTimestamp;
    private long? _endpointEndTimestamp;

    private RequestPhaseDiagnosticsContext()
    {
    }

    public static RequestPhaseDiagnosticsContext? Current => CurrentContext.Value;

    public string RequestId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool HasException { get; set; }
    public bool Cancelled { get; set; }
    public double TotalMs { get; set; }
    public double ThresholdMs { get; set; }
    public NpgsqlAccumulator Npgsql { get; } = new();

    public static IDisposable Begin()
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = new RequestPhaseDiagnosticsContext();
        return new Scope(previous);
    }

    public void MarkEndpointStart() => _endpointStartTimestamp ??= Stopwatch.GetTimestamp();

    public void MarkEndpointEnd() => _endpointEndTimestamp ??= Stopwatch.GetTimestamp();

    public void RecordNpgsqlConnectionAcquire(double elapsedMs)
    {
        lock (_gate)
        {
            Npgsql.ConnectionAcquireCount++;
            Npgsql.ConnectionAcquireTotalMs += elapsedMs;
            Npgsql.ConnectionAcquireMaxMs = Math.Max(Npgsql.ConnectionAcquireMaxMs, elapsedMs);
        }
    }

    public void RecordNpgsqlCommand(double elapsedMs)
    {
        lock (_gate)
        {
            Npgsql.CommandCount++;
            Npgsql.CommandTotalMs += elapsedMs;
            Npgsql.CommandMaxMs = Math.Max(Npgsql.CommandMaxMs, elapsedMs);
        }
    }

    public void RecordNpgsqlResultConsumption(double elapsedMs)
    {
        lock (_gate)
        {
            Npgsql.ResultConsumptionTotalMs += elapsedMs;
            Npgsql.ResultConsumptionMaxMs = Math.Max(Npgsql.ResultConsumptionMaxMs, elapsedMs);
        }
    }

    public void RecordNpgsqlMaterialization(double elapsedMs)
    {
        lock (_gate)
        {
            Npgsql.MaterializationTotalMs += elapsedMs;
            Npgsql.MaterializationMaxMs = Math.Max(Npgsql.MaterializationMaxMs, elapsedMs);
        }
    }

    public void RecordNpgsqlCleanup(double elapsedMs)
    {
        lock (_gate)
        {
            Npgsql.CleanupTotalMs += elapsedMs;
            Npgsql.CleanupMaxMs = Math.Max(Npgsql.CleanupMaxMs, elapsedMs);
        }
    }

    public RequestPhaseDiagnosticRecord Snapshot(NpgsqlDiagnosticsSnapshot? concurrency = null)
    {
        lock (_gate)
        {
            var endpointStart = _endpointStartTimestamp ?? _startTimestamp;
            var endpointEnd = _endpointEndTimestamp ?? Stopwatch.GetTimestamp();
            return new RequestPhaseDiagnosticRecord(
                RequestId,
                TraceId,
                SpanId,
                Method,
                Route,
                Endpoint,
                StatusCode,
                TotalMs,
                ThresholdMs,
                ElapsedMilliseconds(endpointStart, endpointEnd),
                Npgsql.Snapshot(),
                concurrency,
                HasException,
                Cancelled);
        }
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1000.0 / Stopwatch.Frequency;

    private sealed class Scope(RequestPhaseDiagnosticsContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentContext.Value = previous;
            _disposed = true;
        }
    }
}

public sealed class NpgsqlAccumulator
{
    public int CommandCount { get; set; }
    public int ConnectionAcquireCount { get; set; }
    public double ConnectionAcquireTotalMs { get; set; }
    public double ConnectionAcquireMaxMs { get; set; }
    public double CommandTotalMs { get; set; }
    public double CommandMaxMs { get; set; }
    public double ResultConsumptionTotalMs { get; set; }
    public double ResultConsumptionMaxMs { get; set; }
    public double MaterializationTotalMs { get; set; }
    public double MaterializationMaxMs { get; set; }
    public double CleanupTotalMs { get; set; }
    public double CleanupMaxMs { get; set; }

    public NpgsqlPhaseSnapshot Snapshot() => new(
        CommandCount,
        ConnectionAcquireCount,
        ConnectionAcquireTotalMs,
        ConnectionAcquireMaxMs,
        CommandTotalMs,
        CommandMaxMs,
        ResultConsumptionTotalMs,
        ResultConsumptionMaxMs,
        MaterializationTotalMs,
        MaterializationMaxMs,
        CleanupTotalMs,
        CleanupMaxMs,
        CommandSubsegmentsAvailable: false);
}

public sealed record NpgsqlPhaseSnapshot(
    int CommandCount,
    int ConnectionAcquireCount,
    double ConnectionAcquireTotalMs,
    double ConnectionAcquireMaxMs,
    double CommandTotalMs,
    double CommandMaxMs,
    double ResultConsumptionTotalMs,
    double ResultConsumptionMaxMs,
    double MaterializationTotalMs,
    double MaterializationMaxMs,
    double CleanupTotalMs,
    double CleanupMaxMs,
    bool CommandSubsegmentsAvailable);

public sealed record RequestPhaseDiagnosticRecord(
    string RequestId,
    string TraceId,
    string SpanId,
    string Method,
    string Route,
    string Endpoint,
    int StatusCode,
    double TotalMs,
    double ThresholdMs,
    double EndpointExecutionMs,
    NpgsqlPhaseSnapshot Npgsql,
    NpgsqlDiagnosticsSnapshot? Concurrency,
    bool HasException,
    bool Cancelled);
