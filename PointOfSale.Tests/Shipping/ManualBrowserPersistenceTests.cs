using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Shipping;

public sealed class ManualBrowserPersistenceTests
{
    [Fact]
    public async Task ActualArabicMobileBrowserOrder_DeliveredThroughNormalStaffApi_RemainsUncollectedWithPublishedProjection()
    {
        var connection = await ShippingIsolatedPostgres.VerifyAsync("SHIPPING_MANUAL_TEST_CONNECTION", "shipping_manual_e2e");
        var tenant = new TenantContext(); tenant.SetTenantId(ManualShippingRuntimeFixtureTests.TenantId);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, tenant);
        var id = Guid.Parse("81ba4acb-7375-4d67-a91f-f1105beee1c2");
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal(OrderStatus.Delivered, order.Status); Assert.Equal(FulfillmentStatus.Delivered, order.FulfillmentStatus);
        Assert.Equal(FulfillmentMode.Manual, order.FulfillmentMode); Assert.Equal("COD", order.PaymentMethodCode);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); Assert.False(order.IsPaid);
        Assert.Equal("USD", order.Currency); Assert.Equal(40m, order.ShippingCost); Assert.Equal(140m, order.Total);
        Assert.Equal("80 Synthetic Arabic Delivery Street", order.ShippingStreet);
        Assert.Equal("https://synthetic.example.invalid/browser-local-proof", order.ProofOfDeliveryUrl);
        var shipment = Assert.Single(await db.Shipments.AsNoTracking().Where(s => s.OrderId == id).ToListAsync());
        Assert.Equal(Guid.Parse("cd473f4a-d755-438e-8669-389cd02f6a89"), shipment.Id);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status); Assert.Equal("manual", shipment.CarrierCode);
        Assert.Equal(40m, shipment.ShippingCost); Assert.Equal("SYNTHETIC-BROWSER-LOCAL", shipment.TrackingNumber);
        Assert.Equal(0, shipment.ExternalRegistrationAttemptCount); Assert.False(shipment.CodCollected);
        // Delivery did not collect cash or manufacture payment-driven stock/accounting history.
        var reservation = Assert.Single(await db.InventoryReservations.AsNoTracking().Where(r => r.OrderId == id).ToListAsync());
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status); Assert.Equal(1m, reservation.RemainingReservedQuantity);
        Assert.Equal(0m, reservation.CommittedQuantity); Assert.Equal(0m, reservation.ReleasedQuantity);
        Assert.Empty(await db.AccountingOutboxMessages.Where(m => m.TenantId == order.TenantId && m.BusinessEntityId == id).ToListAsync());
        Assert.Empty(await db.JournalEntries.Where(j => j.TenantId == order.TenantId && j.SourceId == id).ToListAsync());
        var messages = await db.MarketplaceEventOutboxMessages.AsNoTracking().Where(m => m.TenantId == order.TenantId && m.EntityId == id &&
            (m.EventType == "OrderCreatedEvent" || m.EventType == "OrderDeliveredEvent")).ToListAsync();
        Assert.Equal(2, messages.Count); Assert.All(messages, m => { Assert.NotNull(m.PublishedAtUtc); Assert.False(m.IsFailed); });
        var mongoConnection = Environment.GetEnvironmentVariable("SHIPPING_MANUAL_MONGO_CONNECTION")
            ?? throw new InvalidOperationException("Explicit isolated Mongo connection required.");
        var projection = await new MongoClient(mongoConnection).GetDatabase(ManualShippingRuntimeFixtureTests.MongoDatabase)
            .GetCollection<OrderDocument>("orders").Find(o => o.OrderId == id && o.TenantId == order.TenantId).SingleAsync();
        Assert.Equal("Delivered", projection.Status); Assert.Equal("Delivered", projection.ShippingStatus);
        Assert.Equal("COD", projection.PaymentMethodCode); Assert.Equal("Pending", projection.PaymentStatus);
        Assert.Equal(40m, projection.ShippingCost); Assert.Equal(140m, projection.Total); Assert.Equal("USD", projection.Currency);
        Assert.Equal(order.ShippingStreet, projection.ShippingAddress!.Street);
    }

    [Theory]
    // Retain the failed first browser run exactly as persisted, not "repair"
    // its payment/inventory history to make a certificate green.
    [InlineData("7d3a2b78-0661-4cb2-b7e9-9a60deb15164", "20 Synthetic Street", 25, 125, PaymentStatus.Expired, InventoryReservationStatus.Released)]
    [InlineData("bf6285ad-f3de-411b-9de5-00673ef54913", "40 Synthetic Arabic Street", 40, 140, PaymentStatus.Pending, InventoryReservationStatus.Released)]
    [InlineData("c1a330e0-05b5-4dca-bcae-e3a8f78b0cee", "50 Synthetic Retest Street", 25, 125, PaymentStatus.Pending, InventoryReservationStatus.Released)]
    [InlineData("a07397e6-281a-4f46-85cb-84e17c8e9f81", "60 Synthetic Final Street", 25, 125, PaymentStatus.Pending, InventoryReservationStatus.Active)]
    [InlineData("fe7af09a-37e7-4496-ab3f-93a11c49ee9c", "70 Synthetic Arabic Final Street", 40, 140, PaymentStatus.Pending, InventoryReservationStatus.Active)]
    public async Task ActualBrowserOrders_KeepSelectedPriceAndUncollectedCod_AndExactHistoricalInventory(
        string orderId, string street, decimal shipping, decimal total, PaymentStatus expectedPayment, InventoryReservationStatus expectedReservation)
    {
        var connection = await ShippingIsolatedPostgres.VerifyAsync("SHIPPING_MANUAL_TEST_CONNECTION", "shipping_manual_e2e");
        var tenant = new TenantContext(); tenant.SetTenantId(ManualShippingRuntimeFixtureTests.TenantId);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options, tenant);
        var id = Guid.Parse(orderId);
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == id);
        Assert.Equal(ManualShippingRuntimeFixtureTests.TenantId, order.TenantId);
        Assert.Equal(FulfillmentMode.Manual, order.FulfillmentMode); Assert.Null(order.FulfillmentCarrierCode);
        Assert.Equal("USD", order.Currency); Assert.Equal(shipping, order.ShippingCost); Assert.Equal(total, order.Total);
        Assert.Equal(street, order.ShippingStreet); Assert.Equal("US", order.ShippingCountry);
        Assert.Equal(expectedPayment, order.PaymentStatus); Assert.False(order.IsPaid);
        var reservation = Assert.Single(await db.InventoryReservations.Where(r => r.OrderId == id).ToListAsync());
        Assert.Equal(expectedReservation, reservation.Status);
        Assert.Equal(0m, reservation.CommittedQuantity);
        if (expectedReservation == InventoryReservationStatus.Released)
        {
            Assert.Equal(1m, reservation.ReleasedQuantity); Assert.Equal(0m, reservation.RemainingReservedQuantity);
            if (expectedPayment == PaymentStatus.Pending)
            {
                // These are retained FAILED historical browser inventory runs.
                // The real cross-domain sweep receipt, not today's corrected code,
                // explains their release. Do not restore stock or change payment.
                var receipt = await db.InventoryEventInbox.SingleAsync(e => e.EventId == reservation.LastMutationEventId && e.TenantId == order.TenantId);
                Assert.Equal("CommerceHubInventoryReconciliationJob", receipt.Source);
                Assert.Equal("commercehub-inventory-reconciliation", receipt.CorrelationId);
                Assert.NotNull(receipt.AppliedAtUtc);
            }
        }
        else
        {
            Assert.Equal(1m, reservation.RemainingReservedQuantity); Assert.Equal(0m, reservation.ReleasedQuantity);
        }
        if (expectedPayment == PaymentStatus.Expired)
        {
            Assert.Equal(OrderStatus.Cancelled, order.Status);
            Assert.Equal("Inventory reservation expired and was released by cleanup.", order.CancellationReason);
        }
        else
        {
            Assert.Empty(await db.AccountingOutboxMessages.Where(m => m.TenantId == order.TenantId && m.BusinessEntityId == id).ToListAsync());
            Assert.Empty(await db.JournalEntries.Where(j => j.TenantId == order.TenantId && j.SourceId == id).ToListAsync());
        }
        Assert.Null(order.OnlineCustomerId); Assert.True(order.IsGuestCheckout); Assert.False(string.IsNullOrWhiteSpace(order.SessionId));
        var line = Assert.Single(order.Items);
        Assert.Equal(ManualShippingRuntimeFixtureTests.ProductId, line.ProductId);
        Assert.Equal(ManualShippingRuntimeFixtureTests.VariantId, line.VariantId);
        Assert.Equal(1, line.Quantity); Assert.Equal(100m, line.UnitPrice);
        var method = await db.ShippingMethods.SingleAsync(m => m.Id == order.ShippingMethodId);
        Assert.Equal("manual", method.CarrierCode); Assert.Equal(shipping, method.BaseRate);
        var created = Assert.Single(await db.MarketplaceEventOutboxMessages.Where(m => m.TenantId == order.TenantId && m.EntityId == id && m.EventType == "OrderCreatedEvent").ToListAsync());
        Assert.NotNull(created.PublishedAtUtc); Assert.False(created.IsFailed);
        var mongoConnection = Environment.GetEnvironmentVariable("SHIPPING_MANUAL_MONGO_CONNECTION")
            ?? throw new InvalidOperationException("Explicit isolated Mongo connection required.");
        var mongo = new MongoClient(mongoConnection).GetDatabase(ManualShippingRuntimeFixtureTests.MongoDatabase);
        var projected = await mongo.GetCollection<OrderDocument>("orders").Find(m => m.OrderId == id && m.TenantId == order.TenantId).SingleAsync();
        Assert.Equal("USD", projected.Currency); Assert.Equal(shipping, projected.ShippingCost); Assert.Equal(total, projected.Total);
        Assert.Equal("COD", projected.PaymentMethodCode); Assert.Equal(expectedPayment.ToString(), projected.PaymentStatus);
        Assert.Equal(street, projected.ShippingAddress!.Street);
        Assert.Equal(order.ShippingMethodName, projected.ShippingMethodName);
    }
}
