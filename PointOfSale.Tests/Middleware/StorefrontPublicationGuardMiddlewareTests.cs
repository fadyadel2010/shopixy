using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Middleware;

public sealed class StorefrontPublicationGuardMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UnpublishedAnonymousStorefrontRequest_Returns404()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var publicationStatusService = new Mock<IStorefrontPublicationStatusService>();
        publicationStatusService
            .Setup(service => service.IsPublishedAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var nextCalled = false;
        var middleware = new StorefrontPublicationGuardMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<StorefrontPublicationGuardMiddleware>.Instance);

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .AddSingleton(publicationStatusService.Object)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "storefront-guard-test",
            RequestServices = services
        };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/api/storefront/products";
        httpContext.Response.Body = new MemoryStream();
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            "public-storefront"));

        await middleware.InvokeAsync(httpContext);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal("STOREFRONT_NOT_PUBLISHED", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("Storefront not found.", document.RootElement.GetProperty("message").GetString());
    }
}
