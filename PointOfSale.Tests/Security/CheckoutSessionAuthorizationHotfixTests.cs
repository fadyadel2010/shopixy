using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Tests.Security;

public sealed class CheckoutSessionAuthorizationHotfixTests
{
    [Fact]
    public async Task CreateOrResume_WhenGuestCartOwnershipDoesNotMatch_ReturnsGenericNotFound_AndDoesNotIssueTokens()
    {
        var tenantId = Guid.NewGuid();
        var requestedCartId = Guid.NewGuid();
        var authoritativeCartId = Guid.NewGuid();

        var checkoutSessionService = new Mock<ICheckoutSessionService>(MockBehavior.Strict);
        var cartService = new Mock<ICartService>(MockBehavior.Strict);
        var tokenService = new Mock<ICheckoutSessionTokenService>(MockBehavior.Strict);

        cartService
            .Setup(service => service.GetCartAsync(
                tenantId,
                It.Is<CartOwner>(owner => owner.OwnerType == "session" && owner.OwnerId == "guest-session-123"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartDto
            {
                CartId = authoritativeCartId,
                TenantId = tenantId,
                OwnerType = "session",
                OwnerId = "guest-session-123",
                Currency = "EGP",
                GrandTotal = 125m
            });

        var controller = new CheckoutSessionController(
            checkoutSessionService.Object,
            tokenService.Object,
            cartService.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(AuthenticateResult.NoResult(), AuthenticateResult.NoResult())
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        controller.Request.Headers["X-Session-Id"] = "guest-session-123";

        var result = await controller.CreateOrResume(
            new CreateCheckoutSessionRequest
            {
                CartId = requestedCartId,
                Currency = "EGP",
                CartTotal = 125m
            },
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("CHECKOUT_CART_NOT_FOUND", payload.Code);
        Assert.Equal("Cart not found.", payload.Message);

        checkoutSessionService.Verify(
            service => service.CreateOrResumeSessionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<decimal?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        tokenService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateOrResume_WhenAuthenticatedCustomerRequestsAnotherCart_ReturnsGenericNotFound()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var requestedCartId = Guid.NewGuid();
        var authoritativeCartId = Guid.NewGuid();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("CustomerId", customerId.ToString("D")),
            new Claim("TenantId", tenantId.ToString("D"))
        ], "CustomerJwt"));

        var checkoutSessionService = new Mock<ICheckoutSessionService>(MockBehavior.Strict);
        var cartService = new Mock<ICartService>(MockBehavior.Strict);
        var tokenService = new Mock<ICheckoutSessionTokenService>(MockBehavior.Strict);

        cartService
            .Setup(service => service.GetCartAsync(
                tenantId,
                It.Is<CartOwner>(owner => owner.OwnerType == "customer" && owner.OwnerId == customerId.ToString("N")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CartDto
            {
                CartId = authoritativeCartId,
                TenantId = tenantId,
                OwnerType = "customer",
                OwnerId = customerId.ToString("N"),
                Currency = "EGP",
                GrandTotal = 200m
            });

        var controller = new CheckoutSessionController(
            checkoutSessionService.Object,
            tokenService.Object,
            cartService.Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<CheckoutSessionController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(
                    AuthenticateResult.Success(new AuthenticationTicket(principal, "CustomerJwt")),
                    AuthenticateResult.NoResult())
            }
        };

        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        var result = await controller.CreateOrResume(
            new CreateCheckoutSessionRequest
            {
                CartId = requestedCartId,
                Currency = "EGP",
                CartTotal = 200m
            },
            CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var payload = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("CHECKOUT_CART_NOT_FOUND", payload.Code);
        Assert.Equal("Cart not found.", payload.Message);

        checkoutSessionService.Verify(
            service => service.CreateOrResumeSessionAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<decimal?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        tokenService.VerifyNoOtherCalls();
    }

    private static DefaultHttpContext CreateHttpContext(
        AuthenticateResult customerResult,
        AuthenticateResult storefrontResult)
    {
        var authenticationService = new Mock<IAuthenticationService>(MockBehavior.Strict);
        authenticationService
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), "CustomerJwt"))
            .ReturnsAsync(customerResult);
        authenticationService
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), "StorefrontJwt"))
            .ReturnsAsync(storefrontResult);
        authenticationService
            .Setup(service => service.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        authenticationService
            .Setup(service => service.ForbidAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        authenticationService
            .Setup(service => service.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        authenticationService
            .Setup(service => service.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services
        };
    }
}
