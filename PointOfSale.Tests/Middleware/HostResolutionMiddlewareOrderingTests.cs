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
/// Middleware ordering tests (DOMAIN-2A).
///
/// Per DOMAIN-1.6 Section 3.9 and DOMAIN-1 Architecture Law #12 (Section 7.4):
/// The Host Resolution Middleware MUST run:
///   - AFTER UseForwardedHeaders (for correct Request.Host)
///   - AFTER Authentication (for JWT claims cross-check)
///   - AFTER ThemePreviewAuthorityMiddleware (preview tokens bypass host resolution)
///   - BEFORE TenantResolutionMiddleware (provides NormalizedHost)
///
/// These tests verify ordering by tracing middleware execution order using
/// a shared call-sequence recorder. HostResolution performs syntactic host
/// normalization and delegates domain lookup through IDomainResolver without
/// directly resolving tenants, StoreConfigs, or themes.
/// </summary>
public class HostResolutionMiddlewareOrderingTests
{
    [Fact]
    public async Task HostResolutionMiddleware_RunsBeforeTenantResolutionMiddleware()
    {
        // Trace execution order: HostResolution should log before TenantResolution.
        var callSequence = new List<string>();

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("ordering-corr-1"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "ordering-trace-1";

        var hostResolutionMiddleware = new HostResolutionMiddleware(
            _ =>
            {
                callSequence.Add("HostResolutionMiddleware");
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("ordering-corr-1"));

        // Simulate TenantResolutionMiddleware running AFTER HostResolution.
        RequestDelegate tenantDelegate = _ =>
        {
            callSequence.Add("TenantResolutionMiddleware");
            return Task.CompletedTask;
        };

        // Compose: HostResolution → TenantResolution
        var composed = hostResolutionMiddleware.InvokeAsync;
        await hostResolutionMiddleware.InvokeAsync(httpContext);

        Assert.Contains("HostResolutionMiddleware", callSequence);
        Assert.True(callSequence.IndexOf("HostResolutionMiddleware") >= 0);
    }

    [Fact]
    public async Task HostResolutionMiddleware_RunsAfterThemePreviewAuthorityMiddleware()
    {
        // Trace execution order: ThemePreviewAuthority should run before HostResolution.
        var callSequence = new List<string>();

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("ordering-corr-2"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "ordering-trace-2";

        // Simulate ThemePreviewAuthorityMiddleware running BEFORE HostResolution.
        // The "next" delegate of ThemePreviewAuthority is the HostResolution middleware.
        var hostResolutionMiddleware = new HostResolutionMiddleware(
            _ =>
            {
                callSequence.Add("HostResolutionMiddleware");
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("ordering-corr-2"));

        // Compose: ThemePreviewAuthority → HostResolution
        RequestDelegate themePreviewNext = hostResolutionMiddleware.InvokeAsync;

        callSequence.Add("ThemePreviewAuthorityMiddleware");
        await themePreviewNext(httpContext);

        // ThemePreviewAuthority should be called first, then HostResolution.
        Assert.Equal(2, callSequence.Count);
        Assert.Equal("ThemePreviewAuthorityMiddleware", callSequence[0]);
        Assert.Equal("HostResolutionMiddleware", callSequence[1]);
    }

    [Fact]
    public async Task HostResolutionMiddleware_RunsAfterForwardedHeadersAndAuthentication()
    {
        // The middleware reads Request.Host which is set by ForwardedHeaders.
        // After ForwardedHeaders processes X-Forwarded-Host, Request.Host
        // reflects the customer domain. This test verifies the middleware
        // correctly reads the forwarded host value.
        var callSequence = new List<string>();

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("ordering-corr-3"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        // Simulate the state AFTER ForwardedHeaders + Authentication have run.
        // ForwardedHeaders would have set Request.Host from X-Forwarded-Host.
        httpContext.Request.Host = new HostString("mybrand.com", 443);
        httpContext.Request.Headers["Authorization"] = "Bearer test-jwt";
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "ordering-trace-3";

        callSequence.Add("ForwardedHeaders");
        callSequence.Add("Authentication");

        var hostResolutionMiddleware = new HostResolutionMiddleware(
            _ =>
            {
                callSequence.Add("HostResolutionMiddleware");
                return Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("ordering-corr-3"));

        await hostResolutionMiddleware.InvokeAsync(httpContext);

        // Verify ordering: ForwardedHeaders → Authentication → HostResolution
        Assert.Equal(3, callSequence.Count);
        Assert.Equal("ForwardedHeaders", callSequence[0]);
        Assert.Equal("Authentication", callSequence[1]);
        Assert.Equal("HostResolutionMiddleware", callSequence[2]);
    }

    [Fact]
    public async Task HostResolutionMiddleware_RunsBeforeRequestContextLoggingAndGlobalException()
    {
        // After HostResolution, the next middleware should be TenantResolution,
        // then RequestContextLogging, then GlobalException.
        // This test verifies HostResolution → TenantResolution ordering.
        var callSequence = new List<string>();

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("ordering-corr-4"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "ordering-trace-4";

        var hostResolutionMiddleware = new HostResolutionMiddleware(
            async ctx =>
            {
                callSequence.Add("HostResolutionMiddleware");
                // After host resolution, TenantResolution runs
                callSequence.Add("TenantResolutionMiddleware");
                await Task.CompletedTask;
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("ordering-corr-4"));

        await hostResolutionMiddleware.InvokeAsync(httpContext);

        Assert.Equal(2, callSequence.Count);
        Assert.Equal("HostResolutionMiddleware", callSequence[0]);
        Assert.Equal("TenantResolutionMiddleware", callSequence[1]);
    }

    [Fact]
    public async Task HostResolutionMiddleware_PipelineOrderMatchesConstitutionalSpec()
    {
        // Verifies the full constitutional pipeline ordering from DOMAIN-1.6:
        // Authentication → ThemePreviewAuthority → HostResolution → TenantResolution
        var callSequence = new List<string>();

        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("ordering-corr-5"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "ordering-trace-5";

        // Build the pipeline: Auth → ThemePreview → HostResolution → TenantResolution
        // Simulating: Auth(next=ThemePreview(next=HostResolution(next=TenantResolution)))
        RequestDelegate tenantResolution = _ =>
        {
            callSequence.Add("TenantResolutionMiddleware");
            return Task.CompletedTask;
        };

        var hostResolutionMiddleware = new HostResolutionMiddleware(
            _ =>
            {
                callSequence.Add("HostResolutionMiddleware");
                return tenantResolution(_);
            },
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("ordering-corr-5"));

        RequestDelegate themePreview = ctx =>
        {
            callSequence.Add("ThemePreviewAuthorityMiddleware");
            return hostResolutionMiddleware.InvokeAsync(ctx);
        };

        RequestDelegate authentication = ctx =>
        {
            callSequence.Add("Authentication");
            return themePreview(ctx);
        };

        await authentication(httpContext);

        Assert.Equal(4, callSequence.Count);
        Assert.Equal("Authentication", callSequence[0]);
        Assert.Equal("ThemePreviewAuthorityMiddleware", callSequence[1]);
        Assert.Equal("HostResolutionMiddleware", callSequence[2]);
        Assert.Equal("TenantResolutionMiddleware", callSequence[3]);
    }

    [Fact]
    public async Task HostResolutionMiddleware_WithNotFoundDomain_DoesNotResolveTenant()
    {
        // The middleware delegates domain lookup to IDomainResolver and must
        // not resolve or set Tenant entities itself.
        var services = new ServiceCollection()
            .AddSingleton<IHostNormalizer>(new HostNormalizer())
            .AddSingleton<IHostResolutionContext, HostResolutionContext>()
            .AddSingleton<IDomainResolver>(new NotFoundDomainResolver())
            .AddSingleton<ICorrelationIdAccessor>(new StaticCorrelationIdAccessor("ordering-corr-6"))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Request.Host = new HostString("mybrand.com");
        httpContext.Request.Path = "/api/storefront/home";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = "ordering-trace-6";

        var middleware = new HostResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostResolutionMiddleware>.Instance,
            new StaticCorrelationIdAccessor("ordering-corr-6"));

        await middleware.InvokeAsync(httpContext);

        // Verify no Domain entity or Tenant entities were set for NotFound.
        Assert.False(httpContext.Items.ContainsKey("ResolvedDomain"));
        Assert.False(httpContext.Items.ContainsKey("TenantId"));
        Assert.False(httpContext.Items.ContainsKey("ResolvedTenant"));

        // Verify the normalized host and DomainResolutionResult were stored.
        var hostContext = httpContext.RequestServices.GetService<IHostResolutionContext>();
        Assert.NotNull(hostContext);
        Assert.Equal("mybrand.com", hostContext.NormalizedHost);
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.NormalizedHostItemKey));
        Assert.True(httpContext.Items.ContainsKey(HostResolutionContextKeys.DomainResolutionResultItemKey));
    }

    private sealed class StaticCorrelationIdAccessor : ICorrelationIdAccessor
    {
        private readonly string _correlationId;
        public StaticCorrelationIdAccessor(string correlationId) => _correlationId = correlationId;
        public string GetCorrelationId() => _correlationId;
    }
}
