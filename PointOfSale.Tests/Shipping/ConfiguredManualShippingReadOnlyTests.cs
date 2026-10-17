using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Npgsql;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Shipping;

/// <summary>
/// Opt-in observation of a NEW synthetic browser order in the configured runtime.
/// No fixture creation, migrations, SaveChanges, reset, replay or history repair.
/// PostgreSQL enforces READ ONLY for the entire observation transaction.
/// This is not a replacement for the disposable concurrency/migration suite.
/// </summary>
public sealed class ConfiguredManualShippingReadOnlyTests
{
    [ConfiguredShippingReadOnlyFact]
    public async Task BackorderableSyntheticBrowserOrder_HasOriginalAuthority_UnpaidDelivery_AndPublishedProjection()
    {
        var cs = new NpgsqlConnectionStringBuilder(Required("SHIPPING_CONFIGURED_READONLY_CONNECTION"));
        if (cs.Host != "192.168.1.5" || cs.Port != 5432 || cs.Database != "PosEcommerceDb_Baseline" || !string.IsNullOrEmpty(cs.SearchPath))
            throw new InvalidOperationException("Configured read-only observation endpoint mismatch; no fallback allowed.");
        var orderId = Guid.Parse(Required("SHIPPING_CONFIGURED_READONLY_ORDER_ID"));
        var tenantId = Guid.Parse(Required("SHIPPING_CONFIGURED_READONLY_TENANT_ID"));
        var context = new TenantContext(); context.SetTenantId(tenantId);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs.ConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options, context);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        Assert.Equal("on", await db.Database.SqlQueryRaw<string>("SELECT current_setting('transaction_read_only') AS \"Value\"").SingleAsync());
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);
        Assert.StartsWith("SYNTHETIC SHIPPING FINAL 20261014.", order.CustomerNotes);
        Assert.Equal("shipping-final-20261014@example.invalid", order.GuestEmail);
        Assert.Equal("0000000000", order.GuestPhone);
        Assert.Equal(Guid.Parse(Required("SHIPPING_CONFIGURED_READONLY_CART_ID")), order.CartId);
        Assert.Equal(Required("SHIPPING_CONFIGURED_READONLY_GUEST_SESSION"), order.SessionId);
        Assert.Equal(Guid.Parse("0f1a22f9-e017-4443-b94e-d37053fa7bca"), order.ShippingMethodId);
        Assert.Equal("Standard Shipping", order.ShippingMethodName);
        Assert.Equal("EGP", order.Currency); Assert.Equal(10m, order.ShippingCost); Assert.Equal(35m, order.Total);
        Assert.Equal("SYNTHETIC FINAL ONLY - 20 Test Street - no physical delivery", order.ShippingStreet);
        Assert.Equal(OrderStatus.Delivered, order.Status); Assert.Equal(FulfillmentStatus.Delivered, order.FulfillmentStatus);
        Assert.Equal(FulfillmentMode.Manual, order.FulfillmentMode);
        Assert.Equal("COD", order.PaymentMethodCode); Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); Assert.False(order.IsPaid);
        Assert.Equal("SYNTHETIC-LOCAL-VERIFICATION-20261014", order.DeliveryReference);
        Assert.Equal("https://synthetic.example.invalid/manual-verification-proof", order.ProofOfDeliveryUrl);
        var sessionId = Guid.Parse(Required("SHIPPING_CONFIGURED_READONLY_CHECKOUT_SESSION_ID"));
        var session = await db.CheckoutSessions.SingleAsync(s => s.Id == sessionId);
        Assert.Equal(order.CartId, session.CartId); Assert.Equal(order.SessionId, session.SessionId);
        Assert.Equal(order.Id, session.ConvertedOrderId); Assert.Equal(CheckoutSessionStatus.ConvertedToOrder, session.Status);
        var resolutionId = Guid.Parse(Required("SHIPPING_CONFIGURED_READONLY_RESOLUTION_ID"));
        var resolution = await db.WarehouseResolutions.SingleAsync(r => r.Id == resolutionId);
        Assert.Equal(sessionId, resolution.CheckoutSessionId); Assert.Equal(order.CartId, resolution.CartId);
        Assert.Equal(order.Id, resolution.ConvertedOrderId); Assert.Equal(order.WarehouseId, resolution.WarehouseId);
        Assert.Equal(1, await db.Orders.CountAsync(o => o.CartId == order.CartId && o.SessionId == order.SessionId));
        var shipment = Assert.Single(await db.Shipments.Where(s => s.OrderId == orderId).ToListAsync());
        Assert.Equal("manual", shipment.CarrierCode); Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
        Assert.Equal(10m, shipment.ShippingCost); Assert.False(shipment.CodCollected);
        Assert.Equal(0, shipment.ExternalRegistrationAttemptCount);
        var item = Assert.Single(order.Items);
        Assert.Equal("CERT-MEDIA-S-20261007", item.SKU); Assert.Equal(1m, item.Quantity);
        Assert.Equal(Guid.Parse("15e044bc-eaa1-c905-31a4-658aac4ad14b"), item.ProductId);
        var product = await db.Products.SingleAsync(p => p.Id == item.ProductId);
        // Observed original SQL policy, also exposed through the normal POS
        // product GET. Backorderable checkout does not reserve physical stock.
        // Do not misrepresent this browser case as a COD inventory certificate.
        Assert.True(product.TrackStock); Assert.True(product.AllowBackorder);
        Assert.Empty(await db.InventoryReservations.Where(r => r.OrderId == orderId).ToListAsync());
        var messages = await db.MarketplaceEventOutboxMessages.Where(m => m.TenantId == tenantId && m.EntityId == orderId &&
            (m.EventType == "OrderCreatedEvent" || m.EventType == "OrderDeliveredEvent")).ToListAsync();
        Assert.Equal(2, messages.Count); Assert.Equal(2, messages.Select(m => m.EventType).Distinct().Count());
        Assert.All(messages, m => { Assert.NotNull(m.PublishedAtUtc); Assert.False(m.IsFailed); });
        Assert.Empty(await db.AccountingOutboxMessages.Where(m => m.TenantId == tenantId && m.BusinessEntityId == orderId).ToListAsync());
        Assert.Empty(await db.JournalEntries.Where(j => j.TenantId == tenantId && j.SourceId == orderId).ToListAsync());
        var mongoCs = Required("SHIPPING_CONFIGURED_READONLY_MONGO_CONNECTION");
        var mongoUrl = new MongoUrl(mongoCs);
        if (mongoUrl.Servers.Count() != 1 || mongoUrl.Server.Host != "192.168.109.128" || mongoUrl.Server.Port != 27017)
            throw new InvalidOperationException("Configured Mongo read endpoint mismatch.");
        var mongo = new MongoClient(mongoCs).GetDatabase("ecommerce_read_db");
        var projection = await mongo.GetCollection<OrderDocument>("orders")
            .Find(o => o.OrderId == orderId && o.TenantId == tenantId).SingleAsync();
        Assert.Equal("Delivered", projection.Status); Assert.Equal("Delivered", projection.ShippingStatus);
        Assert.Equal("Pending", projection.PaymentStatus); Assert.Equal("COD", projection.PaymentMethodCode);
        Assert.Equal(35m, projection.Total); Assert.Equal(10m, projection.ShippingCost);
        Assert.Equal(order.GuestEmail, projection.CustomerEmail);
        await transaction.RollbackAsync();
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Explicit actual-browser read-only evidence required: {name}");
}

public sealed class ConfiguredShippingReadOnlyFactAttribute : FactAttribute
{
    public ConfiguredShippingReadOnlyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHIPPING_CONFIGURED_READONLY_ORDER_ID") == null)
            Skip = "Opt-in observation requires identity captured from a fresh synthetic browser checkout; never provisions a fixture.";
    }
}
