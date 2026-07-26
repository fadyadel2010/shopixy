using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class StorefrontCouponServiceCustomerValidationTests
{
    [Fact]
    public async Task ValidateCouponAsync_EmptyGuidCustomer_IsRejectedBeforeCouponLookup()
    {
        var tenantId = Guid.NewGuid();

        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        var service = CreateService(context);

        // Test with Guid.Empty instead of null (compile-time enforcement now prevents null)
        var result = await service.ValidateCouponAsync(
            tenantId,
            "SAVE10",
            CreateCart(tenantId),
            Guid.Empty,
            "corr-guest",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(CouponErrorCodes.AuthenticationRequired, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateCouponAsync_MaxUsagePerCustomerReached_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        context.CouponUsages.Add(new CouponUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CouponId = couponId,
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            CouponCode = "SAVE10",
            DiscountAmount = 10m,
            OrderSubtotal = 100m,
            UsedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var couponRead = new Mock<ICouponReadRepository>();
        var couponDocument = CreateCouponDocument(tenantId, couponId, maxUsagePerCustomer: 1);
        couponRead
            .Setup(repository => repository.GetByCodeAsync(tenantId, "SAVE10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(couponDocument);

        context.Coupons.Add(CreateCouponEntity(couponDocument));
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.ValidateCouponAsync(
            tenantId,
            "SAVE10",
            CreateCart(tenantId),
            customerId,
            "corr-max",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(CouponErrorCodes.CustomerMaxExceeded, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateCouponAsync_FirstOrderOnly_IgnoresPendingOrders_ThenRejectsSuccessfulRepeatOrder()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        var couponRead = new Mock<ICouponReadRepository>();
        var couponDocument = CreateCouponDocument(tenantId, couponId, firstOrderOnly: true, code: "FIRST10");
        couponRead
            .Setup(repository => repository.GetByCodeAsync(tenantId, "FIRST10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(couponDocument);

        context.Coupons.Add(CreateCouponEntity(couponDocument));
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var cart = CreateCart(tenantId);

        var firstResult = await service.ValidateCouponAsync(
            tenantId,
            "FIRST10",
            cart,
            customerId,
            "corr-first-order",
            CancellationToken.None);

        Assert.True(firstResult.IsValid);

        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OnlineCustomerId = customerId,
            OrderNumber = "ORD-FIRST",
            Currency = "USD",
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var pendingOrderResult = await service.ValidateCouponAsync(
            tenantId,
            "FIRST10",
            cart,
            customerId,
            "corr-second-order",
            CancellationToken.None);

        Assert.True(pendingOrderResult.IsValid);

        var successfulOrder = await context.Orders.SingleAsync(order => order.OrderNumber == "ORD-FIRST");
        successfulOrder.Status = OrderStatus.Delivered;
        successfulOrder.PaymentStatus = PaymentStatus.Collected;
        await context.SaveChangesAsync();

        var successfulHistoryResult = await service.ValidateCouponAsync(
            tenantId,
            "FIRST10",
            cart,
            customerId,
            "corr-third-order",
            CancellationToken.None);

        Assert.False(successfulHistoryResult.IsValid);
        Assert.Equal(CouponErrorCodes.FirstOrderOnly, successfulHistoryResult.ErrorCode);
    }

    [Fact]
    public async Task ValidateCouponAsync_WhenApplicableCategoryDoesNotMatchCart_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var requiredCategoryId = Guid.NewGuid();
        var actualCategoryId = Guid.NewGuid();

        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        var couponRead = new Mock<ICouponReadRepository>();
        var couponDocument = CreateCouponDocument(
            tenantId,
            couponId,
            applicableCategoryIds: new List<Guid> { requiredCategoryId });
        couponRead
            .Setup(repository => repository.GetByCodeAsync(tenantId, "SAVE10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(couponDocument);

        context.Coupons.Add(CreateCouponEntity(couponDocument));
        await context.SaveChangesAsync();

        context.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Coupon Product",
            CategoryId = actualCategoryId,
            SalePrice = 100m,
            OnlinePrice = 100m,
            PurchasePrice = 50m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.ValidateCouponAsync(
            tenantId,
            "SAVE10",
            CreateCart(tenantId, productId),
            customerId,
            "corr-category-mismatch",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(CouponErrorCodes.NotApplicable, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateCouponAsync_WhenExcludedProductIsInCart_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var excludedProductId = Guid.NewGuid();

        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        var couponRead = new Mock<ICouponReadRepository>();
        var couponDocument = CreateCouponDocument(
            tenantId,
            couponId,
            excludedProductIds: new List<Guid> { excludedProductId });
        couponRead
            .Setup(repository => repository.GetByCodeAsync(tenantId, "SAVE10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(couponDocument);

        context.Coupons.Add(CreateCouponEntity(couponDocument));
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.ValidateCouponAsync(
            tenantId,
            "SAVE10",
            CreateCart(tenantId, excludedProductId),
            customerId,
            "corr-excluded-product",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(CouponErrorCodes.NotApplicable, result.ErrorCode);
    }

    [Fact]
    public async Task SaveChangesAsync_InvalidCouponConfiguration_IsRejectedBeforeStorefrontValidation()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        await using var context = CreateContext(Guid.NewGuid().ToString("N"), tenantId);
        var coupon = new Coupon
        {
            Id = couponId,
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            MaxDiscountAmount = -1m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        coupon.SetCode("SAVE10");

        context.Coupons.Add(coupon);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private static ApplicationDbContext CreateContext(string databaseName, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static StorefrontCouponService CreateService(ApplicationDbContext context)
    {
        return new StorefrontCouponService(
            CreateCouponValidationService(context),
            context);
    }

    private static ICouponValidationService CreateCouponValidationService(ApplicationDbContext context) =>
        new CouponValidationService(
            context,
            Mock.Of<IEventPublisher>(),
            NullLogger<CouponValidationService>.Instance);

    private static CartDto CreateCart(Guid tenantId, Guid? productId = null)
    {
        var cart = new CartDto
        {
            CartId = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = "customer",
            OwnerId = Guid.NewGuid().ToString("N"),
            Currency = "USD",
            LastUpdatedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Items = new List<CartItemDto>
            {
                new()
                {
                    ProductId = productId ?? Guid.NewGuid(),
                    SKU = "SKU-1",
                    Name = "Coupon Test Product",
                    Quantity = 1,
                    UnitPrice = 100m,
                    AddedAtUtc = DateTime.UtcNow
                }
            }
        };

        cart.RecalculateTotals();
        return cart;
    }

    private static CouponDocument CreateCouponDocument(
        Guid tenantId,
        Guid couponId,
        int? maxUsagePerCustomer = null,
        bool firstOrderOnly = false,
        string code = "SAVE10",
        List<Guid>? applicableCategoryIds = null,
        List<Guid>? excludedProductIds = null)
    {
        return new CouponDocument
        {
            TenantId = tenantId,
            CouponId = couponId,
            Code = code,
            DiscountType = "percentage",
            DiscountValue = 10m,
            CurrentUsage = 0,
            MaxUsagePerCustomer = maxUsagePerCustomer,
            FirstOrderOnly = firstOrderOnly,
            ApplicableCategoryIds = applicableCategoryIds,
            ExcludedProductIds = excludedProductIds,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            SyncedAtUtc = DateTime.UtcNow
        };
    }

    private static Coupon CreateCouponEntity(CouponDocument document)
    {
        var coupon = new Coupon
        {
            Id = document.CouponId,
            TenantId = document.TenantId,
            DiscountType = string.Equals(document.DiscountType, "fixed", StringComparison.OrdinalIgnoreCase)
                ? DiscountType.FixedAmount
                : DiscountType.Percentage,
            DiscountValue = document.DiscountValue,
            MinOrderAmount = document.MinOrderAmount,
            MaxDiscountAmount = document.MaxDiscountAmount,
            MaxUsage = document.MaxUsage,
            CurrentUsage = document.CurrentUsage,
            MaxUsagePerCustomer = document.MaxUsagePerCustomer,
            StartDate = document.StartDate,
            EndDate = document.EndDate,
            IsActive = document.IsActive,
            FirstOrderOnly = document.FirstOrderOnly,
            ApplicableProductIds = SerializeGuidCollection(document.ApplicableProductIds),
            ApplicableCategoryIds = SerializeGuidCollection(document.ApplicableCategoryIds),
            ExcludedProductIds = SerializeGuidCollection(document.ExcludedProductIds),
            Description = document.Description,
            CreatedAt = document.CreatedAtUtc,
            UpdatedAt = document.CreatedAtUtc
        };
        coupon.SetCode(document.Code);
        return coupon;
    }

    private static string? SerializeGuidCollection(ICollection<Guid>? values)
    {
        return values is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(values)
            : null;
    }
}
