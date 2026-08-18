using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests;

public sealed class CheckoutPromotionCorrectnessControllerTests
{
    [Fact]
    public async Task Checkout_WhenPromotionCalculationFails_ReturnsServiceUnavailable()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-promo";

        var checkoutService = new Mock<ICheckoutService>();
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
            .ThrowsAsync(new CheckoutPromotionCalculationException(
                "Promotion calculation failed. Please refresh pricing and try again."));

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

        var result = await controller.Checkout(BuildValidRequest(Guid.NewGuid()), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Equal("PROMOTION_CALCULATION_FAILED", error.Code);
    }

    [Fact]
    public async Task Checkout_WhenTotalsMismatch_ReturnsConflict()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-totals";

        var checkoutService = new Mock<ICheckoutService>();
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
            .ThrowsAsync(new CheckoutTotalMismatchException(new[] { "total" }));

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

        var result = await controller.Checkout(BuildValidRequest(Guid.NewGuid()), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(conflict.Value);
        Assert.Equal("CHECKOUT_TOTAL_MISMATCH", error.Code);
        Assert.NotNull(error.Data);
        
        // Verify MismatchedFields are included in the response
        var dataDict = Assert.IsType<Dictionary<string, object>>(
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                System.Text.Json.JsonSerializer.Serialize(error.Data)));
        Assert.True(dataDict.ContainsKey("MismatchedFields"));
        
        // Validate the MismatchedFields payload contains the expected field name
        var mismatchedFieldsElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            System.Text.Json.JsonSerializer.Serialize(dataDict["MismatchedFields"]));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, mismatchedFieldsElement.ValueKind);
        var mismatchedFields = mismatchedFieldsElement.EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        Assert.Contains("total", mismatchedFields);
    }

    [Fact]
    public async Task Checkout_WhenPhoneVerificationIsRequired_ReturnsBadRequestWithActionableCode()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-phone";
        var checkoutService = BuildThrowingCheckoutService(
            tenantId,
            sessionId,
            new CheckoutException(
                "PHONE_VERIFICATION_REQUIRED",
                "Please verify your phone number before placing the order.",
                "PhoneVerification"));
        var controller = BuildController(checkoutService.Object, tenantId, sessionId);

        var result = await controller.Checkout(BuildValidRequest(Guid.NewGuid()), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Equal("PHONE_VERIFICATION_REQUIRED", error.Code);
        Assert.Contains("verify", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkout_WhenUnexpectedFailureOccurs_ReturnsInternalServerError()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-unexpected";
        var checkoutService = BuildThrowingCheckoutService(
            tenantId,
            sessionId,
            new InvalidOperationException("sensitive root failure"));
        var controller = BuildController(checkoutService.Object, tenantId, sessionId);

        var result = await controller.Checkout(BuildValidRequest(Guid.NewGuid()), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Equal("UNEXPECTED_CHECKOUT_FAILURE", error.Code);
        Assert.DoesNotContain("sensitive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewPricing_WhenPromotionCalculationFails_ReturnsServiceUnavailable()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-preview";

        var checkoutService = new Mock<ICheckoutService>();
        checkoutService
            .Setup(service => service.PreviewPricingAsync(
                tenantId,
                It.IsAny<string>(),
                "session",
                sessionId,
                null,
                It.IsAny<CheckoutPricingPreviewRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CheckoutPromotionCalculationException(
                "Promotion calculation failed. Please refresh pricing and try again."));

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

        var result = await controller.PreviewPricing(
            new CheckoutPricingPreviewRequestDto
            {
                CartId = Guid.NewGuid(),
                ShippingCountry = "EG"
            },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(objectResult.Value);
        Assert.Equal("PROMOTION_CALCULATION_FAILED", error.Code);
    }

    private static DefaultHttpContext BuildHttpContext(Guid tenantId, string sessionId)
    {
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());

        var requestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authenticationService.Object)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices
        };

        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        httpContext.Request.Headers["X-Session-Id"] = sessionId;
        return httpContext;
    }

    private static Mock<ICheckoutService> BuildThrowingCheckoutService(
        Guid tenantId,
        string sessionId,
        Exception exception)
    {
        var checkoutService = new Mock<ICheckoutService>();
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
            .ThrowsAsync(exception);
        return checkoutService;
    }

    private static CheckoutController BuildController(
        ICheckoutService checkoutService,
        Guid tenantId,
        string sessionId) =>
        new(
            checkoutService,
            Mock.Of<ICheckoutAttachmentService>(),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext(tenantId, sessionId)
            }
        };

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
                Name = "Promotion Test Customer",
                Phone = "01000000000",
                Street = "123 Test Street",
                City = "Cairo",
                State = "Cairo",
                Country = "EG"
            },
            ExpectedPricing = CheckoutTestFixtures.CreateDefaultExpectedPricing()
        };
}
