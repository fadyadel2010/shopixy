using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed partial class WarehouseAddressAdmissionTests
{
    [Theory]
    [InlineData(false, false, "clear")]
    [InlineData(false, true, "clear")]
    [InlineData(true, false, "clear")]
    [InlineData(true, true, "clear")]
    [InlineData(false, false, "substitute")]
    [InlineData(false, true, "substitute")]
    [InlineData(true, false, "substitute")]
    [InlineData(true, true, "substitute")]
    public async Task CanonicalShipment_DeletionCannotHidePersistedOwnershipMarkers(bool attempted, bool useAsync, string mutation)
    {
        // The existing fixture uses the real admission and shipment writers with
        // process-local EF stores, disabled Redis and mock event/readiness services.
        // No configured history, SQL database or provider transport is exercised.
        var pair = await CreateCanonicalPairAsync();
        var originalPair = CanonicalShipmentAddresses.Restore(pair.Shipment, pair.Order, pair.Origin, pair.Destination);
        await using var db = Db();
        var shipment = await db.Shipments.SingleAsync();
        if (attempted)
        {
            CaptureOfflineCanonicalAttempt(shipment);
            await db.SaveChangesAsync();
        }
        var shipmentBefore = JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync());
        var orderBefore = JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync());
        var admissionBefore = JsonSerializer.Serialize(await db.AddressAdmissionEvidence.AsNoTracking().OrderBy(e => e.Id).ToListAsync());
        var auditsBefore = await db.GeographicAudit.CountAsync();

        // These are unsaved edits on the same tracked entry. Deleted entries skip
        // ordinary modified-property guards, so retention must use OriginalValues.
        shipment.DestinationAddressAdmissionEvidenceId = mutation == "clear" ? null : Guid.NewGuid();
        shipment.OriginGeographicSnapshotVersion = mutation == "clear" ? null : 1;
        db.Shipments.Remove(shipment);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (useAsync) await db.SaveChangesAsync();
            else db.SaveChanges();
        });
        db.ChangeTracker.Clear();
        var retained = await db.Shipments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync();

        Assert.NotNull(retained);
        Assert.Equal("Canonical shipment address evidence is retained and cannot be deleted.", Assert.IsType<InvalidOperationException>(error).Message);
        Assert.Equal(shipmentBefore, JsonSerializer.Serialize(retained));
        Assert.Equal(orderBefore, JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync()));
        Assert.Equal(admissionBefore, JsonSerializer.Serialize(await db.AddressAdmissionEvidence.AsNoTracking().OrderBy(e => e.Id).ToListAsync()));
        Assert.Equal(auditsBefore, await db.GeographicAudit.CountAsync());
        var restored = CanonicalShipmentAddresses.Restore(retained, pair.Order, pair.Origin, pair.Destination);
        Assert.Equal(originalPair.Fingerprint, restored.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(originalPair.Origin), JsonSerializer.Serialize(restored.Origin));
        Assert.Equal(JsonSerializer.Serialize(originalPair.Destination), JsonSerializer.Serialize(restored.Destination));
    }

    [Theory]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    [InlineData(false, "substitute")]
    [InlineData(true, "substitute")]
    public async Task CanonicalDestination_DeletionCannotHideLinkBeforeFirstOriginCapture(bool useAsync, string mutation)
    {
        // Production shipment creation has committed the canonical destination,
        // but origin capture has not happened yet. No missing origin is fabricated.
        var pair = await CreateCanonicalPairAsync(captureOrigin: false);
        Assert.Null(pair.Shipment.OriginSnapshotJson);
        Assert.Null(pair.Shipment.OriginGeographicSnapshotVersion);
        var original = CanonicalShipmentAddresses.RestoreDestination(pair.Shipment, pair.Order, pair.Destination);
        await using var db = Db();
        var shipment = await db.Shipments.SingleAsync();
        var shipmentBefore = JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync());
        shipment.DestinationAddressAdmissionEvidenceId = mutation == "clear" ? null : Guid.NewGuid();
        db.Shipments.Remove(shipment);
        var error = await Record.ExceptionAsync(async () =>
        {
            if (useAsync) await db.SaveChangesAsync();
            else db.SaveChanges();
        });
        db.ChangeTracker.Clear();
        var retained = await db.Shipments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync();
        Assert.NotNull(retained);
        Assert.Equal("Canonical shipment address evidence is retained and cannot be deleted.", Assert.IsType<InvalidOperationException>(error).Message);
        Assert.Equal(shipmentBefore, JsonSerializer.Serialize(retained));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(CanonicalShipmentAddresses.RestoreDestination(retained, pair.Order, pair.Destination)));
        Assert.Null(retained.OriginSnapshotJson);
        Assert.Null(retained.OriginGeographicSnapshotVersion);
        Assert.Null(retained.CarrierRequestSnapshotJson);
        Assert.Equal(0, retained.ExternalRegistrationAttemptCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalShipment_UnrelatedSaveKeepsOriginalOwnershipAndRequestEvidence(bool attempted)
    {
        var pair = await CreateCanonicalPairAsync();
        var originalPair = CanonicalShipmentAddresses.Restore(pair.Shipment, pair.Order, pair.Origin, pair.Destination);
        await using var db = Db();
        var shipment = await db.Shipments.SingleAsync();
        if (attempted) CaptureOfflineCanonicalAttempt(shipment);
        var originJson = shipment.OriginSnapshotJson;
        var requestJson = shipment.CarrierRequestSnapshotJson;
        var requestFingerprint = shipment.CarrierRequestFingerprint;
        var requestId = shipment.CarrierRequestId;
        shipment.Notes = "Unrelated logistics note; no history repair";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var retained = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(pair.Destination.Id, retained.DestinationAddressAdmissionEvidenceId);
        Assert.Equal(2, retained.OriginGeographicSnapshotVersion);
        Assert.Equal(originJson, retained.OriginSnapshotJson);
        Assert.Equal(requestJson, retained.CarrierRequestSnapshotJson);
        Assert.Equal(requestFingerprint, retained.CarrierRequestFingerprint);
        Assert.Equal(requestId, retained.CarrierRequestId);
        Assert.Equal(attempted ? 1 : 0, retained.ExternalRegistrationAttemptCount);
        if (attempted) Assert.Equal("ReconciliationRequired", retained.ExternalRegistrationState);
        Assert.Equal(originalPair.Fingerprint, CanonicalShipmentAddresses.Restore(retained, pair.Order, pair.Origin, pair.Destination).Fingerprint);
    }

    [Fact]
    public async Task LegacyShipmentWithoutCanonicalEvidence_RemainsUsableWithoutInventedCapture()
    {
        await using var db = Db();
        var (shipment, _) = await AddShipment(db, "US");
        shipment.Notes = "Legacy logistics note";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var retained = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Null(retained.DestinationAddressAdmissionEvidenceId);
        Assert.Null(retained.OriginGeographicSnapshotVersion);
        Assert.Null(retained.OriginSnapshotJson);
        Assert.Null(retained.CarrierRequestSnapshotJson);
        Assert.Equal(0, retained.ExternalRegistrationAttemptCount);
        Assert.Equal("Legacy logistics note", retained.Notes);
    }

    private static void CaptureOfflineCanonicalAttempt(Shipment shipment)
    {
        // Opaque retained fixture bytes only, never provider/account authority.
        shipment.CarrierRequestId = "offline-retained-request";
        shipment.CarrierRequestSnapshotJson = "{\"syntheticRetainedRequest\":true}";
        shipment.CarrierRequestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shipment.CarrierRequestSnapshotJson)));
        shipment.ExternalRegistrationAttemptCount = 1;
        shipment.ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow;
        shipment.ExternalRegistrationState = "ReconciliationRequired";
    }
}
