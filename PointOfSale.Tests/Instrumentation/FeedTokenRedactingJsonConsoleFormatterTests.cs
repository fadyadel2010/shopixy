using System.Text.Json;
using System.Net.Sockets;
using Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Feeds;

namespace PointOfSale.Tests.Instrumentation;

public class FeedTokenRedactingJsonConsoleFormatterTests
{
    private static readonly JsonConsoleFormatterOptions FormatterOptions = new()
    {
        IncludeScopes = true,
        UseUtcTimestamp = true,
        TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"
    };

    [Fact]
    public void Write_FeedPathInMessageStateAndScopes_RedactsTokenEverywhere()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var feedPath = $"/api/public/feeds/meta/{rawToken}";

        var state = new List<KeyValuePair<string, object?>>
        {
            new("RequestPath", feedPath),
            new("StatusCode", 200),
            new("ElapsedMs", 12.5)
        };

        var scopeProvider = new LoggerExternalScopeProvider();
        // Mirror the host per-request scope: dictionary scope with raw path + typed values.
        using var hostScope = scopeProvider.Push(new Dictionary<string, object?>
        {
            ["RequestPath"] = feedPath,
            ["RequestId"] = "0HMFAKE0000000000001"
        });
        // Non-dictionary scope rendered as a plain string element.
        using var stringScope = scopeProvider.Push($"GET {feedPath}");

        var formatter = CreateFormatter();
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "Test.FeedCategory",
            new EventId(3, "Handled"),
            state,
            null,
            (s, _) => $"Handled request to {feedPath}");

        var output = Render(formatter, entry, scopeProvider);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.DoesNotContain(rawToken, output);
        Assert.Equal(3, root.GetProperty("EventId").GetInt32());
        Assert.Equal("Information", root.GetProperty("LogLevel").GetString());
        Assert.Equal("Test.FeedCategory", root.GetProperty("Category").GetString());
        Assert.Equal("Handled request to /api/public/feeds/meta/<REDACTED>", root.GetProperty("Message").GetString());
        Assert.Equal("/api/public/feeds/meta/<REDACTED>", root.GetProperty("State").GetProperty("RequestPath").GetString());
        Assert.Equal(200, root.GetProperty("State").GetProperty("StatusCode").GetInt32());
        Assert.Equal(12.5, root.GetProperty("State").GetProperty("ElapsedMs").GetDouble());

        var scopes = root.GetProperty("Scopes");
        Assert.Equal(2, scopes.GetArrayLength());

        var hostScopeElement = scopes[0];
        Assert.Equal("/api/public/feeds/meta/<REDACTED>", hostScopeElement.GetProperty("RequestPath").GetString());
        Assert.Equal("0HMFAKE0000000000001", hostScopeElement.GetProperty("RequestId").GetString());
        Assert.DoesNotContain(rawToken, hostScopeElement.GetProperty("Message").GetString());

        Assert.Equal("GET /api/public/feeds/meta/<REDACTED>", scopes[1].GetString());
    }

    [Fact]
    public void Write_NonFeedPath_IsEmittedUnchanged()
    {
        const string Path = "/api/products/42";
        var state = new List<KeyValuePair<string, object?>> { new("RequestPath", Path) };

        var scopeProvider = new LoggerExternalScopeProvider();
        using var hostScope = scopeProvider.Push(new Dictionary<string, object?> { ["RequestPath"] = Path });

        var formatter = CreateFormatter();
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "Test.OtherCategory",
            new EventId(0),
            state,
            null,
            (s, _) => $"Handled request to {Path}");

        var output = Render(formatter, entry, scopeProvider);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.DoesNotContain("<REDACTED>", output);
        Assert.Equal(0, root.GetProperty("EventId").GetInt32());
        Assert.Equal($"Handled request to {Path}", root.GetProperty("Message").GetString());
        Assert.Equal(Path, root.GetProperty("State").GetProperty("RequestPath").GetString());
        Assert.Equal(Path, root.GetProperty("Scopes")[0].GetProperty("RequestPath").GetString());
    }

    [Fact]
    public void Write_ExceptionTextWithFeedPath_IsRedacted()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var feedPath = $"/api/public/feeds/meta/{rawToken}";
        var exception = new InvalidOperationException($"Feed failed for {feedPath}");

        var formatter = CreateFormatter();
        var entry = new LogEntry<string>(
            LogLevel.Error,
            "Test.ErrorCategory",
            new EventId(1),
            "state",
            exception,
            (s, ex) => "Feed request failed");

        var output = Render(formatter, entry, scopeProvider: null);

        Assert.DoesNotContain(rawToken, output);
        using var document = JsonDocument.Parse(output);
        Assert.Contains("/api/public/feeds/meta/<REDACTED>", document.RootElement.GetProperty("Exception").GetString());
    }

    [Fact]
    public void RedactEmbedded_MidStringAndMultipleOccurrences_AreRedacted()
    {
        const string Token = "AbCdEf0123_-GhIjKl";
        const string Prefix = "/api/public/feeds/meta/";

        var single = $"RequestPath:{Prefix}{Token} RequestId:0H1";
        Assert.Equal($"RequestPath:{Prefix}<REDACTED> RequestId:0H1", PublicFeedPathRedactor.RedactEmbedded(single));

        var multi = $"first {Prefix}{Token} second {Prefix}{Token}";
        Assert.Equal($"first {Prefix}<REDACTED> second {Prefix}<REDACTED>", PublicFeedPathRedactor.RedactEmbedded(multi));

        var caseVariant = $"see {Prefix.ToUpperInvariant()}{Token}";
        Assert.Equal($"see {Prefix}<REDACTED>", PublicFeedPathRedactor.RedactEmbedded(caseVariant));

        var exact = PublicFeedPathRedactor.RedactEmbedded($"{Prefix}{Token}");
        Assert.Equal($"{Prefix}<REDACTED>", exact);

        Assert.Equal("no feed here", PublicFeedPathRedactor.RedactEmbedded("no feed here"));
        Assert.Equal(string.Empty, PublicFeedPathRedactor.RedactEmbedded(null));
    }

    [Fact]
    public void Write_TikTokFeedPathInMessageStateAndScopes_RedactsTokenEverywhere()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var feedPath = $"/api/public/feeds/tiktok/{rawToken}";

        var state = new List<KeyValuePair<string, object?>>
        {
            new("RequestPath", feedPath),
            new("StatusCode", 200),
            new("ElapsedMs", 12.5)
        };

        var scopeProvider = new LoggerExternalScopeProvider();
        using var hostScope = scopeProvider.Push(new Dictionary<string, object?>
        {
            ["RequestPath"] = feedPath,
            ["RequestId"] = "0HMFAKE0000000002"
        });
        using var stringScope = scopeProvider.Push($"GET {feedPath}");

        var formatter = CreateFormatter();
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "Test.TikTokFeedCategory",
            new EventId(3, "Handled"),
            state,
            null,
            (s, _) => $"Handled request to {feedPath}");

        var output = Render(formatter, entry, scopeProvider);
        Assert.DoesNotContain(rawToken, output);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(3, root.GetProperty("EventId").GetInt32());
        Assert.Equal("Information", root.GetProperty("LogLevel").GetString());
        Assert.Equal("Test.TikTokFeedCategory", root.GetProperty("Category").GetString());
        Assert.Equal("Handled request to /api/public/feeds/tiktok/<REDACTED>", root.GetProperty("Message").GetString());
        Assert.Equal("/api/public/feeds/tiktok/<REDACTED>", root.GetProperty("State").GetProperty("RequestPath").GetString());
        Assert.Equal(200, root.GetProperty("State").GetProperty("StatusCode").GetInt32());
        Assert.Equal(12.5, root.GetProperty("State").GetProperty("ElapsedMs").GetDouble());

        var scopes = root.GetProperty("Scopes");
        Assert.Equal(2, scopes.GetArrayLength());

        var hostScopeElement = scopes[0];
        Assert.Equal("/api/public/feeds/tiktok/<REDACTED>", hostScopeElement.GetProperty("RequestPath").GetString());
        Assert.Equal("0HMFAKE0000000002", hostScopeElement.GetProperty("RequestId").GetString());
        Assert.DoesNotContain(rawToken, hostScopeElement.GetProperty("Message").GetString());

        Assert.Equal("GET /api/public/feeds/tiktok/<REDACTED>", scopes[1].GetString());
    }

    [Fact]
    public void Write_TikTokFeedExceptionPath_IsRedacted()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var feedPath = $"/api/public/feeds/tiktok/{rawToken}";
        var exception = new InvalidOperationException($"Feed failed for {feedPath}");

        var formatter = CreateFormatter();
        var entry = new LogEntry<string>(
            LogLevel.Error,
            "Test.TikTokErrorCategory",
            new EventId(1),
            "state",
            exception,
            (s, ex) => "Feed request failed");

        var output = Render(formatter, entry, scopeProvider: null);

        Assert.DoesNotContain(rawToken, output);
        using var document = JsonDocument.Parse(output);
        Assert.Contains("/api/public/feeds/tiktok/<REDACTED>", document.RootElement.GetProperty("Exception").GetString());
    }

    [Fact]
    public void RedactEmbedded_TikTokPath_RedactsTokenInRenderedText()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var text = $"RequestPath:/api/public/feeds/tiktok/{rawToken} RequestId:0H1";

        var redacted = PublicFeedPathRedactor.RedactEmbedded(text);

        Assert.DoesNotContain(rawToken, redacted);
        Assert.Contains("/api/public/feeds/tiktok/<REDACTED>", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_SnapchatFeedPathInMessageStateAndScopes_RedactsTokenEverywhere()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var feedPath = $"/api/public/feeds/snapchat/{rawToken}";

        var state = new List<KeyValuePair<string, object?>>
        {
            new("RequestPath", feedPath),
            new("StatusCode", 200),
            new("ElapsedMs", 12.5)
        };

        var scopeProvider = new LoggerExternalScopeProvider();
        using var hostScope = scopeProvider.Push(new Dictionary<string, object?>
        {
            ["RequestPath"] = feedPath,
            ["RequestId"] = "0HMFAKE000000003"
        });
        using var stringScope = scopeProvider.Push($"GET {feedPath}");

        var formatter = CreateFormatter();
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            "Test.SnapchatFeedCategory",
            new EventId(3, "Handled"),
            state,
            null,
            (s, _) => $"Handled request to {feedPath}");

        var output = Render(formatter, entry, scopeProvider);
        Assert.DoesNotContain(rawToken, output);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(3, root.GetProperty("EventId").GetInt32());
        Assert.Equal("Information", root.GetProperty("LogLevel").GetString());
        Assert.Equal("Test.SnapchatFeedCategory", root.GetProperty("Category").GetString());
        Assert.Equal("Handled request to /api/public/feeds/snapchat/<REDACTED>", root.GetProperty("Message").GetString());
        Assert.Equal("/api/public/feeds/snapchat/<REDACTED>", root.GetProperty("State").GetProperty("RequestPath").GetString());
        Assert.Equal(200, root.GetProperty("State").GetProperty("StatusCode").GetInt32());
        Assert.Equal(12.5, root.GetProperty("State").GetProperty("ElapsedMs").GetDouble());

        var scopes = root.GetProperty("Scopes");
        Assert.Equal(2, scopes.GetArrayLength());

        var hostScopeElement = scopes[0];
        Assert.Equal("/api/public/feeds/snapchat/<REDACTED>", hostScopeElement.GetProperty("RequestPath").GetString());
        Assert.Equal("0HMFAKE000000003", hostScopeElement.GetProperty("RequestId").GetString());
        Assert.DoesNotContain(rawToken, hostScopeElement.GetProperty("Message").GetString());

        Assert.Equal("GET /api/public/feeds/snapchat/<REDACTED>", scopes[1].GetString());
    }

    [Fact]
    public void Write_SnapchatFeedExceptionPath_IsRedacted()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var feedPath = $"/api/public/feeds/snapchat/{rawToken}";
        var exception = new InvalidOperationException($"Feed failed for {feedPath}");

        var formatter = CreateFormatter();
        var entry = new LogEntry<string>(
            LogLevel.Error,
            "Test.SnapchatErrorCategory",
            new EventId(1),
            "state",
            exception,
            (s, ex) => "Feed request failed");

        var output = Render(formatter, entry, scopeProvider: null);

        Assert.DoesNotContain(rawToken, output);
        using var document = JsonDocument.Parse(output);
        Assert.Contains("/api/public/feeds/snapchat/<REDACTED>", document.RootElement.GetProperty("Exception").GetString());
    }

    [Fact]
    public void Write_ExceptionWithUnsafeStackRenderer_DoesNotInvokeVirtualFormatting()
    {
        var exception = new UnsafeFormattingException("Original acquisition failure", new TimeoutException("Connection acquisition timed out"));
        var output = RenderException(exception);
        using var document = JsonDocument.Parse(output);
        var rendered = document.RootElement.GetProperty("Exception").GetString()!;
        Assert.Contains(nameof(UnsafeFormattingException), rendered);
        Assert.Contains("Original acquisition failure", rendered);
        Assert.Contains("Connection acquisition timed out", rendered);
        Assert.Equal(0, exception.FormattingCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_NestedDatabaseFailure_PreservesProviderCodeAndAllCauses_AndRedacts(bool aggregate)
    {
        var token = ProductFeedTokenService.GenerateRawToken();
        var database = new PostgresException($"Acquisition failed /api/public/feeds/meta/{token}", "ERROR", "ERROR", "40001");
        Exception exception = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.", database);
        if (aggregate) exception = new AggregateException("Background dependency failures", exception,
            new NpgsqlException("Failed to connect to isolated PostgreSQL", new SocketException((int)SocketError.ConnectionRefused)));
        var output = RenderException(exception);
        Assert.DoesNotContain(token, output);
        using var document = JsonDocument.Parse(output);
        var rendered = document.RootElement.GetProperty("Exception").GetString()!;
        Assert.Contains(nameof(InvalidOperationException), rendered);
        Assert.Contains(nameof(PostgresException), rendered);
        Assert.Contains("40001", rendered);
        Assert.Contains("Acquisition failed /api/public/feeds/meta/<REDACTED>", rendered);
        if (aggregate)
        {
            Assert.Contains(nameof(NpgsqlException), rendered);
            Assert.Contains(nameof(SocketException), rendered);
            Assert.Contains("ConnectionRefused", rendered);
        }
    }

    [Fact]
    public async Task Write_RealUnavailableIsolatedPostgresConnection_PreservesFailureAndStaysUsable()
    {
        // Bound an unused loopback port for the duration, rather than guessing a
        // merchant or disposable database endpoint. No PostgreSQL traffic can escape.
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        listener.Listen(1); // Accept TCP, but never answer a PostgreSQL handshake.
        var port = ((System.Net.IPEndPoint)listener.LocalEndPoint!).Port;
        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Database=shipping_logger_failure;Username=shipping_logger;Timeout=1;Pooling=false");
        var exception = await Assert.ThrowsAsync<NpgsqlException>(() => connection.OpenAsync());
        Assert.IsType<TimeoutException>(exception.InnerException);
        for (var i = 0; i < 20; i++)
        {
            using var document = JsonDocument.Parse(RenderException(exception));
            var rendered = document.RootElement.GetProperty("Exception").GetString()!;
            Assert.Contains(nameof(NpgsqlException), rendered);
            Assert.Contains(nameof(TimeoutException), rendered);
            Assert.Contains(exception.Message, rendered);
        }
        using var healthy = JsonDocument.Parse(Render(CreateFormatter(), new LogEntry<string>(LogLevel.Information,
            "AfterDatabaseFailure", new EventId(0), "still responsive", null, (state, _) => state), null));
        Assert.Equal("still responsive", healthy.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public void Write_ThrownAsyncGenericException_RetainsFrameLocationsWithoutSignatureFormatting()
    {
        Exception exception;
        try { ThrowGenericAsync<Dictionary<string, List<Guid>>>().GetAwaiter().GetResult(); throw new Exception("unreachable"); }
        catch (InvalidOperationException error) { exception = error; }
        using var document = JsonDocument.Parse(RenderException(exception));
        var rendered = document.RootElement.GetProperty("Exception").GetString()!;
        Assert.Contains("Original async failure", rendered);
        Assert.Contains("MoveNext", rendered);
        Assert.Contains("IL", rendered);
    }

    [Fact]
    public void Write_DeepInnerChain_RetainsDatabaseCauseWithoutRecursiveToString()
    {
        Exception exception = new PostgresException("original database cause", "ERROR", "ERROR", "53300");
        for (var i = 0; i < 64; i++) exception = new InvalidOperationException($"wrapper-{i}", exception);
        using var document = JsonDocument.Parse(RenderException(exception));
        var rendered = document.RootElement.GetProperty("Exception").GetString()!;
        Assert.Contains("wrapper-63", rendered);
        Assert.Contains("wrapper-0", rendered);
        Assert.Contains("original database cause", rendered);
        Assert.Contains("53300", rendered);
    }

    [Fact]
    public void Write_ExceptionInStructuredStateAndScope_UsesTheSameSafeRedactedFormatter()
    {
        var token = ProductFeedTokenService.GenerateRawToken();
        var exception = new UnsafeFormattingException($"failure /api/public/feeds/meta/{token}", new TimeoutException("timeout"));
        var state = new List<KeyValuePair<string, object?>> { new("Cause", exception) };
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(exception);
        var entry = new LogEntry<List<KeyValuePair<string, object?>>>(LogLevel.Error, "Database", new EventId(1), state,
            exception, (_, _) => "Database failure");
        var output = Render(CreateFormatter(), entry, scopes);
        Assert.DoesNotContain(token, output);
        using var document = JsonDocument.Parse(output);
        Assert.Contains("<REDACTED>", document.RootElement.GetProperty("State").GetProperty("Cause").GetString());
        Assert.Contains("timeout", document.RootElement.GetProperty("Scopes")[0].GetString());
        Assert.Equal(0, exception.FormattingCalls);
    }

    private static string RenderException(Exception exception) => Render(CreateFormatter(),
        new LogEntry<string>(LogLevel.Error, "Database", new EventId(1), "Database acquisition failed", exception, (state, _) => state), null);

    private static async Task ThrowGenericAsync<T>()
    {
        await Task.Yield();
        throw new InvalidOperationException("Original async failure");
    }

    private sealed class UnsafeFormattingException(string message, Exception inner) : Exception(message, inner)
    {
        public int FormattingCalls;
        public override string ToString() { FormattingCalls++; throw new InvalidOperationException("Unsafe exception ToString invoked"); }
        public override string StackTrace { get { FormattingCalls++; throw new InvalidOperationException("Unsafe exception StackTrace invoked"); } }
    }

    private static FeedTokenRedactingJsonConsoleFormatter CreateFormatter() =>
        new(new StaticOptionsMonitor<JsonConsoleFormatterOptions>(FormatterOptions));

    private static string Render<TState>(
        FeedTokenRedactingJsonConsoleFormatter formatter,
        in LogEntry<TState> entry,
        IExternalScopeProvider? scopeProvider)
    {
        using var writer = new StringWriter();
        formatter.Write(in entry, scopeProvider, writer);
        return writer.ToString();
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue) => CurrentValue = currentValue;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
