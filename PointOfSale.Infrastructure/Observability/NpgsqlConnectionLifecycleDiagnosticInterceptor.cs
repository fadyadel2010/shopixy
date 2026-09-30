using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Records public EF Core connection lifecycle timings without inspecting provider internals.
/// Mutable correlation state is scoped with the interceptor.
/// </summary>
public sealed class NpgsqlConnectionLifecycleDiagnosticInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<NpgsqlConnectionLifecycleDiagnosticInterceptor> _logger;
    private readonly NpgsqlDiagnosticsTelemetry _telemetry;
    private readonly NpgsqlDiagnosticsOptions _options;
    private readonly ConcurrentDictionary<Guid, long> _openingConnections = new();

    public NpgsqlConnectionLifecycleDiagnosticInterceptor(
        ILogger<NpgsqlConnectionLifecycleDiagnosticInterceptor> logger,
        NpgsqlDiagnosticsTelemetry telemetry,
        IOptions<NpgsqlDiagnosticsOptions>? options = null)
    {
        _logger = logger;
        _telemetry = telemetry;
        _options = options?.Value ?? new NpgsqlDiagnosticsOptions();
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        OnConnectionOpening(connection, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        OnConnectionOpening(connection, eventData);
        return ValueTask.FromResult(result);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        OnConnectionOpened(connection, eventData);

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        OnConnectionOpened(connection, eventData);
        return Task.CompletedTask;
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        OnConnectionFailed(connection, eventData);

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        OnConnectionFailed(connection, eventData);
        return Task.CompletedTask;
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) =>
        OnConnectionClosed(connection);

    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        OnConnectionClosed(connection);
        return Task.CompletedTask;
    }

    private void OnConnectionOpening(DbConnection connection, ConnectionEventData eventData)
    {
        if (_options.Enabled && connection is NpgsqlConnection)
            _openingConnections[eventData.ConnectionId] = Stopwatch.GetTimestamp();
    }

    private void OnConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (!_options.Enabled || connection is not NpgsqlConnection)
            return;

        var elapsedMs = _openingConnections.TryRemove(eventData.ConnectionId, out var startTimestamp)
            ? ElapsedMilliseconds(startTimestamp)
            : eventData.Duration.TotalMilliseconds;

        _telemetry.RecordConnectionAcquisition(elapsedMs);
        _telemetry.ConnectionOpened();
        RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlConnectionAcquire(elapsedMs);
    }

    private void OnConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        if (!_options.Enabled || connection is not NpgsqlConnection)
            return;

        var elapsedMs = _openingConnections.TryRemove(eventData.ConnectionId, out var startTimestamp)
            ? ElapsedMilliseconds(startTimestamp)
            : eventData.Duration.TotalMilliseconds;

        _telemetry.RecordConnectionAcquisition(elapsedMs);
        _telemetry.RecordConnectionAcquisitionTimeout();
        RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlConnectionAcquire(elapsedMs);

        _logger.LogWarning(
            "Npgsql connection acquisition failed after {DurationMs} ms ({ExceptionType})",
            elapsedMs,
            eventData.Exception.GetType().Name);
    }

    private void OnConnectionClosed(DbConnection connection)
    {
        if (_options.Enabled && connection is NpgsqlConnection)
            _telemetry.ConnectionClosed();
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
}
