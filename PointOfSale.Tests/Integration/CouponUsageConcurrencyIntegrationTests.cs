using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class CouponUsageConcurrencyIntegrationTests
{
    [Fact]
    public async Task ReserveCouponAsync_WithConcurrentOrders_OnlyMaxUsageReservationsSucceed()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;

        const int maxUsage = 5;
        const int requestCount = 12;
        var couponId = Guid.NewGuid();
        var attempts = Enumerable.Range(0, requestCount)
            .Select(index => new ReservationAttempt(
                Guid.NewGuid(),
                Guid.NewGuid(),
                $"reserve-max-{index:D2}"))
            .ToArray();

        await using (var seedContext = scope.CreateDbContext(tenantContext))
        {
            await SeedScenarioAsync(seedContext, tenantId, couponId, maxUsage, attempts);
            await seedContext.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = attempts
            .Select(attempt => RunReservationAttemptAsync(
                scope,
                tenantContext,
                tenantId,
                couponId,
                attempt,
                startGate.Task))
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(maxUsage, results.Count(result => result.Succeeded));
        Assert.Equal(requestCount - maxUsage, results.Count(result => !result.Succeeded));

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(maxUsage, await verifyContext.CouponReservations.CountAsync());
        Assert.Equal(0, await verifyContext.CouponUsages.CountAsync());
        Assert.Equal(
            0,
            await verifyContext.Coupons
                .Where(coupon => coupon.Id == couponId)
                .Select(coupon => coupon.CurrentUsage)
                .SingleAsync());
    }

    [Fact]
    public async Task ReserveCouponAsync_WhenSameOrderIsRetriedConcurrently_ReusesOneReservation()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;

        var couponId = Guid.NewGuid();
        var attempt = new ReservationAttempt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "reserve-idempotent");

        await using (var seedContext = scope.CreateDbContext(tenantContext))
        {
            await SeedScenarioAsync(seedContext, tenantId, couponId, maxUsage: 10, new[] { attempt });
            await seedContext.SaveChangesAsync();
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => RunReservationAttemptAsync(
                scope,
                tenantContext,
                tenantId,
                couponId,
                attempt,
                startGate.Task))
            .ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
        Assert.Single(results.Select(result => result.ReservationId).Distinct());

        await using var verifyContext = scope.CreateDbContext(tenantContext);
        Assert.Equal(1, await verifyContext.CouponReservations.CountAsync());
        Assert.Equal(
            CouponReservationStatus.Reserved,
            await verifyContext.CouponReservations
                .Where(reservation => reservation.OrderId == attempt.OrderId)
                .Select(reservation => reservation.Status)
                .SingleAsync());
    }

    private static async Task<ReservationAttemptResult> RunReservationAttemptAsync(
        TestDatabaseScope scope,
        TestTenantContext tenantContext,
        Guid tenantId,
        Guid couponId,
        ReservationAttempt attempt,
        Task startGate)
    {
        await startGate;

        await using var context = scope.CreateDbContext(tenantContext);
        var authority = CreateAuthority(context);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var reservation = await authority.ReserveCouponAsync(
                tenantId,
                couponId,
                "SAVE10",
                attempt.OrderId,
                attempt.CustomerId,
                new CouponValidationSnapshot
                {
                    OrderSubtotal = 100m,
                    AppliedDiscountAmount = 10m
                },
                attempt.CorrelationId,
                CancellationToken.None);

            await transaction.CommitAsync();
            return new ReservationAttemptResult(true, reservation.ReservationId.GetValueOrDefault(), null);
        }
        catch (Exception ex)
        {
            return new ReservationAttemptResult(false, Guid.Empty, ex.Message);
        }
    }

    private static CouponValidationService CreateAuthority(ApplicationDbContext context)
    {
        var publisher = new Mock<IEventPublisher>();
        publisher.SetReturnsDefault(Task.CompletedTask);

        return new CouponValidationService(
            context,
            publisher.Object,
            NullLogger<CouponValidationService>.Instance);
    }

    private static async Task SeedScenarioAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid couponId,
        int maxUsage,
        IReadOnlyCollection<ReservationAttempt> attempts)
    {
        var now = DateTime.UtcNow;

        context.Coupons.Add(new Coupon
        {
            Id = couponId,
            TenantId = tenantId,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            MaxUsage = maxUsage,
            IsActive = true,
            CreatedAt = now
        });
        context.Coupons.Local.Single(coupon => coupon.Id == couponId).SetCode("SAVE10");

        foreach (var attempt in attempts)
        {
            context.OnlineCustomers.Add(new OnlineCustomer
            {
                Id = attempt.CustomerId,
                TenantId = tenantId,
                Email = $"customer-{attempt.CustomerId:N}@example.com",
                NormalizedEmail = $"customer-{attempt.CustomerId:N}@example.com",
                DisplayName = "Concurrent Customer",
                Provider = AuthProvider.Local,
                IsActive = true,
                EmailVerified = true,
                CreatedAtUtc = now
            });

            context.Orders.Add(new Order
            {
                Id = attempt.OrderId,
                TenantId = tenantId,
                OnlineCustomerId = attempt.CustomerId,
                OrderNumber = $"ORD-{attempt.OrderId.ToString("N")[..8]}",
                Currency = "USD",
                Subtotal = 100m,
                Total = 90m,
                DiscountAmount = 10m,
                CouponId = couponId,
                CouponCode = "SAVE10",
                PaymentMethodCode = "STRIPE",
                Status = OrderStatus.Pending,
                CreatedAt = now
            });
        }
    }

    private readonly record struct ReservationAttempt(
        Guid OrderId,
        Guid CustomerId,
        string CorrelationId);

    private readonly record struct ReservationAttemptResult(
        bool Succeeded,
        Guid ReservationId,
        string? ErrorMessage);
}
