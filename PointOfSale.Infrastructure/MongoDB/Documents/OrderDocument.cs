using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model for Order projections.
/// 
/// PHASE 10.5.A: Order Lifecycle State Machine
/// 
/// This document is projected from PostgreSQL (write model) via domain events.
/// It is the ONLY source for Storefront order reads.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL = Write model (source of truth for orders)
/// - MongoDB = Read model (Storefront/Tracking projections)
/// - Storefront MUST read from this collection, NOT from SQL
/// - Updates via consumers: OrderProjectionConsumer
/// </summary>
[BsonIgnoreExtraElements]
public class OrderDocument
{
    /// <summary>
    /// MongoDB internal document ID - auto-generated if not set
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; set; }

    /// <summary>
    /// The tenant that owns this order.
    /// CRITICAL: This MUST match the event's TenantId for tenant isolation.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    /// <summary>
    /// The PostgreSQL Order.Id - source of truth reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid OrderId { get; set; }

    /// <summary>
    /// Human-readable order number (e.g., "ORD-20260112-0001")
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// POS customer ID. Storefront ownership must use OnlineCustomerId.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Online customer ID for authenticated storefront purchases.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? OnlineCustomerId { get; set; }

    /// <summary>
    /// Storefront analytics session reference captured at checkout.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Customer email (for both authenticated and guest orders)
    /// </summary>
    public string? CustomerEmail { get; set; }

    /// <summary>
    /// Customer name
    /// </summary>
    public string? CustomerName { get; set; }

    /// <summary>
    /// Current order status (e.g., "Pending", "Processing", "Shipped", "Delivered", "Cancelled")
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Current payment status (e.g., "Pending", "Paid", "Failed", "Refunded")
    /// </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>
    /// Current shipping status (e.g., "Pending", "Processing", "Shipped", "Delivered")
    /// </summary>
    public string ShippingStatus { get; set; } = string.Empty;

    /// <summary>
    /// Reason if cancelled
    /// </summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Order subtotal (before shipping and discounts)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Subtotal { get; set; }

    /// <summary>
    /// PHASE: Promotions Engine - Promotion discount amount (from product offers)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal PromotionDiscountAmount { get; set; }

    /// <summary>
    /// Discount amount applied (coupon discount)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Tax amount
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Shipping cost
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal ShippingCost { get; set; }

    /// <summary>
    /// Order grand total
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; set; }

    /// <summary>
    /// Currency code (e.g., "USD", "EGP")
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Payment method used (e.g., "COD", "STRIPE", "PAYMOB")
    /// </summary>
    public string? PaymentMethodCode { get; set; }

    /// <summary>
    /// Payment method display name
    /// </summary>
    public string? PaymentMethodName { get; set; }

    /// <summary>
    /// Shipping method display name
    /// </summary>
    public string? ShippingMethodName { get; set; }

    /// <summary>
    /// Coupon code used (if any)
    /// </summary>
    public string? CouponCode { get; set; }

    /// <summary>
    /// Order items
    /// </summary>
    public List<OrderItemDocument> Items { get; set; } = new();

    /// <summary>
    /// Shipping address
    /// </summary>
    public OrderAddressDocument? ShippingAddress { get; set; }

    /// <summary>
    /// Tracking number (if shipped)
    /// </summary>
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Carrier name (if shipped)
    /// </summary>
    public string? CarrierName { get; set; }

    /// <summary>
    /// Tracking URL (if available)
    /// </summary>
    public string? TrackingUrl { get; set; }

    /// <summary>
    /// Estimated delivery date
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EstimatedDeliveryDate { get; set; }

    /// <summary>
    /// Actual delivery date
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeliveredAt { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // FULFILLMENT OWNERSHIP ARCHITECTURE
    // 
    // These fields are snapshotted from Order entity at creation.
    // They determine WHO controls the fulfillment lifecycle.
    // UI MUST rely ONLY on these fields (never infer from ShippingMethod).
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fulfillment ownership mode: "Manual" or "Carrier"
    /// Determines who controls fulfillment lifecycle.
    /// IMMUTABLE after order creation.
    /// </summary>
    public string FulfillmentMode { get; set; } = "Manual";

    /// <summary>
    /// Carrier code for carrier-managed orders (e.g., "bosta", "aramex").
    /// NULL for manual fulfillment orders.
    /// IMMUTABLE after order creation.
    /// </summary>
    public string? FulfillmentCarrierCode { get; set; }

    /// <summary>
    /// Computed property: true if this order allows manual admin fulfillment transitions.
    /// UI uses this to determine if manual action buttons should be shown.
    /// </summary>
    [BsonIgnore]
    public bool IsManualFulfillment => FulfillmentMode == "Manual";

    /// <summary>
    /// Computed property: true if this order is managed by carrier webhooks.
    /// UI uses this to determine if read-only carrier tracking panel should be shown.
    /// </summary>
    [BsonIgnore]
    public bool IsCarrierFulfillment => FulfillmentMode == "Carrier";

    /// <summary>
    /// Order timeline (status changes, events)
    /// </summary>
    public List<OrderTimelineItem> Timeline { get; set; } = new();

    /// <summary>
    /// When the order was created
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this document was last updated
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// The EventId that last updated this document (for debugging/tracing)
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    /// <summary>
    /// Monotonic freshness guard for lifecycle projections. Consumers must reject
    /// events older than this timestamp so stale outbox delivery cannot regress
    /// customer-facing order state.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    /// <summary>
    /// Aggregate mutation version carried by the newest lifecycle event applied
    /// to this document. Used as a tie-breaker when events share the same
    /// occurrence timestamp.
    /// </summary>
    public int LastEventVersion { get; set; }

    /// <summary>
    /// Independent freshness guard for order status projections. This is separate
    /// from payment and shipment so out-of-order delivery of unrelated events
    /// cannot prevent a valid status mutation from being applied.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastStatusEventOccurredAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastStatusEventId { get; set; }

    public int LastStatusEventVersion { get; set; }

    /// <summary>
    /// Independent freshness guard for payment projections.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastPaymentEventOccurredAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastPaymentEventId { get; set; }

    public int LastPaymentEventVersion { get; set; }

    /// <summary>
    /// Independent freshness guard for shipment/tracking projections.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastShipmentEventOccurredAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastShipmentEventId { get; set; }

    public int LastShipmentEventVersion { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // AF-35: AFFILIATE ATTRIBUTION
    //
    // Set by AffiliateAttributedEvent consumer (no SQL join per request).
    // IsAffiliateOrder defaults to false for all pre-existing orders.
    // BsonIgnoreExtraElements ensures zero-crash backward compat.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-35: True if this order was placed via an affiliate referral.
    /// Set by AffiliateAttributedEvent consumer.
    /// </summary>
    public bool IsAffiliateOrder { get; set; }

    /// <summary>
    /// AF-35: The affiliate account ID that referred this order.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? AffiliateId { get; set; }

    /// <summary>
    /// AF-35: Referral code used for this attribution (snapshot at attribution time).
    /// </summary>
    public string? AffiliateReferralCode { get; set; }

    /// <summary>
    /// AF-35: Total commission earned on this order.
    /// Set by AffiliateCommissionEarnedEvent consumer.
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? AffiliateCommissionAmount { get; set; }

    /// <summary>
    /// Independent freshness guard for affiliate commission projections. This prevents
    /// stale commission events from overwriting money fields without coupling them to
    /// unrelated order status/shipping event ordering.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastAffiliateCommissionEventOccurredAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastAffiliateCommissionEventId { get; set; }

    public int LastAffiliateCommissionEventVersion { get; set; }

    /// <summary>
    /// Independent freshness guard for affiliate attribution projections.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastAffiliateAttributionEventOccurredAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastAffiliateAttributionEventId { get; set; }

    public int LastAffiliateAttributionEventVersion { get; set; }

    /// <summary>
    /// AF-35: UTC timestamp when this order was attributed to the affiliate.
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? AttributedAtUtc { get; set; }
}

/// <summary>
/// Order item in MongoDB projection
/// 
/// PHASE 11.7: Added VariantId and VariantName for SKU-level tracking.
/// </summary>
[BsonIgnoreExtraElements]
public class OrderItemDocument
{
    /// <summary>
    /// Product ID reference
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID (SKU identifier). Null for legacy orders before Phase 11.
    /// PHASE 11.7: Added for SKU-level tracking.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Product SKU
    /// </summary>
    public string SKU { get; set; } = string.Empty;

    /// <summary>
    /// Product name
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Variant name/title (e.g., "Red / XL"). Null for simple products.
    /// </summary>
    public string? VariantName { get; set; }

    /// <summary>
    /// Quantity ordered
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price at time of order
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Line item total (Quantity * UnitPrice)
    /// </summary>
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Total { get; set; }

    /// <summary>
    /// Product image URL
    /// </summary>
    public string? ImageUrl { get; set; }
}

/// <summary>
/// Shipping address in MongoDB projection
/// </summary>
[BsonIgnoreExtraElements]
public class OrderAddressDocument
{
    public string Street { get; set; } = string.Empty;
    public string? Street2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// Timeline entry for order status tracking
/// </summary>
[BsonIgnoreExtraElements]
public class OrderTimelineItem
{
    /// <summary>
    /// When this event occurred
    /// </summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime At { get; set; }

    /// <summary>
    /// Type of timeline entry (e.g., "OrderCreated", "StatusChanged", "PaymentReceived", "Shipped", "Delivered")
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Previous status (for status changes)
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// New status (for status changes)
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// Additional description or notes
    /// </summary>
    public string? Description { get; set; }
}
