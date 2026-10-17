using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("unserviceable")]
    [InlineData("rotated")]
    [InlineData("foreign")]
    [InlineData("manual")]
    [InlineData("environment")]
    [InlineData("changed-serviceability")]
    [InlineData("ambiguous")]
    [InlineData("address-tamper")]
    public async Task BostaNative_NoCanonicalCatalog_AdmitsOwnedSnapshot_AndControlledCreateCancel(string variant)
    {
        var tenant = Guid.NewGuid(); var actor = Guid.NewGuid(); var accountId = Guid.NewGuid(); var shipmentId = Guid.NewGuid();
        var context = new TenantContext(); context.SetTenantId(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, context);
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "TEST-NATIVE-NO-LIVE", Currency = "EGP", Total = 150m,
            PaymentMethodCode = "COD", Status = OrderStatus.Confirmed, ShippingMethodId = Guid.NewGuid(), ShippingMethodName = "Test native Bosta",
            ShippingServiceLevel = "standard", FulfillmentCarrierCode = variant == "manual" ? "manual" : "bosta", FulfillmentMode = FulfillmentMode.Carrier,
            ShippingAddress = "TEST-ONLY no physical delivery", ShippingCity = "Original free-text city", ShippingCountry = "EG", WarehouseId = Guid.NewGuid() };
        var shipment = new Shipment { Id = shipmentId, TenantId = tenant, OrderId = order.Id, CarrierCode = "bosta", ServiceLevel = "standard",
            ShippingMethodId = order.ShippingMethodId!.Value, ShippingMethodName = order.ShippingMethodName, ShipmentNumber = "TEST-NATIVE-NO-LIVE",
            ShippingAddress = order.ShippingAddress, ShippingCity = order.ShippingCity, ShippingCountry = "EG", RecipientName = "Synthetic recipient", RecipientPhone = "01000000000",
            Status = ShipmentStatus.PendingExternalRegistration, Currency = "EGP", ExternalRegistrationState = "PendingExternalCall" };
        var account = new CarrierAccount { Id = accountId, TenantId = tenant, CarrierCode = "bosta", Environment = CarrierAccountEnvironment.Production,
            ActiveCredentialReferenceId = Guid.NewGuid() };
        db.CarrierAccounts.Add(account); db.Orders.Add(order); db.Shipments.Add(shipment);
        db.Tenants.Add(new() { Id = tenant, Name = "TEST-ONLY native", Slug = "test-" + tenant.ToString("N"), IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new() { Id = actor, TenantId = tenant, Username = "synthetic-native", Email = "synthetic@example.invalid", PasswordHash = "not-a-password", Role = UserRole.Warehouse, IsActive = true });
        db.Warehouses.Add(new() { Id = order.WarehouseId.Value, TenantId = tenant, Name = "TEST inventory only", Code = "TEST", IsActive = true });
        db.OrderItems.Add(new() { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(), Quantity = 1 });
        await db.SaveChangesAsync();
        var originalAddress = order.ShippingAddress;
        var transport = new NativeEffects { Unserviceable = variant == "unserviceable" };
        var factory = new CarrierConnectionConfigurationTests.Factory(new HttpClient(transport));
        var adapter = new BostaAdapter(factory, NullLogger<BostaAdapter>.Instance);
        var credentials = new CarrierCredentials { ApiKey = "SYNTHETIC-ONLY-NO-NETWORK" };
        var config = JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($$"""{"Id":"{{Guid.NewGuid()}}","CarrierCode":"bosta","IsEnabled":true,"DisplayName":"TEST"}""")!;
        var authority = BoundAuthority(tenant, "bosta", () => new(true, "bosta", credentials, config, []));
        authority.Setup(a => a.ResolveConnectionCredentialsAsync(It.IsAny<Guid>(), "bosta", It.IsAny<CancellationToken>())).ReturnsAsync(new CarrierCredentialsResolution(true, "bosta", credentials, config, []));
        var registry = new ShippingCarrierRegistry([adapter], NullLogger<ShippingCarrierRegistry>.Instance);
        var service = new BostaNativeDestinationService(db, authority.Object, registry);
        if (variant == "foreign")
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveAsync(Guid.NewGuid(), accountId, shipmentId, BostaGeographyFixture.CityId, BostaGeographyFixture.DistrictId, default));
            Assert.Equal(0, transport.Reads); return;
        }
        if (variant == "environment")
        {
            credentials.UseSandbox = true;
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(tenant, accountId, shipmentId, BostaGeographyFixture.CityId, BostaGeographyFixture.DistrictId, default));
            Assert.Equal(0, transport.Reads); return;
        }
        if (variant is "unserviceable" or "manual")
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(tenant, accountId, shipmentId, BostaGeographyFixture.CityId, BostaGeographyFixture.DistrictId, default));
            Assert.Null(shipment.CarrierGeographicMappingSnapshotJson); Assert.Equal(0, transport.Posts); return;
        }
        var accepted = await service.SaveAsync(tenant, accountId, shipmentId, BostaGeographyFixture.CityId, BostaGeographyFixture.DistrictId, default);
        Assert.Equal(BostaGeographyFixture.DistrictId, accepted.District.Id);
        Assert.Null(shipment.DestinationCountryPlaceId); Assert.Null(shipment.CarrierGeographicMappingReleaseId);
        Assert.Empty(await db.GeographicPlaces.ToListAsync()); Assert.Empty(await db.GeographicCatalogReleases.ToListAsync());
        Assert.Empty(await db.CarrierGeographicMappingReleases.ToListAsync());
        if (variant == "address-tamper")
        {
            shipment.ShippingAddress = "Substituted address";
            await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            Assert.Equal(originalAddress, (await db.Shipments.AsNoTracking().SingleAsync()).ShippingAddress);
            Assert.Equal(0, transport.Posts); return;
        }
        await db.Entry(order).Collection(o => o.Items).LoadAsync();
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(ParcelMeasurements.Admit(new(20, 15, 10, "cm", 1.25m, "kg", 1, "TEST-NATIVE")
            { Goods = new("Synthetic goods only", 75m, "EGP") }, shipment, order, actor));
        await db.SaveChangesAsync();
        if (variant == "rotated") { account.ActiveCredentialReferenceId = Guid.NewGuid(); await db.SaveChangesAsync(); }
        if (variant == "changed-serviceability") transport.Unserviceable = true;
        if (variant == "ambiguous") transport.AmbiguousCreate = true;
        var encryption = new CarrierCredentialEncryptionService(new EphemeralDataProtectionProvider(), NullLogger<CarrierCredentialEncryptionService>.Instance);
        var forbiddenMapping = new Mock<PointOfSale.Core.Interfaces.Geographic.ICarrierGeographicMappingService>(MockBehavior.Strict);
        var carrier = CreateCarrierService(db, tenant, adapter, authority.Object, encryption, forbiddenMapping.Object);
        var result = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, shipmentId, "standard");
        if (variant == "rotated") { Assert.False(result.Success); Assert.Equal(0, transport.Posts); return; }
        if (variant is "changed-serviceability" or "ambiguous")
        {
            Assert.False(result.Success);
            if (variant == "changed-serviceability") Assert.Equal("BOSTA_DESTINATION_NOT_SERVICEABLE", result.ErrorCode);
            var frozen = (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson;
            var repeat = await carrier.ExecuteCarrierForExistingShipmentAsync(tenant, shipmentId, "standard");
            Assert.False(repeat.Success); Assert.Equal(variant == "ambiguous" ? 1 : 0, transport.Posts);
            Assert.Equal(frozen, (await db.Shipments.AsNoTracking().SingleAsync()).CarrierRequestSnapshotJson);
            return;
        }
        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal(originalAddress, (await db.Orders.AsNoTracking().SingleAsync()).ShippingAddress);
        var retained = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.NotNull(retained.CarrierRequestSnapshotJson); Assert.Equal(1, retained.ExternalRegistrationAttemptCount);
        Assert.Equal(accepted.District.Id, BostaNativeDestination.Restore(retained).District.Id);
        var cancellation = new ShipmentCancellationService(db, registry, authority.Object, new NoOpEventPublisher(), context, encryption);
        Assert.True((await cancellation.CancelForAdminAsync(tenant, shipmentId, actor, null, "TEST-ONLY", "test")).Confirmed);
        Assert.True((await cancellation.CancelForAdminAsync(tenant, shipmentId, actor, null, "TEST-ONLY", "test")).Confirmed);
        Assert.Equal(1, transport.Posts); Assert.Equal(1, transport.Deletes); Assert.Equal(2, transport.StateReads);
        Assert.Empty(await db.JournalEntries.ToListAsync());
        forbiddenMapping.VerifyNoOtherCalls();
    }

    private sealed class NativeEffects : HttpMessageHandler
    {
        public int Reads, Posts, Deletes, StateReads;
        public bool Unserviceable;
        public bool AmbiguousCreate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; string body;
            if (request.Method == HttpMethod.Get && path == "/api/v2/pickup-locations") { Reads++; body = BostaGeographyFixture.PickupJson.Replace("\"address\":", "\"isDefault\":true,\"address\":"); }
            else if (request.Method == HttpMethod.Get && path == "/api/v2/cities") { Reads++; body = BostaGeographyFixture.CitiesJson; }
            else if (request.Method == HttpMethod.Get && path == $"/api/v2/cities/{BostaGeographyFixture.CityId}/districts")
            { Reads++; body = Unserviceable ? BostaGeographyFixture.DistrictsJson.Replace("\"dropOffAvailability\":true", "\"dropOffAvailability\":false") : BostaGeographyFixture.DistrictsJson; }
            else if (request.Method == HttpMethod.Post && path == "/api/v2/deliveries")
            {
                Posts++; using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(BostaGeographyFixture.DistrictId, payload.RootElement.GetProperty("dropOffAddress").GetProperty("districtId").GetString());
                Assert.Equal(75m, payload.RootElement.GetProperty("goodsInfo").GetProperty("amount").GetDecimal());
                Assert.Equal(1, payload.RootElement.GetProperty("specs").GetProperty("packageDetails").GetProperty("itemsCount").GetInt32());
                if (AmbiguousCreate) throw new TaskCanceledException("Controlled ambiguous create timeout; no network exists.");
                body = """{"success":true,"data":{"_id":"TEST-NATIVE-DELIVERY","trackingNumber":"TEST-NATIVE-TRACKING","state":"TICKET_CREATED"}}""";
            }
            else if (request.Method == HttpMethod.Delete && path == "/api/v2/deliveries/business/TEST-NATIVE-TRACKING/terminate") { Deletes++; body = """{"success":true,"data":{"_id":"TEST-NATIVE-DELIVERY"}}"""; }
            else if (request.Method == HttpMethod.Get && path == "/api/v2/deliveries/business/TEST-NATIVE-TRACKING")
            { StateReads++; body = JsonSerializer.Serialize(new { success = true, data = new { _id = "TEST-NATIVE-DELIVERY", trackingNumber = "TEST-NATIVE-TRACKING", state = Deletes == 0 ? "TICKET_CREATED" : "CANCELLED" } }); }
            else throw new InvalidOperationException("No network exists; only controlled transport effects are available.");
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}

public sealed class BostaNativeDestinationTests
{
    [Theory]
    [InlineData("invalid")][InlineData("foreign-parent")][InlineData("unavailable")][InlineData("stale")]
    public void RejectsInvalidForeignUnavailableAndStaleReferences(string variant)
    {
        var country = new BostaGeographicReference("country", BostaGeographyFixture.CountryId, "Egypt", "EG", null, true, true);
        var city = new BostaGeographicReference("city", BostaGeographyFixture.CityId, "Cairo", null, country.Id, true, true);
        var district = new BostaGeographicReference("district", BostaGeographyFixture.DistrictId, "Zahraa Nasr City", null,
            variant == "foreign-parent" ? "foreign-city" : city.Id, true, variant != "unavailable");
        var result = new BostaGeographyResult(true, [country, city, district], null, variant == "stale" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() => BostaNativeDestination.Select(result, city.Id, variant == "invalid" ? "not-in-reference" : district.Id));
    }
    [Theory]
    [InlineData("standard", "EG", "Real address text", null)]
    [InlineData("express", "EG", "Real address text", "BOSTA_SERVICE_LEVEL_UNSUPPORTED")]
    [InlineData("standard", "SA", "Real address text", "BOSTA_DOMESTIC_EGYPT_ROUTE_REQUIRED")]
    [InlineData("standard", "EG", "short", "BOSTA_DESTINATION_ADDRESS_REQUIRED")]
    public void CheckoutAddressIsNotBookingOrCanonicalIdentity(string service, string country, string street, string? error) =>
        Assert.Equal(error, BostaDestinationAdmission.ValidateCheckoutAddress(service, country, street));
}
