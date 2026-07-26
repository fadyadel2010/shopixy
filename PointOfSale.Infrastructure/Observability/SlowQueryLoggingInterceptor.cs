using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PointOfSale.Infrastructure.Observability;

/// <summary>
/// Logs EF Core commands that exceed the slow-query threshold so hot paths can be
/// tuned before they become production incidents.
/// </summary>
public sealed class SlowQueryLoggingInterceptor : DbCommandInterceptor
{
    private const double SlowQueryThresholdMs = 500;
    private const int MaxCommandLength = 600;

    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;

    public SlowQueryLoggingInterceptor(ILogger<SlowQueryLoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogIfSlow(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(
        DbCommand command,
        CommandErrorEventData eventData)
    {
        LogIfSlowOrFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogIfSlowOrFailed(command, eventData);
        return Task.CompletedTask;
    }

    private void LogIfSlow(DbCommand command, CommandExecutedEventData eventData)
    {
        if (eventData.Duration.TotalMilliseconds < SlowQueryThresholdMs)
        {
            return;
        }

        _logger.LogWarning(
            "SLOW SQL | DurationMs: {DurationMs} | Context: {ContextName} | Command: {CommandText}",
            eventData.Duration.TotalMilliseconds,
            eventData.Context?.GetType().Name ?? "UnknownContext",
            NormalizeCommandText(command.CommandText));
    }

    private static string NormalizeCommandText(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return string.Empty;
        }

        var normalized = string.Join(
            ' ',
            commandText
                .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= MaxCommandLength
            ? normalized
            : $"{normalized[..MaxCommandLength]}...";
    }

    private void LogIfSlowOrFailed(DbCommand command, CommandErrorEventData eventData)
    {
        var durationMs = eventData.Duration.TotalMilliseconds;
        var isSlow = durationMs >= SlowQueryThresholdMs;

        _logger.LogError(
            eventData.Exception,
            "FAILED SQL{SlowMarker} | DurationMs: {DurationMs} | Context: {ContextName} | Command: {CommandText}",
            isSlow ? " (SLOW)" : string.Empty,
            durationMs,
            eventData.Context?.GetType().Name ?? "UnknownContext",
            NormalizeCommandText(command.CommandText));
    }
}
