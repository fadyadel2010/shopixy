using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Interfaces;
using Xunit;

namespace PointOfSale.Tests.Middleware;

/// <summary>
/// Unit tests for Forwarded Host handling in HostResolutionMiddleware (DOMAIN-2A).
///
/// Per DOMAIN-1.6 Section 3.1 and DOMAIN-1 Architecture Law #11:
/// The ForwardedHeaders middleware must include X-Forwarded-Host so that
/// Request.Host reflects the true customer domain behind a reverse proxy.
/// This test suite verifies the middleware correctly reads and normalizes
/// the forwarded host when X-Forwarded-Host is present.
///
/// These tests do NOT use a real ForwardedHeadersMiddleware — they simulate
/// its effect by setting Request.Host directly (as ForwardedHeaders would
/// after processing X-Forwarded-Host). This isolates the HostResolutionMiddleware
/// behavior without requiring a full proxy simulation.
/// </summary>
public class HostResolutionForwardedHostTests
{
    [Fact]
    public async Task InvokeAsync_ForwardedHost_CorrectlyNormalizedAndStoredInContext()
    {
        // Simulates: reverse proxy forwards X-Forwarded-Host: MyBrand.com:443
        // After ForwardedHeaders middleware processes it, Request.Host = "MyBrand.com:443"
        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var correlationAccessor = new StaticCorrelationIdAccessor("fwd-correlation-1");
        var domainResolver = new NotFoundDomainResolver();

        string? nextCalledHost = null;
        var httpContext = CreateHttpContext(
            host: "WWW.MyBrand.com:443",
            forwardedHostHeader: "WWW.MyBrand.com:443");

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(domainResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();
        httpContext.RequestServices = services;

        var middleware = new HostResolutionMiddleware(
            HttpContext =>
            {
                nextCalledHost = HttpContext.RequestServices
                    .GetService<IHostResolutionContext>()
                    ?.NormalizedHost;
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // The forwarded host should have been normalized (lowercased, port stripped).
        Assert.Equal("www.mybrand.com", nextCalledHost);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_NoForwardedHost_UsesRequestHostDirectly()
    {
        // Simulates: no reverse proxy, Request.Host = "localhost:5174"
        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var correlationAccessor = new StaticCorrelationIdAccessor("fwd-correlation-2");
        var domainResolver = new NotFoundDomainResolver();

        var httpContext = CreateHttpContext(
            host: "localhost:5174",
            forwardedHostHeader: null);

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(domainResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();
        httpContext.RequestServices = services;

        string? nextCalledHost = null;
        var middleware = new HostResolutionMiddleware(
            HttpContext =>
            {
                nextCalledHost = HttpContext.RequestServices
                    .GetService<IHostResolutionContext>()
                    ?.NormalizedHost;
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // Should normalize "localhost:5174" → "localhost"
        Assert.Equal("localhost", nextCalledHost);
    }

    [Fact]
    public async Task InvokeAsync_ForwardedHostWithMixedCase_NormalizedToExpected()
    {
        // Simulates: X-Forwarded-Host: MyStore.Shopixy.com
        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var correlationAccessor = new StaticCorrelationIdAccessor("fwd-correlation-3");
        var domainResolver = new NotFoundDomainResolver();

        var httpContext = CreateHttpContext(
            host: "MyStore.Shopixy.com",
            forwardedHostHeader: "MyStore.Shopixy.com");

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(domainResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();
        httpContext.RequestServices = services;

        string? nextCalledHost = null;
        var middleware = new HostResolutionMiddleware(
            HttpContext =>
            {
                nextCalledHost = HttpContext.RequestServices
                    .GetService<IHostResolutionContext>()
                    ?.NormalizedHost;
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal("mystore.shopixy.com", nextCalledHost);
    }

    [Fact]
    public async Task InvokeAsync_ExcludedPath_SkipsHostResolution()
    {
        // Excluded paths (health, test-auth, etc.) should bypass host resolution.
        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var correlationAccessor = new StaticCorrelationIdAccessor("fwd-correlation-4");
        var domainResolver = new NotFoundDomainResolver();

        var httpContext = CreateHttpContext(
            host: "mybrand.com",
            forwardedHostHeader: "mybrand.com",
            path: "/health");

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(domainResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();
        httpContext.RequestServices = services;

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // Context should NOT be populated for excluded paths.
        Assert.Null(context.NormalizedHost);
    }

    [Fact]
    public async Task InvokeAsync_NullHost_DoesNotCrashAndContinuesPipeline()
    {
        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var correlationAccessor = new StaticCorrelationIdAccessor("fwd-correlation-5");
        var domainResolver = new NotFoundDomainResolver();

        var httpContext = CreateHttpContext(
            host: "",
            forwardedHostHeader: null);

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(domainResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();
        httpContext.RequestServices = services;

        var nextCalled = false;
        var middleware = new HostResolutionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
        Assert.Null(context.NormalizedHost);
    }

    [Fact]
    public async Task InvokeAsync_ForwardedHostFlag_CorrectlyDetected()
    {
        // The middleware should log ForwardedHostUsed=true when X-Forwarded-Host
        // header is present on the inbound request (before ForwardedHeaders
        // middleware strips it). This test verifies the detection logic.
        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var logEntries = new List<(string message, object?[] args)>();
        var logger = new CaptureLogger<HostResolutionMiddleware>(logEntries);
        var correlationAccessor = new StaticCorrelationIdAccessor("fwd-correlation-6");
        var domainResolver = new NotFoundDomainResolver();

        var httpContext = CreateHttpContext(
            host: "mybrand.com",
            forwardedHostHeader: "mybrand.com");

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(domainResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();
        httpContext.RequestServices = services;

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            logger,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // The log entry should contain ForwardedHostUsed=true
        var debugEntry = logEntries.FirstOrDefault(e => e.message.Contains("ForwardedHostUsed"));
        Assert.NotNull(debugEntry.message);
        // The structured state includes the forwarded host flag
        Assert.Contains("ForwardedHostUsed", debugEntry.message);
        Assert.Contains("True", debugEntry.message);
    }

    private static DefaultHttpContext CreateHttpContext(
        string host,
        string? forwardedHostHeader,
        string path = "/api/storefront/home")
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "test-trace"
        };
        httpContext.Request.Host = new HostString(host.Split(":")[0], host.Contains(":") ? int.Parse(host.Split(":")[1]) : 80);
        httpContext.Request.Path = path;
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();

        if (forwardedHostHeader != null)
        {
            httpContext.Request.Headers["X-Forwarded-Host"] = forwardedHostHeader;
        }

        return httpContext;
    }

    /// <summary>
    /// Simple correlation ID accessor for tests — returns a fixed value.
    /// </summary>
    private sealed class StaticCorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly string _correlationId;
        public StaticCorrelationIdAccessor(string correlationId) => _correlationId = correlationId;
        public string GetCorrelationId() => _correlationId;
    }

    /// <summary>
    /// Minimal logger that captures log entries for assertion.
    /// </summary>
    private sealed class CaptureLogger<T> : ILogger<T>, IDisposable
    {
        private readonly List<(string, object?[])> _entries;
        public CaptureLogger(List<(string, object?[])> entries) => _entries = entries;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => this;
        public void Dispose() { }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var args = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(p => p.Value).ToArray()
                : Array.Empty<object?>();
            _entries.Add((msg, args));
        }
    }
}
