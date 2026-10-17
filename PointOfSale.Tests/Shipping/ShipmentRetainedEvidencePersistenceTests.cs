using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Theory]
    [InlineData(nameof(Shipment.ParcelEvidenceJson), "clear", "async")]
    [InlineData(nameof(Shipment.ParcelEvidenceJson), "replace", "sync")]
    [InlineData(nameof(Shipment.CarrierRequestSnapshotJson), "clear", "async")]
    [InlineData(nameof(Shipment.CarrierRequestSnapshotJson), "replace", "sync")]
    [InlineData(nameof(Shipment.CarrierRequestFingerprint), "clear", "async")]
    [InlineData(nameof(Shipment.CarrierRequestFingerprint), "replace", "sync")]
    [InlineData(nameof(Shipment.CarrierRequestId), "replace", "async")]
    [InlineData(nameof(Shipment.CarrierRequestSnapshotJson), "clear", "acceptAsync")]
    [InlineData(nameof(Shipment.OriginSnapshotJson), "clear", "acceptSync")]
    [InlineData(nameof(Shipment.ParcelEvidenceJson), "clear", "detached")]
    [InlineData(nameof(Shipment.OriginSnapshotJson), "clear", "detached")]
    [InlineData(nameof(Shipment.CarrierRequestSnapshotJson), "clear", "rebaseline")]
    [InlineData(nameof(Shipment.OrderId), "replace", "async")]
    [InlineData(nameof(Shipment.CarrierCode), "replace", "async")]
    public async Task RetainedEvidence_AllSaveEntryPointsRejectLossOrSubstitution(string property, string mutation, string path)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, "dhl",
            carrierRequestId: "original-operation", externalRegistrationAttemptCount: 1,
            externalRegistrationState: "ReconciliationRequired");
        var shipment = await db.Shipments.SingleAsync();
        // Opaque retained bytes are deliberately not merchant/provider authority.
        shipment.ParcelEvidenceJson = "retained-measurement";
        shipment.CarrierRequestSnapshotJson = "retained-original-request";
        shipment.CarrierRequestFingerprint = "retained-original-fingerprint";
        shipment.ExternalRegistrationAttemptCount = 0;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        shipment = await db.Shipments.SingleAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync());
        if (path == "detached") db.ChangeTracker.Clear();
        var entry = db.Entry(shipment);
        entry.Property(property).CurrentValue = property == nameof(Shipment.OrderId) ? Guid.NewGuid() :
            mutation == "clear" ? null : "substituted-evidence";
        if (path == "detached") entry.State = EntityState.Modified;
        if (path == "rebaseline")
        {
            entry.OriginalValues.SetValues(shipment);
            entry.State = EntityState.Unchanged;
            shipment.Notes = "Rebaselined entry still cannot remove persisted evidence";
        }
        var error = await Record.ExceptionAsync(async () =>
        {
            if (path == "sync") db.SaveChanges();
            else if (path == "acceptSync") db.SaveChanges(true);
            else if (path == "acceptAsync") await db.SaveChangesAsync(true);
            else await db.SaveChangesAsync();
        });
        db.ChangeTracker.Clear();
        Assert.NotNull(error);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainRequest_RowCannotDisappearAfterClearingCurrentEvidence(bool softDelete)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, "dhl",
            externalRegistrationState: "ReconciliationRequired");
        var shipment = await db.Shipments.SingleAsync();
        shipment.CarrierRequestSnapshotJson = "retained-uncertain-request";
        await db.SaveChangesAsync();
        shipment.CarrierRequestSnapshotJson = null;
        shipment.OriginSnapshotJson = null;
        shipment.ExternalRegistrationState = null;
        if (softDelete) shipment.IsDeleted = true;
        else db.Shipments.Remove(shipment);
        var error = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.IsType<InvalidOperationException>(error);
        var retained = await db.Shipments.IgnoreQueryFilters().SingleAsync();
        Assert.False(retained.IsDeleted);
        Assert.Equal("retained-uncertain-request", retained.CarrierRequestSnapshotJson);
        Assert.Equal("ReconciliationRequired", retained.ExternalRegistrationState);
        Assert.True(CarrierRegistrationEvidence.HasOutboundEvidence(retained));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvidenceFirstCaptureAndOrdinaryNotesRemainLegitimate(bool acceptOverload)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, "dhl");
        var shipment = await db.Shipments.SingleAsync();
        shipment.ParcelEvidenceJson = "original-measurement";
        shipment.CarrierRequestSnapshotJson = "original-request";
        shipment.CarrierRequestFingerprint = "original-fingerprint";
        shipment.ExternalRegistrationAttemptCount = 1;
        shipment.ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow;
        if (acceptOverload) await db.SaveChangesAsync(false);
        else await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        shipment = await db.Shipments.SingleAsync();
        shipment.Notes = "Ordinary logistics note";
        await db.SaveChangesAsync();
        Assert.Equal("original-request", shipment.CarrierRequestSnapshotJson);
        Assert.Equal("original-measurement", shipment.ParcelEvidenceJson);
        Assert.Equal("original-fingerprint", shipment.CarrierRequestFingerprint);
    }
}
