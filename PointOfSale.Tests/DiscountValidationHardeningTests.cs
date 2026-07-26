using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.SmartOffers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Validation;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests;

public sealed class DiscountValidationHardeningTests
{
    private static readonly InMemoryDatabaseRoot _sharedRoot = new();
    [Fact]
    public async Task CreateCoupon_NegativeDiscount_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);

        var result = await controller.CreateCoupon(
            new CreateCouponDto
            {
                Code = "NEGATIVE",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = -5m,
                IsActive = true
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Discount must be > 0", badRequest.Value?.ToString());
        Assert.Equal(0, await context.Coupons.CountAsync());
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<CouponCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateCoupon_ValidDiscount_ReturnsCreated()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);

        var result = await controller.CreateCoupon(
            new CreateCouponDto
            {
                Code = "SAVE10",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                IsActive = true
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CouponDto>(created.Value);
        Assert.Equal(10m, dto.DiscountValue);
        Assert.Equal(DiscountType.Percentage, dto.DiscountType);
        Assert.Equal(1, await context.Coupons.CountAsync());
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<CouponCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateCoupon_EndDateBeforeStartDate_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);

        var result = await controller.CreateCoupon(
            new CreateCouponDto
            {
                Code = "DATEFAIL",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                StartDate = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow,
                IsActive = true
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Start date cannot be after end date.", badRequest.Value?.ToString());
        Assert.Equal(0, await context.Coupons.CountAsync());
    }

    [Fact]
    public async Task CreateCoupon_MaxUsagePerCustomerExceedsMaxUsage_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);

        var result = await controller.CreateCoupon(
            new CreateCouponDto
            {
                Code = "LIMITFAIL",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 10m,
                MaxUsage = 1,
                MaxUsagePerCustomer = 2,
                IsActive = true
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Max usage per customer cannot exceed max usage.", badRequest.Value?.ToString());
        Assert.Equal(0, await context.Coupons.CountAsync());
    }

    [Fact]
    public async Task CreateCoupon_ForeignProductEligibility_ReturnsBadRequestBeforePersistence()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignProductId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);

        context.Products.Add(new Product
        {
            Id = foreignProductId,
            TenantId = foreignTenantId,
            Name = "Foreign product",
            CategoryId = Guid.NewGuid(),
            SalePrice = 10m,
            PurchasePrice = 5m,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var result = await controller.CreateCoupon(
            new CreateCouponDto
            {
                Code = "FOREIGNPRODUCT",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 10m,
                ApplicableProductIds = System.Text.Json.JsonSerializer.Serialize(new[] { foreignProductId }),
                IsActive = true
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(
            "Coupon product eligibility must reference existing products owned by the current tenant.",
            badRequest.Value?.ToString());
        Assert.Equal(0, await context.Coupons.CountAsync());
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<CouponCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreatePromotion_PercentageAbove100_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreatePromotionsController(context, tenantId, publishEndpoint, databaseName);

        var result = await controller.CreatePromotion(
            new CreatePromotionDto
            {
                Name = "Mega Sale",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 200m,
                Scope = PromotionScope.Cart,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddDays(1),
                IsActive = true
            });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Percentage cannot exceed 100%", badRequest.Value?.ToString());
        Assert.Equal(0, await context.Promotions.CountAsync());
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<PromotionCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCoupons_PageSizeAboveMax_IsCapped()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        SeedCoupons(context, tenantId, count: 150);
        await context.SaveChangesAsync();

        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);
        var result = await controller.GetCoupons(page: 1, pageSize: 1000);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var coupons = Assert.IsAssignableFrom<IEnumerable<CouponDto>>(ok.Value);
        Assert.Equal(PaginationGuard.MaxPageSize, coupons.Count());
    }

    [Fact]
    public async Task GetPromotions_PageSizeAboveMax_IsCapped()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        SeedPromotions(context, tenantId, count: 150);
        await context.SaveChangesAsync();

        var controller = CreatePromotionsController(context, tenantId, publishEndpoint, databaseName);
        var result = await controller.GetPromotions(page: 1, pageSize: 1000);

        var promotions = Assert.IsType<List<PromotionDto>>(result.Value);
        Assert.Equal(PaginationGuard.MaxPageSize, promotions.Count);
    }

    [Fact]
    public async Task CreateSmartOffer_FixedDiscountAboveMaximum_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        var publishEndpoint = CreateEventPublisherMock();
        var controller = CreateSmartOffersController(context, tenantId, publishEndpoint);

        var result = await controller.CreateSmartOffer(
            new UpsertSmartOfferRequest
            {
                Name = "Exit Offer",
                Status = SmartOfferStatus.Active,
                TriggerType = SmartOfferTriggerType.ExitIntent,
                PagesScope = SmartOfferPagesScope.All,
                PopupTitle = "Wait",
                ButtonText = "Claim",
                CouponCode = "HUGE",
                CouponDiscountType = DiscountType.FixedAmount,
                CouponDiscountValue = DiscountValidation.MaxFixedDiscountValue + 0.01m
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Discount too large", badRequest.Value?.ToString());
        Assert.Equal(0, await context.SmartOffers.CountAsync());
        Assert.Equal(0, await context.Coupons.CountAsync());
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<SmartOfferCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<CouponCreatedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsCoupon_WhenControllerBypassUsesInvalidDiscount()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using var context = CreateContext(databaseName, tenantId);
        var coupon = new Coupon
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 200m,
            IsActive = true
        };
        coupon.SetCode("SAVE200");
        context.Coupons.Add(coupon);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Percentage cannot exceed 100%", exception.Message);

        await using var verifyContext = CreateContext(databaseName, tenantId);
        Assert.Equal(0, await verifyContext.Coupons.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_RejectsSmartOffer_WhenControllerBypassUsesOversizedFixedDiscount()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");

        await using var context = CreateContext(databaseName, tenantId);
        context.SmartOffers.Add(new SmartOffer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Popup",
            Status = SmartOfferStatus.Active,
            TriggerType = SmartOfferTriggerType.ExitIntent,
            PagesScope = SmartOfferPagesScope.All,
            PopupTitle = "Wait",
            ButtonText = "Claim",
            CouponCode = "MEGA",
            CouponDiscountType = DiscountType.FixedAmount,
            CouponDiscountValue = DiscountValidation.MaxFixedDiscountValue + 1m
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        Assert.Contains("Discount too large", exception.Message);

        await using var verifyContext = CreateContext(databaseName, tenantId);
        Assert.Equal(0, await verifyContext.SmartOffers.CountAsync());
    }

    [Fact]
    public async Task SyncPromotionsToMongo_WhenPublishFails_ThrowsInsteadOfSwallowing()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        SeedPromotions(context, tenantId, count: 2);
        await context.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<PromotionCreatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("projection unavailable"));

        var controller = CreatePromotionsController(context, tenantId, publishEndpoint, databaseName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SyncPromotionsToMongo());
    }

    [Fact]
    public async Task SyncCouponsToMongo_WhenPublishFails_ThrowsInsteadOfSwallowing()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = CreateContext(databaseName, tenantId);
        SeedCoupons(context, tenantId, count: 2);
        await context.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<CouponCreatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("projection unavailable"));

        var controller = CreateCouponsController(context, tenantId, publishEndpoint, databaseName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SyncCouponsToMongo(CancellationToken.None));
    }

    private static ApplicationDbContext CreateContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, _sharedRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static CouponsController CreateCouponsController(
        ApplicationDbContext context,
        Guid tenantId,
        Mock<IEventPublisher> publishEndpoint,
        string databaseName)
    {
        var syncScopeFactory = CreateSyncScopeFactory(tenantId, publishEndpoint.Object, databaseName);

        return new CouponsController(
            context,
            new TestTenantContext(tenantId),
            publishEndpoint.Object,
            CreateCouponValidationService(context),
            NullLogger<CouponsController>.Instance,
            syncScopeFactory)
        {
            ControllerContext = CreateControllerContext()
        };
    }

    private static PromotionsController CreatePromotionsController(
        ApplicationDbContext context,
        Guid tenantId,
        Mock<IEventPublisher> publishEndpoint,
        string databaseName)
    {
        var syncScopeFactory = CreateSyncScopeFactory(tenantId, publishEndpoint.Object, databaseName);

        return new PromotionsController(
            context,
            new TestTenantContext(tenantId),
            publishEndpoint.Object,
            NullLogger<PromotionsController>.Instance,
            syncScopeFactory)
        {
            ControllerContext = CreateControllerContext()
        };
    }

    private static AdminSmartOffersController CreateSmartOffersController(
        ApplicationDbContext context,
        Guid tenantId,
        Mock<IEventPublisher> publishEndpoint)
    {
        return new AdminSmartOffersController(
            context,
            new TestTenantContext(tenantId),
            publishEndpoint.Object,
            NullLogger<AdminSmartOffersController>.Instance)
        {
            ControllerContext = CreateControllerContext()
        };
    }

    private static ControllerContext CreateControllerContext()
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private static IServiceScopeFactory CreateSyncScopeFactory(Guid tenantId, IEventPublisher eventPublisher, string databaseName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(eventPublisher);
        services.AddSingleton<ITenantContext>(new TestTenantContext(tenantId));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, _sharedRoot));

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static Mock<IEventPublisher> CreateEventPublisherMock()
    {
        var mock = new Mock<IEventPublisher>();
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<CouponCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<CouponUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<CouponDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PromotionCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PromotionUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PromotionDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<SmartOfferCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<SmartOfferUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(endpoint => endpoint.PublishAsync(It.IsAny<SmartOfferDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ICouponValidationService CreateCouponValidationService(ApplicationDbContext context) =>
        new CouponValidationService(
            context,
            Mock.Of<IEventPublisher>(),
            NullLogger<CouponValidationService>.Instance);

    private static void SeedCoupons(ApplicationDbContext context, Guid tenantId, int count)
    {
        var utcNow = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
        {
            var coupon = new Coupon
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                IsActive = true,
                CreatedAt = utcNow.AddMinutes(index)
            };
            coupon.SetCode($"SAVE{index:D3}");
            context.Coupons.Add(coupon);
        }
    }

    private static void SeedPromotions(ApplicationDbContext context, Guid tenantId, int count)
    {
        var utcNow = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
        {
            context.Promotions.Add(new Promotion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Promo {index:D3}",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 5m,
                Scope = PromotionScope.Cart,
                StartDate = utcNow.AddDays(-1),
                EndDate = utcNow.AddDays(1),
                IsActive = true,
                Priority = index % 5,
                CreatedAt = utcNow.AddMinutes(index)
            });
        }
    }
}
