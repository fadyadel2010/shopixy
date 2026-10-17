using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed partial class WarehouseAddressAdmissionTests
{
    [Theory]
    [InlineData("order")]
    [InlineData("shipment-detached")]
    [InlineData("shipment-accept")]
    public async Task CanonicalRetention_UsesPersistedAuthorityForAlternateEntryPaths(string path)
    {
        var pair = await CreateCanonicalPairAsync(captureOrigin: false);
        await using var db = Db();
        var original = CanonicalShipmentAddresses.RestoreDestination(pair.Shipment, pair.Order, pair.Destination);
        if (path == "order")
        {
            var order = await db.Orders.SingleAsync();
            order.ShippingAddressAdmissionEvidenceId = null;
            order.BillingAddressAdmissionEvidenceId = null;
            db.Orders.Remove(order);
        }
        else
        {
            var shipment = await db.Shipments.SingleAsync();
            if (path == "shipment-detached") db.ChangeTracker.Clear();
            shipment.DestinationAddressAdmissionEvidenceId = null;
            db.Entry(shipment).State = EntityState.Deleted;
        }
        var error = await Record.ExceptionAsync(async () =>
        {
            if (path == "shipment-accept") await db.SaveChangesAsync(true);
            else await db.SaveChangesAsync();
        });
        db.ChangeTracker.Clear();
        Assert.IsType<InvalidOperationException>(error);
        var retained = await db.Shipments.SingleAsync();
        Assert.NotNull(await db.Orders.SingleAsync());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(original),
            System.Text.Json.JsonSerializer.Serialize(CanonicalShipmentAddresses.RestoreDestination(retained, pair.Order, pair.Destination)));
    }
}
