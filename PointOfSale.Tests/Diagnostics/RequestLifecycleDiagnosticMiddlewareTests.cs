using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Middleware;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Diagnostics;

public sealed class RequestLifecycleDiagnosticMiddlewareTests
{
    [Fact]
    public async Task DiagnosticsDisabled_RequestContextAndMetricsRemainSafeWithoutDetailedLogging()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var logger = new CapturingLogger<RequestLifecycleDiagnosticMiddleware>();
        var contextWasAvailable = false;
        var middleware = new RequestLifecycleDiagnosticMiddleware(
            _ =>
            {
                contextWasAvailable = RequestPhaseDiagnosticsContext.Current is not null;
                RequestPhaseDiagnosticsContext.Current?.RecordNpgsqlCommand(4);
                return Task.CompletedTask;
            },
            logger,
            telemetry,
            new RequestPhaseDiagnosticRateLimiter(),
            new StaticOptionsMonitor<RequestPhaseDiagnosticsOptions>(new RequestPhaseDiagnosticsOptions
            {
                Enabled = false
            }));

        await middleware.Invoke(NewContext());

        Assert.True(contextWasAvailable);
        Assert.Empty(logger.Messages);
        Assert.Null(RequestPhaseDiagnosticsContext.Current);
    }

    [Fact]
    public async Task EnabledDetailedLogging_DoesNotEmitHeadersQueryOrTenantData()
    {
        using var telemetry = new NpgsqlDiagnosticsTelemetry();
        var logger = new CapturingLogger<RequestLifecycleDiagnosticMiddleware>();
        var middleware = new RequestLifecycleDiagnosticMiddleware(
            _ => Task.CompletedTask,
            logger,
            telemetry,
            new RequestPhaseDiagnosticRateLimiter(),
            new StaticOptionsMonitor<RequestPhaseDiagnosticsOptions>(new RequestPhaseDiagnosticsOptions
            {
                Enabled = true,
                SlowRequestThresholdMs = 0,
                MaxRecordsPerSecond = 10
            }));
        var context = NewContext();
        context.Request.Path = "/api/storefront/products/private-tenant-id";
        context.Request.QueryString = new QueryString("?token=secret-token");
        context.Request.Headers.Authorization = "Bearer secret-token";
        context.Request.Headers["X-Tenant-Id"] = "private-tenant-id";

        await middleware.Invoke(context);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("SLOW NPGSQL REQUEST", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-tenant-id", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RateLimiter_EnforcesTheConfiguredPerSecondBound()
    {
        var limiter = new RequestPhaseDiagnosticRateLimiter();

        Assert.True(limiter.TryAcquire(2));
        Assert.True(limiter.TryAcquire(2));
        Assert.False(limiter.TryAcquire(2));
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "request-1";
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/storefront/products";
        context.Response.StatusCode = StatusCodes.Status200OK;
        return context;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
