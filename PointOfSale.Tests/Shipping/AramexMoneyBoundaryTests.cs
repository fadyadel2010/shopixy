using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

public sealed class AramexMoneyBoundaryTests
{
    [Fact]
    public async Task DeliveredAramexWithClaimedMoneyCannotQualifyBostaCollectionContract()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Synthetic Aramex money boundary", Slug = $"aramex-{tenantId:N}",
            IsActive = true, Status = TenantStatus.Active, Currency = "EGP"
        });
        await db.SaveChangesAsync();
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenantId, FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = "aramex", PaymentMethodCode = "COD", Total = 100,
            Currency = "EGP", FulfillmentStatus = FulfillmentStatus.Delivered
        };
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, CarrierCode = "aramex",
            Status = ShipmentStatus.Delivered, IsCashOnDelivery = true, CodAmount = 100, Currency = "EGP"
        };
        var config = new CarrierConfiguration
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CarrierCode = "aramex", IsEnabled = true,
            SupportsCod = true, SupportsWebhooks = true
        };

        // Even a caller asserting a supported Bosta money observation cannot make
        // an Aramex delivery into qualified collection or a financial posting.
        var code = await CarrierCollectionMoneyAuthority.QualifyAsync(db, order, shipment, config, null,
            new CarrierMoneyObservation("synthetic-reference", 45, 10, DateTime.UtcNow, 100,
                "COD_AMOUNT_OBSERVED", null, "bosta:delivery-status-webhook:v1", "synthetic-fingerprint"),
            CancellationToken.None);

        Assert.Equal("COD_CARRIER_OWNERSHIP_MISMATCH", code);
        Assert.Empty(await db.CarrierCollectionEvidence.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.JournalEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CarrierRemittances.IgnoreQueryFilters().ToListAsync());
    }
}
