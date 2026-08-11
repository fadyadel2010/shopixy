using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using Xunit;

namespace PointOfSale.Tests.Middleware;

/// <summary>
/// Unit tests for HostResolutionMiddleware pipeline execution (DOMAIN-2A).
///
/// Per DOMAIN-1.6 Section 3.9: the middleware must read the normalized host,
/// store it in runtime context, log structured diagnostics, and continue
/// the pipeline. It must NOT perform any database access, tenant lookup,
/// StoreConfig lookup, caching, theme resolution, preview resolution, or
/// domain verification.
/// </summary>
public class HostResolutionPipelineTests
{
    [Fact]
    public async Task InvokeAsync_ContinuesToNextMiddleware()
    {
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("www.mybrand.com");
        httpContext.RequestServices = services;

        var nextCalled = false;
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            services,
            out var normalizer,
            out var correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_SetsNormalizedHostInRuntimeContext()
    {
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("WWW.MyBrand.COM");
        httpContext.RequestServices = services;

        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            services,
            out var normalizer,
            out var correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // The normalized host should be stored in the IHostResolutionContext.
        Assert.Equal("www.mybrand.com", hostContext.NormalizedHost);
    }

    [Fact]
    public async Task InvokeAsync_SetsNormalizedHostInHttpContextItems()
    {
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("www.MyBrand.com");
        httpContext.RequestServices = services;

        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            services,
            out var normalizer,
            out var correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // The normalized host should also be in HttpContext.Items for
        // middleware that doesn't use DI.
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.NormalizedHostItemKey));
        Assert.Equal("www.mybrand.com", httpContext.Items[HostResolutionContextKeys.NormalizedHostItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotPerformDatabaseAccess()
    {
        // The middleware must not accept any DbContext or repository dependency.
        // This test verifies by confirming the middleware constructs without
        // any database-related services and executes successfully.
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("mybrand.com");
        httpContext.RequestServices = services;

        var nextCalled = false;
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            services,
            out var _,
            out var _);

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
        Assert.Equal("mybrand.com", hostContext.NormalizedHost);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotResolveTenant()
    {
        // The middleware must NOT set any tenant context. This test verifies
        // that no tenant-related items are set in HttpContext.Items.
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("mybrand.com");
        httpContext.RequestServices = services;

        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            services,
            out var _,
            out var _);

        await middleware.InvokeAsync(httpContext);

        // No tenant-related items should be set.
        Assert.False(httpContext.Items.ContainsKey("TenantId"));
        Assert.False(httpContext.Items.ContainsKey("ResolvedTenant"));
        // No Domain entity should be set (test resolver returns NotFound).
        Assert.False(httpContext.Items.ContainsKey("ResolvedDomain"));

        // The test resolver should have stored a DomainResolutionResult
        // with NotFound status — no actual domain was resolved.
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.DomainResolutionResultItemKey));
        var resolutionResult = httpContext.Items[HostResolutionContextKeys.DomainResolutionResultItemKey] as DomainResolutionResult;
        Assert.NotNull(resolutionResult);
        Assert.Equal(DomainResolutionStatus.NotFound, resolutionResult!.Status);
        Assert.Equal("mybrand.com", resolutionResult.NormalizedHost);

        // Only host-resolution items should be set.
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.NormalizedHostItemKey));
    }

    [Fact]
    public async Task InvokeAsync_ExcludedPath_SkipsContextPopulation()
    {
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("mybrand.com", "/health");
        httpContext.RequestServices = services;

        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            services,
            out var _,
            out var _);

        await middleware.InvokeAsync(httpContext);

        // Excluded paths should NOT populate the host resolution context.
        Assert.Null(hostContext.NormalizedHost);
        Assert.False(httpContext.Items.ContainsKey(HostResolutionContextKeys.NormalizedHostItemKey));
    }

    [Fact]
    public async Task InvokeAsync_NullHost_DoesNotCrash()
    {
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("");
        httpContext.RequestServices = services;

        var nextCalled = false;
        var middleware = CreateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            services,
            out var _,
            out var _);

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
        Assert.Null(hostContext.NormalizedHost);
    }

    [Fact]
    public async Task InvokeAsync_LogsStructuredDiagnostics()
    {
        var services = CreateServiceProvider(out var hostContext);
        var httpContext = CreateHttpContext("MyBrand.com:443");
        httpContext.RequestServices = services;

        var logEntries = new List<string>();
        var logger = new SimpleCaptureLogger<HostResolutionMiddleware>(logEntries);

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            logger,
            new StaticCorrelationIdAccessor("pipeline-corr-1"));

        await middleware.InvokeAsync(httpContext);

        // Should have logged a Debug-level structured message with
        // Host, NormalizedHost, ResolutionStatus, CacheAttempted, ForwardedHostUsed, and CorrelationId.
        Assert.Contains(logEntries, e => e.Contains("Host resolved"));
        Assert.Contains(logEntries, e => e.Contains("MyBrand.com"));
        Assert.Contains(logEntries, e => e.Contains("mybrand.com"));
        Assert.Contains(logEntries, e => e.Contains("ResolutionStatus"));
        Assert.Contains(logEntries, e => e.Contains("NotFound"));
        Assert.Contains(logEntries, e => e.Contains("CacheAttempted"));
        Assert.Contains(logEntries, e => e.Contains("ForwardedHostUsed"));
        Assert.Contains(logEntries, e => e.Contains("pipeline-corr-1"));
    }

    private static IServiceProvider CreateServiceProvider(out HostResolutionContext hostContext)
    {
        hostContext = new HostResolutionContext();
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext>(hostContext)
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("pipeline-corr-default"))
            .BuildServiceProvider();
        return services;
    }

    private static HostResolutionMiddleware CreateMiddleware(
        RequestDelegate next,
        IServiceProvider services,
        out HostNormalizer normalizer,
        out StaticCorrelationIdAccessor correlationAccessor)
    {
        normalizer = new HostNormalizer();
        correlationAccessor = new StaticCorrelationIdAccessor("pipeline-corr");
        return new HostResolutionMiddleware(
            next,
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);
    }

    private static DefaultHttpContext CreateHttpContext(string host, string path = "/api/storefront/home")
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "test-pipeline-trace"
        };
        // Parse host:port format
        if (host.Contains(":"))
        {
            var parts = host.Split(":");
            httpContext.Request.Host = new HostString(parts[0], int.Parse(parts[1]));
        }
        else if (!string.IsNullOrEmpty(host))
        {
            httpContext.Request.Host = new HostString(host);
        }
        httpContext.Request.Path = path;
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private sealed class StaticCorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly string _correlationId;
        public StaticCorrelationIdAccessor(string correlationId) => _correlationId = correlationId;
        public string GetCorrelationId() => _correlationId;
    }

    /// <summary>
    /// Minimal logger that captures formatted log messages.
    /// </summary>
    private sealed class SimpleCaptureLogger<T> : ILogger<T>, IDisposable
    {
        private readonly List<string> _entries;
        public SimpleCaptureLogger(List<string> entries) => _entries = entries;
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
            _entries.Add(formatter(state, exception));
        }
    }
}
