using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using PointOfSale.API.Middleware;

namespace PointOfSale.API.Instrumentation;

/// <summary>
/// FEED-1B security fix: JSON console formatter that keeps the exact log shape
/// of the built-in <c>JsonConsoleFormatter</c> (Timestamp / EventId / LogLevel /
/// Category / Message / Exception / State / Scopes) but redacts the public
/// Meta feed token wherever it can appear in rendered text. The token is part
/// of the feed URL by design (Feed-1A contract), and the ASP.NET Core host
/// pushes a per-request logging scope that carries the raw request path before
/// any application middleware runs; with IncludeScopes enabled that credential
/// would be copied into every log line of the request. Redaction is
/// deterministic prefix + final-segment based (<see cref="PublicFeedPathRedactor"/>);
/// non-feed state values retain the built-in shape. Exception diagnostics use a
/// safe renderer rather than the CLR's reflection-heavy stack formatter.
/// </summary>
public sealed class FeedTokenRedactingJsonConsoleFormatter : ConsoleFormatter, IDisposable
{
    private readonly IDisposable? _optionsReloadToken;

    public const string FormatterName = "feedTokenRedactingJson";

    public FeedTokenRedactingJsonConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        ReloadLoggerOptions(options.CurrentValue);
        _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (logEntry.Exception == null && message == null)
        {
            return;
        }

        DateTimeOffset stamp = FormatterOptions.TimestampFormat != null
            ? (FormatterOptions.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now)
            : DateTimeOffset.MinValue;

        // Non-generic worker mirrors JsonConsoleFormatter.WriteInternal to keep
        // the output shape identical across generic TState specializations.
        WriteInternal(
            scopeProvider,
            textWriter,
            message,
            logEntry.LogLevel,
            logEntry.Category,
            logEntry.EventId.Id,
            logEntry.Exception == null ? null : ExceptionDiagnosticFormatter.Format(logEntry.Exception),
            logEntry.State != null,
            ToInvariantString(logEntry.State),
            logEntry.State as IReadOnlyList<KeyValuePair<string, object?>>,
            stamp);
    }

    private void WriteInternal(
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter,
        string? message,
        LogLevel logLevel,
        string category,
        int eventId,
        string? exception,
        bool hasState,
        string? stateMessage,
        IReadOnlyList<KeyValuePair<string, object?>>? stateProperties,
        DateTimeOffset stamp)
    {
        const int DefaultBufferSize = 1024;
        var output = new ArrayBufferWriter<byte>(DefaultBufferSize);
        using (var writer = new Utf8JsonWriter(output, FormatterOptions.JsonWriterOptions))
        {
            writer.WriteStartObject();
            var timestampFormat = FormatterOptions.TimestampFormat;
            if (timestampFormat != null)
            {
                writer.WriteString("Timestamp", stamp.ToString(timestampFormat));
            }

            writer.WriteNumber(nameof(LogEntry<object>.EventId), eventId);
            writer.WriteString(nameof(LogEntry<object>.LogLevel), GetLogLevelString(logLevel));
            writer.WriteString(nameof(LogEntry<object>.Category), category);
            writer.WriteString("Message", PublicFeedPathRedactor.RedactEmbedded(message));

            if (exception != null)
            {
                writer.WriteString(nameof(Exception), PublicFeedPathRedactor.RedactEmbedded(exception));
            }

            if (hasState)
            {
                writer.WriteStartObject(nameof(LogEntry<object>.State));
                writer.WriteString("Message", PublicFeedPathRedactor.RedactEmbedded(stateMessage));
                if (stateProperties != null)
                {
                    foreach (KeyValuePair<string, object?> item in stateProperties)
                    {
                        WriteItem(writer, item);
                    }
                }

                writer.WriteEndObject();
            }

            WriteScopeInformation(writer, scopeProvider);
            writer.WriteEndObject();
            writer.Flush();
        }

        var messageBytes = output.WrittenSpan;
        var logMessageBuffer = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(messageBytes.Length));
        try
        {
            var charsWritten = Encoding.UTF8.GetChars(messageBytes, logMessageBuffer);
            textWriter.Write(logMessageBuffer, 0, charsWritten);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(logMessageBuffer);
        }

        textWriter.Write(Environment.NewLine);
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Information",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private void WriteScopeInformation(Utf8JsonWriter writer, IExternalScopeProvider? scopeProvider)
    {
        if (FormatterOptions.IncludeScopes && scopeProvider != null)
        {
            writer.WriteStartArray("Scopes");
            scopeProvider.ForEachScope(static (scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> scopeItems)
                {
                    state.WriteStartObject();
                    state.WriteString("Message", PublicFeedPathRedactor.RedactEmbedded(ToInvariantString(scope)));
                    foreach (KeyValuePair<string, object?> item in scopeItems)
                    {
                        WriteItem(state, item);
                    }

                    state.WriteEndObject();
                }
                else
                {
                    state.WriteStringValue(PublicFeedPathRedactor.RedactEmbedded(ToInvariantString(scope)));
                }
            }, writer);
            writer.WriteEndArray();
        }
    }

    private static void WriteItem(Utf8JsonWriter writer, KeyValuePair<string, object?> item)
    {
        var key = item.Key;
        switch (item.Value)
        {
            case bool boolValue:
                writer.WriteBoolean(key, boolValue);
                break;
            case byte byteValue:
                writer.WriteNumber(key, byteValue);
                break;
            case sbyte sbyteValue:
                writer.WriteNumber(key, sbyteValue);
                break;
            case char charValue:
                writer.WriteString(key, MemoryMarshal.CreateSpan(ref charValue, 1));
                break;
            case decimal decimalValue:
                writer.WriteNumber(key, decimalValue);
                break;
            case double doubleValue:
                writer.WriteNumber(key, doubleValue);
                break;
            case float floatValue:
                writer.WriteNumber(key, floatValue);
                break;
            case int intValue:
                writer.WriteNumber(key, intValue);
                break;
            case uint uintValue:
                writer.WriteNumber(key, uintValue);
                break;
            case long longValue:
                writer.WriteNumber(key, longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumber(key, ulongValue);
                break;
            case short shortValue:
                writer.WriteNumber(key, shortValue);
                break;
            case ushort ushortValue:
                writer.WriteNumber(key, ushortValue);
                break;
            case null:
                writer.WriteNull(key);
                break;
            default:
                writer.WriteString(key, PublicFeedPathRedactor.RedactEmbedded(ToInvariantString(item.Value)));
                break;
        }
    }

    private static string? ToInvariantString(object? obj) => obj is Exception exception
        ? ExceptionDiagnosticFormatter.Format(exception)
        : Convert.ToString(obj, CultureInfo.InvariantCulture);

    internal JsonConsoleFormatterOptions FormatterOptions { get; set; }

    [MemberNotNull(nameof(FormatterOptions))]
    private void ReloadLoggerOptions(JsonConsoleFormatterOptions options)
    {
        FormatterOptions = options;
    }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
    }
}
