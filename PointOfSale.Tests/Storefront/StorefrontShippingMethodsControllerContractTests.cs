using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontShippingMethodsControllerContractTests
{
    [Fact]
    public async Task PreviewShippingMethods_MissingCheckoutSessionId_ReturnsRequiredSessionContract()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(context => context.TenantId).Returns(tenantId);

        var authentication = new Mock<IAuthenticationService>();
        authentication
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());

        var services = new ServiceCollection()
            .AddSingleton(tenantContext.Object)
            .AddSingleton(authentication.Object)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "trace-step-2h"
        };
        httpContext.Request.Headers["X-Session-Id"] = "guest-session";

        var warehouseResolutionService = new Mock<IWarehouseResolutionService>();
        var controller = new StorefrontShippingMethodsController(
            Mock.Of<ICartService>(),
            Mock.Of<IShippingMethodService>(),
            warehouseResolutionService.Object,
            Mock.Of<ILogger<StorefrontShippingMethodsController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var result = await controller.PreviewShippingMethods(
            new ShippingMethodsPreviewRequestDto(),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("CHECKOUT_SESSION_REQUIRED", error.Code);
        Assert.Equal("Checkout session is required.", error.Message);

        warehouseResolutionService.Verify(service => service.LoadPreviewCartAsync(
            It.IsAny<Guid>(),
            It.IsAny<CartOwner>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
