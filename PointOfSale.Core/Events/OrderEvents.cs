using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events;

#region Order Events

/// <summary>
/// Event raised when a new order is created.
/// 
/// PHASE 10.5.A: Enhanced for Order Lifecycle State Machine MongoDB projection.
/// PHASE: Abandoned Cart Hardening - Added CartId for recovery tracking.
/// </summary>
public class OrderCreatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public OrderSource Source { get; set; }
    public string? SourceOrderId { get; set; }
    public decimal Subtotal { get; set; }
    /// <summary>
    /// PHASE: Promotions Engine - Promotion discount from product offers
    /// </summary>
    public decimal PromotionDiscountAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemInfo> Items { get; set; } = new();
    
    // PHASE 10.5.A: Additional fields for MongoDB projection
    public string? ShippingMethodName { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? PaymentMethodName { get; set; }
    public string? SessionId { get; set; }
    public bool IsPaid { get; set; }
    
    // FULFILLMENT OWNERSHIP SNAPSHOT (IMMUTABLE)
    public FulfillmentMode? FulfillmentMode { get; set; }
    public string? FulfillmentCarrierCode { get; set; }
    
    // PHASE 10.5.A2: Shipping address for order tracking
    public string? ShippingStreet { get; set; }
    public string? ShippingStreet2 { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingZipCode { get; set; }
    public string? ShippingCountry { get; set; }
    public string? ShippingPhone { get; set; }
    public string? RecipientName { get; set; }
    
    /// <summary>
    /// PHASE: Abandoned Cart Hardening - Cart ID for recovery tracking.
    /// When this order is created, the abandoned cart with this ID
    /// will be marked as Recovered.
    /// </summary>
    public Guid? CartId { get; set; }
}

/// <summary>
/// Event raised when order status changes
/// </summary>
public class OrderStatusChangedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

public abstract class PaymentIntentOrderDomainEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

public sealed class OrderAcceptedEvent : PaymentIntentOrderDomainEvent;

public sealed class OrderAwaitingEvidenceEvent : PaymentIntentOrderDomainEvent;

public sealed class OrderOperationalReviewQueuedEvent : PaymentIntentOrderDomainEvent;

public sealed class OrderRejectedEvent : PaymentIntentOrderDomainEvent;

/// <summary>
/// Event raised when payment status changes
/// </summary>
public class OrderPaymentStatusChangedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public PaymentStatus OldStatus { get; set; }
    public PaymentStatus NewStatus { get; set; }
    public decimal? AmountPaid { get; set; }
    public string? PaymentReference { get; set; }
    
    /// <summary>
    /// Optional description for audit timeline (e.g., "Manual wallet payment approved by admin")
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Event raised when an order is shipped
/// </summary>
public class OrderShippedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public DateTime ShippedAt { get; set; }
}

/// <summary>
/// Event raised when an order is delivered
/// </summary>
public class OrderDeliveredEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime DeliveredAt { get; set; }
    public Guid? ShipmentId { get; set; }
    public string? DeliveryReference { get; set; }
    public string? ProofOfDeliveryUrl { get; set; }
}

/// <summary>
/// Event raised when an order is cancelled
/// </summary>
public class OrderCancelledEvent : IntegrationEvent, IAuthoritativeIntegrationEvent
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }
    public DateTime CancelledAt { get; set; }
    public bool RestoreInventory { get; set; }
}

/// <summary>
/// Order item information for order events
/// 
/// PHASE 11.7: Added VariantId and variant fields for SKU-level tracking.
/// </summary>
public class OrderItemInfo
{
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Variant ID (SKU identifier). Null for legacy orders before Phase 11.
    /// PHASE 11.7: Added for variant-aware projections.
    /// </summary>
    public Guid? VariantId { get; set; }
    
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>
    /// Variant name/title (e.g., "Red / XL"). Null for simple products.
    /// </summary>
    public string? VariantName { get; set; }
    
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    
    /// <summary>
    /// Product/Variant image URL for display.
    /// </summary>
    public string? ImageUrl { get; set; }
}

#endregion
