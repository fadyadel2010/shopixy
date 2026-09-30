using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Records Npgsql command lifecycle activities and metrics. The interceptor remains
/// scoped because command correlation is mutable; only the ActivitySource is shared.
/// </summary>
public sealed class NpgsqlLifecycleDiagnosticInterceptor : DbCommandInterceptor
{
    internal const string ActivitySourceName = "PointOfSale.Npgsql.Diagnostics";
    internal const string ActivitySourceVersion = "1.0.0";

    internal static readonly ActivitySource SharedActivitySource =
        new(ActivitySourceName, ActivitySourceVersion);

    private const double VerySlowThresholdMs = 5000;
    private const double CriticalThresholdMs = 30000;

    private readonly ILogger<NpgsqlLifecycleDiagnosticInterceptor> _logger;
    private readonly NpgsqlDiagnosticsTelemetry _telemetry;
    private readonly NpgsqlDiagnosticsOptions _options;
    private readonly NpgsqlDiagnosticLogRateLimiter? _logRateLimiter;
    private readonly ConcurrentDictionary<Guid, CommandTimingState> _commands = new();
    private int _executingCommands;

    internal ActivitySource ActivitySource => SharedActivitySource;

    internal int TrackedCommandCount => _commands.Count;

    internal int ExecutingCommandCount => Volatile.Read(ref _executingCommands);

    public NpgsqlLifecycleDiagnosticInterceptor(
        ILogger<NpgsqlLifecycleDiagnosticInterceptor> logger,
        NpgsqlDiagnosticsTelemetry telemetry,
        IOptions<NpgsqlDiagnosticsOptions>? options = null,
        NpgsqlDiagnosticLogRateLimiter? logRateLimiter = null)
    {
        _logger = logger;
        _telemetry = telemetry;
        _options = options?.Value ?? new NpgsqlDiagnosticsOptions();
        _logRateLimiter = logRateLimiter;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        OnCommandStarting(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        OnCommandStarting(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object?> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object?> result)
    {
        OnCommandStarting(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object?>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object?> result,
        CancellationToken cancellationToken = default)
    {
        OnCommandStarting(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        OnCommandStarting(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        OnCommandStarting(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        OnCommandCompleted(eventData, false);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        OnCommandCompleted(eventData, false);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        OnCommandCompleted(eventData, false);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        OnCommandCompleted(eventData, false);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        OnCommandCompleted(eventData, false);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        OnCommandCompleted(eventData, false);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        OnCommandCompleted(eventData, true);

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        OnCommandCompleted(eventData, true);
        return Task.CompletedTask;
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) =>
        OnCommandCanceled(eventData);

    public override Task CommandCanceledAsync(
        DbCommand command,
        CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        OnCommandCanceled(eventData);
        return Task.CompletedTask;
    }

    public override InterceptionResult DataReaderClosing(
        DbCommand command,
        DataReaderClosingEventData eventData,
        InterceptionResult result)
    {
        MarkReaderClosing(eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult> DataReaderClosingAsync(
        DbCommand command,
        DataReaderClosingEventData eventData,
        InterceptionResult result)
    {
        MarkReaderClosing(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result)
    {
        OnReaderDisposing(eventData);
        return result;
    }

    private void OnCommandStarting(DbCommand command, CommandEventData eventData)
    {
        if (!_options.Enabled || command is not NpgsqlCommand npgsqlCommand)
            return;

        var state = new CommandTimingState
        {
            CommandStartTimestamp = Stopwatch.GetTimestamp(),
            Operation = GetCommandType(npgsqlCommand.CommandText),
            ConnectionId = _options.CaptureConnectionIdentityInActivities
                ? GetConnectionId(npgsqlCommand.Connection as NpgsqlConnection)
                : null,
            Statement = _options.CaptureCommandTextInActivities
                ? TruncateCommandText(npgsqlCommand.CommandText, _options.MaxStatementLength)
                : null
        };

        state.Activity = SharedActivitySource.StartActivity(
            "Npgsql.Command.Execute",
            ActivityKind.Internal);

        if (state.Activity is { } activity)
        {
            activity.SetTag("db.operation", state.Operation);
            activity.SetTag("db.system", "postgresql");

            if (state.ConnectionId is not null)
                activity.SetTag("db.connection.id", state.ConnectionId);
            if (state.Statement is not null)
                activity.SetTag("db.statement", state.Statement);
        }

        _commands[eventData.CommandId] = state;
        Interlocked.Increment(ref _executingCommands);
        _telemetry.CommandStarted();
    }

    private void OnCommandCompleted(CommandEventData eventData, bool isError)
    {
        if (!_commands.TryGetValue(eventData.CommandId, out var state))
            return;

        var totalDurationMs = eventData switch
        {
            CommandExecutedEventData executed => executed.Duration.TotalMilliseconds,
            CommandErrorEventData failed => failed.Duration.TotalMilliseconds,
            _ => 0
        };
        _telemetry.RecordCommandExecution(totalDurationMs);
        RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlCommand(totalDurationMs);

        var isReaderCommand = eventData is CommandExecutedEventData executedEvent
            && executedEvent.ExecuteMethod == DbCommandMethod.ExecuteReader;

        if (isReaderCommand)
        {
            state.ReaderOpenedTimestamp = Stopwatch.GetTimestamp();
        }
        else
        {
            _telemetry.RecordTotalCommandDuration(totalDurationMs);
        }

        if (totalDurationMs >= _options.SlowCommandThresholdMs)
        {
            state.Activity?.SetTag("db.command.slow", true);
            state.Activity?.SetTag("db.command.total_duration_ms", totalDurationMs);

            if (_options.SlowCommandLoggingEnabled
                && _logRateLimiter?.TryAcquire(_options.MaxSlowCommandLogsPerSecond) == true)
            {
                var logLevel = totalDurationMs >= CriticalThresholdMs
                    ? LogLevel.Critical
                    : totalDurationMs >= VerySlowThresholdMs
                        ? LogLevel.Error
                        : LogLevel.Warning;

                _logger.Log(
                    logLevel,
                    "SLOW NPGSQL COMMAND | TotalMs: {TotalMs} | Operation: {Operation} | IsError: {IsError}",
                    totalDurationMs,
                    state.Operation,
                    isError);
            }
        }

        if (state.Activity is { } activity)
        {
            activity.SetTag("db.command.duration_ms", totalDurationMs);
            activity.SetTag("db.command.error", isError);

            if (isError)
                activity.SetStatus(ActivityStatusCode.Error);
            else
                activity.SetStatus(ActivityStatusCode.Ok);

            if (!isReaderCommand)
                StopActivity(state);
        }

        if (!isReaderCommand)
            CompleteCommand(eventData.CommandId, state);
    }

    private void MarkReaderClosing(Guid commandId)
    {
        if (_commands.TryGetValue(commandId, out var state))
            state.ReaderClosingTimestamp = Stopwatch.GetTimestamp();
    }

    private void OnReaderDisposing(DataReaderDisposingEventData eventData)
    {
        if (!_commands.TryGetValue(eventData.CommandId, out var state))
            return;

        if (state.ReaderOpenedTimestamp is { } readerOpened)
        {
            var consumptionMs = ElapsedMilliseconds(readerOpened);
            _telemetry.RecordResultConsumption(consumptionMs);
            _telemetry.RecordEFMaterialization(consumptionMs);
            RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlResultConsumption(consumptionMs);
            RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlMaterialization(consumptionMs);
        }

        _telemetry.RecordTotalCommandDuration(ElapsedMilliseconds(state.CommandStartTimestamp));

        if (state.ReaderClosingTimestamp is { } readerClosing)
        {
            var cleanupMs = ElapsedMilliseconds(readerClosing);
            _telemetry.RecordCleanup(cleanupMs);
            RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlCleanup(cleanupMs);
        }

        CompleteCommand(eventData.CommandId, state);
    }

    private void OnCommandCanceled(CommandEndEventData eventData)
    {
        if (!_commands.TryGetValue(eventData.CommandId, out var state))
            return;

        state.Activity?.SetTag("db.command.cancelled", true);
        state.Activity?.SetStatus(ActivityStatusCode.Error, "Command cancelled");
        _telemetry.RecordCommandTimeout();
        CompleteCommand(eventData.CommandId, state);
    }

    private void CompleteCommand(Guid commandId, CommandTimingState state)
    {
        if (!_commands.TryRemove(commandId, out _))
            return;

        Interlocked.Decrement(ref _executingCommands);
        _telemetry.CommandCompleted();
        StopActivity(state);
    }

    private static void StopActivity(CommandTimingState state)
    {
        if (state.Activity is not { } activity)
            return;

        activity.Stop();
        activity.Dispose();
        state.Activity = null;
    }

    private static double ElapsedMilliseconds(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    private static string GetConnectionId(NpgsqlConnection? connection) => connection is null
        ? "unknown"
        : $"{connection.Host}:{connection.Port}/{connection.Database}#{connection.ProcessID}";

    private static string GetCommandType(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return "UNKNOWN";

        var trimmed = commandText.TrimStart();
        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return "SELECT";
        if (trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) return "INSERT";
        if (trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)) return "UPDATE";
        if (trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)) return "DELETE";
        if (trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)) return "CTE";
        if (trimmed.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("START TRANSACTION", StringComparison.OrdinalIgnoreCase)) return "BEGIN";
        if (trimmed.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase)) return "COMMIT";
        if (trimmed.StartsWith("ROLLBACK", StringComparison.OrdinalIgnoreCase)) return "ROLLBACK";
        if (trimmed.StartsWith("SET", StringComparison.OrdinalIgnoreCase)) return "SET";
        if (trimmed.StartsWith("PREPARE", StringComparison.OrdinalIgnoreCase)) return "PREPARE";
        if (trimmed.StartsWith("EXECUTE", StringComparison.OrdinalIgnoreCase)) return "EXECUTE";
        return "OTHER";
    }

    private static string TruncateCommandText(string commandText, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return string.Empty;

        var normalized = string.Join(
            ' ',
            commandText.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private sealed class CommandTimingState
    {
        public long CommandStartTimestamp { get; init; }
        public long? ReaderOpenedTimestamp { get; set; }
        public long? ReaderClosingTimestamp { get; set; }
        public string? ConnectionId { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string? Statement { get; init; }
        public Activity? Activity { get; set; }
    }
}
