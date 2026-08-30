using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Fulfillment;

/// <summary>
/// PHASE OFL: Order Fulfillment Lifecycle Service
/// 
/// Implementation of fulfillment lifecycle state machine.
/// 
/// SECURITY HARDENING:
/// 1. All fulfillment status changes MUST go through this service
/// 2. All methods require explicit FulfillmentRequestSource
/// 3. Source is validated against FulfillmentMode (ownership enforcement)
/// 4. Manual source CANNOT update Carrier orders
/// 5. Webhook source CANNOT update Manual orders
/// 6. All violations logged as SECURITY_VIOLATION
/// 7. Delivery events trigger payment lifecycle actions (for COD, Wallet)
/// 8. State transitions are validated before execution
/// 9. Domain events are published for each transition
/// 
/// INTEGRATION:
/// OrderFulfillmentService → OrderDeliveredEvent → EventHandler → PaymentLifecycleService → Ledger
/// </summary>
public class OrderFulfillmentService : IOrderFulfillmentService
{
    private const int FulfillmentCommitMaxAttempts = 3;
    private const int MaxDeliveryReferenceLength = 200;
    private const int MaxProofOfDeliveryUrlLength = 2048;
    private const int MaxRecipientNameLength = 200;
    private const int MaxLifecycleReasonLength = 1000;
    private const int MaxReturnAuthorizationLength = 200;

    private readonly ApplicationDbContext _db;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(
        ApplicationDbContext db,
        IEventPublisher eventPublisher,
        ILogger<OrderFulfillmentService> logger)
    {
        _db = db;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    #region Transition Methods

    public async Task<OrderFulfillmentResult> ConfirmAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? confirmedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await TransitionAsync<FulfillmentConfirmedEvent>(
            orderId,
            source,
            FulfillmentStatus.Confirmed,
            confirmedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new FulfillmentConfirmedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = confirmedBy,
                TransitionedAt = DateTime.UtcNow,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> PackAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? packingSlipNumber = null,
        string? packedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await TransitionAsync<OrderPackedEvent>(
            orderId,
            source,
            FulfillmentStatus.Packed,
            packedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new OrderPackedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = packedBy,
                TransitionedAt = DateTime.UtcNow,
                PackingSlipNumber = packingSlipNumber,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        Guid? shipmentId = null,
        string? trackingNumber = null,
        string? updatedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await TransitionAsync<OrderOutForDeliveryEvent>(
            orderId,
            source,
            FulfillmentStatus.OutForDelivery,
            updatedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new OrderOutForDeliveryEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = updatedBy,
                TransitionedAt = DateTime.UtcNow,
                ShipmentId = shipmentId,
                TrackingNumber = trackingNumber,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> MarkDeliveredAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? deliveryReference = null,
        string? proofOfDeliveryUrl = null,
        string? recipientName = null,
        string? deliveredBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (source == FulfillmentRequestSource.Manual &&
            (string.IsNullOrWhiteSpace(deliveryReference) || string.IsNullOrWhiteSpace(proofOfDeliveryUrl)))
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "MANUAL_DELIVERY_EVIDENCE_REQUIRED: Manual delivery requires a delivery reference and proof of delivery evidence.",
                idempotencyKey);
        }

        var normalizedDeliveryReference = deliveryReference?.Trim();
        var normalizedProofOfDeliveryUrl = proofOfDeliveryUrl?.Trim();
        var normalizedRecipientName = recipientName?.Trim();

        if (source == FulfillmentRequestSource.Manual &&
            (normalizedDeliveryReference!.Length > MaxDeliveryReferenceLength ||
             normalizedProofOfDeliveryUrl!.Length > MaxProofOfDeliveryUrlLength ||
             (normalizedRecipientName?.Length ?? 0) > MaxRecipientNameLength))
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "MANUAL_DELIVERY_EVIDENCE_INVALID: Delivery evidence exceeds the supported length.",
                idempotencyKey);
        }

        if (source == FulfillmentRequestSource.Manual && !IsValidProofOfDeliveryUrl(normalizedProofOfDeliveryUrl))
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "MANUAL_DELIVERY_PROOF_URL_INVALID: Proof of delivery must be an absolute HTTP or HTTPS URL.",
                idempotencyKey);
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null)
            return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

        if (order.FulfillmentStatus == FulfillmentStatus.Delivered)
        {
            var referenceConflicts = !string.IsNullOrWhiteSpace(order.DeliveryReference) &&
                                     !string.Equals(order.DeliveryReference, normalizedDeliveryReference, StringComparison.Ordinal);
            var proofConflicts = !string.IsNullOrWhiteSpace(order.ProofOfDeliveryUrl) &&
                                 !string.Equals(order.ProofOfDeliveryUrl, normalizedProofOfDeliveryUrl, StringComparison.Ordinal);

            if (referenceConflicts || proofConflicts)
            {
                return OrderFulfillmentResult.Failed(
                    order.Id,
                    order.OrderNumber,
                    order.FulfillmentStatus,
                    source,
                    "MANUAL_DELIVERY_EVIDENCE_CONFLICT: Delivery evidence is immutable once captured.",
                    idempotencyKey);
            }

            // Recovery for orders delivered before canonical evidence columns existed.
            // Only fill missing values; never overwrite previously captured evidence.
            if (string.IsNullOrWhiteSpace(order.DeliveryReference) ||
                string.IsNullOrWhiteSpace(order.ProofOfDeliveryUrl))
            {
                order.DeliveryReference ??= normalizedDeliveryReference;
                order.ProofOfDeliveryUrl ??= normalizedProofOfDeliveryUrl;
                await _db.SaveChangesAsync(ct);
            }

            return OrderFulfillmentResult.Succeeded(
                order.Id,
                order.OrderNumber,
                FulfillmentStatus.Delivered,
                FulfillmentStatus.Delivered,
                source,
                idempotencyKey);
        }

        return await TransitionAsync<FulfillmentDeliveredEvent>(
            orderId,
            source,
            FulfillmentStatus.Delivered,
            deliveredBy,
            idempotencyKey,
            (order, oldStatus, newStatus) =>
            {
                // Persist the same evidence carried by the event. Payment/accounting can
                // qualify later than delivery (notably COD), so approval replay must read
                // durable SQL truth rather than depend on a one-shot event payload.
                order.DeliveryReference = normalizedDeliveryReference;
                order.ProofOfDeliveryUrl = normalizedProofOfDeliveryUrl;

                return new FulfillmentDeliveredEvent
                {
                    TenantId = order.TenantId,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    TransitionedBy = deliveredBy,
                    TransitionedAt = DateTime.UtcNow,
                    DeliveryReference = normalizedDeliveryReference,
                    ProofOfDeliveryUrl = normalizedProofOfDeliveryUrl,
                    RecipientName = normalizedRecipientName,
                    PaymentMethod = order.PaymentMethodCode ?? "unknown",
                    PaymentStatus = order.PaymentStatus,
                    OrderTotal = order.Total,
                    Source = source,
                    IdempotencyKey = idempotencyKey
                };
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> SynchronizeFromShipmentAsync(
        Guid orderId,
        FulfillmentStatus targetStatus,
        FulfillmentRequestSource source,
        Guid? shipmentId = null,
        string? trackingNumber = null,
        string? actor = null,
        string? idempotencyKeyPrefix = null,
        CancellationToken ct = default)
    {
        if (!IsShipmentSynchronizationTarget(targetStatus))
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                $"Shipment synchronization cannot target fulfillment status {targetStatus}.",
                BuildShipmentSyncIdempotencyKey(orderId, shipmentId, idempotencyKeyPrefix, targetStatus));
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null)
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "Order not found",
                BuildShipmentSyncIdempotencyKey(orderId, shipmentId, idempotencyKeyPrefix, targetStatus));
        }

        if (IsAtOrBeyondOutboundTarget(order.FulfillmentStatus, targetStatus))
        {
            return OrderFulfillmentResult.Succeeded(
                order.Id,
                order.OrderNumber,
                order.FulfillmentStatus,
                order.FulfillmentStatus,
                source,
                BuildShipmentSyncIdempotencyKey(orderId, shipmentId, idempotencyKeyPrefix, targetStatus));
        }

        var steps = GetShipmentSynchronizationSteps(order.FulfillmentStatus, targetStatus).ToList();
        if (steps.Count == 0)
        {
            return OrderFulfillmentResult.Failed(
                order.Id,
                order.OrderNumber,
                order.FulfillmentStatus,
                source,
                $"Cannot synchronize shipment fulfillment from {order.FulfillmentStatus} to {targetStatus}.",
                BuildShipmentSyncIdempotencyKey(orderId, shipmentId, idempotencyKeyPrefix, targetStatus));
        }

        OrderFulfillmentResult? lastResult = null;
        foreach (var step in steps)
        {
            var stepIdempotencyKey = BuildShipmentSyncIdempotencyKey(orderId, shipmentId, idempotencyKeyPrefix, step);
            lastResult = step switch
            {
                FulfillmentStatus.Confirmed => await ConfirmAsync(orderId, source, actor, stepIdempotencyKey, ct),
                FulfillmentStatus.Packed => await PackAsync(orderId, source, trackingNumber, actor, stepIdempotencyKey, ct),
                FulfillmentStatus.OutForDelivery => await MarkOutForDeliveryAsync(orderId, source, shipmentId, trackingNumber, actor, stepIdempotencyKey, ct),
                FulfillmentStatus.Delivered => await MarkDeliveredAsync(orderId, source, trackingNumber, deliveredBy: actor, idempotencyKey: stepIdempotencyKey, ct: ct),
                _ => OrderFulfillmentResult.Failed(orderId, order.OrderNumber, order.FulfillmentStatus, source, $"Unsupported shipment synchronization step {step}.", stepIdempotencyKey)
            };

            if (!lastResult.Success)
            {
                return lastResult;
            }
        }

        return lastResult ?? OrderFulfillmentResult.Succeeded(
            order.Id,
            order.OrderNumber,
            order.FulfillmentStatus,
            order.FulfillmentStatus,
            source,
            BuildShipmentSyncIdempotencyKey(orderId, shipmentId, idempotencyKeyPrefix, targetStatus));
    }

    public async Task<OrderFulfillmentResult> MarkCompletedAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? completedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await TransitionAsync<OrderCompletedEvent>(
            orderId,
            source,
            FulfillmentStatus.Completed,
            completedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new OrderCompletedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = completedBy,
                TransitionedAt = DateTime.UtcNow,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> MarkReturnedAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string reason,
        bool shouldRefund = false,
        decimal? refundAmount = null,
        string? returnAuthorizationNumber = null,
        string? returnedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var normalizedReason = reason?.Trim() ?? string.Empty;
        var normalizedReturnAuthorization = returnAuthorizationNumber?.Trim();
        if (normalizedReason.Length == 0 ||
            normalizedReason.Length > MaxLifecycleReasonLength ||
            (normalizedReturnAuthorization?.Length ?? 0) > MaxReturnAuthorizationLength)
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "RETURN_EVIDENCE_INVALID: A supported return reason is required.",
                idempotencyKey);
        }

        if (shouldRefund || refundAmount.HasValue)
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "REFUND_LIFECYCLE_REQUIRED: Fulfillment return cannot create refund authority. Use IPaymentLifecycleService after recording the physical return.",
                idempotencyKey);
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null)
            return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

        return await TransitionAsync<OrderReturnedEvent>(
            orderId,
            source,
            FulfillmentStatus.Returned,
            returnedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new OrderReturnedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = returnedBy,
                TransitionedAt = DateTime.UtcNow,
                Reason = normalizedReason,
                ReturnAuthorizationNumber = normalizedReturnAuthorization,
                ShouldRefund = shouldRefund,
                RefundAmount = refundAmount ?? order.Total,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> MarkDeliveryFailedAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string reason,
        int attemptNumber = 1,
        bool canRetry = true,
        string? updatedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length == 0 || normalizedReason.Length > MaxLifecycleReasonLength)
        {
            return OrderFulfillmentResult.Failed(
                orderId,
                "",
                FulfillmentStatus.Created,
                source,
                "FAILED_DELIVERY_EVIDENCE_INVALID: A supported failure reason is required.",
                idempotencyKey);
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null)
            return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

        return await TransitionAsync<OrderDeliveryFailedEvent>(
            orderId,
            source,
            FulfillmentStatus.FailedDelivery,
            updatedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new OrderDeliveryFailedEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = updatedBy,
                TransitionedAt = DateTime.UtcNow,
                Reason = normalizedReason,
                AttemptNumber = attemptNumber,
                CanRetry = canRetry,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> RetryDeliveryAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? updatedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        return await TransitionAsync<OrderOutForDeliveryEvent>(
            orderId,
            source,
            FulfillmentStatus.OutForDelivery,
            updatedBy,
            idempotencyKey,
            (order, oldStatus, newStatus) => new OrderOutForDeliveryEvent
            {
                TenantId = order.TenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                TransitionedBy = updatedBy,
                TransitionedAt = DateTime.UtcNow,
                Source = source,
                IdempotencyKey = idempotencyKey
            },
            ct);
    }

    public async Task<OrderFulfillmentResult> CancelAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string reason,
        bool releaseInventory = true,
        string? cancelledBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null)
            return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

        return OrderFulfillmentResult.Failed(
            orderId,
            order.OrderNumber,
            order.FulfillmentStatus,
            source,
            "ORDER_CANCELLATION_LIFECYCLE_REQUIRED: Use the authoritative order cancellation service so payment cancellation and inventory restoration commit together.",
            idempotencyKey);
    }

    #endregion

    #region Actions Query

    public async Task<FulfillmentActionsResult> GetAvailableActionsAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null)
        {
            return new FulfillmentActionsResult
            {
                OrderId = orderId,
                OrderNumber = "",
                CurrentStatus = FulfillmentStatus.Created,
                AvailableActions = new List<FulfillmentAction>()
            };
        }

        // FULFILLMENT OWNERSHIP ENFORCEMENT
        // Carrier-managed orders have NO manual actions available
        if (!order.AllowsManualFulfillment)
        {
            _logger.LogDebug(
                "[Fulfillment] No manual actions available for carrier order | " +
                "OrderId: {OrderId} | FulfillmentMode: {FulfillmentMode} | CarrierCode: {CarrierCode}",
                order.Id, order.FulfillmentMode, order.FulfillmentCarrierCode);

            return new FulfillmentActionsResult
            {
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                CurrentStatus = order.FulfillmentStatus,
                AvailableActions = new List<FulfillmentAction>()
            };
        }

        var currentStatus = order.FulfillmentStatus;
        var allowedTransitions = currentStatus.GetAllowedTransitions();
        allowedTransitions = allowedTransitions
            .Where(target => target != FulfillmentStatus.Cancelled)
            .ToList();
        
        // Filter out outbound fulfillment actions when payment isn't recognized
        if (RequiresRecognizedPaymentBeforeFulfillment(order))
        {
            allowedTransitions = allowedTransitions
                .Where(target => !IsOutboundFulfillmentProgression(target))
                .ToList();
        }
        
        // Filter out outbound fulfillment actions when accounting isn't posted
        if (RequiresPostedCollectionAccountingBeforeFulfillment(order))
        {
            allowedTransitions = allowedTransitions
                .Where(target => !IsOutboundFulfillmentProgression(target))
                .ToList();
        }

        if (OrderOperationalFinancialAuthority.GetCompletionBlockReason(order) != null)
        {
            allowedTransitions = allowedTransitions
                .Where(target => target != FulfillmentStatus.Completed)
                .ToList();
        }

        var actions = allowedTransitions.Select(target => new FulfillmentAction
        {
            Code = GetActionCode(currentStatus, target),
            Label = GetActionLabel(currentStatus, target),
            TargetStatus = target,
            RequiresConfirmation = target == FulfillmentStatus.Cancelled || 
                                   target == FulfillmentStatus.Returned,
            RequiredFields = GetRequiredFields(target),
            IconHint = GetIconHint(target)
        }).ToList();

        return new FulfillmentActionsResult
        {
            OrderId = orderId,
            OrderNumber = order.OrderNumber,
            CurrentStatus = currentStatus,
            AvailableActions = actions
        };
    }

    #endregion

    #region Private Helpers

    private async Task<OrderFulfillmentResult> TransitionAsync<TEvent>(
        Guid orderId,
        FulfillmentRequestSource source,
        FulfillmentStatus targetStatus,
        string? transitionedBy,
        string? idempotencyKey,
        Func<Order, FulfillmentStatus, FulfillmentStatus, TEvent> eventFactory,
        CancellationToken ct,
        int attempt = 1)
        where TEvent : OrderFulfillmentEventBase
    {
        var ownsTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
        await using IDbContextTransaction? transaction = ownsTransaction
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        var order = await LoadOrderForFulfillmentMutationAsync(orderId, ct);

        if (order == null)
            return OrderFulfillmentResult.Failed(orderId, "", FulfillmentStatus.Created, source, "Order not found", idempotencyKey);

        // SECURITY ENFORCEMENT: Source Validation
        if (!source.IsAllowedFor(order.FulfillmentMode))
        {
            _logger.LogError(
                "[SECURITY_VIOLATION] Fulfillment source mismatch REJECTED | " +
                "OrderId: {OrderId} | OrderNumber: {OrderNumber} | " +
                "FulfillmentMode: {FulfillmentMode} | CarrierCode: {CarrierCode} | " +
                "RequestSource: {RequestSource} | TargetStatus: {TargetStatus}",
                order.Id, order.OrderNumber, order.FulfillmentMode, order.FulfillmentCarrierCode,
                source, targetStatus);

            var errorMessage = (source, order.FulfillmentMode) switch
            {
                (FulfillmentRequestSource.Manual, FulfillmentMode.Carrier) =>
                    $"Cannot manually update fulfillment status for carrier-managed orders. " +
                    $"This order is managed by carrier '{order.FulfillmentCarrierCode}' and can only be updated via carrier webhooks.",
                
                (FulfillmentRequestSource.Webhook, FulfillmentMode.Manual) =>
                    $"Cannot process carrier webhook for manually-managed orders. " +
                    $"This order is manually managed and can only be updated by admin.",
                
                _ => $"Fulfillment source '{source}' is not allowed for fulfillment mode '{order.FulfillmentMode}'."
            };

            return OrderFulfillmentResult.Failed(orderId, order.OrderNumber, order.FulfillmentStatus, source, errorMessage, idempotencyKey);
        }

        var oldStatus = order.FulfillmentStatus;

        if (oldStatus == targetStatus)
        {
            _logger.LogInformation(
                "[Fulfillment] Replay collapsed against current SQL fulfillment authority | OrderId: {OrderId} | OrderNumber: {OrderNumber} | CurrentStatus: {CurrentStatus} | RequestedStatus: {RequestedStatus} | Source: {Source}",
                order.Id,
                order.OrderNumber,
                oldStatus,
                targetStatus,
                source);

            return OrderFulfillmentResult.Succeeded(
                order.Id,
                order.OrderNumber,
                oldStatus,
                oldStatus,
                source,
                idempotencyKey);
        }

        if (IsTerminalOrderLifecycle(order.Status) &&
            IsOutboundFulfillmentProgression(targetStatus))
        {
            _logger.LogCritical(
                "[Fulfillment] Blocked outbound progression for terminal order | OrderId: {OrderId} | OrderNumber: {OrderNumber} | OrderStatus: {OrderStatus} | FulfillmentStatus: {FulfillmentStatus} | TargetStatus: {TargetStatus}",
                order.Id,
                order.OrderNumber,
                order.Status,
                order.FulfillmentStatus,
                targetStatus);

            return OrderFulfillmentResult.Failed(
                orderId,
                order.OrderNumber,
                oldStatus,
                source,
                $"Cannot advance fulfillment for terminal order status {order.Status}.",
                idempotencyKey);
        }

        if (IsOutboundFulfillmentProgression(targetStatus) &&
            IsAtOrBeyondOutboundTarget(oldStatus, targetStatus))
        {
            _logger.LogInformation(
                "[Fulfillment] Replay collapsed against current SQL fulfillment authority | OrderId: {OrderId} | OrderNumber: {OrderNumber} | CurrentStatus: {CurrentStatus} | RequestedStatus: {RequestedStatus} | Source: {Source}",
                order.Id,
                order.OrderNumber,
                oldStatus,
                targetStatus,
                source);

            return OrderFulfillmentResult.Succeeded(
                order.Id,
                order.OrderNumber,
                oldStatus,
                oldStatus,
                source,
                idempotencyKey);
        }

        if (IsOutboundFulfillmentProgression(targetStatus) &&
            RequiresRecognizedPaymentBeforeFulfillment(order))
        {
            _logger.LogCritical(
                "[Fulfillment] Blocked outbound progression before payment authority exists | OrderId: {OrderId} | OrderNumber: {OrderNumber} | PaymentMethodCode: {PaymentMethodCode} | PaymentStatus: {PaymentStatus} | TargetStatus: {TargetStatus}",
                order.Id,
                order.OrderNumber,
                order.PaymentMethodCode,
                order.PaymentStatus,
                targetStatus);

            return OrderFulfillmentResult.Failed(
                orderId,
                order.OrderNumber,
                oldStatus,
                source,
                "Cannot advance fulfillment until payment is confirmed.",
                idempotencyKey);
        }

        if (IsOutboundFulfillmentProgression(targetStatus) &&
            RequiresPostedCollectionAccountingBeforeFulfillment(order))
        {
            _logger.LogCritical(
                "[Fulfillment] Blocked paid order fulfillment until collection accounting is posted | OrderId: {OrderId} | OrderNumber: {OrderNumber} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus} | TargetStatus: {TargetStatus}",
                order.Id,
                order.OrderNumber,
                order.PaymentStatus,
                order.FinancialPostingStatus,
                targetStatus);

            return OrderFulfillmentResult.Failed(
                orderId,
                order.OrderNumber,
                oldStatus,
                source,
                "Cannot advance fulfillment until the payment collection accounting journal is posted.",
                idempotencyKey);
        }

        if (targetStatus == FulfillmentStatus.Completed)
        {
            var completionBlockReason = OrderOperationalFinancialAuthority.GetCompletionBlockReason(order);
            if (completionBlockReason != null)
            {
                _logger.LogWarning(
                    "[Fulfillment] Blocked completion before financial obligations were satisfied | OrderId: {OrderId} | OrderNumber: {OrderNumber} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus}",
                    order.Id,
                    order.OrderNumber,
                    order.PaymentStatus,
                    order.FinancialPostingStatus);

                return OrderFulfillmentResult.Failed(
                    orderId,
                    order.OrderNumber,
                    oldStatus,
                    source,
                    completionBlockReason,
                    idempotencyKey);
            }
        }

        // Validate transition
        if (!oldStatus.CanTransitionTo(targetStatus))
        {
            _logger.LogWarning(
                "Invalid fulfillment transition | OrderId: {OrderId} | From: {OldStatus} | To: {TargetStatus}",
                orderId, oldStatus, targetStatus);

            return OrderFulfillmentResult.Failed(
                orderId, order.OrderNumber, oldStatus, source,
                $"Cannot transition from {oldStatus} to {targetStatus}", idempotencyKey);
        }

        try
        {
            // Enable authorized context for interceptor
            FulfillmentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
            try
            {
                order.FulfillmentStatus = targetStatus;
                order.AcceptFulfillmentStatusChange();

                var oldOrderStatus = order.Status;
                var newOrderStatus = ResolveOrderStatus(order.Status, targetStatus);
                if (newOrderStatus != oldOrderStatus)
                {
                    order.Status = newOrderStatus;
                    ApplyOrderStatusTimestamp(order, newOrderStatus);

                    await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
                    {
                        TenantId = order.TenantId,
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        OldStatus = oldOrderStatus,
                        NewStatus = newOrderStatus,
                        Reason = $"Converged from authoritative fulfillment transition {oldStatus} -> {targetStatus}"
                    }, ct);
                }

                var domainEvent = eventFactory(order, oldStatus, targetStatus);
                await _eventPublisher.PublishAsync(domainEvent, ct);

                await _db.SaveChangesAsync(ct);
                if (transaction != null)
                {
                    await transaction.CommitAsync(ct);
                }
            }
            finally
            {
                FulfillmentStatusSaveChangesInterceptor.DisableAuthorizedContext();
            }
        }
        catch (Exception ex) when (
            ownsTransaction &&
            attempt < FulfillmentCommitMaxAttempts &&
            IsFulfillmentSerializationFailure(ex))
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
            }

            _db.ChangeTracker.Clear();
            _logger.LogWarning(
                ex,
                "Retrying fulfillment transition after serializable race | OrderId: {OrderId} | TargetStatus: {TargetStatus} | Attempt: {Attempt}/{MaxAttempts}",
                orderId,
                targetStatus,
                attempt,
                FulfillmentCommitMaxAttempts);

            await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), ct);
            return await TransitionAsync(
                orderId,
                source,
                targetStatus,
                transitionedBy,
                idempotencyKey,
                eventFactory,
                ct,
                attempt + 1);
        }

        _logger.LogInformation(
            "[Fulfillment] Status changed | OrderId: {OrderId} | OrderNumber: {OrderNumber} | " +
            "From: {OldStatus} | To: {NewStatus} | Source: {Source} | By: {TransitionedBy}",
            order.Id, order.OrderNumber, oldStatus, targetStatus, source, transitionedBy ?? "system");

        return OrderFulfillmentResult.Succeeded(orderId, order.OrderNumber, oldStatus, targetStatus, source, idempotencyKey);
    }

    private static bool IsFulfillmentSerializationFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Order?> LoadOrderForFulfillmentMutationAsync(Guid orderId, CancellationToken ct)
    {
        var providerName = _db.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _db.Orders
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _db.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [IsDeleted] = 0")
                .FirstOrDefaultAsync(ct);
        }

        return await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted, ct);
    }

    private static bool IsTerminalOrderLifecycle(OrderStatus status) =>
        status is OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.Completed;

    private static OrderStatus ResolveOrderStatus(
        OrderStatus currentStatus,
        FulfillmentStatus targetStatus)
    {
        if (IsTerminalOrderLifecycle(currentStatus))
        {
            return currentStatus;
        }

        var targetOrderStatus = targetStatus switch
        {
            FulfillmentStatus.Confirmed => OrderStatus.Confirmed,
            FulfillmentStatus.Packed => OrderStatus.Processing,
            FulfillmentStatus.OutForDelivery => OrderStatus.Shipped,
            FulfillmentStatus.Delivered => OrderStatus.Delivered,
            FulfillmentStatus.Completed => OrderStatus.Completed,
            FulfillmentStatus.FailedDelivery => OrderStatus.OnHold,
            _ => currentStatus
        };

        if (targetOrderStatus == OrderStatus.OnHold)
        {
            return targetOrderStatus;
        }

        return GetOrderProgressionRank(targetOrderStatus) > GetOrderProgressionRank(currentStatus)
            ? targetOrderStatus
            : currentStatus;
    }

    private static int GetOrderProgressionRank(OrderStatus status) =>
        status switch
        {
            OrderStatus.Pending => 0,
            OrderStatus.Confirmed => 1,
            OrderStatus.Processing => 2,
            OrderStatus.Shipped => 3,
            OrderStatus.Delivered => 4,
            OrderStatus.Completed => 5,
            OrderStatus.OnHold => 2,
            _ => int.MaxValue
        };

    private static void ApplyOrderStatusTimestamp(Order order, OrderStatus status)
    {
        var now = DateTime.UtcNow;
        order.UpdatedAt = now;

        if (status == OrderStatus.Shipped)
        {
            order.ShippedAt ??= now;
        }
        else if (status == OrderStatus.Delivered)
        {
            order.DeliveredAt ??= now;
        }
    }

    private static bool RequiresPostedCollectionAccountingBeforeFulfillment(Order order)
    {
        return OrderOperationalFinancialAuthority.RequiresPostedCollectionAccountingBeforeOutboundProgress(order);
    }

    private static bool RequiresRecognizedPaymentBeforeFulfillment(Order order)
    {
        return OrderOperationalFinancialAuthority.RequiresRecognizedPaymentBeforeOutboundProgress(order);
    }

    private static bool IsOutboundFulfillmentProgression(FulfillmentStatus targetStatus) =>
        targetStatus is FulfillmentStatus.Confirmed
            or FulfillmentStatus.Packed
            or FulfillmentStatus.OutForDelivery
            or FulfillmentStatus.Delivered
            or FulfillmentStatus.Completed;

    private static bool IsShipmentSynchronizationTarget(FulfillmentStatus targetStatus) =>
        targetStatus is FulfillmentStatus.Confirmed
            or FulfillmentStatus.Packed
            or FulfillmentStatus.OutForDelivery
            or FulfillmentStatus.Delivered;

    private static bool IsAtOrBeyondOutboundTarget(FulfillmentStatus currentStatus, FulfillmentStatus targetStatus)
    {
        var currentRank = GetOutboundProgressionRank(currentStatus);
        var targetRank = GetOutboundProgressionRank(targetStatus);

        return currentRank.HasValue &&
               targetRank.HasValue &&
               currentRank.Value >= targetRank.Value;
    }

    private static IEnumerable<FulfillmentStatus> GetShipmentSynchronizationSteps(
        FulfillmentStatus currentStatus,
        FulfillmentStatus targetStatus)
    {
        if (currentStatus == FulfillmentStatus.FailedDelivery &&
            GetOutboundProgressionRank(targetStatus) >= GetOutboundProgressionRank(FulfillmentStatus.OutForDelivery))
        {
            yield return FulfillmentStatus.OutForDelivery;
            currentStatus = FulfillmentStatus.OutForDelivery;
        }

        if (currentStatus == FulfillmentStatus.Created &&
            GetOutboundProgressionRank(targetStatus) >= GetOutboundProgressionRank(FulfillmentStatus.Confirmed))
        {
            yield return FulfillmentStatus.Confirmed;
            currentStatus = FulfillmentStatus.Confirmed;
        }

        if (currentStatus == FulfillmentStatus.Confirmed &&
            GetOutboundProgressionRank(targetStatus) >= GetOutboundProgressionRank(FulfillmentStatus.Packed))
        {
            yield return FulfillmentStatus.Packed;
            currentStatus = FulfillmentStatus.Packed;
        }

        if (currentStatus == FulfillmentStatus.Packed &&
            GetOutboundProgressionRank(targetStatus) >= GetOutboundProgressionRank(FulfillmentStatus.OutForDelivery))
        {
            yield return FulfillmentStatus.OutForDelivery;
            currentStatus = FulfillmentStatus.OutForDelivery;
        }

        if (currentStatus == FulfillmentStatus.OutForDelivery &&
            targetStatus == FulfillmentStatus.Delivered)
        {
            yield return FulfillmentStatus.Delivered;
        }
    }

    private static int? GetOutboundProgressionRank(FulfillmentStatus status)
    {
        return status switch
        {
            FulfillmentStatus.Created => 0,
            FulfillmentStatus.Confirmed => 1,
            FulfillmentStatus.Packed => 2,
            FulfillmentStatus.OutForDelivery => 3,
            FulfillmentStatus.Delivered => 4,
            FulfillmentStatus.Completed => 5,
            _ => null
        };
    }

    private static string BuildShipmentSyncIdempotencyKey(
        Guid orderId,
        Guid? shipmentId,
        string? idempotencyKeyPrefix,
        FulfillmentStatus step)
    {
        var prefix = string.IsNullOrWhiteSpace(idempotencyKeyPrefix)
            ? $"shipment-sync:{shipmentId?.ToString("N") ?? orderId.ToString("N")}"
            : idempotencyKeyPrefix;

        return $"{prefix}:fulfillment:{step}";
    }

    private static string GetIconHint(FulfillmentStatus status)
    {
        return status switch
        {
            FulfillmentStatus.Confirmed => "check-circle",
            FulfillmentStatus.Packed => "package",
            FulfillmentStatus.OutForDelivery => "truck",
            FulfillmentStatus.Delivered => "home",
            FulfillmentStatus.Completed => "check-double",
            FulfillmentStatus.Returned => "undo",
            FulfillmentStatus.FailedDelivery => "x-circle",
            FulfillmentStatus.Cancelled => "x",
            _ => "circle"
        };
    }

    private static List<string> GetRequiredFields(FulfillmentStatus status)
    {
        return status switch
        {
            FulfillmentStatus.Delivered => new List<string> { "deliveryReference", "proofOfDeliveryUrl" },
            FulfillmentStatus.FailedDelivery => new List<string> { "reason" },
            FulfillmentStatus.Returned => new List<string> { "reason" },
            _ => new List<string>()
        };
    }

    private static bool IsValidProofOfDeliveryUrl(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Length <= MaxProofOfDeliveryUrlLength &&
               !value.Any(char.IsWhiteSpace) &&
               Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetActionCode(FulfillmentStatus currentStatus, FulfillmentStatus targetStatus)
    {
        return currentStatus == FulfillmentStatus.FailedDelivery && targetStatus == FulfillmentStatus.OutForDelivery
            ? "retry-delivery"
            : targetStatus.GetActionCode();
    }

    private static string GetActionLabel(FulfillmentStatus currentStatus, FulfillmentStatus targetStatus)
    {
        return currentStatus == FulfillmentStatus.FailedDelivery && targetStatus == FulfillmentStatus.OutForDelivery
            ? "Retry Delivery"
            : targetStatus.GetActionLabel();
    }

    #endregion
}
