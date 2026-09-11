using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests;

public sealed class CheckoutControllerIdempotencyHeaderTests
{
    [Fact]
    public async Task Checkout_AllowsMissingClientIdempotencyHeaders_AndGeneratesCompatibilityRequestId()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        const string sessionId = "guest-session-1";

        var checkoutService = new Mock<ICheckoutService>();
        string? capturedRequestId = null;

        checkoutService
            .Setup(service => service.ProcessCheckoutAsync(
                tenantId,
                It.IsAny<string>(),
                "session",
                sessionId,
                null,
                It.IsAny<string>(),
                It.IsAny<CheckoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string?, Guid?, string, CheckoutRequestDto, string, CancellationToken>(
                (_, _, _, _, _, requestId, _, _, _) => capturedRequestId = requestId)
            .ReturnsAsync(new CheckoutResponseDto
            {
                OrderId = Guid.NewGuid(),
                OrderNumber = "ORD-S15A-001",
                TotalAmount = 125m,
                Currency = "EGP",
                CartId = cartId,
                Status = "PendingPayment"
            });

        var controller = new CheckoutController(
            checkoutService.Object,
            Mock.Of<ICheckoutAttachmentService>(),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(tenantId, sessionId)
            }
        };

        var result = await controller.Checkout(
            BuildValidRequest(cartId),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CheckoutResponseDto>(ok.Value);
        Assert.Equal(cartId, response.CartId);
        Assert.False(string.IsNullOrWhiteSpace(capturedRequestId));

        checkoutService.Verify(service => service.ProcessCheckoutAsync(
            tenantId,
            sessionId,
            "session",
            sessionId,
            null,
            It.Is<string>(value => !string.IsNullOrWhiteSpace(value)),
            It.IsAny<CheckoutRequestDto>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Checkout_RejectsMismatchedCompatibilityHeaders()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-2";
        var checkoutService = new Mock<ICheckoutService>();

        var controller = new CheckoutController(
            checkoutService.Object,
            Mock.Of<ICheckoutAttachmentService>(),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(
                    tenantId,
                    sessionId,
                    idempotencyKey: "legacy-key-a",
                    requestId: "legacy-key-b")
            }
        };

        var result = await controller.Checkout(
            BuildValidRequest(Guid.NewGuid()),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("IDEMPOTENCY_KEY_MISMATCH", error.Code);

        checkoutService.Verify(service => service.ProcessCheckoutAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<string>(),
            It.IsAny<CheckoutRequestDto>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Checkout_GuestReplayWithoutOriginalCookie_ReissuesTrackingCookieForSameOrder()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string sessionId = "guest-replay-session";
        const string idempotencyKey = "guest-replay-key";
        var issuedTokens = new Queue<string>(["initial-tracking-token", "replacement-tracking-token"]);
        var checkoutService = new Mock<ICheckoutService>();

        checkoutService
            .Setup(service => service.ProcessCheckoutAsync(
                tenantId,
                sessionId,
                "session",
                sessionId,
                null,
                idempotencyKey,
                It.IsAny<CheckoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CheckoutResponseDto
            {
                OrderId = orderId,
                OrderNumber = "ORD-GUEST-REPLAY",
                TotalAmount = 125m,
                Currency = "EGP",
                CartId = cartId,
                Status = "PendingPayment",
                TrackingToken = issuedTokens.Dequeue()
            });

        var controller = new CheckoutController(
            checkoutService.Object,
            Mock.Of<ICheckoutAttachmentService>(),
            NullLogger<CheckoutController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(tenantId, sessionId, idempotencyKey, idempotencyKey)
        };
        var firstResult = await controller.Checkout(BuildValidRequest(cartId), CancellationToken.None);
        var firstResponse = Assert.IsType<CheckoutResponseDto>(Assert.IsType<OkObjectResult>(firstResult).Value);
        var firstCookie = controller.Response.Headers.SetCookie.ToString();

        Assert.Equal(orderId, firstResponse.OrderId);
        Assert.Null(firstResponse.TrackingToken);
        Assert.Contains($"{GuestOrderAccessCookie.TrackingTokenCookieName}=initial-tracking-token", firstCookie, StringComparison.Ordinal);

        // Simulate a client that lost the original cookie but retains the checkout idempotency key and session.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(tenantId, sessionId, idempotencyKey, idempotencyKey)
        };
        var replayResult = await controller.Checkout(BuildValidRequest(cartId), CancellationToken.None);
        var replayResponse = Assert.IsType<CheckoutResponseDto>(Assert.IsType<OkObjectResult>(replayResult).Value);
        var replayCookie = controller.Response.Headers.SetCookie.ToString();

        Assert.Equal(orderId, replayResponse.OrderId);
        Assert.Null(replayResponse.TrackingToken);
        Assert.Contains($"{GuestOrderAccessCookie.TrackingTokenCookieName}=replacement-tracking-token", replayCookie, StringComparison.Ordinal);
        checkoutService.VerifyAll();
    }

    private static DefaultHttpContext BuildHttpContext(
        Guid tenantId,
        string sessionId,
        string? idempotencyKey = null,
        string? requestId = null)
    {
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());

        var requestServices = new ServiceCollection()
            .AddSingleton(authenticationService.Object)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices
        };

        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        httpContext.Request.Headers["X-Session-Id"] = sessionId;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            httpContext.Request.Headers["X-Request-Id"] = requestId;
        }

        return httpContext;
    }

    private static CheckoutRequestDto BuildValidRequest(Guid cartId) =>
        new()
        {
            CartId = cartId,
            CheckoutSessionId = Guid.NewGuid(),
            WarehouseResolutionId = Guid.NewGuid(),
            WarehouseResolutionVersion = 1,
            ShippingMethodId = Guid.NewGuid(),
            PaymentMethod = "COD",
            ShippingAddress = new AddressDto
            {
                Name = "Test Customer",
                Phone = "01000000000",
                Street = "123 Test Street",
                City = "Cairo",
                State = "Cairo",
                Country = "EG"
            },
            ExpectedPricing = CheckoutTestFixtures.CreateDefaultExpectedPricing()
        };
}
