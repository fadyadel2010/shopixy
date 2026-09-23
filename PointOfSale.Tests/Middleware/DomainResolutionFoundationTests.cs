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
/// Focused runtime tests for the DOMAIN-2C Host → Domain Resolution foundation.
///
/// Per the DOMAIN-2C mission, these tests verify that:
///   - The IDomainResolver receives the normalized host from the middleware.
///   - No database usage occurs (no DbContext, no EF queries).
///   - No tenant lookup occurs (no TenantId set in HttpContext.Items).
///   - No StoreConfig lookup occurs.
///   - No Theme lookup occurs.
///   - No CMS lookup occurs.
///   - The pipeline continues to the next middleware.
///   - Runtime contracts (DomainResolutionResult, IDomainResolver) remain immutable.
///   - The cache abstraction (IDomainResolutionCache) is a no-op in DOMAIN-2C.
/// </summary>
public class DomainResolutionFoundationTests
{
    /// <summary>
    /// Verifies that the IDomainResolver receives the normalized host
    /// passed to it by the HostResolutionMiddleware. This is the core
    /// HOST → DOMAIN contract: the resolver accepts a NormalizedHost.
    /// </summary>
    [Fact]
    public async Task DomainResolver_ReceivesNormalizedHostFromMiddleware()
    {
        string? capturedHost = null;

        var normalizer = new HostNormalizer();
        var context = new HostResolutionContext();
        var correlationAccessor = new StaticCorrelationIdAccessor("domain-test-corr-1");
        var capturingResolver = new CapturingDomainResolver((host) => capturedHost = host);
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(normalizer)
            .AddSingleton<IHostResolutionContext>(context)
            .AddSingleton<IDomainResolver>(capturingResolver)
            .AddSingleton<ICorrelationIdAccessor>(correlationAccessor)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("WWW.MyBrand.COM:443");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-1";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            correlationAccessor);

        await middleware.InvokeAsync(httpContext);

        // The resolver should have received the NORMALIZED host.
        Assert.Equal("www.mybrand.com", capturedHost);
    }

    /// <summary>
    /// Verifies that the no-op test resolver always returns NotFound status.
    /// No domain is found because no database access is performed.
    /// </summary>
    [Fact]
    public async Task NotFoundDomainResolver_AlwaysReturnsNotFound()
    {
        var resolver = new NotFoundDomainResolver();

        var result = await resolver.ResolveAsync("mybrand.com");

        Assert.Equal(DomainResolutionStatus.NotFound, result.Status);
        Assert.False(result.CacheAttempted);
        Assert.Equal("mybrand.com", result.NormalizedHost);
    }

    /// <summary>
    /// Verifies that the no-op test resolver returns NotFound even for a
    /// well-known host — it does not query any domain table.
    /// </summary>
    [Theory]
    [InlineData("shopixy.com")]
    [InlineData("www.shopixy.com")]
    [InlineData("mybrand.com")]
    [InlineData("localhost")]
    public async Task NotFoundDomainResolver_NoDatabaseAccess_ReturnsNotFound(string host)
    {
        var resolver = new NotFoundDomainResolver();

        var result = await resolver.ResolveAsync(host);

        Assert.Equal(DomainResolutionStatus.NotFound, result.Status);
        Assert.False(result.CacheAttempted);
        Assert.Equal(host, result.NormalizedHost);
    }

    /// <summary>
    /// Verifies that the middleware stores the DomainResolutionResult in
    /// HttpContext.Items, making it available to downstream middleware
    /// (e.g., future TenantResolutionMiddleware that consults ResolvedDomain).
    /// </summary>
    [Fact]
    public async Task Middleware_StoresDomainResolutionResultInHttpContextItems()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-2"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-2";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-2"));

        await middleware.InvokeAsync(httpContext);

        // The DomainResolutionResult should be stored in HttpContext.Items.
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.DomainResolutionResultItemKey));
        var result = httpContext.Items[HostResolutionContextKeys.DomainResolutionResultItemKey] as DomainResolutionResult;
        Assert.NotNull(result);
        Assert.Equal(DomainResolutionStatus.NotFound, result!.Status);
        Assert.Equal("mybrand.com", result.NormalizedHost);
    }

    /// <summary>
    /// Verifies that the middleware does NOT perform any tenant resolution.
    /// No TenantId should appear in HttpContext.Items after middleware runs.
    /// </summary>
    [Fact]
    public async Task Middleware_DoesNotResolveTenant()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-3"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-3";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-3"));

        await middleware.InvokeAsync(httpContext);

        // No tenant resolution performed.
        Assert.False(httpContext.Items.ContainsKey("TenantId"));
        Assert.False(httpContext.Items.ContainsKey("ResolvedTenant"));
        // No Domain entity set; only the DomainResolutionResult abstraction is stored.
        Assert.False(httpContext.Items.ContainsKey("ResolvedDomain"));
    }

    /// <summary>
    /// Verifies that the middleware does NOT perform any StoreConfig lookup.
    /// Domain resolution stops at the domain result and does not query for StoreConfig.
    /// </summary>
    [Fact]
    public async Task Middleware_DoesNotLookupStoreConfig()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-4"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-4";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-4"));

        await middleware.InvokeAsync(httpContext);

        // No StoreConfig lookup performed.
        Assert.False(httpContext.Items.ContainsKey("StoreConfig"));
        Assert.False(httpContext.Items.ContainsKey("ResolvedStoreConfig"));
    }

    /// <summary>
    /// Verifies that the middleware does NOT perform any Theme lookup.
    /// Domain resolution stops at the domain result and does not query for themes.
    /// </summary>
    [Fact]
    public async Task Middleware_DoesNotLookupTheme()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-5"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-5";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-5"));

        await middleware.InvokeAsync(httpContext);

        // No theme lookup performed.
        Assert.False(httpContext.Items.ContainsKey("ThemeId"));
        Assert.False(httpContext.Items.ContainsKey("ResolvedTheme"));
    }

    /// <summary>
    /// Verifies that the middleware does NOT perform any CMS lookup.
    /// Domain resolution stops at the domain result and does not query for CMS pages.
    /// </summary>
    [Fact]
    public async Task Middleware_DoesNotLookupCms()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-6"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-6";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-6"));

        await middleware.InvokeAsync(httpContext);

        // No CMS lookup performed.
        Assert.False(httpContext.Items.ContainsKey("CmsPage"));
        Assert.False(httpContext.Items.ContainsKey("ResolvedCmsPage"));
    }

    /// <summary>
    /// Verifies that the middleware continues the pipeline after resolution.
    /// The next middleware delegate must be invoked.
    /// </summary>
    [Fact]
    public async Task Middleware_PipelineContinuesAfterResolution()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-7"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-7";

        var nextCalled = false;
        var middleware = new HostResolutionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-7"));

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
    }

    /// <summary>
    /// Verifies that the DomainResolutionResult abstraction is immutable
    /// in its shape — it exposes only the constitutional minimum fields required
    /// for downstream pipeline stages. Per DOMAIN-1.6, this includes:
    /// NormalizedHost, Status, CacheAttempted (DOMAIN-2C foundation) plus
    /// DomainId, TenantId, RedirectType, RedirectTargetNormalizedHost (DOMAIN-2C
    /// persistence activation). No EF entities, no database models.
    /// </summary>
    [Fact]
    public void DomainResolutionResult_RemainsImmutableContract()
    {
        var result = DomainResolutionResult.Found(
            "mybrand.com", Guid.NewGuid(), Guid.NewGuid());

        // The result must contain only the constitutional fields.
        Assert.Equal("mybrand.com", result.NormalizedHost);
        Assert.Equal(DomainResolutionStatus.Found, result.Status);
        Assert.False(result.CacheAttempted);
        // DOMAIN-2C: persistence carries DomainId + TenantId (pass-through, not resolved).
        Assert.True(result.DomainId.HasValue);
        Assert.True(result.TenantId.HasValue);
        // No additional public properties beyond the seven above.
        var props = typeof(DomainResolutionResult).GetProperties();
        Assert.Equal(7, props.Length);
    }

    /// <summary>
    /// Verifies that DomainResolutionStatus enum has only the expected
    /// constitutional values.
    /// </summary>
    [Fact]
    public void DomainResolutionStatus_OnlyConstitutionalValues()
    {
        var values = Enum.GetValues<DomainResolutionStatus>();
        Assert.Contains(DomainResolutionStatus.NotFound, values);
        Assert.Contains(DomainResolutionStatus.Found, values);
        Assert.Contains(DomainResolutionStatus.Error, values);
        // No additional values beyond the three constitutional ones.
        Assert.Equal(3, values.Length);
    }

    /// <summary>
    /// Verifies the compatibility no-op executes the authoritative factory.
    /// </summary>
    [Fact]
    public async Task NoOpDomainResolutionCache_NoOpBehavior()
    {
        var cache = new NoOpDomainResolutionCache();
        var result = DomainResolutionResult.NotFound("mybrand.com");

        // GetAsync always returns null (cache miss).
        var cached = await cache.GetAsync("mybrand.com");
        Assert.Null(cached);

        // SetAsync is a no-op (no storage).
        await cache.SetAsync("mybrand.com", result);

        // RemoveAsync is a no-op.
        await cache.RemoveAsync("mybrand.com");

        // GetAsync still returns null after SetAsync.
        cached = await cache.GetAsync("mybrand.com");
        Assert.Null(cached);

        var populated = await cache.GetOrCreateAsync(
            "mybrand.com",
            _ => ValueTask.FromResult(result));
        Assert.Same(result, populated);

        await cache.InvalidateAllAsync();
    }

    /// <summary>
    /// Verifies that the middleware correctly stores the normalized host
    /// in HttpContext.Items under the expected key, and that downstream
    /// consumers can read it without EF dependencies.
    /// </summary>
    [Fact]
    public async Task Middleware_NormalizedHostAvailableInHttpContextItems()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-8"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("WWW.MyBrand.COM:8080");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-8";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-8"));

        await middleware.InvokeAsync(httpContext);

        // The normalized host should be available in HttpContext.Items.
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.NormalizedHostItemKey));
        Assert.Equal("www.mybrand.com", httpContext.Items[HostResolutionContextKeys.NormalizedHostItemKey]);

        // The DomainResolutionResult should also be available.
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.DomainResolutionResultItemKey));
    }

    /// <summary>
    /// Verifies that the resolver receives the normalized host (not the raw
    /// host with port, mixed case, or trailing dot). This tests the HOST →
    /// NORMALIZE → IDomainResolver pipeline integration.
    /// </summary>
    [Theory]
    [InlineData("WWW.MyBrand.COM:443", "www.mybrand.com")]
    [InlineData("mybrand.com.", "mybrand.com")]
    [InlineData("  MYBRAND.COM:80  ", "mybrand.com")]
    public async Task Resolver_ReceivesNormalizedHost_NotRawHost(string rawHost, string expectedNormalized)
    {
        string? capturedHost = null;
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new CapturingDomainResolver((host) => capturedHost = host))
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("domain-test-corr-9"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        // Simulate that ForwardedHeaders already set Request.Host.
        if (rawHost.Contains(":"))
        {
            var parts = rawHost.Trim().Split(":");
            httpContext.Request.Host = new HostString(parts[0], int.Parse(parts[1]));
        }
        else
        {
            httpContext.Request.Host = new HostString(rawHost);
        }
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "domain-test-trace-9";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("domain-test-corr-9"));

        await middleware.InvokeAsync(httpContext);

        // The resolver should receive the NORMALIZED host, not the raw host.
        Assert.Equal(expectedNormalized, capturedHost);
    }

    private sealed class StaticCorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly string _correlationId;
        public StaticCorrelationIdAccessor(string correlationId) => _correlationId = correlationId;
        public string GetCorrelationId() => _correlationId;
    }

    /// <summary>
    /// Test double that captures the normalized host passed to ResolveAsync.
    /// </summary>
    private sealed class CapturingDomainResolver : IDomainResolver
    {
        private readonly Action<string?> _onResolve;

        public CapturingDomainResolver(Action<string?> onResolve)
        {
            _onResolve = onResolve;
        }

        public ValueTask<DomainResolutionResult> ResolveAsync(string? normalizedHost, CancellationToken cancellationToken = default)
        {
            _onResolve(normalizedHost);
            return new(DomainResolutionResult.NotFound(normalizedHost));
        }
    }
}
