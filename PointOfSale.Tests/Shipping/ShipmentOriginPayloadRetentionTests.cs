using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData(false, "clear")]
    [InlineData(false, "empty")]
    [InlineData(false, "whitespace")]
    [InlineData(false, "substitute")]
    [InlineData(true, "clear")]
    [InlineData(true, "empty")]
    [InlineData(true, "whitespace")]
    [InlineData(true, "substitute")]
    public async Task LegacyOriginPayload_TrackedSaveCannotEraseOrSubstituteAttemptedEvidence(bool inconsistentCounter, string mutation)
    {
        // EF process-local store only. The retained v1 fixture deliberately has no
        // canonical-v2 payload; preserving it must not require inventing one.
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, CarrierCodes.Dhl,
            carrierRequestId: "retained-request", externalRegistrationAttemptCount: 1,
            externalRegistrationState: "ReconciliationRequired", originPostalCode: "01234");
        var shipment = await db.Shipments.SingleAsync();
        shipment.CarrierRequestSnapshotJson = "{\"originalRequest\":\"offline-uncertain-evidence\"}";
        shipment.CarrierRequestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shipment.CarrierRequestSnapshotJson)));
        if (inconsistentCounter) shipment.ExternalRegistrationAttemptCount = 0;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        shipment = await db.Shipments.SingleAsync();
        var originalJson = Assert.IsType<string>(shipment.OriginSnapshotJson);
        var originalOrigin = JsonSerializer.Deserialize<ShipmentOriginDto>(originalJson)!;
        Assert.Equal(1, originalOrigin.SnapshotVersion);
        Assert.Null(originalOrigin.CanonicalAddressEvidenceJson);
        var shipmentBefore = JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync());
        var orderBefore = JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync());
        var auditsBefore = await db.GeographicAudit.CountAsync();

        // This is the actual tracked persistence path, not an in-memory-only
        // change to the object supplied to the legacy restoration reader.
        shipment.OriginSnapshotJson = mutation switch
        {
            "clear" => null,
            "empty" => "",
            "whitespace" => " ",
            "substitute" => JsonSerializer.Serialize(originalOrigin with { Street = "Substituted street", ContactPhone = "15555559999" }),
            _ => throw new ArgumentException("Unknown mutation fixture.")
        };
        var saveError = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        shipment = await db.Shipments.SingleAsync();
        var order = await db.Orders.SingleAsync();
        var adapter = new DeterministicCarrierAdapter();
        var service = CreateService(db, tenant, adapter);
        var resolver = typeof(CarrierShipmentService).GetMethod("ResolveOrRestoreShipmentOriginAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var restored = await (Task<ShipmentOriginDto?>)resolver.Invoke(service, [shipment, order, CancellationToken.None])!;

        Assert.Equal(originalJson, shipment.OriginSnapshotJson);
        Assert.Equal(originalOrigin, restored);
        Assert.Contains("origin snapshot evidence is immutable", Assert.IsType<InvalidOperationException>(saveError).Message);
        Assert.Equal(shipmentBefore, JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync()));
        Assert.Equal(orderBefore, JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync()));
        Assert.Equal(auditsBefore, await db.GeographicAudit.CountAsync());
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        Assert.Equal(0, adapter.LookupByCarrierRequestIdCallCount);
        Assert.Equal(0, adapter.TrackingCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOriginPayload_UnrelatedSaveAndExactReplayPreserveOriginalBytes(bool inconsistentCounter)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, CarrierCodes.Dhl,
            carrierRequestId: "retained-request", externalRegistrationAttemptCount: 1,
            externalRegistrationState: "ReconciliationRequired", originPostalCode: "01234");
        var shipment = await db.Shipments.SingleAsync();
        if (inconsistentCounter) shipment.ExternalRegistrationAttemptCount = 0;
        var originalJson = Assert.IsType<string>(shipment.OriginSnapshotJson);
        var originalOrigin = JsonSerializer.Deserialize<ShipmentOriginDto>(originalJson)!;
        shipment.OriginSnapshotJson = originalJson;
        shipment.Notes = "Unrelated logistics note; no evidence repair";
        (await db.Warehouses.SingleAsync()).Address = "Later warehouse address";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        shipment = await db.Shipments.SingleAsync();
        var order = await db.Orders.SingleAsync();
        var adapter = new DeterministicCarrierAdapter();
        var service = CreateService(db, tenant, adapter);
        var resolver = typeof(CarrierShipmentService).GetMethod("ResolveOrRestoreShipmentOriginAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var restored = await (Task<ShipmentOriginDto?>)resolver.Invoke(service, [shipment, order, CancellationToken.None])!;
        Assert.Equal(originalOrigin, restored);
        Assert.Equal(originalJson, shipment.OriginSnapshotJson);
        Assert.Equal(1, shipment.OriginGeographicSnapshotVersion);
        Assert.Equal("01234", restored!.PostalCode);
        Assert.NotEqual("Later warehouse address", restored.Street);
        Assert.Null(restored.CanonicalAddressEvidenceJson);
        Assert.Null(shipment.CarrierRequestSnapshotJson);
        Assert.Null(shipment.CarrierRequestFingerprint);
        Assert.Equal("ReconciliationRequired", shipment.ExternalRegistrationState);
        Assert.Equal(inconsistentCounter ? 0 : 1, shipment.ExternalRegistrationAttemptCount);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
        Assert.Equal(0, adapter.LookupByCarrierRequestIdCallCount);
    }
}
