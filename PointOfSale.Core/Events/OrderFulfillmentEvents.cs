using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events;

/// <summary>
/// PHASE OFL: Order Fulfillment Lifecycle Events
/// 
/// Domain events that represent fulfillment state transitions.
/// These events integrate with PaymentLifecycle for financial actions.
/// </summary>

#region Fulfillment Transition Events

/// <summary>
/// Base event for all fulfillment transitions.
/// Contains common properties for tracking and correlation.
/// 
/// SECURITY: Includes Source and IdempotencyKey for audit trail and replay protection.
/// </summary>
public abstract class OrderFulfillmentEventBase : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public FulfillmentStatus OldStatus { get; set; }
    public FulfillmentStatus NewStatus { get; set; }
    public string? TransitionedBy { get; set; }
    public DateTime TransitionedAt { get; set; }
    
    /// <summary>
    /// SECURITY: Source of the fulfillment request (Manual, Webhook, System).
    /// Used for audit trail and security monitoring.
    /// </summary>
    public FulfillmentRequestSource Source { get; set; }
    
    /// <summary>
    /// SECURITY: Idempotency key for webhook replay protection.
    /// Webhooks MUST provide this. Manual requests may omit.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Event raised when an order is confirmed for fulfillment.
/// </summary>
public class FulfillmentConfirmedEvent : OrderFulfillmentEventBase
{
    public FulfillmentConfirmedEvent()
    {
        NewStatus = FulfillmentStatus.Confirmed;
    }
}

/// <summary>
/// Event raised when an order is packed and ready for shipping.
/// </summary>
public class OrderPackedEvent : OrderFulfillmentEventBase
{
    public OrderPackedEvent()
    {
        NewStatus = FulfillmentStatus.Packed;
    }
    
    /// <summary>
    /// Packing slip reference if applicable.
    /// </summary>
    public string? PackingSlipNumber { get; set; }
}

/// <summary>
/// Event raised when an order is handed off to carrier/driver.
/// </summary>
public class OrderOutForDeliveryEvent : OrderFulfillmentEventBase
{
    public OrderOutForDeliveryEvent()
    {
        NewStatus = FulfillmentStatus.OutForDelivery;
    }
    
    /// <summary>
    /// Associated shipment ID.
    /// </summary>
    public Guid? ShipmentId { get; set; }
    
    /// <summary>
    /// Carrier tracking number if available.
    /// </summary>
    public string? TrackingNumber { get; set; }
}

/// <summary>
/// Event raised when an order is delivered to the customer.
/// 
/// PAYMENT INTEGRATION:
/// - Offline collection orders: Triggers IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync()
/// - Wallet (AwaitingVerification): May trigger auto-approval
/// - Online (already Collected): No financial action
/// 
/// This is the key integration point between Fulfillment and Payment lifecycles.
/// 
/// NOTE: Named FulfillmentDeliveredEvent to avoid conflict with existing OrderDeliveredEvent.
/// </summary>
public class FulfillmentDeliveredEvent : OrderFulfillmentEventBase
{
    public FulfillmentDeliveredEvent()
    {
        NewStatus = FulfillmentStatus.Delivered;
    }
    
    /// <summary>
    /// Delivery confirmation reference (POD number, signature ID, etc.)
    /// </summary>
    public string? DeliveryReference { get; set; }
    
    /// <summary>
    /// Proof of delivery image URL if captured.
    /// </summary>
    public string? ProofOfDeliveryUrl { get; set; }
    
    /// <summary>
    /// Recipient name if different from customer.
    /// </summary>
    public string? RecipientName { get; set; }
    
    /// <summary>
    /// Payment method of the order.
    /// Used by handlers to determine if PaymentLifecycle action is needed.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;
    
    /// <summary>
    /// Current payment status at time of delivery.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; }
    
    /// <summary>
    /// Order total for COD collection.
    /// </summary>
    public decimal OrderTotal { get; set; }
}

/// <summary>
/// Event raised when an order is marked as completed.
/// Lifecycle final state for successful orders.
/// </summary>
public class OrderCompletedEvent : OrderFulfillmentEventBase
{
    public OrderCompletedEvent()
    {
        NewStatus = FulfillmentStatus.Completed;
    }
}

/// <summary>
/// Event raised when an order is returned by the customer.
/// 
/// PAYMENT INTEGRATION:
/// - May trigger IPaymentLifecycleService.RecordRefundAsync()
/// - Depends on return policy and payment status
/// </summary>
public class OrderReturnedEvent : OrderFulfillmentEventBase
{
    public OrderReturnedEvent()
    {
        NewStatus = FulfillmentStatus.Returned;
    }
    
    /// <summary>
    /// Reason for return.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Return authorization number if applicable.
    /// </summary>
    public string? ReturnAuthorizationNumber { get; set; }
    
    /// <summary>
    /// Whether a refund should be processed.
    /// </summary>
    public bool ShouldRefund { get; set; }
    
    /// <summary>
    /// Refund amount if different from order total.
    /// </summary>
    public decimal? RefundAmount { get; set; }
}

/// <summary>
/// Event raised when a delivery attempt fails.
/// </summary>
public class OrderDeliveryFailedEvent : OrderFulfillmentEventBase
{
    public OrderDeliveryFailedEvent()
    {
        NewStatus = FulfillmentStatus.FailedDelivery;
    }
    
    /// <summary>
    /// Reason for delivery failure.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of delivery attempts made.
    /// </summary>
    public int AttemptNumber { get; set; }
    
    /// <summary>
    /// Whether retry is possible.
    /// </summary>
    public bool CanRetry { get; set; } = true;
}

/// <summary>
/// Event raised when an order is cancelled.
/// </summary>
public class OrderFulfillmentCancelledEvent : OrderFulfillmentEventBase
{
    public OrderFulfillmentCancelledEvent()
    {
        NewStatus = FulfillmentStatus.Cancelled;
    }
    
    /// <summary>
    /// Reason for cancellation.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Who requested the cancellation.
    /// </summary>
    public string? CancelledBy { get; set; }
    
    /// <summary>
    /// Whether inventory should be released.
    /// </summary>
    public bool ReleaseInventory { get; set; } = true;
}

#endregion
