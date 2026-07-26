namespace PointOfSale.Core.Enums;

/// <summary>
/// PHASE OFL: Order Fulfillment Lifecycle Status
/// 
/// This enum represents the physical fulfillment journey of an order.
/// SEPARATED from PaymentStatus to ensure clear distinction between:
/// - Fulfillment: Physical order state (packing, shipping, delivery)
/// - Payment: Financial state (pending, collected, settled)
/// 
/// CRITICAL: FulfillmentStatus can ONLY be changed via IOrderFulfillmentService.
/// Direct writes are blocked at runtime by SaveChanges interceptor.
/// </summary>
public enum FulfillmentStatus
{
    /// <summary>
    /// Order has been created but not yet confirmed for fulfillment.
    /// Initial state after checkout.
    /// </summary>
    Created = 0,
    
    /// <summary>
    /// Order confirmed and accepted for fulfillment.
    /// Inventory has been reserved.
    /// </summary>
    Confirmed = 1,
    
    /// <summary>
    /// Order is being packed in the warehouse.
    /// Ready for handoff to shipping.
    /// </summary>
    Packed = 2,
    
    /// <summary>
    /// Order has been handed to carrier/driver.
    /// En route to customer.
    /// </summary>
    OutForDelivery = 3,
    
    /// <summary>
    /// Order physically delivered to customer.
    /// 
    /// TRIGGERS PAYMENT LIFECYCLE:
    /// - Offline collection: IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync()
    /// - Wallet (AwaitingVerification): Auto-approve may trigger
    /// - Online: No action (already Collected via webhook)
    /// </summary>
    Delivered = 4,
    
    /// <summary>
    /// Order lifecycle complete.
    /// All financial and fulfillment obligations satisfied.
    /// </summary>
    Completed = 5,
    
    /// <summary>
    /// Order was returned by customer after delivery.
    /// May trigger refund workflow.
    /// </summary>
    Returned = 6,
    
    /// <summary>
    /// Delivery attempt failed (customer not available, wrong address, etc.)
    /// May be retried or cancelled.
    /// </summary>
    FailedDelivery = 7,
    
    /// <summary>
    /// Order was cancelled before delivery.
    /// Inventory is released, no financial recognition.
    /// </summary>
    Cancelled = 8
}

/// <summary>
/// Extension methods for FulfillmentStatus transitions.
/// </summary>
public static class FulfillmentStatusExtensions
{
    /// <summary>
    /// Returns the list of valid next states from current state.
    /// </summary>
    public static IReadOnlyList<FulfillmentStatus> GetAllowedTransitions(this FulfillmentStatus current)
    {
        return current switch
        {
            FulfillmentStatus.Created => new[] { FulfillmentStatus.Confirmed, FulfillmentStatus.Cancelled },
            FulfillmentStatus.Confirmed => new[] { FulfillmentStatus.Packed, FulfillmentStatus.Cancelled },
            FulfillmentStatus.Packed => new[] { FulfillmentStatus.OutForDelivery, FulfillmentStatus.Cancelled },
            FulfillmentStatus.OutForDelivery => new[] { FulfillmentStatus.Delivered, FulfillmentStatus.FailedDelivery },
            FulfillmentStatus.Delivered => new[] { FulfillmentStatus.Completed, FulfillmentStatus.Returned },
            FulfillmentStatus.FailedDelivery => new[] { FulfillmentStatus.OutForDelivery, FulfillmentStatus.Cancelled },
            FulfillmentStatus.Completed => Array.Empty<FulfillmentStatus>(),
            FulfillmentStatus.Returned => Array.Empty<FulfillmentStatus>(),
            FulfillmentStatus.Cancelled => Array.Empty<FulfillmentStatus>(),
            _ => Array.Empty<FulfillmentStatus>()
        };
    }
    
    /// <summary>
    /// Checks if transition from current to target state is allowed.
    /// </summary>
    public static bool CanTransitionTo(this FulfillmentStatus current, FulfillmentStatus target)
    {
        return current.GetAllowedTransitions().Contains(target);
    }
    
    /// <summary>
    /// Returns a human-readable action name for the transition.
    /// </summary>
    public static string GetActionLabel(this FulfillmentStatus target)
    {
        return target switch
        {
            FulfillmentStatus.Confirmed => "Confirm Order",
            FulfillmentStatus.Packed => "Mark as Packed",
            FulfillmentStatus.OutForDelivery => "Out for Delivery",
            FulfillmentStatus.Delivered => "Mark as Delivered",
            FulfillmentStatus.Completed => "Complete Order",
            FulfillmentStatus.Returned => "Mark as Returned",
            FulfillmentStatus.FailedDelivery => "Failed Delivery",
            FulfillmentStatus.Cancelled => "Cancel Order",
            _ => target.ToString()
        };
    }
    
    /// <summary>
    /// Returns the action code for API calls.
    /// </summary>
    public static string GetActionCode(this FulfillmentStatus target)
    {
        return target switch
        {
            FulfillmentStatus.Confirmed => "confirm",
            FulfillmentStatus.Packed => "pack",
            FulfillmentStatus.OutForDelivery => "out-for-delivery",
            FulfillmentStatus.Delivered => "delivered",
            FulfillmentStatus.Completed => "complete",
            FulfillmentStatus.Returned => "returned",
            FulfillmentStatus.FailedDelivery => "failed-delivery",
            FulfillmentStatus.Cancelled => "cancel",
            _ => target.ToString().ToLowerInvariant()
        };
    }
}
