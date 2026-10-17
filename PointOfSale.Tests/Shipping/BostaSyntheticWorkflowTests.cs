using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public async Task Bosta_SyntheticReferenceToPersistedCreateAndIndependentCancellation_UsesExternalPickupNotWarehouseAddress(bool wrongConfirmation, bool providerDefault, bool changedDefault)
    {
        await using var f = await BostaGeographyFixture.Create();
        await f.Synchronize(); await f.Accept(); await f.Publish();
        var id = Guid.NewGuid(); var actor = Guid.NewGuid();
        var db = f.Db;
        db.Tenants.Add(new() {Id = f.Tenant, Name = "TEST-ONLY Bosta workflow", Slug = "synthetic-" + f.Tenant.ToString("N"), IsActive = true, Status = TenantStatus.Active});
        db.Users.Add(new() {Id = actor, TenantId = f.Tenant, Username = "synthetic-operator", Email = "operator@example.invalid", PasswordHash = "not-a-password", Role = UserRole.Warehouse, IsActive = true});
        SeedOrderAndShipment(db, f.Tenant, id, ShipmentStatus.PendingExternalRegistration, "bosta", beforeSave: (candidate, sourceOrder) => {
            sourceOrder.Status = OrderStatus.Confirmed;
            candidate.DestinationCountryPlaceId = f.Country; candidate.DestinationGeographicCatalogReleaseId = f.Catalog;
            candidate.DestinationAdministrativeAreaPlaceId = f.City; candidate.DestinationLocalityPlaceId = f.Locality; candidate.DestinationDistrictPlaceId = f.District;
            candidate.DestinationStreet = "TEST-ONLY destination; not a real shipment";
        });
        var shipment = await db.Shipments.SingleAsync(); var order = await db.Orders.SingleAsync();
        db.OrderItems.Add(new() {Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(), Quantity = 1});
        var warehouse = await db.Warehouses.SingleAsync(); warehouse.Address = ""; warehouse.Phone = "";
        await db.SaveChangesAsync();
        // This orchestration test starts with admitted parcel input. It exercises
        // the supported EF in-memory orchestration path, not the relational parcel
        // authoring transaction or PostgreSQL locking/trigger certification.
        await db.Entry(order).Collection(candidate => candidate.Items).LoadAsync();
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(ParcelMeasurements.Admit(
            new(20, 15, 10, "cm", 1.25m, "kg", 1, "TEST-ONLY-PACKAGE") {Goods = new("Synthetic goods; no physical declaration", 75m, "EGP")},
            shipment, order, actor));
        await db.SaveChangesAsync();
        var transport = new SyntheticBostaEffects(wrongConfirmation, changedDefault);
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(h => h.CreateClient("Bosta")).Returns(new HttpClient(transport));
        var adapter = new BostaAdapter(factory.Object, NullLogger<BostaAdapter>.Instance);
        var credentials = new CarrierCredentials {ApiKey = "SYNTHETIC-TRANSPORT-ONLY", ExtraConfig = new() {["businessLocationId"] = BostaGeographyFixture.PickupId}};
        if (providerDefault) credentials.ExtraConfig.Clear();
        var config = JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{Guid.NewGuid()}}","CarrierCode":"bosta","UseSandbox":false,"IsEnabled":true,"DisplayName":"Test only"}""")!;
        var authority = BoundAuthority(f.Tenant, "bosta", () => new(true, "bosta", credentials, config, []));
        var encryption = new CarrierCredentialEncryptionService(new EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
        transport.BeforePost = async () => {
            var retained = await db.Shipments.AsNoTracking().SingleAsync();
            Assert.Equal(1, retained.ExternalRegistrationAttemptCount);
            Assert.NotNull(retained.CarrierRequestFingerprint); Assert.NotNull(retained.CarrierRequestSnapshotJson);
            Assert.Null(retained.OriginSnapshotJson); Assert.Null(retained.OriginStreet);
            using var intent = JsonDocument.Parse(retained.CarrierRequestSnapshotJson);
            var pickup = intent.RootElement.GetProperty("Request").GetProperty("BostaPickupOrigin");
            Assert.Equal(warehouse.Id, pickup.GetProperty("InventoryWarehouseId").GetGuid());
            Assert.Equal(BostaGeographyFixture.PickupId, pickup.GetProperty("BusinessLocationId").GetString());
            Assert.DoesNotContain("SYNTHETIC-TRANSPORT-ONLY", retained.CarrierRequestSnapshotJson);
        };
        var service = CreateCarrierService(db, f.Tenant, adapter, authority.Object, encryption, f.Mapping);
        var created = await service.ExecuteCarrierForExistingShipmentAsync(f.Tenant, id, "standard");
        if (providerDefault) Assert.Empty(credentials.ExtraConfig);
        if (changedDefault)
        {
            Assert.False(created.Success); Assert.Equal("BOSTA_PICKUP_CHANGED", created.ErrorCode);
            var frozen = (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson;
            var repeated = await service.ExecuteCarrierForExistingShipmentAsync(f.Tenant, id, "standard");
            Assert.False(repeated.Success); Assert.Equal(0, transport.Posts);
            Assert.Equal(frozen, (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson);
            Assert.Empty(await db.JournalEntries.ToListAsync());
            return;
        }
        Assert.True(created.Success, created.ErrorCode);
        db.ChangeTracker.Clear();
        var persisted = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal("TEST-DELIVERY-NOT-LIVE", persisted.ExternalReferenceId);
        Assert.Equal("TEST-TRACKING-NOT-LIVE", persisted.TrackingNumber);
        Assert.Equal(f.Release, persisted.CarrierGeographicMappingReleaseId);
        var registry = new Mock<IShippingCarrierRegistry>(); registry.Setup(r => r.GetAdapter("bosta")).Returns(adapter);
        var cancellation = new ShipmentCancellationService(db, registry.Object, authority.Object, new NoOpEventPublisher(), f.TenantContext, encryption);
        var cancelled = await cancellation.CancelForAdminAsync(f.Tenant, id, actor, null, "Synthetic-only cancellation", "synthetic-test");
        Assert.Equal(!wrongConfirmation, cancelled.Confirmed);
        var replay = await cancellation.CancelForAdminAsync(f.Tenant, id, actor, null, "Synthetic-only cancellation", "synthetic-replay");
        Assert.Equal(cancelled.Confirmed, replay.Confirmed);
        Assert.Equal(1, transport.Posts); Assert.Equal(1, transport.Deletes); Assert.Equal(2, transport.StateReads);
        Assert.False((await db.Shipments.AsNoTracking().SingleAsync()).CodCollected);
        Assert.Empty(await db.JournalEntries.ToListAsync()); Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
    }

    private sealed class SyntheticBostaEffects(bool wrongConfirmation, bool changedDefault) : HttpMessageHandler
    {
        public int Posts, Deletes, StateReads;
        public Func<Task>? BeforePost;
        private int pickupReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (request.Method == HttpMethod.Get && path == "/api/v2/pickup-locations")
            {
                pickupReads++;
                body = BostaGeographyFixture.PickupJson.Replace("\"address\":", "\"isDefault\":true,\"address\":", StringComparison.Ordinal);
                if (changedDefault && pickupReads > 1) body = body.Replace(BostaGeographyFixture.PickupId, "CHANGED-DEFAULT", StringComparison.Ordinal);
            }
            else if (request.Method == HttpMethod.Post && path == "/api/v2/deliveries")
            {
                Posts++; await BeforePost!();
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(BostaGeographyFixture.DistrictId, payload.RootElement.GetProperty("dropOffAddress").GetProperty("districtId").GetString());
                Assert.Equal(BostaGeographyFixture.PickupId, payload.RootElement.GetProperty("businessLocationId").GetString());
                body = """{"success":true,"data":{"_id":"TEST-DELIVERY-NOT-LIVE","trackingNumber":"TEST-TRACKING-NOT-LIVE","state":"TICKET_CREATED"}}""";
            }
            else if (request.Method == HttpMethod.Delete && path == "/api/v2/deliveries/business/TEST-TRACKING-NOT-LIVE/terminate")
            {
                Deletes++; body = """{"success":true,"data":{"_id":"TEST-DELIVERY-NOT-LIVE"}}""";
            }
            else if (request.Method == HttpMethod.Get && path == "/api/v2/deliveries/business/TEST-TRACKING-NOT-LIVE")
            {
                StateReads++;
                body = JsonSerializer.Serialize(new {success = true, data = new {_id = wrongConfirmation && Deletes > 0 ? "WRONG-TARGET" : "TEST-DELIVERY-NOT-LIVE",
                    trackingNumber = "TEST-TRACKING-NOT-LIVE", state = Deletes == 0 ? "TICKET_CREATED" : "CANCELLED"}});
            }
            else throw new InvalidOperationException("Only explicit synthetic Bosta effects are available; no network transport exists.");
            return new(HttpStatusCode.OK) {Content = new StringContent(body)};
        }
    }
}
