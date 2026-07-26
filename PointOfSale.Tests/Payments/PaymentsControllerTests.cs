using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Attributes;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Payments;

public sealed class PaymentsControllerTests
{
    [Fact]
    public void PaymentsController_ClassLevelSecurityAttributes_AreHardened()
    {
        var attributes = typeof(PaymentsController).GetCustomAttributes(inherit: true);

        Assert.Contains(attributes, attribute =>
            attribute is AuthorizeAttribute authorize &&
            authorize.AuthenticationSchemes == "StorefrontJwt,CustomerJwt");
        Assert.DoesNotContain(attributes, attribute => attribute is AllowAnonymousAttribute);
        Assert.DoesNotContain(attributes, attribute => attribute is AllowAnonymousTenantAttribute);
    }

    [Fact]
    public async Task CreatePaymentIntent_WhenAuthenticatedCustomerOwnsOrder_CallsService()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, customerId, "session-123", false));
        paymentService
            .Setup(service => service.CreatePaymentIntentAsync(
                tenantId,
                orderId,
                It.IsAny<CreatePaymentIntentRequestDto>(),
                It.IsAny<bool>(),
                "req-123",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentIntentResponseDto
            {
                OrderId = orderId,
                PaymentIntentId = Guid.NewGuid()
            });

        var controller = CreateController(
            paymentService.Object,
            tenantId,
            CreateAuthenticationService(storefrontCustomerId: customerId));
        controller.Request.Headers["X-Request-Id"] = "req-123";

        var result = await controller.CreatePaymentIntent(
            orderId,
            new CreatePaymentIntentRequestDto(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentIntentResponseDto>(ok.Value);
        Assert.Equal(orderId, response.OrderId);

        paymentService.Verify(service => service.CreatePaymentIntentAsync(
            tenantId,
            orderId,
            It.IsAny<CreatePaymentIntentRequestDto>(),
            It.IsAny<bool>(),
            "req-123",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmCodOrder_WhenGuestSessionMatches_CallsService()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string sessionId = "guest-session-123";
        const string checkoutToken = "guest-token-123";
        var paymentService = new Mock<IPaymentService>();
        var tokenService = new Mock<ICheckoutSessionTokenService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, sessionId, true));
        tokenService
            .Setup(service => service.ValidatePaymentToken(checkoutToken, tenantId))
            .Returns(new CheckoutSessionTokenValidationResult(
                true,
                false,
                orderId.ToString("D"),
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "checkout-payment"));
        paymentService
            .Setup(service => service.ConfirmCodOrderAsync(
                tenantId,
                orderId,
                "req-123",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodConfirmResponseDto
            {
                OrderId = orderId,
                OrderNumber = "ORD-1"
            });

        var controller = CreateController(
            paymentService.Object,
            tenantId,
            CreateAuthenticationService(),
            tokenService: tokenService.Object);
        controller.Request.Headers["X-Request-Id"] = "req-123";
        controller.Request.Headers["X-Session-Id"] = sessionId;
        controller.Request.Headers["X-Checkout-Token"] = checkoutToken;

        var result = await controller.ConfirmCodOrder(orderId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CodConfirmResponseDto>(ok.Value);
        Assert.Equal(orderId, response.OrderId);

        paymentService.Verify(service => service.ConfirmCodOrderAsync(
            tenantId,
            orderId,
            "req-123",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmCodOrder_WhenGuestSessionMissing_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, "stored-session", true));

        var controller = CreateController(paymentService.Object, tenantId, CreateAuthenticationService());
        controller.Request.Headers["X-Request-Id"] = "req-123";

        var result = await controller.ConfirmCodOrder(orderId, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("SESSION_HEADER_MISSING", error.Code);

        paymentService.Verify(service => service.ConfirmCodOrderAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrderPaymentStatus_WhenAuthenticatedCustomerDoesNotOwnOrder_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var ownerCustomerId = Guid.NewGuid();
        var callerCustomerId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, ownerCustomerId, "session-123", false));

        var controller = CreateController(
            paymentService.Object,
            tenantId,
            CreateAuthenticationService(storefrontCustomerId: callerCustomerId));

        var result = await controller.GetOrderPaymentStatus(orderId, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);

        var error = Assert.IsType<ApiErrorResponse>(forbidden.Value);
        Assert.Equal("ORDER_ACCESS_DENIED", error.Code);

        paymentService.Verify(service => service.GetOrderPaymentStatusAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCanonicalPaymentStatus_WhenGuestSessionMatches_ReturnsCanonicalStatus()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string sessionId = "guest-session-123";
        const string checkoutToken = "guest-token-456";
        var paymentService = new Mock<IPaymentService>();
        var tokenService = new Mock<ICheckoutSessionTokenService>();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, null, sessionId, true));
        tokenService
            .Setup(service => service.ValidatePaymentToken(checkoutToken, tenantId))
            .Returns(new CheckoutSessionTokenValidationResult(
                true,
                false,
                orderId.ToString("D"),
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "checkout-payment"));
        paymentService
            .Setup(service => service.GetCanonicalPaymentStatusAsync(
                tenantId,
                orderId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CanonicalPaymentStatusDto
            {
                OrderId = orderId,
                OrderNumber = "ORD-100",
                Status = CanonicalPaymentStatusResolver.Processing
            });

        var controller = CreateController(
            paymentService.Object,
            tenantId,
            CreateAuthenticationService(),
            tokenService: tokenService.Object);
        controller.Request.Headers["X-Session-Id"] = sessionId;
        controller.Request.Headers["X-Checkout-Token"] = checkoutToken;

        var result = await controller.GetCanonicalPaymentStatus(orderId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CanonicalPaymentStatusDto>(ok.Value);
        Assert.Equal(orderId, response.OrderId);
        Assert.Equal(CanonicalPaymentStatusResolver.Processing, response.Status);

        paymentService.Verify(service => service.GetCanonicalPaymentStatusAsync(
            tenantId,
            orderId,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePaymentIntent_WhenManualWalletRequestIsSentDirectly_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentService = new Mock<IPaymentService>();
        await using var dbContext = CreateDbContext(tenantId);

        dbContext.Orders.Add(new PointOfSale.Core.Entities.Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-MW-1",
            Status = PointOfSale.Core.Enums.OrderStatus.Pending,
            PaymentStatus = PointOfSale.Core.Enums.PaymentStatus.Pending,
            PaymentMethod = PointOfSale.Core.Enums.PaymentMethod.Card,
            PaymentMethodCode = "MANUALWALLET",
            Total = 150m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        paymentService
            .Setup(service => service.GetOrderAccessInfoAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentOrderAccessInfo(null, customerId, "session-123", false));

        var controller = CreateController(
            paymentService.Object,
            tenantId,
            CreateAuthenticationService(storefrontCustomerId: customerId),
            dbContext);
        controller.Request.Headers["X-Request-Id"] = "req-123";

        var result = await controller.CreatePaymentIntent(
            orderId,
            new CreatePaymentIntentRequestDto
            {
                Provider = "ManualWallet",
                WalletType = "VodafoneCash",
                WalletPhoneNumber = "01011112222",
                ProofFileKey = $"tenants/{tenantId:D}/manual-wallet-proofs/proof.png"
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("MANUAL_WALLET_CHECKOUT_ONLY", error.Code);
        paymentService.Verify(service => service.CreatePaymentIntentAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CreatePaymentIntentRequestDto>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PaymentsController CreateController(
        IPaymentService paymentService,
        Guid tenantId,
        IAuthenticationService authenticationService,
        ApplicationDbContext? dbContext = null,
        ICheckoutSessionTokenService? tokenService = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var services = new ServiceCollection()
            .AddSingleton(authenticationService)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };

        var paymentRequestAuthorizationService = new PaymentRequestAuthorizationService(
            paymentService,
            tokenService ?? Mock.Of<ICheckoutSessionTokenService>(),
            authenticationService,
            NullLogger<PaymentRequestAuthorizationService>.Instance);

        return new PaymentsController(
            paymentService,
            paymentRequestAuthorizationService,
            tenantContext,
            dbContext ?? CreateDbContext(tenantId),
            NullLogger<PaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IAuthenticationService CreateAuthenticationService(
        Guid? storefrontCustomerId = null,
        Guid? customerJwtCustomerId = null)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(CreateAuthenticateResult(storefrontCustomerId, "StorefrontJwt"));

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                CustomerSecurityConstants.AuthenticationScheme))
            .ReturnsAsync(CreateAuthenticateResult(
                customerJwtCustomerId,
                CustomerSecurityConstants.AuthenticationScheme));

        return authenticationService.Object;
    }

    private static AuthenticateResult CreateAuthenticateResult(Guid? customerId, string scheme)
    {
        if (!customerId.HasValue)
        {
            return AuthenticateResult.NoResult();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("CustomerId", customerId.Value.ToString()),
                new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString())
            },
            scheme));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, scheme));
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(options, tenantContext);
    }
}
