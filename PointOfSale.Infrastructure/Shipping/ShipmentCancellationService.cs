using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Policies;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping;

public sealed record ShipmentCancellationResult(
    Guid? OperationId, Guid ShipmentId, string Outcome, string? ConfirmationBasis,
    DateTime? ConfirmedAtUtc, ShipmentStatus CurrentStatus, bool ExternalConfirmationExists, string Code)
{
    public bool Confirmed => Outcome == nameof(ShipmentCancellationOutcome.Confirmed);
    public ShipmentStatus? PreviousStatus { get; init; }
    public string ShipmentNumber { get; init; } = string.Empty;
}

public interface IShipmentCancellationService
{
    Task<ShipmentCancellationResult> CancelForAdminAsync(Guid tenantId, Guid shipmentId, Guid actorId,
        Guid? operationId, string reason, string correlationId, CancellationToken ct = default);
    Task<ShipmentCancellationResult> CancelForOrderAsync(Guid tenantId, Guid shipmentId, string reason,
        CancellationToken ct = default);
}

/// <summary>Single cancellation executor, with a permanent, single-winner pre-call barrier.</summary>
public sealed class ShipmentCancellationService(
    ApplicationDbContext db, IShippingCarrierRegistry registry, ICarrierIntegrationAuthority integration,
    IEventPublisher events, ITenantContext tenantContext, ICarrierCredentialEncryptionService? encryption = null) : IShipmentCancellationService
{
    public Task<ShipmentCancellationResult> CancelForAdminAsync(Guid tenantId, Guid shipmentId, Guid actorId,
        Guid? operationId, string reason, string correlationId, CancellationToken ct = default) =>
        CancelAsync(tenantId, shipmentId, actorId, operationId, reason, correlationId, false, ct);

    public Task<ShipmentCancellationResult> CancelForOrderAsync(Guid tenantId, Guid shipmentId, string reason,
        CancellationToken ct = default) =>
        CancelAsync(tenantId, shipmentId, null, null, reason, $"order-cancellation:{shipmentId:N}", true, ct);

    private async Task<ShipmentCancellationResult> CancelAsync(Guid tenantId, Guid shipmentId, Guid? actorId,
        Guid? operationId, string reason, string correlationId, bool orderCompensation, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || tenantContext.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new ArgumentException("CANCELLATION_REASON_REQUIRED");
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Cancellation must own its transaction.");
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Cancellation cannot discard pending caller changes.");
        reason = reason.Trim();
        await AuthorizeAsync(tenantId, actorId, orderCompensation, ct);
        db.ChangeTracker.Clear();
        var snapshot = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == shipmentId && x.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException("SHIPMENT_NOT_FOUND");
        ShipmentCancellationOperation operation;
        CarrierCredentialsResolution? credentials = null;
        IShippingCarrierAdapter? adapter = null;
        var executeCarrier = false;

        // ReadCommitted + explicit row locks: waiting requests see the committed
        // operation after acquiring the shipment lock, rather than a stale snapshot.
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null)
        {
            var order = await LockOrderAsync(tenantId, snapshot.OrderId, ct);
            var shipment = await LockShipmentAsync(tenantId, shipmentId, ct);
            if (order == null || shipment == null || shipment.OrderId != order.Id) throw new KeyNotFoundException("SHIPMENT_NOT_FOUND");
            if (orderCompensation && order.Status != OrderStatus.Cancelled) throw new UnauthorizedAccessException("ORDER_CANCELLATION_AUTHORITY_REQUIRED");
            operation = await db.ShipmentCancellationOperations.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, ct)
                ?? new ShipmentCancellationOperation();
            if (operation.Id != Guid.Empty)
            {
                if (operation.OrderId != shipment.OrderId || operation.RequestFingerprint != Fingerprint(operation, reason))
                    return Result(operation, shipment, "CANCELLATION_REQUEST_CONFLICT", nameof(ShipmentCancellationOutcome.ReconciliationRequired));
                if (operation.Outcome == ShipmentCancellationOutcome.Confirmed &&
                    (shipment.Status != ShipmentStatus.Cancelled || !TargetMatches(operation, shipment)))
                    return Result(operation, shipment, "CANCELLATION_STATE_CHANGED", "ReconciliationRequired");
                // A replay cannot distinguish an active call from a crash. Close
                // the unresolved claim; never invent documented provider-pending semantics.
                if (operation.Outcome == ShipmentCancellationOutcome.Requested || operation.Code == "CANCELLATION_ATTEMPT_UNRESOLVED")
                {
                    operation.Outcome = ShipmentCancellationOutcome.ReconciliationRequired;
                    operation.Code = "CANCELLATION_REPLAY_UNRESOLVED";
                    await SaveAsync(operation, ct);
                    if (tx != null) await tx.CommitAsync(ct);
                }
                return Result(operation, shipment);
            }

            operation = new ShipmentCancellationOperation
            {
                Id = operationId is { } requestedId && requestedId != Guid.Empty ? requestedId : Guid.NewGuid(),
                TenantId = tenantId, ShipmentId = shipmentId, OrderId = shipment.OrderId,
                ActorIdentity = actorId?.ToString("D") ?? "system:order-cancellation", CreatedBy = actorId,
                Source = orderCompensation ? "OrderCancellation" : "PosAdmin", Reason = reason,
                CorrelationId = correlationId.Length > 200 ? correlationId[..200] : correlationId,
                CarrierCode = shipment.CarrierCode, TrackingNumber = shipment.TrackingNumber, AwbNumber = shipment.AwbNumber,
                ExternalReferenceId = shipment.ExternalReferenceId, CarrierRequestId = shipment.CarrierRequestId,
                PreviousStatus = shipment.Status, RequestedAtUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                // Requested is reserved for documented provider-pending semantics.
                // A pre-call/crash claim is explicitly unconfirmed reconciliation.
                Outcome = ShipmentCancellationOutcome.ReconciliationRequired, Code = "CANCELLATION_ATTEMPT_UNRESOLVED"
            };
            operation.RequestFingerprint = Fingerprint(operation, reason);
            db.ShipmentCancellationOperations.Add(operation);

            if (shipment.Status == ShipmentStatus.Cancelled)
                Fail(operation, "LEGACY_CANCELLATION_UNVERIFIED");
            else if (!OrderCarrierMatches(order, shipment))
                Fail(operation, "CANCELLATION_ORDER_CARRIER_MISMATCH");
            else if (order.Status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded)
                Fail(operation, "CANCELLATION_ORDER_STATE_INVALID");
            else if (!ShipmentLifecycleTransitionPolicy.CanTransition(shipment.Status, ShipmentStatus.Cancelled))
                Fail(operation, "CANNOT_CANCEL", ShipmentCancellationOutcome.Unsupported);
            else if (CanCancelLocally(shipment))
                await ConfirmAsync(operation, shipment, order, shipment.IsCarrierManaged ? "NoExternalShipment" : "LocalOnly", ct);
            else if (!shipment.IsCarrierManaged || !HasCompletedRegistration(shipment))
                Fail(operation, "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED");
            else if (string.IsNullOrWhiteSpace(shipment.TrackingNumber))
                Fail(operation, "CARRIER_CANCELLATION_TARGET_UNAVAILABLE");
            else if (CarrierOperationSupports.Describe(shipment.CarrierCode).Single(x => x.Operation == "cancellation").Implementation == "Unavailable")
                Fail(operation, "CARRIER_CANCELLATION_UNSUPPORTED", ShipmentCancellationOutcome.Unsupported);
            else
            {
                adapter = registry.GetAdapter(shipment.CarrierCode);
                if (adapter == null) Fail(operation, "CARRIER_CANCELLATION_UNSUPPORTED", ShipmentCancellationOutcome.Unsupported);
                else
                {
                    credentials = await integration.ResolveCredentialsAsync(tenantId, shipment.CarrierCode, CarrierIntegrationPurpose.Cancellation, ct);
                    if (!credentials.Succeeded || credentials.Credentials == null || credentials.Configuration == null ||
                        !string.Equals(credentials.CarrierCode, shipment.CarrierCode, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(credentials.Configuration.CarrierCode, shipment.CarrierCode, StringComparison.OrdinalIgnoreCase))
                        Fail(operation, "CARRIER_CANCEL_CONFIG_MISSING");
                    else if (shipment.CarrierCode is CarrierCodes.Bosta or CarrierCodes.Aramex &&
                        !CarrierOriginalAccountIdentity.Verifies(shipment, credentials, encryption))
                        Fail(operation, "CARRIER_ORIGINAL_ACCOUNT_REQUIRED_RECONCILIATION_REQUIRED");
                    else
                    {
                        operation.ConfigurationId = credentials.Configuration.Id;
                        operation.AccountId = credentials.Credentials.AccountId;
                        operation.Environment = credentials.Credentials.UseSandbox ? "Sandbox" : "Production";
                        operation.AttemptStartedAtUtc = DateTime.UtcNow;
                        executeCarrier = true;
                    }
                }
            }
            await SaveAsync(operation, ct);
            if (tx != null) await tx.CommitAsync(ct);
            if (!executeCarrier) return Result(operation, shipment);
        }

        // Once claimed, there is exactly one outbound attempt. SQL retries/replay
        // can never re-enter this block. A killed process leaves a durable barrier.
        CancelShipmentResult external;
        try
        {
            external = adapter is IIdentityBoundCarrierCancellation bound
                ? await bound.CancelShipmentAsync(new(operation.TrackingNumber!, operation.ExternalReferenceId ?? ""), credentials!.Credentials!, ct)
                : await adapter!.CancelShipmentAsync(operation.TrackingNumber!, credentials!.Credentials!, ct);
        }
        catch (Exception)
        {
            external = new CancelShipmentResult { Disposition = CarrierCancellationDisposition.Unknown, ErrorCode = "CARRIER_CANCELLATION_AMBIGUOUS" };
        }

        try { return await CompleteAsync(tenantId, shipmentId, actorId, orderCompensation, operation, snapshot, external); }
        catch (Exception)
        {
            // No external retry on rollback/commit failure. Retain provider evidence
            // in a fresh save; the pre-call claim survives a database outage.
            db.ChangeTracker.Clear();
            using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var saved = await db.ShipmentCancellationOperations.SingleAsync(x => x.Id == operation.Id && x.TenantId == tenantId, recoveryTimeout.Token);
                var shipment = await db.Shipments.AsNoTracking().SingleAsync(x => x.Id == shipmentId && x.TenantId == tenantId, recoveryTimeout.Token);
                if (saved.Outcome == ShipmentCancellationOutcome.Confirmed) return Result(saved, shipment);
                Fail(saved, "CANCELLATION_COMMIT_UNRESOLVED");
                saved.EvidenceHash = Limit(external.EvidenceHash, 64);
                saved.EvidenceReference = Limit(external.EvidenceReference, 200);
                await SaveAsync(saved, recoveryTimeout.Token);
                return Result(saved, shipment);
            }
            catch (Exception) { return Result(operation, snapshot, "CANCELLATION_COMMIT_UNRESOLVED", "ReconciliationRequired"); }
        }
    }

    private async Task<ShipmentCancellationResult> CompleteAsync(Guid tenantId, Guid shipmentId, Guid? actorId,
        bool orderCompensation, ShipmentCancellationOperation operation, Shipment snapshot, CancelShipmentResult external)
    {
        // Use a finite independent token to retain uncertainty after HTTP/request timeout.
        using var persistenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var persistenceToken = persistenceTimeout.Token;
        db.ChangeTracker.Clear();
        await using var completionTx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, persistenceToken) : null;
        var currentOrder = await LockOrderAsync(tenantId, operation.OrderId, persistenceToken);
        var current = await LockShipmentAsync(tenantId, shipmentId, persistenceToken);
        var persisted = await db.ShipmentCancellationOperations.SingleAsync(x => x.Id == operation.Id && x.TenantId == tenantId, persistenceToken);
        persisted.EvidenceReference = Limit(external.EvidenceReference, 200);
        persisted.EvidenceHash = Limit(external.EvidenceHash, 64);
        if (persisted.Outcome != ShipmentCancellationOutcome.ReconciliationRequired || persisted.Code != "CANCELLATION_ATTEMPT_UNRESOLVED" ||
            persisted.Version != operation.Version || current == null || currentOrder == null ||
            !IdentityMatches(persisted, current) || current.Status != persisted.PreviousStatus ||
            !OrderCarrierMatches(currentOrder, current) ||
            !ShipmentLifecycleTransitionPolicy.CanTransition(current.Status, ShipmentStatus.Cancelled) ||
            currentOrder.Status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded ||
            (orderCompensation && currentOrder.Status != OrderStatus.Cancelled))
            Fail(persisted, "CANCELLATION_STATE_CHANGED");
        else
        {
            await AuthorizeAsync(tenantId, actorId, orderCompensation, persistenceToken);
            var binding = await integration.ResolveCredentialsAsync(tenantId, current.CarrierCode, CarrierIntegrationPurpose.Cancellation, persistenceToken);
            if (!binding.Succeeded || binding.Configuration?.Id != persisted.ConfigurationId ||
                binding.Credentials?.AccountId != persisted.AccountId ||
                (binding.Credentials?.UseSandbox == true ? "Sandbox" : "Production") != persisted.Environment ||
                (current.CarrierCode is CarrierCodes.Bosta or CarrierCodes.Aramex && !CarrierOriginalAccountIdentity.Verifies(current, binding, encryption)))
                Fail(persisted, "CANCELLATION_ACCOUNT_CHANGED");
            else if (external.Disposition == CarrierCancellationDisposition.Confirmed &&
                      external.TargetTrackingNumber == persisted.TrackingNumber &&
                      (current.CarrierCode != CarrierCodes.Bosta || external.EvidenceReference == persisted.ExternalReferenceId) &&
                      !string.IsNullOrWhiteSpace(external.EvidenceReference) && IsHash(external.EvidenceHash))
                await ConfirmAsync(persisted, current, currentOrder, "CarrierConfirmed", persistenceToken);
            else
                Fail(persisted, external.Disposition == CarrierCancellationDisposition.Unsupported
                    ? "CARRIER_CANCELLATION_UNSUPPORTED" : "CARRIER_CANCELLATION_UNCONFIRMED",
                    external.Disposition == CarrierCancellationDisposition.Unsupported
                        ? ShipmentCancellationOutcome.Unsupported : ShipmentCancellationOutcome.ReconciliationRequired);
        }
        await SaveAsync(persisted, persistenceToken);
        if (completionTx != null) await completionTx.CommitAsync(persistenceToken);
        return Result(persisted, current ?? snapshot);
    }

    private async Task AuthorizeAsync(Guid tenantId, Guid? actorId, bool compensation, CancellationToken ct)
    {
        if (!await db.Tenants.AsNoTracking().AnyAsync(x => x.Id == tenantId && x.IsActive && x.Status == TenantStatus.Active, ct))
            throw new UnauthorizedAccessException();
        if (compensation) return; // further qualified by locked, cancelled SQL order
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actorId && x.TenantId == tenantId && x.IsActive && !x.IsDeleted, ct);
        if (actor == null || !PosRolePermissionCatalog.IsGranted(actor.Role, PosPermissions.ShippingFulfill))
            throw new UnauthorizedAccessException();
    }

    private async Task<Shipment?> LockShipmentAsync(Guid tenant, Guid id, CancellationToken ct) =>
        await (db.Database.IsNpgsql() ? db.Shipments.FromSqlInterpolated($"SELECT * FROM \"Shipments\" WHERE \"Id\"={id} AND \"TenantId\"={tenant} AND NOT \"IsDeleted\" FOR UPDATE") : db.Shipments)
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenant && !x.IsDeleted, ct);

    private async Task<Order?> LockOrderAsync(Guid tenant, Guid id, CancellationToken ct) =>
        await (db.Database.IsNpgsql() ? db.Orders.FromSqlInterpolated($"SELECT xmin, * FROM \"Orders\" WHERE \"Id\"={id} AND \"TenantId\"={tenant} AND NOT \"IsDeleted\" FOR UPDATE") : db.Orders)
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenant && !x.IsDeleted, ct);

    internal static bool CanCancelLocally(Shipment s) =>
        // Original create evidence survives an inconsistent counter/timestamp.
        // Missing or malformed identity is uncertainty, never local absence proof.
        !CarrierRegistrationEvidence.HasOutboundEvidence(s) &&
        string.IsNullOrWhiteSpace(s.TrackingNumber) && string.IsNullOrWhiteSpace(s.AwbNumber) &&
        string.IsNullOrWhiteSpace(s.ExternalReferenceId) && string.IsNullOrWhiteSpace(s.CarrierCreateResponse) &&
        string.IsNullOrWhiteSpace(s.CarrierTrackingResponse) && s.LastCarrierSyncAtUtc == null &&
        s.ExternalRegistrationAttemptCount == 0 && s.ExternalRegistrationLastAttemptAtUtc == null &&
        s.ExternalRegistrationCompletedAtUtc == null && s.ExternalRegistrationFailedAtUtc == null &&
        string.IsNullOrWhiteSpace(s.ExternalRegistrationError) &&
        (!s.IsCarrierManaged ? string.IsNullOrWhiteSpace(s.ExternalRegistrationState) &&
            string.IsNullOrWhiteSpace(s.CarrierRequestId) && s.ExternalRegistrationRequestedAtUtc == null :
            s.Status == ShipmentStatus.PendingExternalRegistration && s.ExternalRegistrationRequestedAtUtc != null && s.ExternalRegistrationState == "PendingExternalCall");

    private static bool HasCompletedRegistration(Shipment s) => s.ExternalRegistrationCompletedAtUtc != null &&
        s.ExternalRegistrationState == "Completed" && string.IsNullOrWhiteSpace(s.ExternalRegistrationError);
    private static bool OrderCarrierMatches(Order order, Shipment shipment) => shipment.IsCarrierManaged
        ? order.FulfillmentMode == FulfillmentMode.Carrier &&
          !string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode) &&
          CarrierCodes.Normalize(order.FulfillmentCarrierCode) == CarrierCodes.Normalize(shipment.CarrierCode)
        : order.FulfillmentMode == FulfillmentMode.Manual && CarrierCodes.IsManual(order.FulfillmentCarrierCode);
    private static bool IdentityMatches(ShipmentCancellationOperation op, Shipment s) =>
        TargetMatches(op, s) && HasCompletedRegistration(s);
    private static bool TargetMatches(ShipmentCancellationOperation op, Shipment s) =>
        s.TenantId == op.TenantId && s.Id == op.ShipmentId && s.OrderId == op.OrderId && !s.IsDeleted &&
        s.CarrierCode == op.CarrierCode && s.TrackingNumber == op.TrackingNumber && s.AwbNumber == op.AwbNumber &&
        s.ExternalReferenceId == op.ExternalReferenceId && s.CarrierRequestId == op.CarrierRequestId;
    private static string Fingerprint(ShipmentCancellationOperation op, string reason) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { op.TenantId, op.ShipmentId, op.OrderId, op.CarrierCode, op.TrackingNumber, op.AwbNumber, op.ExternalReferenceId, op.CarrierRequestId, Reason = reason }))));
    private static bool IsHash(string? hash) => hash?.Length == 64 && hash.All(Uri.IsHexDigit);
    private static string? Limit(string? value, int max) => value?.Length > max ? value[..max] : value;
    private static void Fail(ShipmentCancellationOperation op, string code, ShipmentCancellationOutcome outcome = ShipmentCancellationOutcome.ReconciliationRequired)
    { op.Outcome = outcome; op.Code = code; }
    private async Task SaveAsync(ShipmentCancellationOperation op, CancellationToken ct)
    {
        using var authority = CancellationPersistenceAuthority.Begin();
        op.Version = Guid.NewGuid(); op.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }

    private async Task ConfirmAsync(ShipmentCancellationOperation op, Shipment shipment, Order order, string basis, CancellationToken ct)
    {
        op.Outcome = ShipmentCancellationOutcome.Confirmed;
        op.ConfirmationBasis = basis;
        op.ConfirmedAtUtc = DateTime.UtcNow;
        op.Code = "CANCELLATION_CONFIRMED";
        var previous = shipment.Status;
        shipment.Status = ShipmentStatus.Cancelled;
        shipment.CancelledAtUtc = op.ConfirmedAtUtc;
        shipment.CancellationReason = op.Reason;
        shipment.UpdatedAt = op.ConfirmedAtUtc;
        shipment.UpdatedBy = op.CreatedBy;
        // Publisher is the existing transactional EF outbox; no order/financial mutation.
        await events.PublishAsync(new ShipmentCancelledEvent
        {
            TenantId = op.TenantId, ShipmentId = shipment.Id, OrderId = order.Id, ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = order.OrderNumber, PreviousStatus = previous, CancellationReason = op.Reason,
            CancelledAt = op.ConfirmedAtUtc.Value, CorrelationId = op.CorrelationId
        }, ct);
        await events.PublishAsync(new ShipmentStatusChangedEvent
        {
            TenantId = op.TenantId, ShipmentId = shipment.Id, OrderId = order.Id, ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = order.OrderNumber, PreviousStatus = previous, NewStatus = ShipmentStatus.Cancelled,
            TrackingNumber = shipment.TrackingNumber, CarrierName = shipment.CarrierCode, CorrelationId = op.CorrelationId
        }, ct);
    }

    public static ShipmentCancellationResult Result(ShipmentCancellationOperation op, Shipment s, string? code = null, string? outcome = null) =>
        new(op.Id, s.Id, outcome ?? op.Outcome.ToString(), outcome == "ReconciliationRequired" ? null : op.ConfirmationBasis,
            outcome == "ReconciliationRequired" ? null : op.ConfirmedAtUtc, s.Status,
            outcome == null && op.Outcome == ShipmentCancellationOutcome.Confirmed && op.ConfirmationBasis == "CarrierConfirmed", code ?? op.Code)
            { PreviousStatus = op.PreviousStatus, ShipmentNumber = s.ShipmentNumber };

    public static ShipmentCancellationResult? ReadResult(Shipment s, ShipmentCancellationOperation? op) => op != null
        ? Result(op, s) with { Outcome = op.Outcome == ShipmentCancellationOutcome.Requested ? "ReconciliationRequired" : op.Outcome.ToString() }
        : s.IsCarrierManaged && s.Status == ShipmentStatus.Cancelled
            ? new(null, s.Id, "ReconciliationRequired", null, null, s.Status, false, "LEGACY_CANCELLATION_UNVERIFIED") : null;
}
