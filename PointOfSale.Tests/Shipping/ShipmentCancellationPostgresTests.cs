using System.Net;
using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.API.Consumers;
using PointOfSale.API.Services.Shipping;
using PointOfSale.API.DTOs.Shipping;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;

namespace PointOfSale.Tests.Shipping;

public sealed class ShipmentCancellationPostgresTests : IDisposable
{
    // Generic cancellation persistence fixtures are not Bosta contract or
    // account certification. Real Bosta/Aramex require the original create intent.
    private const string DeterministicCarrierCode = "test-cancellation";
    private readonly TestDatabaseScope scope = TestDatabaseScope.Open();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid actor = Guid.NewGuid();
    private readonly Mock<IShippingCarrierAdapter> adapter = new();
    private readonly Mock<ICarrierIntegrationAuthority> integration = new();
    private readonly Mock<IEventPublisher> events = new();
    private readonly Mock<IShippingCarrierRegistry> registry = new();
    private readonly CommerceReliabilityTelemetry telemetry = new();
    private readonly Guid configurationId = Guid.NewGuid();
    private int calls;

    public ShipmentCancellationPostgresTests()
    {
        events.SetReturnsDefault(Task.CompletedTask);
        registry.Setup(x => x.GetAdapter(It.IsAny<string>())).Returns(adapter.Object);
        integration.Setup(x => x.ResolveCredentialsAsync(tenant, It.IsAny<string>(), CarrierIntegrationPurpose.Cancellation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierCredentialsResolution(true, DeterministicCarrierCode, new CarrierCredentials { ApiKey = "mock-only", AccountId = "account-1" },
                JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($"{{\"Id\":\"{configurationId}\",\"CarrierCode\":\"{DeterministicCarrierCode}\",\"SupportsCancellation\":true}}")!, []));
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return Confirmation(); });
    }

    private static CancelShipmentResult Confirmation() => new()
    {
        Disposition = CarrierCancellationDisposition.Confirmed, TargetTrackingNumber = "TRACK-1",
        EvidenceReference = "provider-cancellation-1", EvidenceHash = new string('A', 64)
    };
    private ShipmentCancellationService Service(ApplicationDbContext db) => new(db, registry.Object, integration.Object, events.Object, new TestTenantContext(tenant));
    private Task<ShipmentCancellationResult> Cancel(ApplicationDbContext db, Guid shipment, string reason = "Customer request", Guid? id = null) =>
        Service(db).CancelForAdminAsync(tenant, shipment, actor, id, reason, "isolated-cancellation-test");

    private async Task<Shipment> Seed(string carrier = DeterministicCarrierCode, Action<Shipment>? configure = null, bool cancelledOrder = false, Action<Order>? configureOrder = null)
    {
        await using var db = scope.CreateDbContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Isolated cancellation", Slug = $"cancel-{tenant:N}", IsActive = true, Status = TenantStatus.Active });
        db.Users.Add(new User { Id = actor, TenantId = tenant, Username = $"actor-{actor:N}", Email = $"{actor:N}@example.invalid", PasswordHash = "mock", Role = UserRole.Warehouse, IsActive = true });
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"CANCEL-{Guid.NewGuid():N}", Status = cancelledOrder ? OrderStatus.Cancelled : OrderStatus.Confirmed,
            Currency = "EGP", Total = 100, ShippingAddress = "Isolated street", PaymentStatus = PaymentStatus.Pending };
        order.FulfillmentMode = carrier == "manual" ? FulfillmentMode.Manual : FulfillmentMode.Carrier;
        order.FulfillmentCarrierCode = carrier == "manual" ? null : carrier;
        configureOrder?.Invoke(order);
        db.Orders.Add(order);
        var shipment = new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id, CarrierCode = carrier,
            ShipmentNumber = $"SHP-{Guid.NewGuid():N}", RecipientName = "Fixture", ShippingAddress = "Isolated street", Currency = "EGP",
            Status = carrier == "manual" ? ShipmentStatus.Pending : ShipmentStatus.ReadyForPickup,
            TrackingNumber = carrier == "manual" ? null : "TRACK-1", ExternalReferenceId = carrier == "manual" ? null : "delivery-1",
            ExternalRegistrationState = carrier == "manual" ? null : "Completed",
            ExternalRegistrationCompletedAtUtc = carrier == "manual" ? null : DateTime.UtcNow,
            ExternalRegistrationAttemptCount = carrier == "manual" ? 0 : 1 };
        configure?.Invoke(shipment);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        return shipment;
    }

    [Fact]
    public async Task Manual_Confirmed_Replay_PreservesActor_EventsAndAllFinancialBoundaries()
    {
        var shipment = await Seed("manual");
        await using var db = scope.CreateDbContext(tenant);
        var beforeOrder = JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync());
        var first = await Cancel(db, shipment.Id);
        await using var replayDb = scope.CreateDbContext(tenant);
        var replay = await Cancel(replayDb, shipment.Id, id: Guid.NewGuid());
        Assert.True(first.Confirmed);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal("LocalOnly", replay.ConfirmationBasis);
        Assert.Equal(actor, (await replayDb.ShipmentCancellationOperations.SingleAsync()).CreatedBy);
        Assert.Equal(beforeOrder, JsonSerializer.Serialize(await replayDb.Orders.AsNoTracking().SingleAsync()));
        Assert.Empty(await replayDb.PaymentIntents.ToListAsync());
        Assert.Empty(await replayDb.OrderRefunds.ToListAsync());
        Assert.Empty(await replayDb.AccountingOutboxMessages.ToListAsync());
        Assert.Empty(await replayDb.JournalEntries.ToListAsync());
        Assert.Empty(await replayDb.StockMovements.ToListAsync());
        Assert.Empty(await replayDb.PaymentCancellationOutboxMessages.ToListAsync());
        events.Verify(x => x.PublishAsync(It.IsAny<ShipmentCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        events.Verify(x => x.PublishAsync(It.IsAny<ShipmentStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExternalConfirmation_CommitsOnlyAfterBoundEvidence_AndFreshState()
    {
        var shipment = await Seed();
        await using var beforeDb = scope.CreateDbContext(tenant);
        var orderBefore = JsonSerializer.Serialize(await beforeDb.Orders.AsNoTracking().SingleAsync());
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                calls++;
                await using var current = scope.CreateDbContext(tenant);
                Assert.Equal(ShipmentStatus.ReadyForPickup, (await current.Shipments.SingleAsync()).Status);
                Assert.NotNull((await current.ShipmentCancellationOperations.SingleAsync()).AttemptStartedAtUtc);
                return Confirmation();
            });
        await using var db = scope.CreateDbContext(tenant);
        var result = await Cancel(db, shipment.Id);
        Assert.True(result.Confirmed);
        Assert.True(result.ExternalConfirmationExists);
        Assert.Equal(ShipmentStatus.Cancelled, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(new string('A', 64), (await db.ShipmentCancellationOperations.SingleAsync()).EvidenceHash);
        Assert.Equal(orderBefore, JsonSerializer.Serialize(await db.Orders.AsNoTracking().SingleAsync()));
        Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
        Assert.Empty(await db.PaymentCancellationOutboxMessages.ToListAsync());
        Assert.Empty(await db.StockMovements.ToListAsync());
        Assert.Empty(await db.OrderRefunds.ToListAsync());
        events.Verify(x => x.PublishAsync(It.IsAny<ShipmentCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(CarrierCancellationDisposition.Unknown)]
    [InlineData(CarrierCancellationDisposition.Pending)]
    [InlineData(CarrierCancellationDisposition.Rejected)]
    [InlineData(CarrierCancellationDisposition.Unsupported)]
    public async Task UnconfirmedTypedResult_EvenLegacySuccessTrue_NeverFinalizes(CarrierCancellationDisposition disposition)
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new CancelShipmentResult { Success = true, Disposition = disposition }; });
        await using var db = scope.CreateDbContext(tenant);
        var result = await Cancel(db, shipment.Id);
        Assert.False(result.Confirmed);
        Assert.Null(result.ConfirmedAtUtc);
        Assert.False(result.ExternalConfirmationExists);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        var replay = await Cancel(db, shipment.Id, id: Guid.NewGuid());
        Assert.Equal(result.OperationId, replay.OperationId);
        Assert.Equal(1, calls);
        events.Verify(x => x.PublishAsync(It.IsAny<ShipmentCancelledEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("reference-only")]
    [InlineData("attempt")]
    [InlineData("completed-no-reference")]
    [InlineData("create-response")]
    [InlineData("reconciliation")]
    [InlineData("manual-external")]
    public async Task ExternalCreationFacts_NeverAllowFalseLocalCancellation(string scenario)
    {
        var shipment = await Seed(scenario == "manual-external" ? "manual" : DeterministicCarrierCode, s =>
        {
            s.TrackingNumber = null;
            if (scenario != "reference-only") s.ExternalReferenceId = null;
            if (scenario == "attempt") { s.ExternalRegistrationCompletedAtUtc = null; s.ExternalRegistrationState = "PendingExternalCall"; }
            if (scenario == "create-response") s.CarrierCreateResponse = "{\"accepted\":true}";
            if (scenario == "reconciliation") s.ExternalRegistrationState = "ReconciliationRequired";
            if (scenario == "manual-external") s.AwbNumber = "AWB-1";
        });
        await using var db = scope.CreateDbContext(tenant);
        Assert.False((await Cancel(db, shipment.Id)).Confirmed);
        Assert.NotEqual(ShipmentStatus.Cancelled, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task NeverAttemptedExternalIntent_LocalProof_StopsSubsequentRegistrationClaim()
    {
        var shipment = await Seed(DeterministicCarrierCode, s => { s.Status = ShipmentStatus.PendingExternalRegistration; s.TrackingNumber = s.ExternalReferenceId = null;
            s.ExternalRegistrationCompletedAtUtc = null; s.ExternalRegistrationAttemptCount = 0; s.ExternalRegistrationRequestedAtUtc = DateTime.UtcNow; s.ExternalRegistrationState = "PendingExternalCall"; });
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal("NoExternalShipment", (await Cancel(db, shipment.Id)).ConfirmationBasis);
        await using var other = scope.CreateDbContext(tenant);
        Assert.Equal(0, await Claim(other, shipment));
    }

    private CarrierShipmentService CarrierService(ApplicationDbContext db) => new(db, registry.Object,
        Mock.Of<ICarrierCredentialEncryptionService>(), events.Object, Mock.Of<IOrderFulfillmentService>(), telemetry, new TestTenantContext(tenant),
        Mock.Of<ICustomsEvidenceAdmissionService>(), Mock.Of<ICarrierGeographicMappingService>(), NullLogger<CarrierShipmentService>.Instance, integration.Object);
    private async Task<int> Claim(ApplicationDbContext db, Shipment shipment) => await (Task<int>)typeof(CarrierShipmentService)
        .GetMethod("ClaimShipmentForExternalCallAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(CarrierService(db), [shipment.Id, tenant, 0, 1, DateTime.UtcNow, "standard", $"ship-{tenant:N}-{shipment.Id:N}", CancellationToken.None])!;

    [Fact]
    public async Task RegistrationClaim_Wins_CancellationSeesAttemptAndNeverFinalizes()
    {
        var shipment = await Seed(DeterministicCarrierCode, s => { s.Status = ShipmentStatus.PendingExternalRegistration; s.TrackingNumber = s.ExternalReferenceId = null;
            s.ExternalRegistrationCompletedAtUtc = null; s.ExternalRegistrationAttemptCount = 0; s.ExternalRegistrationState = "PendingExternalCall"; s.ExternalRegistrationRequestedAtUtc = DateTime.UtcNow; });
        await using var claimDb = scope.CreateDbContext(tenant);
        Assert.Equal(1, await Claim(claimDb, shipment));
        await using var cancelDb = scope.CreateDbContext(tenant);
        Assert.Equal("ReconciliationRequired", (await Cancel(cancelDb, shipment.Id)).Outcome);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RegistrationSuccessAfterCancellation_RetainsExternalFacts_NotLifecycleOrBarrier()
    {
        var shipment = await Seed(DeterministicCarrierCode, s => { s.Status = ShipmentStatus.PendingExternalRegistration; s.TrackingNumber = s.ExternalReferenceId = null;
            s.ExternalRegistrationCompletedAtUtc = null; s.ExternalRegistrationState = "PendingExternalCall"; });
        await using var db = scope.CreateDbContext(tenant);
        var cancel = await Cancel(db, shipment.Id);
        var opBefore = await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync();
        var current = await db.Shipments.SingleAsync();
        var order = await db.Orders.SingleAsync();
        var registration = await (Task<CarrierShipmentResult?>)typeof(CarrierShipmentService)
            .GetMethod("RejectStaleCarrierRegistrationAuthorityAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(CarrierService(db), [current, order, "test", CancellationToken.None, new CreateShipmentResult { Success = true, TrackingNumber = "NEW-TRACK", ExternalReferenceId = "accepted-id" }])!;
        Assert.Equal("CANCELLATION_BARRIER", registration!.ErrorCode);
        Assert.Equal(ShipmentStatus.PendingExternalRegistration, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("accepted-id", (await db.Shipments.AsNoTracking().SingleAsync()).ExternalReferenceId);
        Assert.Equal(opBefore.RequestFingerprint, (await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync()).RequestFingerprint);
        Assert.Equal(cancel.OperationId, (await Cancel(db, shipment.Id)).OperationId);
    }

    [Fact]
    public async Task TimeoutAfterPossibleAcceptance_IsDurableAndCannotRetryProvider()
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(() => { calls++; throw new TaskCanceledException("lost provider response"); });
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal("ReconciliationRequired", (await Cancel(db, shipment.Id)).Outcome);
        await using var replayDb = scope.CreateDbContext(tenant);
        Assert.False((await Cancel(replayDb, shipment.Id)).Confirmed);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("identity")]
    [InlineData("actor")]
    public async Task PostCallChange_DetectedBeforeCommit_PreservesEvidence(string change)
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                calls++;
                await using var other = scope.CreateDbContext(tenant);
                if (change == "actor") { var user = await other.Users.SingleAsync(); user.IsActive = false; }
                else { var s = await other.Shipments.SingleAsync(); if (change == "status") s.Status = ShipmentStatus.PickedUp; else s.TrackingNumber = "other-target"; }
                await other.SaveChangesAsync();
                return Confirmation();
            });
        await using var db = scope.CreateDbContext(tenant);
        var result = await Cancel(db, shipment.Id);
        Assert.False(result.Confirmed);
        Assert.Equal(new string('A', 64), (await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync()).EvidenceHash);
        Assert.NotEqual(ShipmentStatus.Cancelled, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task SeparateContexts_ConcurrentRequests_OneAttempt_NoConflictingConfirmation()
    {
        var shipment = await Seed();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { Interlocked.Increment(ref calls); entered.SetResult(); await release.Task; return Confirmation(); });
        await using var firstDb = scope.CreateDbContext(tenant);
        await using var secondDb = scope.CreateDbContext(tenant);
        var first = Cancel(firstDb, shipment.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await Cancel(secondDb, shipment.Id, id: Guid.NewGuid());
        release.SetResult();
        var completed = await first;
        Assert.False(second.Confirmed);
        Assert.False(completed.Confirmed); // concurrent replay closed an unresolved claim
        Assert.Equal(second.OperationId, completed.OperationId);
        Assert.Equal(1, calls);
        await using var fresh = scope.CreateDbContext(tenant);
        Assert.Single(await fresh.ShipmentCancellationOperations.ToListAsync());
        Assert.NotEqual(ShipmentStatus.Cancelled, (await fresh.Shipments.SingleAsync()).Status);
    }

    [Fact]
    public async Task FingerprintChange_Conflicts_AndOriginalActorReasonRemainImmutable()
    {
        var shipment = await Seed("manual");
        await using var db = scope.CreateDbContext(tenant);
        var original = await Cancel(db, shipment.Id);
        var changed = await Cancel(db, shipment.Id, "different reason", Guid.NewGuid());
        Assert.Equal("CANCELLATION_REQUEST_CONFLICT", changed.Code);
        Assert.False(changed.Confirmed);
        var op = await db.ShipmentCancellationOperations.SingleAsync();
        Assert.Equal(original.OperationId, op.Id);
        Assert.Equal("Customer request", op.Reason);
        op.Reason = "tamper";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PermanentUniqueConstraint_AndOptimisticToken_AreActuallyEnforcedByPostgres()
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CancelShipmentResult { Disposition = CarrierCancellationDisposition.Unknown });
        await using var db = scope.CreateDbContext(tenant);
        await Cancel(db, shipment.Id);
        await using var first = scope.CreateDbContext(tenant);
        await using var second = scope.CreateDbContext(tenant);
        var a = await first.ShipmentCancellationOperations.SingleAsync();
        var b = await second.ShipmentCancellationOperations.SingleAsync();
        a.Code = "REVIEW_A"; a.Version = Guid.NewGuid(); await first.SaveChangesAsync();
        b.Code = "REVIEW_B"; b.Version = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var duplicateDb = scope.CreateDbContext(tenant);
        var duplicate = JsonSerializer.Deserialize<ShipmentCancellationOperation>(JsonSerializer.Serialize(a))!;
        duplicate.Id = Guid.NewGuid(); duplicate.IsDeleted = true;
        duplicateDb.ShipmentCancellationOperations.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrashClaimBeforeCallOrAfterSuccess_ReplayNeverSendsReplacement(bool afterSuccess)
    {
        var shipment = await Seed();
        // Simulate a process crash using the same durable facts committed before
        // the outbound boundary. No provider is contacted by this fixture.
        await using var db = scope.CreateDbContext(tenant);
        var op = new ShipmentCancellationOperation { Id = Guid.NewGuid(), TenantId = tenant, ShipmentId = shipment.Id, OrderId = shipment.OrderId,
            CarrierCode = shipment.CarrierCode, TrackingNumber = shipment.TrackingNumber, ExternalReferenceId = shipment.ExternalReferenceId,
            ActorIdentity = actor.ToString(), CreatedBy = actor, Reason = "Customer request", Source = "PosAdmin", RequestedAtUtc = DateTime.UtcNow,
            AttemptStartedAtUtc = DateTime.UtcNow, PreviousStatus = shipment.Status, Code = "CANCELLATION_ATTEMPT_UNRESOLVED", Outcome = ShipmentCancellationOutcome.ReconciliationRequired };
        var fingerprint = typeof(ShipmentCancellationService).GetMethod("Fingerprint", BindingFlags.Static | BindingFlags.NonPublic)!;
        op.RequestFingerprint = (string)fingerprint.Invoke(null, [op, op.Reason])!;
        if (afterSuccess) { op.EvidenceReference = "external-confirmation-not-committed"; op.EvidenceHash = new string('A', 64); }
        db.ShipmentCancellationOperations.Add(op); await db.SaveChangesAsync();
        await using var replay = scope.CreateDbContext(tenant);
        Assert.False((await Cancel(replay, shipment.Id, id: Guid.NewGuid())).Confirmed);
        Assert.Equal(0, calls);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await replay.Shipments.SingleAsync()).Status);
    }

    [Fact]
    public async Task ConfirmationPersistenceFailure_RollsBackLifecycle_AndRetainsEvidence_NoResend()
    {
        var shipment = await Seed();
        events.Setup(x => x.PublishAsync(It.IsAny<ShipmentStatusChangedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("isolated outbox failure"));
        await using var db = scope.CreateDbContext(tenant);
        Assert.False((await Cancel(db, shipment.Id)).Confirmed);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(new string('A', 64), (await db.ShipmentCancellationOperations.AsNoTracking().SingleAsync()).EvidenceHash);
        await Cancel(db, shipment.Id);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("foreign-tenant")]
    [InlineData("cashier")]
    [InlineData("inactive")]
    [InlineData("missing-actor")]
    public async Task CrossTenantAndUnauthorized_AreRejectedBeforeAnyOperation(string scenario)
    {
        var shipment = await Seed();
        await using var db = scope.CreateDbContext(tenant);
        if (scenario is "cashier" or "inactive") { var user = await db.Users.SingleAsync(); user.Role = scenario == "cashier" ? UserRole.Cashier : UserRole.Owner; user.IsActive = scenario != "inactive"; await db.SaveChangesAsync(); }
        if (scenario == "foreign-tenant")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).CancelForAdminAsync(Guid.NewGuid(), shipment.Id, actor, null, "reason", "test"));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(db).CancelForAdminAsync(tenant, shipment.Id, scenario == "missing-actor" ? Guid.NewGuid() : actor, null, "reason", "test"));
        Assert.Empty(await db.ShipmentCancellationOperations.ToListAsync());
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(ShipmentStatus.Cancelled, "LEGACY_CANCELLATION_UNVERIFIED")]
    [InlineData(ShipmentStatus.PickedUp, "CANNOT_CANCEL")]
    public async Task LegacyOrPickedUp_NeverConfirmOrRewriteStoredHistory(ShipmentStatus status, string code)
    {
        var shipment = await Seed(DeterministicCarrierCode, s => { s.Status = status; s.CancelledAtUtc = DateTime.UtcNow.AddDays(-1); });
        Assert.Equal(status == ShipmentStatus.Cancelled ? "ReconciliationRequired" : null,
            ShipmentCancellationService.ReadResult(shipment, null)?.Outcome);
        await using var db = scope.CreateDbContext(tenant);
        var storedTime = (await db.Shipments.AsNoTracking().SingleAsync()).CancelledAtUtc;
        var result = await Cancel(db, shipment.Id);
        Assert.False(result.Confirmed); Assert.Equal(code, result.Code);
        Assert.Equal(status, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(storedTime, (await db.Shipments.AsNoTracking().SingleAsync()).CancelledAtUtc);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task OrderCompensationReplay_UsesOriginalDurableResult_AndKeepsCanonicalOrder()
    {
        var shipment = await Seed(cancelledOrder: true);
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new CancelShipmentResult { Disposition = CarrierCancellationDisposition.Unsupported }; });
        await using var first = scope.CreateDbContext(tenant);
        var result = await CarrierService(first).CancelShipmentAsync(tenant, shipment.Id, "Order cancelled");
        await using var second = scope.CreateDbContext(tenant);
        var replay = await CarrierService(second).CancelShipmentAsync(tenant, shipment.Id, "Order cancelled");
        Assert.False(result.Success); Assert.False(replay.Success);
        Assert.Equal(result.Cancellation!.OperationId, replay.Cancellation!.OperationId);
        Assert.Equal(1, calls);
        Assert.Equal(OrderStatus.Cancelled, (await second.Orders.SingleAsync()).Status);
        Assert.Equal("OrderCancellation", (await second.ShipmentCancellationOperations.SingleAsync()).Source);
    }

    [Fact]
    public async Task WaitingRegistrationClaim_SeesCancellationCommittedWhileItsSqlSnapshotWasWaiting()
    {
        var shipment = await Seed(DeterministicCarrierCode, s => { s.Status = ShipmentStatus.PendingExternalRegistration; s.TrackingNumber = s.ExternalReferenceId = null;
            s.ExternalRegistrationCompletedAtUtc = null; s.ExternalRegistrationAttemptCount = 0; s.ExternalRegistrationState = "PendingExternalCall"; });
        await using var blocker = scope.CreateDbContext(tenant);
        await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM \"Shipments\" WHERE \"Id\"={shipment.Id} FOR UPDATE");
        await using var waiting = scope.CreateDbContext(tenant);
        var claim = Claim(waiting, shipment);
        await Task.Delay(100);
        Assert.False(claim.IsCompleted);
        blocker.ShipmentCancellationOperations.Add(new ShipmentCancellationOperation { Id = Guid.NewGuid(), TenantId = tenant, ShipmentId = shipment.Id, OrderId = shipment.OrderId,
            CarrierCode = shipment.CarrierCode, Reason = "claim fixture", ActorIdentity = actor.ToString(), Source = "PosAdmin", RequestFingerprint = new string('A', 64), Code = "CANCELLATION_ATTEMPT_UNRESOLVED" });
        await blocker.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal(0, await claim.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, (await waiting.Shipments.AsNoTracking().SingleAsync()).ExternalRegistrationAttemptCount);
    }

    [Fact]
    public async Task WaitingCancellation_SeesRegistrationCommittedWhileWaitingForShipmentLock()
    {
        var shipment = await Seed(DeterministicCarrierCode, s => { s.Status = ShipmentStatus.PendingExternalRegistration; s.TrackingNumber = s.ExternalReferenceId = null;
            s.ExternalRegistrationCompletedAtUtc = null; s.ExternalRegistrationAttemptCount = 0; s.ExternalRegistrationState = "PendingExternalCall"; s.ExternalRegistrationRequestedAtUtc = DateTime.UtcNow; });
        await using var blocker = scope.CreateDbContext(tenant);
        await using var tx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT * FROM \"Shipments\" WHERE \"Id\"={shipment.Id} FOR UPDATE");
        await using var waiting = scope.CreateDbContext(tenant);
        var cancel = Cancel(waiting, shipment.Id);
        await Task.Delay(100);
        Assert.False(cancel.IsCompleted);
        var current = await blocker.Shipments.SingleAsync(); current.ExternalRegistrationAttemptCount = 1; current.ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow;
        await blocker.SaveChangesAsync(); await tx.CommitAsync();
        Assert.False((await cancel.WaitAsync(TimeSpan.FromSeconds(10))).Confirmed);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("carrier")]
    [InlineData("manual-external")]
    [InlineData("manual-owner")]
    public async Task GenericCarrierWriter_CannotPersistCancelledWithoutDurableConfirmation(string scenario)
    {
        var seeded = await Seed(scenario == "carrier" ? DeterministicCarrierCode : "manual",
            s => { if (scenario == "manual-external") s.AwbNumber = "externally-accepted-awb"; },
            configureOrder: order => { if (scenario == "manual-owner") { order.FulfillmentMode = FulfillmentMode.Carrier; order.FulfillmentCarrierCode = DeterministicCarrierCode; } });
        await using var db = scope.CreateDbContext(tenant);
        (await db.Shipments.SingleAsync()).Status = ShipmentStatus.Cancelled;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await using var fresh = scope.CreateDbContext(tenant);
        Assert.Equal(seeded.Status, (await fresh.Shipments.SingleAsync()).Status);
    }

    [Fact]
    public async Task ForeignShipmentId_ReturnsNotFound_WithoutAnyCrossTenantRecord()
    {
        await Seed();
        var foreignTenant = Guid.NewGuid(); var foreignShipment = Guid.NewGuid(); var foreignOrder = Guid.NewGuid();
        await using (var foreign = scope.CreateDbContext(foreignTenant))
        {
            foreign.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Foreign isolated fixture", Slug = $"foreign-{foreignTenant:N}", Status = TenantStatus.Active, IsActive = true });
            foreign.Orders.Add(new Order { Id = foreignOrder, TenantId = foreignTenant, OrderNumber = $"FOREIGN-{foreignOrder:N}", ShippingAddress = "Fixture", Currency = "EGP" });
            foreign.Shipments.Add(new Shipment { Id = foreignShipment, TenantId = foreignTenant, OrderId = foreignOrder, ShipmentNumber = "FOREIGN", ShippingAddress = "Fixture", RecipientName = "Fixture", Currency = "EGP" });
            await foreign.SaveChangesAsync();
        }
        await using var db = scope.CreateDbContext(tenant);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Cancel(db, foreignShipment));
        Assert.Empty(await db.ShipmentCancellationOperations.ToListAsync());
    }

    [Fact]
    public async Task ConfirmationWithWrongTargetOrMissingEvidence_IsNotAuthority()
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CancelShipmentResult { Disposition = CarrierCancellationDisposition.Confirmed, TargetTrackingNumber = "wrong", EvidenceHash = new string('A', 64), EvidenceReference = "wrong-target" });
        await using var db = scope.CreateDbContext(tenant);
        Assert.False((await Cancel(db, shipment.Id)).Confirmed);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task AccountEnvironmentChangeAfterProviderCall_IsReconciliation()
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls++;
                integration.Setup(x => x.ResolveCredentialsAsync(tenant, DeterministicCarrierCode, CarrierIntegrationPurpose.Cancellation, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CarrierCredentialsResolution(true, DeterministicCarrierCode, new CarrierCredentials { ApiKey = "mock-only", AccountId = "changed-account", UseSandbox = true },
                        JsonSerializer.Deserialize<CarrierIntegrationConfigurationSnapshot>($"{{\"Id\":\"{configurationId}\",\"CarrierCode\":\"{DeterministicCarrierCode}\"}}")!, []));
                return Task.FromResult(Confirmation());
            });
        await using var db = scope.CreateDbContext(tenant);
        Assert.Equal("CANCELLATION_ACCOUNT_CHANGED", (await Cancel(db, shipment.Id)).Code);
        Assert.NotEqual(ShipmentStatus.Cancelled, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task OrderCancellationConsumer_RedeliveryAcknowledgesDurableReconciliation_NoInfiniteRetry()
    {
        var shipment = await Seed(cancelledOrder: true);
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new CancelShipmentResult { Disposition = CarrierCancellationDisposition.Unknown }; });
        await using var db = scope.CreateDbContext(tenant);
        var shipmentReader = new Mock<IShipmentService>();
        shipmentReader.Setup(x => x.GetShipmentByOrderIdAsync(tenant, shipment.OrderId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorefrontShipmentDto { ShipmentId = shipment.Id });
        var context = new Mock<ConsumeContext<OrderCancelledEvent>>();
        context.SetupGet(x => x.Message).Returns(new OrderCancelledEvent { TenantId = tenant, OrderId = shipment.OrderId, CancellationReason = "Order cancelled" });
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        var consumer = new OrderShipmentOrchestrationConsumer(db, shipmentReader.Object, CarrierService(db), NullLogger<OrderShipmentOrchestrationConsumer>.Instance);
        await consumer.Consume(context.Object);
        await consumer.Consume(context.Object);
        Assert.Equal(1, calls);
        Assert.Equal(ShipmentCancellationOutcome.ReconciliationRequired, (await db.ShipmentCancellationOperations.SingleAsync()).Outcome);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await db.Shipments.SingleAsync()).Status);
    }

    [Fact]
    public async Task ActualAdminFacade_UsesSharedExecutor_DetailAndListReloadDurableOutcome()
    {
        var shipment = await Seed();
        adapter.Setup(x => x.CancelShipmentAsync(It.IsAny<string>(), It.IsAny<CarrierCredentials>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new CancelShipmentResult { Disposition = CarrierCancellationDisposition.Unsupported }; });
        await using var db = scope.CreateDbContext(tenant);
        AdminShipmentService Admin(ApplicationDbContext context) => new(context, events.Object, Mock.Of<IOrderFulfillmentService>(),
            Mock.Of<IMarketplaceEventOutboxService>(), NullLogger<AdminShipmentService>.Instance, Service(context));
        var result = await Admin(db).CancelShipmentAsync(tenant, shipment.Id, new AdminCancelShipmentRequest { Reason = "Customer request", ActorUserId = actor }, "admin-fixture");
        Assert.Equal("Unsupported", result.Outcome);
        Assert.Equal(ShipmentStatus.ReadyForPickup, result.CurrentStatus);
        Assert.Null(result.ConfirmedAtUtc);
        await using var reloaded = scope.CreateDbContext(tenant);
        var detail = await Admin(reloaded).GetShipmentDetailsAsync(tenant, shipment.Id, "reload");
        Assert.Equal(result.OperationId, detail!.Cancellation!.OperationId);
        Assert.Equal("Unsupported", detail.Cancellation.Outcome);
        var list = await Admin(reloaded).ListShipmentsAsync(tenant, new AdminShipmentListQuery(), "reload");
        Assert.Equal("Unsupported", Assert.Single(list.Items).Cancellation!.Outcome);
        Assert.Equal(1, calls);
        Assert.Empty(await reloaded.AccountingOutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenericClearingTransfers_RemainRejectedBeforePosting(bool clearingSource)
    {
        await Seed("manual");
        await using var db = scope.CreateDbContext(tenant);
        var clearing = Account.Create(tenant, "1130", "Fixture COD clearing", AccountType.Asset, isClearing: true);
        var bank = Account.Create(tenant, "1030", "Fixture Bank", AccountType.Asset);
        db.LedgerAccounts.AddRange(clearing, bank);
        db.TreasuryAccounts.AddRange(TreasuryAccount.CreateClearing(tenant, clearing.Id, "Clearing"), TreasuryAccount.CreateBank(tenant, bank.Id, "Bank"));
        await db.SaveChangesAsync();
        var posting = new Mock<IFinancialPostingService>(MockBehavior.Strict);
        var transfers = new TreasuryTransferService(posting.Object, db, NullLogger<TreasuryTransferService>.Instance);
        await Assert.ThrowsAsync<TreasuryValidationException>(() => transfers.TransferAsync(tenant,
            clearingSource ? clearing.Id : bank.Id, clearingSource ? bank.Id : clearing.Id, 100, "blocked fixture"));
        posting.Verify(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(await db.JournalEntries.ToListAsync());
        Assert.Empty(await db.AccountingOutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData("manual")]
    [InlineData(DeterministicCarrierCode)]
    public async Task ContradictoryOrderCarrierOwnership_CannotGrantLocalOrExternalCancellation(string carrier)
    {
        var shipment = await Seed(carrier, configureOrder: order =>
        {
            order.FulfillmentMode = carrier == "manual" ? FulfillmentMode.Carrier : FulfillmentMode.Manual;
            order.FulfillmentCarrierCode = carrier == "manual" ? DeterministicCarrierCode : null;
        });
        await using var db = scope.CreateDbContext(tenant);
        var result = await Cancel(db, shipment.Id);
        Assert.False(result.Confirmed);
        Assert.Equal("CANCELLATION_ORDER_CARRIER_MISMATCH", result.Code);
        Assert.Equal(shipment.Status, (await db.Shipments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, calls);
    }

    public void Dispose() { telemetry.Dispose(); scope.Dispose(); }
}
