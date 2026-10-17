using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Policies;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Observability;

namespace PointOfSale.Tests.Integration;

public sealed partial class InventoryReservationLifecycleIntegrationTests
{
    [Theory]
    [InlineData("COD", PaymentStatus.Pending)]
    [InlineData(" cod ", PaymentStatus.Pending)]
    [InlineData("", PaymentStatus.CashOnDelivery)]
    public async Task LiveCod_ExpiredReservationDoesNotExpirePaymentOrReleaseStock_EvenThroughLifecycle(string code, PaymentStatus payment)
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderAsync(db, repository, tenant, code, "Localized delivery payment", PaymentMethod.Cash,
            payment, FulfillmentStatus.Created, OrderStatus.Pending, 2);
        await MarkReservationsStaleAsync(db, scenario.OrderId, 90);
        var options = Options.Create(new InventoryReservationMonitoringOptions
        { StaleReservationThresholdMinutes = 15, ActivePaymentGraceMinutes = 5, CleanupBatchSize = 10 });
        var coupon = CreateCouponValidationMock(); var outbox = CreateAccountingOutboxMock();
        var lifecycle = CreateLifecycleService(db, repository, outbox, coupon);
        var job = new InventoryReservationCleanupJob(db, repository, CreateMetricsService(db, options), coupon.Object, options,
            NullLogger<InventoryReservationCleanupJob>.Instance, lifecycle);
        await job.ExecuteAsync();
        await job.ExecuteAsync();
        var result = await lifecycle.MarkPaymentExpiredAsync(tenant, scenario.OrderId, "Synthetic TTL regression");
        Assert.True(result.IsSuccess); Assert.False(result.StateChanged);
        await using var verify = await CreateDbContextAsync(connection, tenant);
        var order = await verify.Orders.SingleAsync(o => o.Id == scenario.OrderId);
        Assert.Equal(payment, order.PaymentStatus); Assert.Equal(OrderStatus.Pending, order.Status); Assert.False(order.IsPaid);
        var stock = await verify.ProductVariantStocks.SingleAsync(s => s.VariantId == scenario.VariantId);
        Assert.Equal(9m, stock.Quantity); Assert.Equal(2m, stock.ReservedQuantity);
        var reservation = await verify.InventoryReservations.SingleAsync(r => r.OrderId == order.Id);
        Assert.Equal(2m, reservation.RemainingReservedQuantity); Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity); Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
        coupon.Verify(c => c.ReleaseCouponReservationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public void TerminalCod_StillAllowsExplicitReservationRelease(OrderStatus status)
        => Assert.False(CodReservationExpiryPolicy.ProtectsLiveOrder("COD", PaymentStatus.Pending, status));
}
