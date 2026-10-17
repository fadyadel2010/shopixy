using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.Tests.Integration;

public sealed partial class InventoryReservationLifecycleIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Postgres_CodDomainIsolation_BeforeAndAfterTtl_AndDeliveredUnpaid_WithCompetingCleanup(bool expired, bool delivered)
    {
        using var scope = PointOfSale.Tests.Shipping.ShippingIsolatedPostgres.OpenManagedTestScope();
        var tenant = Guid.NewGuid();
        await using var seed = scope.CreateDbContext(tenant);
        var scenario = await SeedReservedOrderAsync(seed, CreateStockRepository(seed), tenant, "COD", "Synthetic COD", PaymentMethod.Cash,
            PaymentStatus.Pending, delivered ? FulfillmentStatus.Delivered : FulfillmentStatus.Created,
            delivered ? OrderStatus.Delivered : OrderStatus.Pending, 2);
        if (expired) await MarkReservationsStaleAsync(seed, scenario.OrderId, 90);
        var original = await seed.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == scenario.OrderId);
        if (!expired) Assert.True(original.ExpiresAtUtc > DateTime.UtcNow);

        // Independent real database contexts compete for the same order lock.
        // No retained order is reused or repaired by this test.
        await Task.WhenAll(Cleanup(), Cleanup());
        await using var verify = scope.CreateDbContext(tenant);
        var reservation = await verify.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == scenario.OrderId);
        Assert.Equal(original.ReservationEventId, reservation.ReservationEventId);
        Assert.Equal(original.LastMutationEventId, reservation.LastMutationEventId);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(2m, reservation.RemainingReservedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.CommittedQuantity);
        var stock = await verify.ProductVariantStocks.AsNoTracking().SingleAsync(s => s.VariantId == scenario.VariantId);
        Assert.Equal(9m, stock.Quantity); Assert.Equal(2m, stock.ReservedQuantity);
        var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == scenario.OrderId);
        Assert.Equal(delivered ? OrderStatus.Delivered : OrderStatus.Pending, order.Status);
        Assert.Equal(delivered ? FulfillmentStatus.Delivered : FulfillmentStatus.Created, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); Assert.False(order.IsPaid);
        Assert.Empty(await verify.InventoryEventInbox.ToListAsync());
        Assert.Empty(await verify.AccountingOutboxMessages.ToListAsync()); Assert.Empty(await verify.JournalEntries.ToListAsync());

        async Task Cleanup()
        {
            await using var db = scope.CreateDbContext(tenant);
            var repository = CreateStockRepository(db);
            var options = Options.Create(new InventoryReservationMonitoringOptions
                { StaleReservationThresholdMinutes = 15, ActivePaymentGraceMinutes = 5, CleanupBatchSize = 10 });
            var coupon = CreateCouponValidationMock();
            var lifecycle = CreateLifecycleService(db, repository, CreateAccountingOutboxMock(), coupon, requireRowLevelLocking: true);
            var cleanup = new InventoryReservationCleanupJob(db, repository, CreateMetricsService(db, options), coupon.Object, options,
                NullLogger<InventoryReservationCleanupJob>.Instance, lifecycle);
            var commerce = new CommerceHubInventoryReconciliationJob(db,
                new InventoryConsistencyService(db, NullLogger<InventoryConsistencyService>.Instance),
                NullLogger<CommerceHubInventoryReconciliationJob>.Instance);
            await cleanup.ExecuteAsync();
            Assert.Equal(0, (await commerce.ExecuteAsync()).ExpiredReservationsReleasedCount);
            var result = await lifecycle.MarkPaymentExpiredAsync(tenant, scenario.OrderId, "Synthetic competing expiry");
            Assert.True(result.IsSuccess, result.ErrorMessage); Assert.False(result.StateChanged);
            coupon.Verify(c => c.ReleaseCouponReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task Postgres_OnlinePaymentExpiry_StillReleasesExactlyOnce_OnlyThroughItsOwnLifecycle()
    {
        using var scope = PointOfSale.Tests.Shipping.ShippingIsolatedPostgres.OpenManagedTestScope();
        var tenant = Guid.NewGuid(); await using var db = scope.CreateDbContext(tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderAsync(db, repository, tenant, "STRIPE", "Synthetic online", PaymentMethod.Card,
            PaymentStatus.Pending, FulfillmentStatus.Created, OrderStatus.Pending, 2);
        await MarkReservationsStaleAsync(db, scenario.OrderId, 90);
        var options = Options.Create(new InventoryReservationMonitoringOptions
            { StaleReservationThresholdMinutes = 15, ActivePaymentGraceMinutes = 5, CleanupBatchSize = 10 });
        var coupon = CreateCouponValidationMock();
        var lifecycle = CreateLifecycleService(db, repository, CreateAccountingOutboxMock(), coupon, requireRowLevelLocking: true);
        var commerce = new CommerceHubInventoryReconciliationJob(db,
            new InventoryConsistencyService(db, NullLogger<InventoryConsistencyService>.Instance), NullLogger<CommerceHubInventoryReconciliationJob>.Instance);
        Assert.Equal(0, (await commerce.ExecuteAsync()).ExpiredReservationsReleasedCount);
        var cleanup = new InventoryReservationCleanupJob(db, repository, CreateMetricsService(db, options), coupon.Object, options,
            NullLogger<InventoryReservationCleanupJob>.Instance, lifecycle);
        await cleanup.ExecuteAsync(); await cleanup.ExecuteAsync();
        await using var verify = scope.CreateDbContext(tenant);
        var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == scenario.OrderId);
        Assert.Equal(PaymentStatus.Expired, order.PaymentStatus); Assert.Equal(OrderStatus.Cancelled, order.Status);
        var reservation = await verify.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == scenario.OrderId);
        Assert.Equal(InventoryReservationStatus.Released, reservation.Status);
        Assert.Equal(2m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Equal(0m, (await verify.ProductVariantStocks.SingleAsync(s => s.VariantId == scenario.VariantId)).ReservedQuantity);
        var releases = await verify.StockMovements.Where(m => m.ReferenceId == scenario.OrderId && m.InventoryEventType == InventoryEventType.StockReleased).ToListAsync();
        Assert.Single(releases);
        Assert.Empty(await verify.InventoryEventInbox.ToListAsync()); // No CommerceHub receipt for a Storefront order.
        Assert.Empty(await verify.JournalEntries.ToListAsync());
    }

    [Theory]
    [InlineData("COD")][InlineData("STRIPE")]
    public async Task Postgres_CommerceHubSweep_CannotReleaseStorefrontReservationOrCreateExpiryReceipt(string code)
    {
        using var scope = PointOfSale.Tests.Shipping.ShippingIsolatedPostgres.OpenManagedTestScope();
        var tenant = Guid.NewGuid(); await using var db = scope.CreateDbContext(tenant);
        var scenario = await SeedReservedOrderAsync(db, CreateStockRepository(db), tenant, code, "Synthetic Storefront payment", PaymentMethod.Cash,
            PaymentStatus.Pending, FulfillmentStatus.Created, OrderStatus.Pending, 2);
        await MarkReservationsStaleAsync(db, scenario.OrderId, 90);
        var consistency = new InventoryConsistencyService(db, NullLogger<InventoryConsistencyService>.Instance);
        var job = new CommerceHubInventoryReconciliationJob(db, consistency, NullLogger<CommerceHubInventoryReconciliationJob>.Instance);
        Assert.Equal(0, (await job.ExecuteAsync()).ExpiredReservationsReleasedCount);
        Assert.Equal(0, (await job.ExecuteAsync()).ExpiredReservationsReleasedCount);
        await using var verify = scope.CreateDbContext(tenant);
        var reservation = await verify.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == scenario.OrderId);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status); Assert.Equal(2m, reservation.RemainingReservedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Equal(2m, (await verify.ProductVariantStocks.AsNoTracking().SingleAsync()).ReservedQuantity);
        Assert.Empty(await verify.InventoryEventInbox.ToListAsync());
        var order = await verify.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatus.Pending, order.Status); Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Empty(await verify.AccountingOutboxMessages.ToListAsync()); Assert.Empty(await verify.JournalEntries.ToListAsync());
    }

    [Theory]
    [InlineData("COD", PaymentStatus.Pending)]
    [InlineData(" cod ", PaymentStatus.Pending)]
    [InlineData("", PaymentStatus.CashOnDelivery)]
    public async Task Postgres_LiveCodTtl_RepeatedCleanupAndLockedLifecycle_PreserveInventoryAndMoney(string code, PaymentStatus payment)
    {
        // Runner supplies the proven disposable cluster and test database prefix.
        // TestDatabaseScope creates a new migrated database, never merchant tables.
        using var scope = PointOfSale.Tests.Shipping.ShippingIsolatedPostgres.OpenManagedTestScope();
        var tenant = Guid.NewGuid();
        await using var db = scope.CreateDbContext(tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderAsync(db, repository, tenant, code, "Localized delivery payment", PaymentMethod.Cash,
            payment, FulfillmentStatus.Created, OrderStatus.Pending, 2);
        await MarkReservationsStaleAsync(db, scenario.OrderId, 90);
        var options = Options.Create(new InventoryReservationMonitoringOptions
        { StaleReservationThresholdMinutes = 15, ActivePaymentGraceMinutes = 5, CleanupBatchSize = 10 });
        var coupon = CreateCouponValidationMock(); var outbox = CreateAccountingOutboxMock();
        var lifecycle = CreateLifecycleService(db, repository, outbox, coupon, requireRowLevelLocking: true);
        var job = new InventoryReservationCleanupJob(db, repository, CreateMetricsService(db, options), coupon.Object, options,
            NullLogger<InventoryReservationCleanupJob>.Instance, lifecycle);
        await job.ExecuteAsync(); await job.ExecuteAsync();
        var result = await lifecycle.MarkPaymentExpiredAsync(tenant, scenario.OrderId, "Synthetic PostgreSQL TTL regression");
        Assert.True(result.IsSuccess, result.ErrorMessage); Assert.False(result.StateChanged);
        await using var verify = scope.CreateDbContext(tenant);
        var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == scenario.OrderId);
        Assert.Equal(payment, order.PaymentStatus); Assert.Equal(OrderStatus.Pending, order.Status); Assert.False(order.IsPaid);
        var stock = await verify.ProductVariantStocks.AsNoTracking().SingleAsync(s => s.VariantId == scenario.VariantId);
        Assert.Equal(9m, stock.Quantity); Assert.Equal(2m, stock.ReservedQuantity);
        var reservation = await verify.InventoryReservations.AsNoTracking().SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        Assert.Equal(2m, reservation.RemainingReservedQuantity); Assert.Equal(0m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Empty(await verify.AccountingOutboxMessages.ToListAsync()); Assert.Empty(await verify.JournalEntries.ToListAsync());
        coupon.Verify(c => c.ReleaseCouponReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Moq.Times.Never);
    }
}
