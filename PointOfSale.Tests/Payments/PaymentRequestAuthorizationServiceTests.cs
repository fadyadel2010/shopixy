using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentRequestAuthorizationServiceTests
{
    [Fact]
    public async Task AuthorizeOrderAccessAsync_WhenGuestCheckoutTokenMissing_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, "guest-session", true));

        var service = CreateService(
            paymentService.Object,
            Mock.Of<ICheckoutSessionTokenService>(),
            CreateAuthenticationService());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Session-Id"] = "guest-session";

        var result = await service.AuthorizeOrderAccessAsync(
            httpContext,
            tenantId,
            orderId,
            "trace-guest-missing-token",
            cancellationToken: CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Equal(StatusCodes.Status400BadRequest, result.FailureStatusCode);
        Assert.Equal("CHECKOUT_TOKEN_MISSING", result.Error?.Code);
    }

    [Fact]
    public async Task AuthorizeOrderAccessAsync_WhenGuestSessionAndTokenMatch_ReturnsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tokenService = new Mock<ICheckoutSessionTokenService>();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, "guest-session", true));
        tokenService
            .Setup(service => service.ValidatePaymentToken("guest-token", tenantId))
            .Returns(new CheckoutSessionTokenValidationResult(
                true,
                false,
                orderId.ToString("D"),
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "checkout-payment"));

        var service = CreateService(
            paymentService.Object,
            tokenService.Object,
            CreateAuthenticationService());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Session-Id"] = "guest-session";
        httpContext.Request.Headers["X-Checkout-Token"] = "guest-token";

        var result = await service.AuthorizeOrderAccessAsync(
            httpContext,
            tenantId,
            orderId,
            "trace-guest-success",
            cancellationToken: CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Null(result.AuthenticatedCustomerId);
        Assert.NotNull(result.OrderAccessInfo);
    }

    [Fact]
    public async Task AuthorizeOrderAccessAsync_WhenGuestCheckoutTokenIsInCookie_ReturnsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tokenService = new Mock<ICheckoutSessionTokenService>();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, "guest-session", true));
        tokenService
            .Setup(service => service.ValidatePaymentToken("guest-cookie-token", tenantId))
            .Returns(new CheckoutSessionTokenValidationResult(
                true,
                false,
                orderId.ToString("D"),
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "checkout-payment"));

        var service = CreateService(
            paymentService.Object,
            tokenService.Object,
            CreateAuthenticationService());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Session-Id"] = "guest-session";
        httpContext.Request.Headers["Cookie"] = $"{GuestOrderAccessCookie.CheckoutTokenCookieName}=guest-cookie-token";

        var result = await service.AuthorizeOrderAccessAsync(
            httpContext,
            tenantId,
            orderId,
            "trace-guest-cookie-success",
            cancellationToken: CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Null(result.AuthenticatedCustomerId);
        Assert.NotNull(result.OrderAccessInfo);
    }

    [Fact]
    public async Task AuthorizeOrderAccessAsync_WhenAuthenticatedCustomerOwnsOrder_ReturnsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, customerId, null, false));

        var service = CreateService(
            paymentService.Object,
            Mock.Of<ICheckoutSessionTokenService>(),
            CreateAuthenticationService(customerId));

        var result = await service.AuthorizeOrderAccessAsync(
            new DefaultHttpContext(),
            tenantId,
            orderId,
            "trace-auth-success",
            cancellationToken: CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Equal(customerId, result.AuthenticatedCustomerId);
    }

    [Fact]
    public async Task AuthorizeOrderAccessAsync_WhenGuestSessionAndTokenDoNotMatch_ReturnsDenied()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var differentOrderId = Guid.NewGuid();
        var tokenService = new Mock<ICheckoutSessionTokenService>();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, "guest-session", true));

        // Token validation returns a different order ID (mismatch)
        tokenService
            .Setup(service => service.ValidatePaymentToken("guest-token", tenantId))
            .Returns(new CheckoutSessionTokenValidationResult(
                true,
                false,
                differentOrderId.ToString("D"), // Different order ID
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "checkout-payment"));

        var service = CreateService(
            paymentService.Object,
            tokenService.Object,
            CreateAuthenticationService());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Session-Id"] = "guest-session";
        httpContext.Request.Headers["X-Checkout-Token"] = "guest-token";

        var result = await service.AuthorizeOrderAccessAsync(
            httpContext,
            tenantId,
            orderId,
            "trace-guest-token-mismatch",
            cancellationToken: CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Null(result.AuthenticatedCustomerId);
        Assert.Null(result.OrderAccessInfo);
        Assert.Equal(StatusCodes.Status403Forbidden, result.FailureStatusCode);
        Assert.NotNull(result.Error);
        Assert.Equal("ORDER_ACCESS_DENIED", result.Error.Code);
    }

    [Fact]
    public async Task AuthorizeOrderAccessAsync_WhenAuthenticatedCustomerDoesNotOwnOrder_ReturnsDenied()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var authenticatedCustomerId = Guid.NewGuid();
        var orderOwnerCustomerId = Guid.NewGuid(); // Different customer owns the order
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, orderOwnerCustomerId, null, false));

        var service = CreateService(
            paymentService.Object,
            Mock.Of<ICheckoutSessionTokenService>(),
            CreateAuthenticationService(authenticatedCustomerId));

        var result = await service.AuthorizeOrderAccessAsync(
            new DefaultHttpContext(),
            tenantId,
            orderId,
            "trace-auth-customer-mismatch",
            cancellationToken: CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.NotEqual(orderOwnerCustomerId, result.AuthenticatedCustomerId);
        Assert.Equal(StatusCodes.Status403Forbidden, result.FailureStatusCode);
        Assert.NotNull(result.Error);
        Assert.Equal("ORDER_ACCESS_DENIED", result.Error.Code);
    }

    private static PaymentRequestAuthorizationService CreateService(
        IPaymentService paymentService,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        IAuthenticationService authenticationService)
    {
        return new PaymentRequestAuthorizationService(
            paymentService,
            checkoutSessionTokenService,
            authenticationService,
            NullLogger<PaymentRequestAuthorizationService>.Instance);
    }

    private static IAuthenticationService CreateAuthenticationService(Guid? customerId = null)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                CustomerSecurityConstants.AuthenticationScheme))
            .ReturnsAsync(customerId.HasValue
                ? AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity(
                                new[]
                                {
                                    new System.Security.Claims.Claim("CustomerId", customerId.Value.ToString("D")),
                                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
                                },
                                CustomerSecurityConstants.AuthenticationScheme)),
                        CustomerSecurityConstants.AuthenticationScheme))
                : AuthenticateResult.NoResult());

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(AuthenticateResult.NoResult());

        return authenticationService.Object;
    }
}
