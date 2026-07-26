using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Interfaces.Fulfillment;

/// <summary>
/// PHASE OFL: Order Fulfillment Lifecycle Service
/// 
/// This is the ONLY gateway for changing order fulfillment status.
/// All status transitions MUST go through this service.
/// 
/// ABSOLUTE LAWS:
/// - Controllers MUST NOT write FulfillmentStatus directly
/// - UI MUST NOT bypass this service
/// - All transitions are validated and event-driven
/// - Integration with PaymentLifecycle is automatic
/// 
/// SECURITY HARDENING:
/// - All methods require explicit FulfillmentRequestSource
/// - Source is validated against FulfillmentMode
/// - Manual source CANNOT update Carrier orders
/// - Webhook source CANNOT update Manual orders
/// - All violations logged as SECURITY_VIOLATION
/// 
/// ARCHITECTURE:
/// Order → [IOrderFulfillmentService] → FulfillmentStatus change → Domain Event
///                                                                       ↓
///                                                              [Event Handler]
///                                                                       ↓
///                                                         [IPaymentLifecycleService] (if needed)
///                                                                       ↓
///                                                              [Ledger Journal]
/// </summary>
public interface IOrderFulfillmentService
{
    /// <summary>
    /// Confirms an order for fulfillment.
    /// Transitions: Created → Confirmed
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    /// <param name="orderId">The order ID</param>
    /// <param name="source">REQUIRED: Request source (Manual, Webhook, System)</param>
    /// <param name="confirmedBy">Optional: who confirmed the order</param>
    /// <param name="idempotencyKey">Optional: idempotency key for webhook replay protection</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Updated order with new fulfillment status</returns>
    Task<OrderFulfillmentResult> ConfirmAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? confirmedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Marks an order as packed and ready for shipping.
    /// Transitions: Confirmed → Packed
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    Task<OrderFulfillmentResult> PackAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? packingSlipNumber = null,
        string? packedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Marks an order as out for delivery.
    /// Transitions: Packed → OutForDelivery
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        Guid? shipmentId = null,
        string? trackingNumber = null,
        string? updatedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Marks an order as delivered.
    /// Transitions: OutForDelivery → Delivered
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// 
    /// PAYMENT INTEGRATION:
    /// - Offline collection: Publishes OrderDeliveredEvent -> Handler calls IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync
    /// - Wallet: May trigger approval workflow
    /// - Online: No financial action (already collected)
    /// </summary>
    Task<OrderFulfillmentResult> MarkDeliveredAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? deliveryReference = null,
        string? proofOfDeliveryUrl = null,
        string? recipientName = null,
        string? deliveredBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);

    /// <summary>
    /// Converges authoritative order fulfillment state from a shipment status mutation.
    /// Shipment services must call this instead of publishing fulfillment lifecycle events directly.
    /// 
    /// ALLOWED VALUES FOR targetStatus (shipment-authoritative statuses only):
    /// - FulfillmentStatus.Confirmed: Order confirmed and accepted for fulfillment
    /// - FulfillmentStatus.Packed: Order packed and ready for shipping
    /// - FulfillmentStatus.OutForDelivery: Order handed to carrier/driver
    /// - FulfillmentStatus.Delivered: Order physically delivered to customer
    /// 
    /// CRITICAL: Callers must NOT pass non-shipment states such as Created, Completed, 
    /// Returned, FailedDelivery, or Cancelled. These states have separate lifecycle methods
    /// and are not valid targets for SynchronizeFromShipmentAsync.
    /// </summary>
    /// <param name="orderId">The order to synchronize fulfillment state for</param>
    /// <param name="targetStatus">
    /// Target FulfillmentStatus to synchronize to. Must be one of the shipment-authoritative 
    /// statuses: Confirmed, Packed, OutForDelivery, or Delivered. Other values will be rejected.
    /// </param>
    /// <param name="source">The source of the fulfillment request (Manual, Webhook, System)</param>
    /// <param name="shipmentId">Optional shipment ID for tracking and idempotency</param>
    /// <param name="trackingNumber">Optional tracking number to associate with the fulfillment state</param>
    /// <param name="actor">Optional actor identifier (e.g., "system:carrier_webhook", "admin:user123")</param>
    /// <param name="idempotencyKeyPrefix">
    /// Optional stable, repeatable prefix for idempotency key construction. The prefix is combined 
    /// with shipmentId (or orderId if shipmentId is null) and the target FulfillmentStatus to form 
    /// a replay-safe idempotency key used to deduplicate retries. Callers should provide a stable 
    /// prefix per external retry source (e.g., "shipment:abc123:ReadyForPickup:correlation-xyz") 
    /// to ensure the same external event always generates the same idempotency key across retries.
    /// If null, a default prefix is generated from shipmentId or orderId.
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result indicating success or failure of the synchronization operation</returns>
    Task<OrderFulfillmentResult> SynchronizeFromShipmentAsync(
        Guid orderId,
        FulfillmentStatus targetStatus,
        FulfillmentRequestSource source,
        Guid? shipmentId = null,
        string? trackingNumber = null,
        string? actor = null,
        string? idempotencyKeyPrefix = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Marks an order as completed.
    /// Transitions: Delivered → Completed
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    Task<OrderFulfillmentResult> MarkCompletedAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? completedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Marks an order as returned.
    /// Transitions: Delivered → Returned
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// 
    /// PAYMENT INTEGRATION:
    /// - If shouldRefund is true, publishes OrderReturnedEvent → Handler may call IPaymentLifecycleService.RecordRefundAsync
    /// </summary>
    Task<OrderFulfillmentResult> MarkReturnedAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string reason,
        bool shouldRefund = false,
        decimal? refundAmount = null,
        string? returnAuthorizationNumber = null,
        string? returnedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Marks a delivery as failed.
    /// Transitions: OutForDelivery → FailedDelivery
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    Task<OrderFulfillmentResult> MarkDeliveryFailedAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string reason,
        int attemptNumber = 1,
        bool canRetry = true,
        string? updatedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Retries delivery after a failed attempt.
    /// Transitions: FailedDelivery → OutForDelivery
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    Task<OrderFulfillmentResult> RetryDeliveryAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string? updatedBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Cancels an order.
    /// Transitions: Created/Confirmed/Packed/FailedDelivery → Cancelled
    /// 
    /// SECURITY: Requires explicit source. Manual source blocked for Carrier orders.
    /// </summary>
    Task<OrderFulfillmentResult> CancelAsync(
        Guid orderId,
        FulfillmentRequestSource source,
        string reason,
        bool releaseInventory = true,
        string? cancelledBy = null,
        string? idempotencyKey = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets available fulfillment actions for an order.
    /// Used by UI to render dynamic action buttons.
    /// 
    /// SECURITY: Returns empty list for Carrier orders (no manual actions allowed).
    /// </summary>
    Task<FulfillmentActionsResult> GetAvailableActionsAsync(
        Guid orderId,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a fulfillment transition operation.
/// </summary>
public class OrderFulfillmentResult
{
    public bool Success { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public FulfillmentStatus OldStatus { get; set; }
    public FulfillmentStatus NewStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime TransitionedAt { get; set; }
    
    /// <summary>
    /// SECURITY: Source of the fulfillment request.
    /// Used for audit trail and security monitoring.
    /// </summary>
    public FulfillmentRequestSource Source { get; set; }
    
    /// <summary>
    /// SECURITY: Idempotency key for webhook replay protection.
    /// </summary>
    public string? IdempotencyKey { get; set; }
    
    public static OrderFulfillmentResult Succeeded(
        Guid orderId, 
        string orderNumber,
        FulfillmentStatus oldStatus, 
        FulfillmentStatus newStatus,
        FulfillmentRequestSource source,
        string? idempotencyKey = null)
    {
        return new OrderFulfillmentResult
        {
            Success = true,
            OrderId = orderId,
            OrderNumber = orderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Source = source,
            IdempotencyKey = idempotencyKey,
            TransitionedAt = DateTime.UtcNow
        };
    }
    
    public static OrderFulfillmentResult Failed(
        Guid orderId, 
        string orderNumber,
        FulfillmentStatus currentStatus,
        FulfillmentRequestSource source,
        string error,
        string? idempotencyKey = null)
    {
        return new OrderFulfillmentResult
        {
            Success = false,
            OrderId = orderId,
            OrderNumber = orderNumber,
            OldStatus = currentStatus,
            NewStatus = currentStatus, // No change
            ErrorMessage = error,
            Source = source,
            IdempotencyKey = idempotencyKey,
            TransitionedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Available fulfillment actions for an order.
/// </summary>
public class FulfillmentActionsResult
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public FulfillmentStatus CurrentStatus { get; set; }
    public List<FulfillmentAction> AvailableActions { get; set; } = new();
}

/// <summary>
/// A single fulfillment action available for an order.
/// </summary>
public class FulfillmentAction
{
    /// <summary>
    /// Action code for API calls (e.g., "confirm", "pack", "delivered")
    /// </summary>
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable label for the action
    /// </summary>
    public string Label { get; set; } = string.Empty;
    
    /// <summary>
    /// Target status after this action
    /// </summary>
    public FulfillmentStatus TargetStatus { get; set; }
    
    /// <summary>
    /// Whether this action requires confirmation
    /// </summary>
    public bool RequiresConfirmation { get; set; }
    
    /// <summary>
    /// CSS class or icon hint for UI
    /// </summary>
    public string? IconHint { get; set; }
}
