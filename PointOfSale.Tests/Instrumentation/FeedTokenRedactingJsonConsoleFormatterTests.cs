using System.Text.Json;
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
