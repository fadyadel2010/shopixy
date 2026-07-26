using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_DependencyFailure_Returns503WithoutLeakingInternalDetails()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-dependency-1"
        };
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Response.Body = new MemoryStream();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-123")
        ], "test"));

        var middleware = new GlobalExceptionMiddleware(
            _ => throw new AmazonS3Exception("bucket-down")
            {
                StatusCode = HttpStatusCode.ServiceUnavailable
            },
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal("DEPENDENCY_UNAVAILABLE", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("A required service is temporarily unavailable. Please try again.", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("corr-dependency-1", document.RootElement.GetProperty("correlationId").GetString());
        Assert.False(document.RootElement.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("bucket-down", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_DbUpdateConcurrencyException_Returns409ConflictPayload()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-concurrency-1"
        };
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionMiddleware(
            _ => throw new DbUpdateConcurrencyException("simulated conflict"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal("CONFLICT", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("Resource was modified by another process", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("corr-concurrency-1", document.RootElement.GetProperty("correlationId").GetString());
        Assert.False(document.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task InvokeAsync_DomainGuardException_ReturnsStructuredConflictContract()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "corr-domain-1"
        };
        httpContext.Request.Path = "/api/orders/transition";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionMiddleware(
            _ => throw new DomainGuardException(
                "ORDER_INVALID_STATE",
                "Order transition is not allowed.",
                details: new Dictionary<string, object?>
                {
                    ["currentState"] = "completed",
                    ["operation"] = "capture_payment"
                }),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(httpContext);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal("ORDER_INVALID_STATE", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("Order transition is not allowed.", document.RootElement.GetProperty("message").GetString());
        Assert.Equal("corr-domain-1", document.RootElement.GetProperty("correlationId").GetString());
        Assert.False(document.RootElement.TryGetProperty("traceId", out _));

        var details = document.RootElement.GetProperty("details");
        Assert.Equal("completed", details.GetProperty("currentState").GetString());
        Assert.Equal("capture_payment", details.GetProperty("operation").GetString());
    }
}
