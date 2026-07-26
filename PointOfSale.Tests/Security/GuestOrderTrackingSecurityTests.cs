using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Security;

public sealed class GuestOrderTrackingSecurityTests
{
    [Fact]
    public async Task GetGuestOrderTracking_WithoutSignedToken_ReturnsForbiddenWithoutOrderData()
    {
        var tenantId = Guid.NewGuid();
        var order = CreateGuestOrder(tenantId);
        var controller = CreateController(tenantId, order, tokenValidation: CheckoutSessionTokenValidationResult.Invalid("guest-order-tracking"));
        controller.HttpContext.Request.Headers["X-Session-Id"] = order.SessionId!;

        var result = await controller.GetGuestOrderTracking(order.OrderId);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(forbidden.Value);

        Assert.Equal("TRACKING_TOKEN_REQUIRED", error.Code);
    }

    [Fact]
    public async Task GetGuestOrderTracking_WithInvalidSignedToken_ReturnsForbiddenWithoutOrderData()
    {
        var tenantId = Guid.NewGuid();
        var order = CreateGuestOrder(tenantId);
        var controller = CreateController(tenantId, order, tokenValidation: CheckoutSessionTokenValidationResult.Invalid("guest-order-tracking"));
        controller.HttpContext.Request.Headers["X-Session-Id"] = order.SessionId!;
        controller.HttpContext.Request.Headers["X-Tracking-Token"] = "invalid-token";

        var result = await controller.GetGuestOrderTracking(order.OrderId);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(forbidden.Value);

        Assert.Equal("TRACKING_TOKEN_REQUIRED", error.Code);
    }

    [Fact]
    public async Task GetGuestOrderTracking_WithSignedTokenHeader_ReturnsFullTrackingDetails()
    {
        var tenantId = Guid.NewGuid();
        var order = CreateGuestOrder(tenantId);
        var validation = new CheckoutSessionTokenValidationResult(
            IsValid: true,
            IsExpired: false,
            Subject: order.OrderId.ToString("N"),
            Nonce: order.SessionId,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            Purpose: "guest-order-tracking");
        var controller = CreateController(tenantId, order, validation);
        controller.HttpContext.Request.Headers["X-Session-Id"] = order.SessionId!;
        controller.HttpContext.Request.Headers["X-Tracking-Token"] = "signed-token";

        var result = await controller.GetGuestOrderTracking(order.OrderId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<StorefrontOrderTrackingDto>(ok.Value);

        Assert.True(dto.TrackingAccessVerified);
        Assert.False(dto.RequiresTrackingToken);
        Assert.Equal(order.CustomerName, dto.CustomerName);
        Assert.Single(dto.Items);
        Assert.Equal(order.ShippingAddress!.Street, dto.ShippingAddress!.Street);
        Assert.Equal(order.ShippingAddress.Phone, dto.ShippingAddress.Phone);
        Assert.Equal(order.TrackingNumber, dto.TrackingNumber);
    }

    [Fact]
    public async Task GetGuestOrderTracking_WithSignedTokenCookie_ReturnsFullTrackingDetails()
    {
        var tenantId = Guid.NewGuid();
        var order = CreateGuestOrder(tenantId);
        var validation = new CheckoutSessionTokenValidationResult(
            IsValid: true,
            IsExpired: false,
            Subject: order.OrderId.ToString("N"),
            Nonce: order.SessionId,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            Purpose: "guest-order-tracking");
        var controller = CreateController(tenantId, order, validation);
        controller.HttpContext.Request.Headers["X-Session-Id"] = order.SessionId!;
        controller.HttpContext.Request.Headers["Cookie"] = $"{GuestOrderAccessCookie.TrackingTokenCookieName}=cookie-tracking-token";

        var result = await controller.GetGuestOrderTracking(order.OrderId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<StorefrontOrderTrackingDto>(ok.Value);

        Assert.True(dto.TrackingAccessVerified);
        Assert.False(dto.RequiresTrackingToken);
        Assert.Equal(order.CustomerName, dto.CustomerName);
    }

    [Fact]
    public async Task TrackGuestOrder_EmailLookup_SendsFreshMagicLinkWithoutReturningOrderData()
    {
        var tenantId = Guid.NewGuid();
        var order = CreateGuestOrder(tenantId);
        var emailSender = new Mock<IEmailSender>();
        var controller = CreateController(
            tenantId,
            order,
            CheckoutSessionTokenValidationResult.Invalid("guest-order-tracking"),
            emailSender);

        var result = await controller.TrackGuestOrder(order.CustomerEmail!, order.OrderNumber);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.IsType<GuestTrackingRecoveryResponse>(accepted.Value);
        emailSender.Verify(
            sender => sender.SendEmailAsync(
                order.CustomerEmail!,
                It.Is<string>(subject => subject.Contains(order.OrderNumber, StringComparison.Ordinal)),
                It.Is<string>(body => body.Contains("#tracking_token=", StringComparison.Ordinal)),
                It.Is<string>(body => body.Contains("#tracking_token=", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public async Task GetGuestOrderTracking_WhenProjectionRepairFails_Returns503()
    {
        var tenantId = Guid.NewGuid();
        var order = CreateGuestOrder(tenantId);
        var validation = new CheckoutSessionTokenValidationResult(
            IsValid: true,
            IsExpired: false,
            Subject: order.OrderId.ToString("N"),
            Nonce: order.SessionId,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
            Purpose: "guest-order-tracking");

        var repairService = new Mock<IStorefrontOrderProjectionRepairService>();
        repairService
            .Setup(service => service.EnsureOrderProjectedAsync(
                tenantId,
                order.OrderId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Projection repair failed"));

        var controller = CreateControllerWithRepairService(tenantId, order, validation, repairService.Object);
        controller.HttpContext.Request.Headers["X-Session-Id"] = order.SessionId!;
        controller.HttpContext.Request.Headers["X-Tracking-Token"] = "signed-token";

        var result = await controller.GetGuestOrderTracking(order.OrderId);

        var serviceUnavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, serviceUnavailable.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(serviceUnavailable.Value);
        Assert.Equal("SERVICE_UNAVAILABLE", error.Code);
    }

    private static StorefrontOrdersReadController CreateControllerWithRepairService(
        Guid tenantId,
        OrderDocument order,
        CheckoutSessionTokenValidationResult tokenValidation,
        IStorefrontOrderProjectionRepairService repairService)
    {
        return CreateControllerCore(tenantId, order, tokenValidation, repairService, null);
    }

    private static StorefrontOrdersReadController CreateController(
        Guid tenantId,
        OrderDocument order,
        CheckoutSessionTokenValidationResult tokenValidation,
        Mock<IEmailSender>? emailSender = null)
    {
        return CreateControllerCore(
            tenantId,
            order,
            tokenValidation,
            Mock.Of<PointOfSale.API.Services.Storefront.IStorefrontOrderProjectionRepairService>(),
            emailSender);
    }

    private static StorefrontOrdersReadController CreateControllerCore(
        Guid tenantId,
        OrderDocument order,
        CheckoutSessionTokenValidationResult tokenValidation,
        IStorefrontOrderProjectionRepairService repairService,
        Mock<IEmailSender>? emailSender = null)
    {
        var orderRead = new Mock<IOrderReadRepository>();
        orderRead
            .Setup(repo => repo.GetByOrderNumberAsync(tenantId, order.OrderNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orderRead
            .Setup(repo => repo.GetOrderTrackingAsync(tenantId, order.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var rateLimiter = new Mock<IGuestOrderTrackingRateLimiter>();
        rateLimiter
            .Setup(limiter => limiter.CheckAsync(It.IsAny<string>()))
            .ReturnsAsync(GuestTrackingRateLimitResult.Allow(1, 10));

        var tokenService = new Mock<ICheckoutSessionTokenService>();
        tokenService
            .Setup(service => service.ValidateGuestTrackingToken(It.IsAny<string>(), tenantId))
            .Returns(tokenValidation);
        tokenService
            .Setup(service => service.CreateGuestTrackingToken(tenantId, order.OrderId, order.SessionId!))
            .Returns("fresh-tracking-token");

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("store.example.test");
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<PointOfSale.Core.Interfaces.ITenantContext>(tenantContext)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new CheckoutSessionSecurityOptions()))
            .BuildServiceProvider();

        var dbContext = CreateInMemoryDbContext();
        dbContext.Orders.Add(new Order
        {
            Id = order.OrderId,
            TenantId = tenantId,
            OrderNumber = order.OrderNumber,
            Source = OrderSource.Ecommerce,
            OnlineCustomerId = null,
            CustomerId = null,
            SessionId = order.SessionId,
            GuestEmail = order.CustomerEmail,
            ShippingAddress = "123 Private Street",
            Subtotal = order.Total,
            Total = order.Total,
            Currency = order.Currency,
            PaymentMethodCode = "COD"
        });
        dbContext.SaveChanges();

        return new StorefrontOrdersReadController(
            dbContext,
            orderRead.Object,
            repairService,
            rateLimiter.Object,
            tokenService.Object,
            (emailSender ?? new Mock<IEmailSender>()).Object,
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontOrdersReadController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static OrderDocument CreateGuestOrder(Guid tenantId)
    {
        return new OrderDocument
        {
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-SEC-001",
            SessionId = Guid.NewGuid().ToString("D"),
            CustomerEmail = "guest@example.com",
            CustomerName = "Guest Customer",
            Status = "Pending",
            PaymentStatus = "Pending",
            ShippingStatus = "Processing",
            Currency = "EGP",
            Total = 250m,
            ShippingMethodName = "Courier",
            TrackingNumber = "TRACK123",
            TrackingUrl = "https://carrier.example/track/TRACK123",
            CreatedAt = DateTime.UtcNow,
            ShippingAddress = new OrderAddressDocument
            {
                Street = "123 Private Street",
                City = "Cairo",
                Country = "EG",
                Phone = "+201000000000"
            },
            Items =
            {
                new OrderItemDocument
                {
                    ProductId = Guid.NewGuid(),
                    ProductName = "Sensitive Product",
                    SKU = "SKU-PRIVATE",
                    Quantity = 1,
                    UnitPrice = 250m,
                    Total = 250m
                }
            }
        };
    }
}
