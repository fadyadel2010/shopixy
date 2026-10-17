using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

// Opt-in actual configured PostgreSQL persistence proof. New UUID-owned rows
// remain inside one rollback-only transaction. No schema, provider or outbox run.
public sealed class BostaNativeConfiguredPersistenceTests
{
    [BostaNativeRollbackFact]
    public async Task ExistingSchema_RetainsTaggedProviderSnapshotWithoutCanonicalIdentity_ThenRollsBackAllRows()
    {
        var tenant = Guid.NewGuid();
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var connection = configured.Database.GetConnectionString()!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        var orderId = Guid.NewGuid(); var shipmentId = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            db.Tenants.Add(new() { Id = tenant, Name = "SYNTHETIC ROLLBACK ONLY", Slug = "native-rollback-" + tenant.ToString("N"), IsActive = true, Status = TenantStatus.Active });
            db.Orders.Add(new() { Id = orderId, TenantId = tenant, OrderNumber = "SYN-" + orderId.ToString("N"), Currency = "EGP", Total = 100,
                PaymentMethodCode = "COD", FulfillmentMode = FulfillmentMode.Carrier, FulfillmentCarrierCode = "bosta", ShippingServiceLevel = "standard",
                Status = OrderStatus.Confirmed, ShippingAddress = "SYNTHETIC ROLLBACK ADDRESS", ShippingCity = "Original text", ShippingCountry = "EG" });
            var s = new Shipment { Id = shipmentId, TenantId = tenant, OrderId = orderId, ShipmentNumber = "SYN-" + shipmentId.ToString("N"), CarrierCode = "bosta",
                ServiceLevel = "standard", ShippingAddress = "SYNTHETIC ROLLBACK ADDRESS", ShippingCity = "Original text", ShippingCountry = "EG", Currency = "EGP",
                RecipientName = "Synthetic", RecipientPhone = "01000000000", Status = ShipmentStatus.PendingExternalRegistration };
            db.Shipments.Add(s); await db.SaveChangesAsync();
            var country = new BostaGeographicReference("country", BostaGeographyFixture.CountryId, "Egypt", "EG", null, true, true);
            var city = new BostaGeographicReference("city", BostaGeographyFixture.CityId, "Cairo", null, country.Id, true, true);
            var district = new BostaGeographicReference("district", BostaGeographyFixture.DistrictId, "Zahraa Nasr City", null, city.Id, true, true);
            var at = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            var e = new BostaNativeDestination(BostaNativeDestination.SnapshotKind, 1, tenant, s.Id, orderId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false,
                BostaNativeDestination.CredentialHash(new() { ApiKey = "SYNTHETIC-ONLY" }), BostaNativeDestination.AddressHash(s), country, city, district, at);
            s.CarrierGeographicMappingSnapshotJson = JsonSerializer.Serialize(e);
            s.CarrierGeographicAccountScope = "BostaCarrierAccount"; s.CarrierGeographicAccountScopeId = e.CarrierAccountId;
            s.CarrierGeographicEnvironment = "Production"; s.CarrierGeographicResolvedAtUtc = at;
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var retained = await db.Shipments.AsNoTracking().SingleAsync();
            Assert.Equal(e, BostaNativeDestination.Restore(retained));
            Assert.Null(retained.DestinationCountryPlaceId); Assert.Null(retained.DestinationGeographicCatalogReleaseId);
            Assert.Null(retained.CarrierGeographicMappingReleaseId); Assert.Null(retained.CarrierGeographicMappingVersion);
            // A detached clear cannot evade persisted immutable-evidence checks.
            retained.CarrierGeographicMappingSnapshotJson = null; db.Update(retained);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new NpgsqlConnection(connection); await observer.OpenAsync();
        foreach (var table in new[] { "Orders", "Shipments", "CarrierAccounts", "OutboxMessages", "GeographicAudit" })
        {
            await using var check = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"TenantId\"=@tenant", observer);
            check.Parameters.AddWithValue("tenant", tenant); Assert.Equal(0L, await check.ExecuteScalarAsync());
        }
        await using var checkTenant = new NpgsqlCommand("SELECT count(*) FROM \"Tenants\" WHERE \"Id\"=@tenant", observer);
        checkTenant.Parameters.AddWithValue("tenant", tenant); Assert.Equal(0L, await checkTenant.ExecuteScalarAsync());
    }
}
public sealed class BostaNativeRollbackFactAttribute : FactAttribute
{
    public BostaNativeRollbackFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHOPIXY_PHASE8_ROLLBACK") != "1") Skip = "Explicit native snapshot rollback-only persistence verification required.";
    }
}
