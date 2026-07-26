using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events;

#region Shipment Events

/// <summary>
/// Base class for all shipment events.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// These events support analytics integration:
/// - ShipmentCreated
/// - ShipmentDelivered
/// - ShipmentFailed
/// </summary>
public abstract class ShipmentEventBase : IntegrationEvent
{
    /// <summary>
    /// Shipment ID.
    /// </summary>
    public Guid ShipmentId { get; set; }

    /// <summary>
    /// Order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Shipment number.
    /// </summary>
    public string ShipmentNumber { get; set; } = string.Empty;

    /// <summary>
    /// Order number.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Carrier code (carrier-agnostic identifier).
    /// </summary>
    public string CarrierCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Service level (standard, express, same_day).
    /// </summary>
    public string ServiceLevel { get; set; } = "standard";
}

/// <summary>
/// Event emitted when a shipment is created with carrier.
/// 
/// ANALYTICS EVENT: ShipmentCreated
/// </summary>
public class ShipmentCreatedEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier display name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number from carrier.
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Tracking URL for customer.
    /// </summary>
    public string? TrackingUrl { get; set; }

    /// <summary>
    /// Shipping method ID (if using POS shipping methods).
    /// </summary>
    public Guid? ShippingMethodId { get; set; }

    /// <summary>
    /// Shipping method name.
    /// </summary>
    public string? ShippingMethodName { get; set; }

    /// <summary>
    /// Shipping cost.
    /// </summary>
    public decimal ShippingCost { get; set; }

    /// <summary>
    /// Currency.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Recipient name.
    /// </summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>
    /// Shipping address.
    /// </summary>
    public string ShippingAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping city.
    /// </summary>
    public string? ShippingCity { get; set; }
    
    /// <summary>
    /// Shipping country.
    /// </summary>
    public string? ShippingCountry { get; set; }

    /// <summary>
    /// Estimated delivery date.
    /// </summary>
    public DateTime? EstimatedDeliveryDate { get; set; }
    
    /// <summary>
    /// Whether this is a COD shipment.
    /// </summary>
    public bool IsCashOnDelivery { get; set; }
    
    /// <summary>
    /// COD amount to collect.
    /// </summary>
    public decimal? CodAmount { get; set; }
}

/// <summary>
/// Event emitted when a shipment is ready for carrier pickup.
/// </summary>
public class ShipmentReadyForPickupEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// When ready for pickup.
    /// </summary>
    public DateTime ReadyAt { get; set; }

    /// <summary>
    /// Notes.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Event emitted when carrier picks up the shipment.
/// </summary>
public class ShipmentPickedUpEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// When picked up.
    /// </summary>
    public DateTime PickedUpAt { get; set; }
}

/// <summary>
/// Event emitted when shipment is in transit.
/// </summary>
public class ShipmentInTransitEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }

    /// <summary>
    /// Tracking number.
    /// </summary>
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tracking URL.
    /// </summary>
    public string? TrackingUrl { get; set; }

    /// <summary>
    /// Estimated delivery date.
    /// </summary>
    public DateTime? EstimatedDeliveryDate { get; set; }

    /// <summary>
    /// When shipped.
    /// </summary>
    public DateTime ShippedAt { get; set; }
    
    /// <summary>
    /// Current location (if available).
    /// </summary>
    public string? CurrentLocation { get; set; }
}

/// <summary>
/// Event emitted when shipment is out for delivery.
/// </summary>
public class ShipmentOutForDeliveryEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// When delivery started.
    /// </summary>
    public DateTime OutForDeliveryAt { get; set; }
}

/// <summary>
/// Event emitted when a shipment is delivered.
/// 
/// ANALYTICS EVENT: ShipmentDelivered
/// </summary>
public class ShipmentDeliveredEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }

    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Actual delivery date.
    /// </summary>
    public DateTime DeliveredAt { get; set; }

    /// <summary>
    /// Delivery notes.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Whether COD was collected.
    /// </summary>
    public bool CodCollected { get; set; }
    
    /// <summary>
    /// COD amount collected.
    /// </summary>
    public decimal? CodAmount { get; set; }
    
    /// <summary>
    /// Number of delivery attempts.
    /// </summary>
    public int DeliveryAttempts { get; set; }
}

/// <summary>
/// Event emitted when shipment delivery fails.
/// 
/// ANALYTICS EVENT: ShipmentFailed
/// </summary>
public class ShipmentFailedEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Failure reason.
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>
    /// When failed.
    /// </summary>
    public DateTime FailedAt { get; set; }
    
    /// <summary>
    /// Number of delivery attempts before failure.
    /// </summary>
    public int DeliveryAttempts { get; set; }
    
    /// <summary>
    /// Whether COD was involved.
    /// </summary>
    public bool IsCashOnDelivery { get; set; }
}

/// <summary>
/// Event emitted when a shipment is cancelled.
/// </summary>
public class ShipmentCancelledEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Cancellation reason.
    /// </summary>
    public string CancellationReason { get; set; } = string.Empty;

    /// <summary>
    /// Previous status before cancellation.
    /// </summary>
    public ShipmentStatus PreviousStatus { get; set; }

    /// <summary>
    /// When cancelled.
    /// </summary>
    public DateTime CancelledAt { get; set; }
}

/// <summary>
/// Event emitted when shipment is returned.
/// </summary>
public class ShipmentReturnedEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Return reason.
    /// </summary>
    public string ReturnReason { get; set; } = string.Empty;

    /// <summary>
    /// When returned.
    /// </summary>
    public DateTime ReturnedAt { get; set; }
    
    /// <summary>
    /// Previous status before return.
    /// </summary>
    public ShipmentStatus PreviousStatus { get; set; }
}

/// <summary>
/// Event emitted when shipment status changes.
/// Generic event for any status transition.
/// </summary>
public class ShipmentStatusChangedEvent : ShipmentEventBase
{
    /// <summary>
    /// Previous status.
    /// </summary>
    public ShipmentStatus PreviousStatus { get; set; }

    /// <summary>
    /// New status.
    /// </summary>
    public ShipmentStatus NewStatus { get; set; }

    /// <summary>
    /// Tracking number (if available).
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// When status changed.
    /// </summary>
    public DateTime ChangedAt { get; set; }
    
    /// <summary>
    /// Current location (if available).
    /// </summary>
    public string? CurrentLocation { get; set; }
}

/// <summary>
/// Durable request to register a persisted shipment with the external carrier after commit.
/// Published through the transactional outbox so carrier APIs are never called inside checkout SQL transactions.
/// </summary>
public class ShipmentExternalRegistrationRequestedEvent : ShipmentEventBase
{
    /// <summary>
    /// Shipping method identifier when available.
    /// </summary>
    public Guid? ShippingMethodId { get; set; }

    /// <summary>
    /// Shipping method name snapshot when available.
    /// </summary>
    public string? ShippingMethodName { get; set; }

    /// <summary>
    /// Whether the shipment is carrier-managed.
    /// Manual shipments should never enqueue this event.
    /// </summary>
    public bool IsCarrierManaged { get; set; }
}

/// <summary>
/// Event emitted when COD is collected by carrier.
/// </summary>
public class ShipmentCodCollectedEvent : ShipmentEventBase
{
    /// <summary>
    /// Carrier name.
    /// </summary>
    public string? CarrierName { get; set; }
    
    /// <summary>
    /// Tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// COD amount collected.
    /// </summary>
    public decimal CodAmount { get; set; }
    
    /// <summary>
    /// Currency.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// When collected.
    /// </summary>
    public DateTime CollectedAt { get; set; }
}

#endregion

#region Legacy Events (Backward Compatibility)

/// <summary>
/// Legacy event for packed status (use ShipmentReadyForPickupEvent instead).
/// </summary>
[Obsolete("Use ShipmentReadyForPickupEvent instead")]
public class ShipmentPackedEvent : ShipmentEventBase
{
    public string? CarrierName { get; set; }
    public DateTime PackedAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Legacy event for shipped status (use ShipmentInTransitEvent instead).
/// </summary>
[Obsolete("Use ShipmentInTransitEvent instead")]
public class ShipmentShippedEvent : ShipmentEventBase
{
    public string? CarrierName { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string? TrackingUrl { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime ShippedAt { get; set; }
}

#endregion
