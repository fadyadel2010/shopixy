using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Integration;

public sealed partial class InventoryReservationLifecycleIntegrationTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Integrity_Return_KeepsCommittedStockOrRollsBackUnconsumedReleaseOnFailure(bool failAfterRelease)
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderAsync(db, repository, tenant, "COD", "COD", PaymentMethod.Cash,
            PaymentStatus.Pending, FulfillmentStatus.Delivered, OrderStatus.Delivered, 2);
        if (!failAfterRelease) await repository.CommitOrderReservationsAsync(tenant, scenario.OrderId);
        var events = new Moq.Mock<PointOfSale.Core.Interfaces.IEventPublisher>();
        events.SetReturnsDefault(Task.CompletedTask);
        if (failAfterRelease)
            events.Setup(e => e.PublishAsync(Moq.It.IsAny<PointOfSale.Core.Events.OrderReturnedEvent>(), Moq.It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SYNTHETIC_RETURN_FAILURE"));
        var service = new PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService(db, events.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService>.Instance, repository);
        if (failAfterRelease)
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkReturnedAsync(scenario.OrderId, FulfillmentRequestSource.Manual, "SYNTHETIC"));
        else
            Assert.True((await service.MarkReturnedAsync(scenario.OrderId, FulfillmentRequestSource.Manual, "SYNTHETIC")).Success);
        db.ChangeTracker.Clear();
        var stock = await db.ProductVariantStocks.SingleAsync();
        var reservation = await db.InventoryReservations.SingleAsync();
        Assert.Equal(failAfterRelease ? 9m : 7m, stock.Quantity);
        Assert.Equal(failAfterRelease ? 2m : 0m, stock.ReservedQuantity);
        Assert.Equal(failAfterRelease ? 0m : 2m, reservation.CommittedQuantity);
        Assert.Equal(0m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.ReturnedQuantity);
        Assert.Equal(failAfterRelease ? FulfillmentStatus.Delivered : FulfillmentStatus.Returned, (await db.Orders.SingleAsync()).FulfillmentStatus);
        Assert.Empty(await db.StockMovements.Where(m => m.InventoryEventType == InventoryEventType.StockReleased || m.InventoryEventType == InventoryEventType.StockIncreased).ToListAsync());
        Assert.Empty(await db.JournalEntries.ToListAsync());
    }

    [Fact]
    public async Task Integrity_UncollectedCodReturn_ReleasesUnconsumedReservationWithoutRestockingOrPostingMoney()
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderAsync(db, repository, tenant, "COD", "COD", PaymentMethod.Cash,
            PaymentStatus.Pending, FulfillmentStatus.Delivered, OrderStatus.Delivered, 2);
        var events = new Moq.Mock<PointOfSale.Core.Interfaces.IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var service = new PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService(db, events.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService>.Instance);
        Assert.True((await service.MarkReturnedAsync(scenario.OrderId, FulfillmentRequestSource.Manual, "SYNTHETIC physical return")).Success);
        Assert.True((await service.MarkReturnedAsync(scenario.OrderId, FulfillmentRequestSource.Manual, "SYNTHETIC replay")).Success);
        db.ChangeTracker.Clear();
        var stock = await db.ProductVariantStocks.SingleAsync();
        Assert.Equal(9m, stock.Quantity); Assert.Equal(0m, stock.ReservedQuantity);
        var reservation = await db.InventoryReservations.SingleAsync(); Assert.Equal(2m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.ReturnedQuantity);
        Assert.Equal(PaymentStatus.Pending, (await db.Orders.SingleAsync()).PaymentStatus);
        Assert.Empty(await db.JournalEntries.ToListAsync()); Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Integrity_ManualReturn_ProgressesOriginalShipmentWithoutCreatingReplacement()
    {
        var tenant = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"shipping-return-integrity-{Guid.NewGuid():N}").Options, new PointOfSale.Tests.Infrastructure.TestTenantContext(tenant));
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "SYNTHETIC-RETURN", Currency = "EGP", ShippingMethodId = Guid.NewGuid(),
            Status = OrderStatus.Delivered, FulfillmentStatus = FulfillmentStatus.Delivered, FulfillmentMode = FulfillmentMode.Manual, PaymentMethodCode = "COD" };
        var shipment = new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, CarrierCode = "manual", Status = ShipmentStatus.Delivered };
        db.Orders.Add(order); db.Shipments.Add(shipment); await db.SaveChangesAsync();
        var events = new Moq.Mock<PointOfSale.Core.Interfaces.IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var service = new PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService(db, events.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService>.Instance);
        Assert.True((await service.MarkReturnedAsync(order.Id, FulfillmentRequestSource.Manual, "SYNTHETIC physical return")).Success);
        Assert.True((await service.MarkReturnedAsync(order.Id, FulfillmentRequestSource.Manual, "SYNTHETIC replay")).Success);
        db.ChangeTracker.Clear();
        var stored = await db.Shipments.SingleAsync(); Assert.Equal(shipment.Id, stored.Id);
        Assert.Equal(ShipmentStatus.Returned, stored.Status); Assert.NotNull(stored.ReturnedAtUtc);
    }

    [Theory]
    [InlineData(FulfillmentMode.Carrier, FulfillmentRequestSource.Manual)]
    [InlineData(FulfillmentMode.Manual, FulfillmentRequestSource.Webhook)]
    public async Task Integrity_DeliveredReplayCannotBackfillProofFromUnauthorizedFulfillmentSource(FulfillmentMode mode, FulfillmentRequestSource source)
    {
        var tenant = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"shipping-proof-integrity-{Guid.NewGuid():N}").Options, new PointOfSale.Tests.Infrastructure.TestTenantContext(tenant));
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "SYNTHETIC-PROOF", Currency = "EGP",
            Status = OrderStatus.Delivered, FulfillmentStatus = FulfillmentStatus.Delivered, FulfillmentMode = mode,
            FulfillmentCarrierCode = mode == FulfillmentMode.Carrier ? "bosta" : null, PaymentMethodCode = "COD" };
        db.Orders.Add(order); await db.SaveChangesAsync();
        var publisher = new Moq.Mock<PointOfSale.Core.Interfaces.IEventPublisher>(Moq.MockBehavior.Strict);
        var service = new PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService(db, publisher.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService>.Instance);
        var result = await service.MarkDeliveredAsync(order.Id, source, "SUBSTITUTED", "https://example.invalid/substituted");
        Assert.False(result.Success);
        db.ChangeTracker.Clear();
        var stored = await db.Orders.SingleAsync(); Assert.Null(stored.DeliveryReference); Assert.Null(stored.ProofOfDeliveryUrl);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Integrity_LiveCodCollectionAfterCheckoutTtl_ConsumesOriginalPhysicalReservationOnce()
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var repository = CreateStockRepository(db);
        var scenario = await SeedReservedOrderAsync(db, repository, tenant, "COD", "COD", PaymentMethod.Cash,
            PaymentStatus.Pending, FulfillmentStatus.Delivered, OrderStatus.Delivered, 2);
        await MarkReservationsStaleAsync(db, scenario.OrderId, 90);
        var lifecycle = CreateLifecycleService(db, repository, CreateAccountingOutboxMock());
        Assert.True((await lifecycle.MarkOfflineCollectionDeliveredAsync(tenant, scenario.OrderId, "SYNTHETIC-COLLECTION")).Success);
        Assert.True((await lifecycle.MarkOfflineCollectionDeliveredAsync(tenant, scenario.OrderId, "SYNTHETIC-COLLECTION")).Success);
        db.ChangeTracker.Clear();
        var stock = await db.ProductVariantStocks.SingleAsync(s => s.VariantId == scenario.VariantId);
        var reservation = await db.InventoryReservations.SingleAsync(r => r.OrderId == scenario.OrderId);
        Assert.Equal(7m, stock.Quantity); Assert.Equal(0m, stock.ReservedQuantity);
        Assert.Equal(2m, reservation.CommittedQuantity);
        Assert.Single(await db.StockMovements.Where(m => m.ReferenceId == scenario.OrderId && m.InventoryEventType == InventoryEventType.StockDecreased).ToListAsync());
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Integrity_MixedOrEntirelyNonphysicalOrder_BindsAndAcceptsPaymentWithoutInventingStock(bool entirelyNonphysical)
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var (order, physical) = await SeedIntegrityOrderAsync(db, tenant, entirelyNonphysical);
        var repository = CreateStockRepository(db);
        if (!entirelyNonphysical)
            Assert.True(await repository.ReserveAsync(tenant, physical.VariantId!.Value, physical.WarehouseId, 2, order.Id, DateTime.UtcNow.AddMinutes(10)));
        await repository.BindOrderReservationsAsync(tenant, order.Id, order.Items.ToList());
        var result = await CreateLifecycleService(db, repository, CreateAccountingOutboxMock()).CaptureOnlinePaymentAsync(
            order.Id, "stripe", "synthetic-integrity", providerAmount: order.Total, providerCurrency: "EGP");
        Assert.True(result.Success, result.ErrorMessage);
        db.ChangeTracker.Clear();
        Assert.Equal(PaymentStatus.Collected, (await db.Orders.SingleAsync(o => o.Id == order.Id)).PaymentStatus);
        Assert.Equal(entirelyNonphysical ? 0 : 1, await db.InventoryReservations.CountAsync(r => r.OrderId == order.Id));
        Assert.Equal(entirelyNonphysical ? 9m : 7m, (await db.ProductVariantStocks.SingleAsync()).Quantity);
    }

    [Theory]
    [InlineData("foreign-order")][InlineData("wrong-warehouse")]
    public async Task Integrity_ReservationBindingRejectsCallerSubstitutedLineageBeforeMutation(string substitution)
    {
        var tenant = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection, tenant);
        var (order, physical) = await SeedIntegrityOrderAsync(db, tenant, false);
        var repository = CreateStockRepository(db);
        Assert.True(await repository.ReserveAsync(tenant, physical.VariantId!.Value, physical.WarehouseId, 2, order.Id, DateTime.UtcNow.AddMinutes(10)));
        var supplied = new OrderItem { Id = physical.Id, OrderId = substitution == "foreign-order" ? Guid.NewGuid() : order.Id,
            ProductId = physical.ProductId, VariantId = physical.VariantId, WarehouseId = substitution == "wrong-warehouse" ? Guid.NewGuid() : physical.WarehouseId, Quantity = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.BindOrderReservationsAsync(tenant, order.Id, [supplied]));
        db.ChangeTracker.Clear();
        Assert.Equal(Guid.Empty, (await db.InventoryReservations.SingleAsync()).OrderItemId);
        Assert.Equal(2m, (await db.ProductVariantStocks.SingleAsync()).ReservedQuantity);
    }

    private static async Task<(Order Order, OrderItem Physical)> SeedIntegrityOrderAsync(ApplicationDbContext db, Guid tenant, bool entirelyNonphysical)
    {
        var seed = await SeedInventoryAsync(db, tenant, 9);
        var otherProduct = new Product { Id = Guid.NewGuid(), TenantId = tenant, CategoryId = (await db.Products.SingleAsync()).CategoryId,
            Name = "Synthetic backorder", IsActive = true, TrackStock = true, AllowBackorder = true };
        var otherVariant = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenant, ProductId = otherProduct.Id, SKU = "SYNTHETIC-BACKORDER", Price = 100, IsActive = true };
        db.Products.Add(otherProduct); db.ProductVariants.Add(otherVariant);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"SYN-{Guid.NewGuid():N}", WarehouseId = seed.WarehouseId,
            Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Pending, PaymentMethodCode = "STRIPE", PaymentMethod = PaymentMethod.Card,
            FulfillmentMode = FulfillmentMode.Manual, Currency = "EGP", Subtotal = 400, Total = 400 };
        var physical = new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = seed.ProductId, VariantId = seed.VariantId,
            WarehouseId = seed.WarehouseId, SKU = "INV-LIFECYCLE-001", Quantity = 2, UnitPrice = 100, Total = 200, CostPrice = 60 };
        order.Items.Add(physical);
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = otherProduct.Id, VariantId = otherVariant.Id,
            WarehouseId = seed.WarehouseId, SKU = otherVariant.SKU, Quantity = 2, UnitPrice = 100, Total = 200 });
        // The new writer will capture this evidence before the first order insert.
        // Seed its wire shape directly so these regressions fail against old readers.
        order.PricingSnapshotJson = JsonSerializer.Serialize(new { inventoryReservation = new { version = 1, tenantId = tenant, orderId = order.Id,
            warehouseId = order.WarehouseId, lines = order.Items.Select(i => new { orderItemId = i.Id, i.ProductId, i.VariantId, i.WarehouseId, i.Quantity,
                requiresPhysicalReservation = !entirelyNonphysical && i.Id == physical.Id }) } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        db.Orders.Add(order); await db.SaveChangesAsync();
        return (order, physical);
    }
}
