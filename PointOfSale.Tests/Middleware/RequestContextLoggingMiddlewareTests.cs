using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Middleware;

public class RequestContextLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_EnrichesCurrentActivityWithCorrelationTenantAndUser()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-scope-1"
        };
        httpContext.Request.Path = "/api/orders";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-456")
        ], "test"));

        var middleware = new RequestContextLoggingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestContextLoggingMiddleware>.Instance);

        using var activity = new Activity("request-context-test");
        activity.Start();

        await middleware.InvokeAsync(httpContext);

        Assert.Equal("corr-scope-1", activity.GetTagItem("correlation.id")?.ToString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", activity.GetTagItem("tenant.id")?.ToString());
        Assert.Equal("user-456", activity.GetTagItem("user.id")?.ToString());
    }

    [Fact]
    public async Task InvokeAsync_LogsStructuredCompletionFields()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-log-1"
        };
        httpContext.Request.Path = "/api/orders/123";
        httpContext.Request.Method = HttpMethods.Put;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-789")
        ], "test"));

        var logger = new TestLogger<RequestContextLoggingMiddleware>();
        var middleware = new RequestContextLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(httpContext);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Contains("HTTP request completed", entry.Message, StringComparison.Ordinal);
        Assert.Equal("corr-log-1", entry.State["CorrelationId"]);
        Assert.Equal("44444444-4444-4444-4444-444444444444", entry.State["TenantId"]);
        Assert.Equal("user-789", entry.State["UserId"]);
        Assert.Equal("PUT /api/orders/123", entry.State["Operation"]);
        Assert.Equal("success", entry.State["Result"]);
        Assert.Equal(StatusCodes.Status202Accepted, Assert.IsType<int>(entry.State["StatusCode"]));
        Assert.True(Convert.ToDouble(entry.State["LatencyMs"]) >= 0d);
    }

    // =========================================================================
    // FEED-1B security regression: the public Meta feed URL embeds the raw
    // feed token as the final path segment. The token must never reach the
    // Operation field, the RequestPath logging scope, rendered messages, or
    // any structured property — while all other routes stay unchanged.
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_FeedRequest_RedactsTokenFromOperation()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var (logger, _) = await InvokeFeedRequest($"/api/public/feeds/meta/{rawToken}");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("GET /api/public/feeds/meta/<REDACTED>", entry.State["Operation"]);
        Assert.DoesNotContain(rawToken, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(rawToken, entry.State["Operation"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_FeedRequest_RedactsTokenFromRequestPathScope()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var (logger, _) = await InvokeFeedRequest($"/api/public/feeds/meta/{rawToken}");

        var scope = Assert.Single(logger.Scopes);
        Assert.Equal("/api/public/feeds/meta/<REDACTED>", scope["RequestPath"]);
        Assert.All(
            scope.Values,
            value => Assert.DoesNotContain(rawToken, value?.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvokeAsync_NonFeedRequest_KeepsExistingPathLogging()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-nonfeed-1"
        };
        httpContext.Request.Path = "/api/products";
        httpContext.Request.Method = HttpMethods.Get;

        var logger = new TestLogger<RequestContextLoggingMiddleware>();
        var middleware = new RequestContextLoggingMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(httpContext);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("GET /api/products", entry.State["Operation"]);

        var scope = Assert.Single(logger.Scopes);
        Assert.Equal("/api/products", scope["RequestPath"]);
    }

    [Fact]
    public async Task InvokeAsync_FeedRequest_RealisticTokenNeverAppearsInAnyLoggedValue()
    {
        // Realistic URL-safe base64 token straight from the production generator.
        var rawToken = ProductFeedTokenService.GenerateRawToken();
        var (logger, activity) = await InvokeFeedRequest($"/api/public/feeds/meta/{rawToken}");

        var entry = Assert.Single(logger.Entries);
        var loggedValues = entry.State.Values
            .Concat(Assert.Single(logger.Scopes).Values)
            .Select(value => value?.ToString() ?? string.Empty)
            .Append(entry.Message)
            .Append(activity.GetTagItem("operation")?.ToString() ?? string.Empty)
            .ToList();

        Assert.All(loggedValues, value => Assert.DoesNotContain(rawToken, value, StringComparison.Ordinal));
        Assert.Contains("/api/public/feeds/meta/<REDACTED>", entry.State["Operation"]?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_FeedRequest_PathVariantsStayRedacted()
    {
        var rawToken = ProductFeedTokenService.GenerateRawToken();

        // ASP.NET Core route matching is case-insensitive, so the redaction
        // must cover alternate casing of the feed route as well.
        var (upperLogger, _) = await InvokeFeedRequest($"/API/PUBLIC/FEEDS/META/{rawToken}");
        var upperEntry = Assert.Single(upperLogger.Entries);
        Assert.DoesNotContain(rawToken, upperEntry.State["Operation"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<REDACTED>", upperEntry.State["Operation"]?.ToString(), StringComparison.Ordinal);

        // Trailing-slash variant must not leak the token either.
        var (slashLogger, _) = await InvokeFeedRequest($"/api/public/feeds/meta/{rawToken}/");
        var slashEntry = Assert.Single(slashLogger.Entries);
        Assert.DoesNotContain(rawToken, slashEntry.State["Operation"]?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(rawToken, slashEntry.Message, StringComparison.Ordinal);
    }

    private static async Task<(TestLogger<RequestContextLoggingMiddleware> Logger, Activity Activity)> InvokeFeedRequest(string path)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("66666666-6666-6666-6666-666666666666"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-feed-1"
        };
        httpContext.Request.Path = path;
        httpContext.Request.Method = HttpMethods.Get;

        var logger = new TestLogger<RequestContextLoggingMiddleware>();
        var middleware = new RequestContextLoggingMiddleware(_ => Task.CompletedTask, logger);

        using var activity = new Activity("feed-redaction-test");
        activity.Start();

        await middleware.InvokeAsync(httpContext);

        return (logger, activity);
    }

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> State);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public List<IReadOnlyDictionary<string, object?>> Scopes { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                Scopes.Add(pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
            }

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var structuredState = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            Entries.Add(new LogEntry(logLevel, formatter(state, exception), structuredState));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
