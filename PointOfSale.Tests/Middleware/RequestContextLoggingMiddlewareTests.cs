using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Middleware;
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

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message,
        IReadOnlyDictionary<string, object?> State);

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

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
