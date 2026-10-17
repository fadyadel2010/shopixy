using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Integration;

public sealed partial class InventoryReservationLifecycleIntegrationTests
{
    [Theory]
    [InlineData("COD", PaymentStatus.Pending, PaymentIntentStatus.Skipped, OrderStatus.Pending)]
    [InlineData("STRIPE", PaymentStatus.Authorized, PaymentIntentStatus.Authorized, OrderStatus.Confirmed)]
    [InlineData("STRIPE", PaymentStatus.Paid, PaymentIntentStatus.Paid, OrderStatus.Confirmed)]
    [InlineData("STRIPE", PaymentStatus.Pending, PaymentIntentStatus.Paid, OrderStatus.Pending)]
    public async Task Phase5_TwoDayOldReservation_CannotOverrideLiveCodConfirmedAuthorizationOrPaidEvidence(
        string code, PaymentStatus payment, PaymentIntentStatus intent, OrderStatus status)
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderWithPaymentIntentAsync(db, repository, tenant, code, "Synthetic policy",
            PaymentMethod.Card, PaymentProvider.Stripe, intent, payment, FulfillmentStatus.Created, status, 2);
        await MarkReservationsStaleAsync(db, scenario.OrderId, 2 * 24 * 60);
        var options = Options.Create(new InventoryReservationMonitoringOptions
            {StaleReservationThresholdMinutes=15,ActivePaymentGraceMinutes=5,CleanupBatchSize=10});
        var coupon = CreateCouponValidationMock();
        var lifecycle = CreateLifecycleService(db, repository, CreateAccountingOutboxMock(), coupon);
        var job = new InventoryReservationCleanupJob(db, repository, CreateMetricsService(db, options), coupon.Object,
            options, NullLogger<InventoryReservationCleanupJob>.Instance, lifecycle);
        await job.ExecuteAsync();
        await job.ExecuteAsync();
        db.ChangeTracker.Clear();
        var order = await db.Orders.SingleAsync(o=>o.Id==scenario.OrderId);
        var reservation = await db.InventoryReservations.SingleAsync(r=>r.OrderId==scenario.OrderId);
        Assert.Equal(status, order.Status);
        Assert.Equal(payment, order.PaymentStatus);
        Assert.Null(order.CancelledAt);
        Assert.Equal(2m, reservation.RemainingReservedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity);
        Assert.Equal(2m, (await db.ProductVariantStocks.SingleAsync(s=>s.VariantId==scenario.VariantId)).ReservedQuantity);
    }
}
