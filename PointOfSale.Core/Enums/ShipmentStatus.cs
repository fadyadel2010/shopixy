namespace PointOfSale.Core.Enums;

/// <summary>
/// Shipment status for e-commerce orders.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// These statuses are designed to work with ANY shipping carrier.
/// Carrier-specific statuses are mapped to these normalized statuses.
/// 
/// CANONICAL RULES (NON-NEGOTIABLE):
/// 
/// Pending:
///   - ONLY used for internal/incomplete states BEFORE successful carrier creation
///   - Manual shipments not yet submitted to carrier
///   - Retry states where carrier API call failed
///   - NEVER map a carrier's "created" status to Pending
/// 
/// ReadyForPickup:
///   - Shipment successfully created/registered at carrier
///   - Awaiting carrier pickup
///   - This is the EXPECTED initial status after successful carrier API call
/// 
/// Lifecycle Rules (STRICT):
/// - PendingExternalRegistration → ReadyForPickup, Cancelled, Failed
/// - Pending → ReadyForPickup, Cancelled
/// - ReadyForPickup → PickedUp, Cancelled
/// - PickedUp → InTransit
/// - InTransit → OutForDelivery, Delivered, Failed, Returned
/// - OutForDelivery → Delivered, Failed, Returned
/// - Delivered → Returned (within return window)
/// - Failed → ❌ (terminal)
/// - Cancelled → ❌ (terminal)
/// - Returned → ❌ (terminal)
///
/// PendingExternalRegistration Transitions:
/// - Created during checkout for carrier-managed shipments
/// - Transitions to ReadyForPickup after successful external carrier registration
/// - Transitions to Cancelled if order is cancelled before registration completes
/// - Transitions to Failed if external registration fails permanently
/// </summary>
public enum ShipmentStatus
{
    /// <summary>
    /// Carrier-managed shipment intent persisted locally but not yet registered externally.
    /// This status is only valid before the post-commit carrier registration worker succeeds.
    /// </summary>
    PendingExternalRegistration = 9,

    /// <summary>
    /// Internal/incomplete state BEFORE carrier creation.
    /// Do NOT use for successfully created carrier shipments.
    /// </summary>
    Pending = 0,
    
    /// <summary>Items packed and ready for carrier pickup (legacy: Packed)</summary>
    ReadyForPickup = 1,
    
    /// <summary>Carrier has picked up the shipment</summary>
    PickedUp = 2,
    
    /// <summary>Shipment is in transit to destination (legacy: Shipped)</summary>
    InTransit = 3,
    
    /// <summary>Shipment is out for final delivery</summary>
    OutForDelivery = 4,
    
    /// <summary>Shipment delivered to customer</summary>
    Delivered = 5,
    
    /// <summary>Shipment delivery failed (address issue, customer unavailable, etc.)</summary>
    Failed = 6,
    
    /// <summary>Shipment cancelled before pickup</summary>
    Cancelled = 7,
    
    /// <summary>Shipment returned to sender/warehouse</summary>
    Returned = 8
}
