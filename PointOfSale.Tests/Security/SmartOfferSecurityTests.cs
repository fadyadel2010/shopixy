using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Security;

public sealed class SmartOfferSecurityTests
{
    [Fact]
    public async Task ApplyCoupon_WhenSmartOfferGeneratedCouponIsProvidedWithoutRedeemToken_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var couponId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.OnlineCustomers.Add(CreateOnlineCustomer(tenantId, customerId));
        dbContext.Coupons.Add(CreateCoupon(tenantId, couponId, "OFFER10"));
        dbContext.SmartOffers.Add(CreateSmartOffer(tenantId, couponId, couponCode: "OFFER10"));
        await dbContext.SaveChangesAsync();

        var cartService = new Mock<ICartService>(MockBehavior.Strict);
        var controller = CreateCartController(
            dbContext,
            tenantId,
            customerId,
            cartService.Object,
            Mock.Of<IStorefrontCouponService>(),
            Mock.Of<ISmartOfferRedeemTokenService>());

        var result = await controller.ApplyCoupon(
            new ApplyCouponRequest
            {
                Code = "offer10"
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal(SmartOfferErrorCodes.SmartOfferTokenRequired, error.Code);
    }

    [Fact]
    public async Task ApplyCoupon_WhenSmartOfferRedeemTokenIsInvalid_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.OnlineCustomers.Add(CreateOnlineCustomer(tenantId, customerId));
        await dbContext.SaveChangesAsync();
        var tokenService = new Mock<ISmartOfferRedeemTokenService>();
        tokenService
            .Setup(service => service.ValidateToken("tampered-token", tenantId, customerId))
            .Returns(SmartOfferRedeemTokenValidationResult.Invalid());

        var controller = CreateCartController(
            dbContext,
            tenantId,
            customerId,
            Mock.Of<ICartService>(),
            Mock.Of<IStorefrontCouponService>(),
            tokenService.Object);

        var result = await controller.ApplyCoupon(
            new ApplyCouponRequest
            {
                SmartOfferRedeemToken = "tampered-token"
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal(SmartOfferErrorCodes.SmartOfferTokenInvalid, error.Code);
    }

    [Fact]
    public async Task RedeemSmartOffer_WhenUnauthenticated_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateSmartOffersController(dbContext, tenantId, customerId: null);

        var result = await controller.RedeemSmartOffer(Guid.NewGuid(), CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(forbidden.Value);
        Assert.Equal(CouponErrorCodes.AuthenticationRequired, error.Code);
    }

    [Fact]
    public async Task GetSmartOffers_PublicResponseDoesNotExposeCouponCode()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Coupons.Add(CreateCoupon(tenantId, couponId, "SECRET10"));
        dbContext.SmartOffers.Add(CreateSmartOffer(tenantId, couponId, couponCode: "SECRET10"));
        await dbContext.SaveChangesAsync();

        var controller = CreateSmartOffersController(dbContext, tenantId, customerId: null);

        var result = await controller.GetSmartOffers(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);

        Assert.DoesNotContain("couponCode", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Spring Offer", json, StringComparison.Ordinal);
    }

    private static StorefrontCartController CreateCartController(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid customerId,
        ICartService cartService,
        IStorefrontCouponService couponService,
        ISmartOfferRedeemTokenService tokenService)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("TenantId", tenantId.ToString("D")),
                new Claim("CustomerId", customerId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, customerId.ToString("D"))
            },
            "StorefrontJwt"));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<ITenantContext>(tenantContext)
                .AddSingleton(CreateAuthenticationService(customerId, tenantId))
                .BuildServiceProvider(),
            User = principal
        };
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        return new StorefrontCartController(
            cartService,
            couponService,
            Mock.Of<IEventPublisher>(),
            tokenService,
            dbContext,
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontCartController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static StorefrontSmartOffersController CreateSmartOffersController(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid? customerId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var principal = customerId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim("TenantId", tenantId.ToString("D")),
                    new Claim("CustomerId", customerId.Value.ToString("D")),
                    new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
                },
                "StorefrontJwt"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<ITenantContext>(tenantContext)
                .AddSingleton(CreateAuthenticationService(customerId, tenantId))
                .BuildServiceProvider(),
            User = principal
        };
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        return new StorefrontSmartOffersController(
            dbContext,
            CreateTokenService(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontSmartOffersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IAuthenticationService CreateAuthenticationService(Guid? customerId, Guid tenantId)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                CustomerSecurityConstants.AuthenticationScheme))
            .ReturnsAsync(customerId.HasValue
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim("TenantId", tenantId.ToString("D")),
                            new Claim("CustomerId", customerId.Value.ToString("D")),
                            new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
                        },
                        CustomerSecurityConstants.AuthenticationScheme)),
                    CustomerSecurityConstants.AuthenticationScheme))
                : AuthenticateResult.NoResult());

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(customerId.HasValue
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim("TenantId", tenantId.ToString("D")),
                            new Claim("CustomerId", customerId.Value.ToString("D")),
                            new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
                        },
                        "StorefrontJwt")),
                    "StorefrontJwt"))
                : AuthenticateResult.NoResult());

        return authenticationService.Object;
    }

    private static ISmartOfferRedeemTokenService CreateTokenService()
    {
        var provider = new EphemeralDataProtectionProvider();
        return new SmartOfferRedeemTokenService(
            provider,
            NullLogger<SmartOfferRedeemTokenService>.Instance);
    }

    private static Coupon CreateCoupon(Guid tenantId, Guid couponId, string code)
    {
        var coupon = new Coupon
        {
            Id = couponId,
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true
        };
        coupon.SetCode(code);
        return coupon;
    }

    private static OnlineCustomer CreateOnlineCustomer(Guid tenantId, Guid customerId) =>
        new()
        {
            Id = customerId,
            TenantId = tenantId,
            Email = "customer@example.com",
            NormalizedEmail = "customer@example.com",
            DisplayName = "Customer",
            IsActive = true
        };

    private static SmartOffer CreateSmartOffer(Guid tenantId, Guid couponId, string couponCode)
    {
        return new SmartOffer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Spring Offer",
            Status = SmartOfferStatus.Active,
            Priority = 100,
            TriggerType = SmartOfferTriggerType.TimeDelay,
            PagesScope = SmartOfferPagesScope.All,
            DelaySeconds = 5,
            ShowOncePerSession = true,
            PopupTitle = "Spring Offer",
            PopupDescription = "Server verified only",
            ButtonText = "Apply",
            CouponCode = couponCode,
            GeneratedCouponId = couponId,
            CouponDiscountType = DiscountType.Percentage,
            CouponDiscountValue = 10m,
            CreatedAt = DateTime.UtcNow
        };
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
