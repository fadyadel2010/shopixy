using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;
using System.Security.Claims;
using ControllerCouponValidationResult = PointOfSale.API.Controllers.CouponValidationResult;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests;

public sealed class CouponsControllerCqrsConsistencyTests
{
    [Fact]
    public async Task CreateCoupon_PublishFailure_DoesNotPersistCoupon()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, tenantContext);
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<PointOfSale.Core.Events.CouponCreatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));

        var controller = new CouponsController(
            context,
            tenantContext,
            publishEndpoint.Object,
            CreateCouponValidationService(context),
            NullLogger<CouponsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.CreateCoupon(
            new CreateCouponDto
            {
                Code = "SAVE10",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                IsActive = true
            },
            CancellationToken.None));

        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Coupons.CountAsync());
    }

    [Fact]
    public void ValidateCouponRequest_DoesNotAcceptClientControlledCustomerId()
    {
        Assert.Null(typeof(ValidateCouponRequest).GetProperty("CustomerId"));
    }

    [Fact]
    public async Task ValidateCoupon_UsesAuthenticatedCustomerId_ForPerCustomerLimit()
    {
        var tenantId = Guid.NewGuid();
        var blockedCustomerId = Guid.NewGuid();
        var freshCustomerId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var coupon = new Coupon
        {
            Id = couponId,
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            MaxUsagePerCustomer = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        coupon.SetCode("SAVE10");
        context.Coupons.Add(coupon);
        context.CouponUsages.Add(new CouponUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CouponId = couponId,
            OrderId = Guid.NewGuid(),
            CustomerId = blockedCustomerId,
            CouponCode = "SAVE10",
            DiscountAmount = 10m,
            OrderSubtotal = 100m,
            UsedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var blockedController = CreateController(context, tenantId, blockedCustomerId);
        var blockedResult = await blockedController.ValidateCoupon(
            new ValidateCouponRequest
            {
                Code = "save10",
                OrderTotal = 100m
            },
            CancellationToken.None);

        var blockedOk = Assert.IsType<OkObjectResult>(blockedResult.Result);
        var blockedPayload = Assert.IsType<ControllerCouponValidationResult>(blockedOk.Value);
        Assert.False(blockedPayload.IsValid);
        Assert.Equal(CouponErrorCodes.CustomerMaxExceeded, blockedPayload.ErrorCode);

        var freshController = CreateController(context, tenantId, freshCustomerId);
        var freshResult = await freshController.ValidateCoupon(
            new ValidateCouponRequest
            {
                Code = "save10",
                OrderTotal = 100m
            },
            CancellationToken.None);

        var freshOk = Assert.IsType<OkObjectResult>(freshResult.Result);
        var freshPayload = Assert.IsType<ControllerCouponValidationResult>(freshOk.Value);
        Assert.True(freshPayload.IsValid);
        Assert.Equal(couponId, freshPayload.CouponId);
    }

    [Fact]
    public async Task SaveChangesAsync_NormalizesCouponCode_WhenCouponIsAdded()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        coupon.SetCode(" save10 ");

        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        Assert.Equal("SAVE10", coupon.Code);
        Assert.Equal("SAVE10", coupon.NormalizedCode);
    }

    [Fact]
    public async Task SaveChangesAsync_RecomputesNormalizedCode_WhenCouponCodeChanges()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var setupContext = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var setupCoupon = new Coupon
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            setupCoupon.SetCode("SAVE10");
            setupContext.Coupons.Add(setupCoupon);
            await setupContext.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var coupon = await context.Coupons.SingleAsync();
        coupon.SetCode(" spring20 ");

        await context.SaveChangesAsync();

        Assert.Equal("SPRING20", coupon.Code);
        Assert.Equal("SPRING20", coupon.NormalizedCode);
    }

    [Fact]
    public async Task GetCoupon_ForeignTenantCoupon_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignCouponId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var setupContext = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var foreignCoupon = BuildCoupon(foreignCouponId, foreignTenantId, "FOREIGN10");
            setupContext.Coupons.Add(foreignCoupon);
            await setupContext.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var controller = CreateAdminController(context, tenantId);

        var result = await controller.GetCoupon(foreignCouponId);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateCoupon_ForeignTenantCoupon_ReturnsNotFoundWithoutMutation()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignCouponId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var setupContext = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var foreignCoupon = BuildCoupon(foreignCouponId, foreignTenantId, "FOREIGN10");
            foreignCoupon.Description = "before";
            setupContext.Coupons.Add(foreignCoupon);
            await setupContext.SaveChangesAsync();
        }

        await using (var adminContext = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var controller = CreateAdminController(adminContext, tenantId);
            var result = await controller.UpdateCoupon(
                foreignCouponId,
                new UpdateCouponDto
                {
                    Code = "LOCAL20",
                    Description = "after",
                    DiscountType = DiscountType.FixedAmount,
                    DiscountValue = 20m,
                    IsActive = true
                },
                CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        await using var verifyContext = new ApplicationDbContext(options, new TestTenantContext(foreignTenantId));
        var coupon = await verifyContext.Coupons.SingleAsync(c => c.Id == foreignCouponId);
        Assert.Equal("FOREIGN10", coupon.Code);
        Assert.Equal("before", coupon.Description);
    }

    [Fact]
    public async Task ToggleCouponStatus_ForeignTenantCoupon_ReturnsNotFoundWithoutMutation()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignCouponId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var setupContext = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var foreignCoupon = BuildCoupon(foreignCouponId, foreignTenantId, "FOREIGN10");
            foreignCoupon.IsActive = true;
            setupContext.Coupons.Add(foreignCoupon);
            await setupContext.SaveChangesAsync();
        }

        await using (var adminContext = new ApplicationDbContext(options, new TestTenantContext(tenantId)))
        {
            var controller = CreateAdminController(adminContext, tenantId);
            var result = await controller.ToggleCouponStatus(foreignCouponId, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        await using var verifyContext = new ApplicationDbContext(options, new TestTenantContext(foreignTenantId));
        var coupon = await verifyContext.Coupons.SingleAsync(c => c.Id == foreignCouponId);
        Assert.True(coupon.IsActive);
    }

    private static CouponsController CreateController(
        ApplicationDbContext context,
        Guid tenantId,
        Guid customerId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("CustomerId", customerId.ToString()),
                new Claim("TenantId", tenantId.ToString())
            ],
            "StorefrontJwt"))
        };

        return new CouponsController(
            context,
            new TestTenantContext(tenantId),
            Mock.Of<IEventPublisher>(),
            CreateCouponValidationService(context),
            NullLogger<CouponsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static CouponsController CreateAdminController(
        ApplicationDbContext context,
        Guid tenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("TenantId", tenantId.ToString())
            ],
            "TestAuth"))
        };

        return new CouponsController(
            context,
            new TestTenantContext(tenantId),
            Mock.Of<IEventPublisher>(),
            CreateCouponValidationService(context),
            NullLogger<CouponsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static Coupon BuildCoupon(Guid couponId, Guid tenantId, string code)
    {
        var coupon = new Coupon
        {
            Id = couponId,
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        coupon.SetCode(code);
        return coupon;
    }

    private static ICouponValidationService CreateCouponValidationService(ApplicationDbContext context) =>
        new CouponValidationService(
            context,
            Mock.Of<IEventPublisher>(),
            NullLogger<CouponValidationService>.Instance);
}
