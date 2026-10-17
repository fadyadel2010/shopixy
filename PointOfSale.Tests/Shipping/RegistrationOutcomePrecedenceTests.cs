using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed partial class ShipmentExternalRegistrationSafetyTests
{
    [Fact]
    public async Task LatePositiveLookup_CannotReplaceCompletedIdentityBehindCancellationBarrier()
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); var store = $"OfflinePositiveRace_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(tenant, store);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, DeterministicCarrierCode,
            carrierRequestId: "original-lookup", externalRegistrationAttemptCount: 1, externalRegistrationState: "PendingExternalCall");
        var adapter = new DeterministicCarrierAdapter { BeforeLookup = async () =>
        {
            await using var winner = CreateDbContext(tenant, store);
            var current = await winner.Shipments.SingleAsync();
            current.Status = ShipmentStatus.ReadyForPickup;
            current.ExternalRegistrationCompletedAtUtc = DateTime.UtcNow;
            current.ExternalRegistrationState = "Completed";
            current.ExternalReferenceId = "winner-identity"; current.TrackingNumber = "winner-tracking";
            current.CarrierCreateResponse = "winner-response";
            winner.ShipmentCancellationOperations.Add(new PointOfSale.Core.Entities.ShipmentCancellationOperation
            {
                Id = Guid.NewGuid(), TenantId = tenant, ShipmentId = id, OrderId = current.OrderId,
                ActorIdentity = "offline", Source = "PosAdmin", Reason = "Synthetic retained barrier",
                CorrelationId = "offline", CarrierCode = current.CarrierCode, RequestFingerprint = "retained-operation-fingerprint",
                Outcome = ShipmentCancellationOutcome.ReconciliationRequired, Code = "CANCELLATION_REPLAY_UNRESOLVED"
            });
            await winner.SaveChangesAsync();
        } };
        adapter.AddExistingShipment("original-lookup", "late-lookup-identity", "late-lookup-tracking");
        var result = await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.Equal("CANCELLATION_BARRIER", result.ErrorCode);
        await using var inspect = CreateDbContext(tenant, store);
        var retained = await inspect.Shipments.SingleAsync();
        Assert.Equal("winner-identity", retained.ExternalReferenceId); Assert.Equal("winner-tracking", retained.TrackingNumber);
        Assert.Equal("winner-response", retained.CarrierCreateResponse); Assert.Equal("Completed", retained.ExternalRegistrationState);
        Assert.Null(retained.ExternalRegistrationError);
        Assert.Equal("CANCELLATION_REPLAY_UNRESOLVED", (await inspect.ShipmentCancellationOperations.SingleAsync()).Code);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCreateFailure_CannotDowngradeIndependentlyCompletedRegistration(bool exception)
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid(); var store = $"OfflineFailureRace_{Guid.NewGuid():N}";
        await using var db = CreateDbContext(tenant, store);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, DeterministicCarrierCode);
        var adapter = new DeterministicCarrierAdapter
        {
            CreateResult = new CreateShipmentResult { Success = false, ErrorCode = "SYNTHETIC_REFUSAL", ErrorMessage = "offline" },
            BeforeCreate = async () =>
            {
                await using var winner = CreateDbContext(tenant, store);
                var current = await winner.Shipments.SingleAsync();
                current.Status = ShipmentStatus.ReadyForPickup;
                current.ExternalRegistrationCompletedAtUtc = DateTime.UtcNow;
                current.ExternalRegistrationState = "Completed";
                current.ExternalReferenceId = "winner-identity";
                current.TrackingNumber = "winner-tracking";
                current.CarrierCreateResponse = "original-completed-response";
                await winner.SaveChangesAsync();
                if (exception) throw new InvalidOperationException("Synthetic transport failure after another winner");
            }
        };
        await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        await using var inspect = CreateDbContext(tenant, store);
        var retained = await inspect.Shipments.SingleAsync();
        Assert.Equal("Completed", retained.ExternalRegistrationState);
        Assert.Null(retained.ExternalRegistrationFailedAtUtc);
        Assert.Null(retained.ExternalRegistrationError);
        Assert.Equal("winner-identity", retained.ExternalReferenceId);
        Assert.Equal("original-completed-response", retained.CarrierCreateResponse);
        Assert.Equal(1, adapter.CreateShipmentCallCount);
    }

    [Fact]
    public async Task Registration_RejectsPendingCallerChangesWithoutSavingOrDiscardingThem()
    {
        var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        await using var db = CreateDbContext(tenant);
        SeedOrderAndShipment(db, tenant, id, ShipmentStatus.PendingExternalRegistration, DeterministicCarrierCode);
        var order = await db.Orders.SingleAsync();
        order.AdminNotes = "Pending caller-owned note";
        var adapter = new DeterministicCarrierAdapter();
        var result = await CreateService(db, tenant, adapter).ExecuteCarrierForExistingShipmentAsync(tenant, id, "standard");
        Assert.False(result.Success);
        Assert.Equal("REGISTRATION_PERSISTENCE_CONTEXT_BUSY", result.ErrorCode);
        Assert.Equal("Pending caller-owned note", order.AdminNotes);
        Assert.True(db.ChangeTracker.HasChanges());
        Assert.Null((await db.Orders.AsNoTracking().SingleAsync()).AdminNotes);
        Assert.Equal(0, adapter.CreateShipmentCallCount);
    }
}
