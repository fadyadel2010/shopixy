using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.RegularExpressions;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.Services.Coupons;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class CouponUsageServiceCqrsConsistencyTests
{
    [Fact]
    public async Task CartValidation_EqualsCheckoutValidation_ForValidCoupon()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        await SeedCouponAsync(context, tenantId, couponId);
        await SeedCustomerAsync(context, tenantId, customerId);

        var authority = CreateAuthority(context);
        var storefront = new StorefrontCouponService(authority, context);
        var checkout = new CouponUsageService(authority);
        var cart = CreateCart(tenantId, 100m);

        var cartDecision = await storefront.ValidateCouponAsync(
            tenantId,
            "SAVE10",
            cart,
            customerId,
            "cart-parity",
            CancellationToken.None);

        var checkoutDecision = await checkout.ValidateCouponForCheckoutAsync(
            tenantId,
            couponId,
            "SAVE10",
            customerId,
            100m,
            "checkout-parity",
            cancellationToken: CancellationToken.None);

        Assert.True(cartDecision.IsValid);
        Assert.True(checkoutDecision.IsValid);
        Assert.Equal(cartDecision.CouponId, checkoutDecision.CouponId);
        Assert.Equal(cartDecision.DiscountAmount, checkoutDecision.CalculatedDiscountAmount);
    }

    [Fact]
    public async Task CartValidation_EqualsCheckoutValidation_ForRejectedCoupon()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        await SeedCouponAsync(context, tenantId, couponId);
        await SeedCustomerAsync(context, tenantId, customerId);
        var usedOrderId = Guid.NewGuid();
        await SeedOrderAsync(context, tenantId, usedOrderId, customerId, couponId, "SAVE10", 100m, 10m);
        context.CouponUsages.Add(new CouponUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CouponId = couponId,
            OrderId = usedOrderId,
            CustomerId = customerId,
            CouponCode = "SAVE10",
            DiscountAmount = 10m,
            OrderSubtotal = 100m,
            UsedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var authority = CreateAuthority(context);
        var storefront = new StorefrontCouponService(authority, context);
        var checkout = new CouponUsageService(authority);
        var cart = CreateCart(tenantId, 100m);

        var cartDecision = await storefront.ValidateCouponAsync(
            tenantId,
            "SAVE10",
            cart,
            customerId,
            "cart-reject",
            CancellationToken.None);

        var checkoutDecision = await checkout.ValidateCouponForCheckoutAsync(
            tenantId,
            couponId,
            "SAVE10",
            customerId,
            100m,
            "checkout-reject",
            cancellationToken: CancellationToken.None);

        Assert.False(cartDecision.IsValid);
        Assert.False(checkoutDecision.IsValid);
        Assert.Equal(CouponErrorCodes.CustomerMaxExceeded, cartDecision.ErrorCode);
        Assert.Equal(CouponErrorCodes.CustomerMaxExceeded, checkoutDecision.ErrorCode);
    }

    [Fact]
    public async Task ValidateCouponForCheckoutAsync_UsesUnifiedSqlAuthority_ForPerCustomerLimit()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        await SeedCouponAsync(context, tenantId, couponId);
        await SeedCustomerAsync(context, tenantId, customerId);
        var usedOrderId = Guid.NewGuid();
        await SeedOrderAsync(context, tenantId, usedOrderId, customerId, couponId, "SAVE10", 100m, 10m);
        context.CouponUsages.Add(new CouponUsage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CouponId = couponId,
            OrderId = usedOrderId,
            CustomerId = customerId,
            CouponCode = "SAVE10",
            DiscountAmount = 10m,
            OrderSubtotal = 100m,
            UsedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateAdapter(context);

        var result = await service.ValidateCouponForCheckoutAsync(
            tenantId,
            couponId,
            "SAVE10",
            customerId,
            100m,
            "cqrs-limit",
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(CouponErrorCodes.CustomerMaxExceeded, result.ErrorCode);
    }

    [Fact]
    public async Task ReserveThenConfirmCouponUsage_OnlyCountsConfirmedUsage_AfterPaymentSuccess()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using (var context = await CreateDbContextAsync(connection, tenantId))
        {
            await SeedCouponAsync(context, tenantId, couponId);
            await SeedCustomerAsync(context, tenantId, customerId);
            await SeedOrderAsync(context, tenantId, orderId, customerId, couponId, "SAVE10", 100m, 10m);

            var authority = CreateAuthority(context);
            await using var transaction = await context.Database.BeginTransactionAsync();

            var reservation = await authority.ReserveCouponAsync(
                tenantId,
                couponId,
                "SAVE10",
                orderId,
                customerId,
                new CouponValidationSnapshot
                {
                    OrderSubtotal = 100m,
                    AppliedDiscountAmount = 10m
                },
                "reserve-confirm",
                CancellationToken.None);

            await transaction.CommitAsync();

            Assert.True(reservation.HasCoupon);
            Assert.True(reservation.WasCreated);
            Assert.Equal(0, await context.CouponUsages.CountAsync());
            Assert.Equal(
                0,
                await context.Coupons
                    .Where(coupon => coupon.Id == couponId)
                    .Select(coupon => coupon.CurrentUsage)
                    .SingleAsync());
        }

        await using (var confirmContext = await CreateDbContextAsync(connection, tenantId))
        {
            var authority = CreateAuthority(confirmContext);
            await using var transaction = await confirmContext.Database.BeginTransactionAsync();

            var confirmation = await authority.ConfirmCouponUsageAsync(
                tenantId,
                orderId,
                "payment-success",
                CancellationToken.None);

            await transaction.CommitAsync();

            Assert.True(confirmation.HasCoupon);
            Assert.True(confirmation.WasConfirmed);
            Assert.Equal(1, await confirmContext.CouponUsages.CountAsync());
            Assert.Equal(
                1,
                await confirmContext.Coupons
                    .Where(coupon => coupon.Id == couponId)
                    .Select(coupon => coupon.CurrentUsage)
                    .SingleAsync());
            Assert.Equal(
                CouponReservationStatus.Confirmed,
                await confirmContext.CouponReservations
                    .Where(reservation => reservation.OrderId == orderId)
                    .Select(reservation => reservation.Status)
                    .SingleAsync());
        }
    }

    [Fact]
    public async Task ReleaseCouponReservation_DoesNotBurnConfirmedUsageCount()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        await SeedCouponAsync(context, tenantId, couponId);
        await SeedCustomerAsync(context, tenantId, customerId);
        await SeedOrderAsync(context, tenantId, orderId, customerId, couponId, "SAVE10", 100m, 10m);

        var authority = CreateAuthority(context);

        await using (var reserveTransaction = await context.Database.BeginTransactionAsync())
        {
            await authority.ReserveCouponAsync(
                tenantId,
                couponId,
                "SAVE10",
                orderId,
                customerId,
                new CouponValidationSnapshot
                {
                    OrderSubtotal = 100m,
                    AppliedDiscountAmount = 10m
                },
                "reserve-release",
                CancellationToken.None);

            await reserveTransaction.CommitAsync();
        }

        await using (var releaseTransaction = await context.Database.BeginTransactionAsync())
        {
            var release = await authority.ReleaseCouponReservationAsync(
                tenantId,
                orderId,
                "payment-failed",
                "payment_failed",
                CancellationToken.None);

            await releaseTransaction.CommitAsync();

            Assert.True(release.WasReleased);
        }

        context.ChangeTracker.Clear();

        Assert.Equal(0, await context.CouponUsages.CountAsync());
        Assert.Equal(
            0,
            await context.Coupons
                .Where(coupon => coupon.Id == couponId)
                .Select(coupon => coupon.CurrentUsage)
                .SingleAsync());
        Assert.Equal(
            CouponReservationStatus.Released,
            await context.CouponReservations
                .Where(reservation => reservation.OrderId == orderId)
                .Select(reservation => reservation.Status)
                .SingleAsync());
    }

    [Fact]
    public async Task ConsumeCouponAsync_ThrowsGuidanceException_BecauseEarlyConsumptionWasRemoved()
    {
        var tenantId = Guid.NewGuid();
        var couponId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var context = await CreateDbContextAsync(connection, tenantId);
        var service = CreateAdapter(context);

        var exception = await Assert.ThrowsAsync<CouponAuthorityException>(() => service.ConsumeCouponAsync(
            tenantId,
            couponId,
            "SAVE10",
            orderId,
            customerId,
            10m,
            100m,
            "legacy-consume",
            CancellationToken.None));

        Assert.Equal("COUPON_EARLY_CONSUMPTION_REMOVED", exception.ErrorCode);
    }

    private static CouponUsageService CreateAdapter(ApplicationDbContext context) =>
        new(CreateAuthority(context));

    private static CouponValidationService CreateAuthority(ApplicationDbContext context)
    {
        var publisher = new Mock<IEventPublisher>();
        publisher.SetReturnsDefault(Task.CompletedTask);

        return new CouponValidationService(
            context,
            publisher.Object,
            NullLogger<CouponValidationService>.Instance);
    }

    private static async Task<ApplicationDbContext> CreateDbContextAsync(SqliteConnection connection, Guid tenantId)
    {
        RegisterSqliteFunctions(connection);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }

    private static async Task SeedCouponAsync(ApplicationDbContext context, Guid tenantId, Guid couponId)
    {
        context.Coupons.Add(new Coupon
        {
            Id = couponId,
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            MaxUsage = 10,
            MaxUsagePerCustomer = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        context.Coupons.Local.Single(coupon => coupon.Id == couponId).SetCode("SAVE10");
        await context.SaveChangesAsync();
    }

    private static async Task SeedCustomerAsync(ApplicationDbContext context, Guid tenantId, Guid customerId)
    {
        context.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = customerId,
            TenantId = tenantId,
            Email = $"customer-{customerId:N}@example.com",
            NormalizedEmail = $"customer-{customerId:N}@example.com",
            DisplayName = "Coupon Customer",
            Provider = AuthProvider.Local,
            IsActive = true,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    private static async Task SeedOrderAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid orderId,
        Guid customerId,
        Guid couponId,
        string couponCode,
        decimal subtotal,
        decimal discountAmount)
    {
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OnlineCustomerId = customerId,
            OrderNumber = $"ORD-{orderId.ToString("N")[..8]}",
            Currency = "USD",
            Subtotal = subtotal,
            Total = subtotal - discountAmount,
            DiscountAmount = discountAmount,
            CouponId = couponId,
            CouponCode = couponCode,
            PaymentMethodCode = "STRIPE",
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    private static CartDto CreateCart(Guid tenantId, decimal subtotal)
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
                    ProductId = Guid.NewGuid(),
                    SKU = "SKU-1",
                    Name = "Coupon Cart Item",
                    Quantity = 1,
                    UnitPrice = subtotal,
                    AddedAtUtc = DateTime.UtcNow
                }
            }
        };

        cart.RecalculateTotals();
        return cart;
    }
}
