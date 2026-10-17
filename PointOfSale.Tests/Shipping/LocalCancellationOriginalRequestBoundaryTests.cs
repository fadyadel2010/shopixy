using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

/// <summary>Process-local EF fixtures only; no SQL, hosted runtime or provider transport.</summary>
public sealed class LocalCancellationOriginalRequestBoundaryTests : IDisposable
{
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid actor = Guid.NewGuid();
    private readonly ApplicationDbContext db;
    private readonly Mock<IShippingCarrierRegistry> registry = new(MockBehavior.Strict);
    private readonly Mock<ICarrierIntegrationAuthority> integration = new(MockBehavior.Strict);
    private readonly Mock<IEventPublisher> events = new();

    public LocalCancellationOriginalRequestBoundaryTests()
    {
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OfflineLocalCancellationProof_{Guid.NewGuid():N}").Options, new TestTenantContext(tenant));
        events.SetReturnsDefault(Task.CompletedTask);
    }

    private ShipmentCancellationService Service() => new(db, registry.Object, integration.Object, events.Object, new TestTenantContext(tenant));

    private async Task<Shipment> Seed(string carrier, string? evidence = null, string value = "retained-original", bool orderCancelled = false)
    {
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic local proof", Slug = $"proof-{tenant:N}", IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = $"actor-{actor:N}", Email = "proof@example.invalid",
            PasswordHash = "fixture", Role = UserRole.Warehouse, IsActive = true });
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"PROOF-{Guid.NewGuid():N}",
            Status = orderCancelled ? OrderStatus.Cancelled : OrderStatus.Confirmed, Currency = "EGP", Total = 100,
            FulfillmentMode = carrier == "manual" ? FulfillmentMode.Manual : FulfillmentMode.Carrier,
            FulfillmentCarrierCode = carrier == "manual" ? null : carrier };
        db.Orders.Add(order);
        var shipment = new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, CarrierCode = carrier,
            ShipmentNumber = $"SHP-{Guid.NewGuid():N}", Currency = "EGP", RecipientName = "Synthetic proof",
            Status = carrier == "manual" ? ShipmentStatus.Pending : ShipmentStatus.PendingExternalRegistration,
            CarrierRequestId = carrier == "manual" ? null : $"ship-{tenant:N}-{Guid.NewGuid():N}",
            ExternalRegistrationRequestedAtUtc = carrier == "manual" ? null : DateTime.UtcNow,
            ExternalRegistrationState = carrier == "manual" ? null : "PendingExternalCall",
            ParcelEvidenceJson = evidence == null ? null : "retained-parcel-evidence" };
        if (evidence == "snapshot") shipment.CarrierRequestSnapshotJson = value;
        else if (evidence == "fingerprint") shipment.CarrierRequestFingerprint = value;
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return shipment;
    }

    [Theory]
    [InlineData("bosta", "snapshot")]
    [InlineData("aramex", "snapshot")]
    [InlineData("dhl", "snapshot")]
    [InlineData("fedex", "snapshot")]
    [InlineData("mylerz", "snapshot")]
    [InlineData("manual", "snapshot")]
    [InlineData("bosta", "fingerprint")]
    [InlineData("aramex", "fingerprint")]
    [InlineData("dhl", "fingerprint")]
    [InlineData("fedex", "fingerprint")]
    [InlineData("mylerz", "fingerprint")]
    [InlineData("manual", "fingerprint")]
    public async Task RetainedOriginalRequest_CannotBecomeLocalAbsenceProof_AndReplayRemainsUnconfirmed(string carrier, string evidence)
    {
        var shipment = await Seed(carrier, evidence);
        await AssertUnconfirmedAndPreserved(shipment, orderCompensation: false);
    }

    [Theory]
    [InlineData("bosta", "snapshot", "")]
    [InlineData("bosta", "snapshot", " ")]
    [InlineData("bosta", "fingerprint", "")]
    [InlineData("bosta", "fingerprint", " ")]
    [InlineData("manual", "snapshot", "")]
    [InlineData("manual", "snapshot", " ")]
    [InlineData("manual", "fingerprint", "")]
    [InlineData("manual", "fingerprint", " ")]
    public async Task NonNullMalformedRequestEvidence_IsUncertainty_NotPermissionToConfirmLocally(string carrier, string evidence, string value)
    {
        var shipment = await Seed(carrier, evidence, value);
        await AssertUnconfirmedAndPreserved(shipment, orderCompensation: false);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("fingerprint")]
    public async Task OrderCompensation_CannotReplaceOriginalRequestEvidenceWithNoExternalShipment(string evidence)
    {
        var shipment = await Seed("bosta", evidence, orderCancelled: true);
        await AssertUnconfirmedAndPreserved(shipment, orderCompensation: true);
    }

    private async Task AssertUnconfirmedAndPreserved(Shipment original, bool orderCompensation)
    {
        var shipmentBefore = JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync());
        var orderBefore = JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync());
        var service = Service();
        const string reason = "Synthetic original-request uncertainty";
        var result = orderCompensation ? await service.CancelForOrderAsync(tenant, original.Id, reason) :
            await service.CancelForAdminAsync(tenant, original.Id, actor, null, reason, "offline-local-proof");
        Assert.False(result.Confirmed);
        Assert.Equal("ReconciliationRequired", result.Outcome);
        Assert.Equal("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", result.Code);
        Assert.Null(result.ConfirmationBasis);
        Assert.Null(result.ConfirmedAtUtc);
        Assert.False(result.ExternalConfirmationExists);
        var operation = await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentCancellationOutcome.ReconciliationRequired, operation.Outcome);
        Assert.Null(operation.AttemptStartedAtUtc);
        Assert.Null(operation.ConfigurationId);
        var operationBefore = JsonSerializer.Serialize(operation);
        var replay = orderCompensation ? await service.CancelForOrderAsync(tenant, original.Id, reason) :
            await service.CancelForAdminAsync(tenant, original.Id, actor, Guid.NewGuid(), reason, "offline-local-proof-replay");
        Assert.Equal(result.OperationId, replay.OperationId);
        Assert.False(replay.Confirmed);
        Assert.Equal(result.Code, replay.Code);
        Assert.Equal(operationBefore, JsonSerializer.Serialize(await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync()));
        Assert.Equal(shipmentBefore, JsonSerializer.Serialize(await db.Shipments.AsNoTracking().SingleAsync()));
        Assert.Equal(orderBefore, JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync()));
        registry.VerifyNoOtherCalls();
        integration.VerifyNoOtherCalls();
        events.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("fedex")]
    [InlineData("mylerz")]
    [InlineData("manual")]
    public async Task TrulyUnattemptedIntent_StillAllowsExistingLocalCancellationWithoutProviderCalls(string carrier)
    {
        var shipment = await Seed(carrier);
        var orderBefore = JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync());
        var result = await Service().CancelForAdminAsync(tenant, shipment.Id, actor, null, "Synthetic never-attempted intent", "offline-local-proof");
        Assert.True(result.Confirmed);
        Assert.Equal(carrier == "manual" ? "LocalOnly" : "NoExternalShipment", result.ConfirmationBasis);
        Assert.False(result.ExternalConfirmationExists);
        Assert.Equal(ShipmentStatus.Cancelled, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Null((await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync()).AttemptStartedAtUtc);
        Assert.Equal(orderBefore, JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync()));
        events.Verify(e => e.PublishAsync(It.IsAny<ShipmentCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        events.Verify(e => e.PublishAsync(It.IsAny<ShipmentStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        events.VerifyNoOtherCalls();
        registry.VerifyNoOtherCalls();
        integration.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("fingerprint")]
    public async Task GenericManualStatusWriter_CannotBypassOriginalRequestUncertainty(string evidence)
    {
        await Seed("manual", evidence);
        var shipment = await db.Shipments.SingleAsync();
        shipment.Status = ShipmentStatus.Cancelled;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("durable confirmation authority", error.Message);
        Assert.Equal(ShipmentStatus.Pending, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.ShipmentCancellationOperations.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("fingerprint")]
    public async Task GenericManualStatusWriter_CannotClearRetainedEvidenceBeforeCancellation(string evidence)
    {
        await Seed("manual", evidence);
        var shipment = await db.Shipments.SingleAsync();
        shipment.CarrierRequestSnapshotJson = null;
        shipment.CarrierRequestFingerprint = null;
        shipment.Status = ShipmentStatus.Cancelled;
        var error = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(ShipmentStatus.Pending, (await db.Shipments.SingleAsync()).Status);
        Assert.Empty(await db.ShipmentCancellationOperations.ToListAsync());
    }

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task Cancellation_RejectsPendingCallerChangesWithoutClearingThem()
    {
        var original = await Seed("manual");
        var shipment = await db.Shipments.SingleAsync();
        shipment.Notes = "Pending caller-owned note";
        var error = await Record.ExceptionAsync(() => Service().CancelForAdminAsync(tenant, original.Id, actor, null,
            "Synthetic clean cancellation", "offline-busy"));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("Pending caller-owned note", shipment.Notes);
        Assert.True(db.ChangeTracker.HasChanges());
        Assert.Empty(await db.ShipmentCancellationOperations.ToListAsync());
        registry.VerifyNoOtherCalls(); integration.VerifyNoOtherCalls(); events.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("fedex")]
    [InlineData("mylerz")]
    public async Task UnsupportedRemoteCancellation_IsRejectedBeforeAccountOrAdapterResolution(string carrier)
    {
        var original = await Seed(carrier);
        var shipment = await db.Shipments.SingleAsync();
        shipment.Status = ShipmentStatus.ReadyForPickup;
        shipment.ExternalRegistrationState = "Completed";
        shipment.ExternalRegistrationCompletedAtUtc = DateTime.UtcNow;
        shipment.TrackingNumber = "SYNTHETIC-COMPLETED";
        await db.SaveChangesAsync();
        var result = await Service().CancelForAdminAsync(tenant, original.Id, actor, null, "Synthetic unsupported command", "offline-support");
        Assert.Equal("Unsupported", result.Outcome);
        Assert.Equal("CARRIER_CANCELLATION_UNSUPPORTED", result.Code);
        Assert.Null((await db.ShipmentCancellationOperations.SingleAsync()).AttemptStartedAtUtc);
        registry.VerifyNoOtherCalls(); integration.VerifyNoOtherCalls(); events.VerifyNoOtherCalls();
    }
}
