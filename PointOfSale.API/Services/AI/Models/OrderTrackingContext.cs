using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.AI.Models;

// =============================================================================
// PHASE 3: Order Tracking Context
// 
// These models provide order and shipment data to the AI for post-purchase
// customer support. The AI can answer questions like:
// - "Where is my order?"
// - "Has my order shipped?"
// - "When will it arrive?"
// 
// CRITICAL RULES:
// - AI can only READ and EXPLAIN this data
// - AI cannot MODIFY order/shipment status
// - AI cannot EXECUTE actions (contact carrier, cancel, refund)
// =============================================================================

/// <summary>
/// Order tracking context for post-purchase AI support.
/// PHASE 3: Event-Aware, Order-Context Intelligence.
/// </summary>
public class OrderTrackingContext
{
    /// <summary>
    /// Order ID (internal)
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Order number for customer reference (e.g., "ORD-10293")
    /// </summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable order status (e.g., "Processing", "Shipped", "Delivered")
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// When the order was created
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Items in the order
    /// </summary>
    [JsonPropertyName("items")]
    public List<OrderItemTrackingContext> Items { get; set; } = new();

    /// <summary>
    /// Payment information
    /// </summary>
    [JsonPropertyName("payment")]
    public PaymentTrackingContext Payment { get; set; } = new();

    /// <summary>
    /// Order total
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>
    /// Currency code
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "EGP";

    /// <summary>
    /// Whether the order was cancelled
    /// </summary>
    [JsonPropertyName("isCancelled")]
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Cancellation reason (if cancelled)
    /// </summary>
    [JsonPropertyName("cancellationReason")]
    public string? CancellationReason { get; set; }
}

/// <summary>
/// Order item for tracking context.
/// </summary>
public class OrderItemTrackingContext
{
    /// <summary>
    /// Product name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Quantity ordered
    /// </summary>
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price
    /// </summary>
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// Variant name (if applicable)
    /// </summary>
    [JsonPropertyName("variant")]
    public string? Variant { get; set; }
}

/// <summary>
/// Payment information for tracking context.
/// </summary>
public class PaymentTrackingContext
{
    /// <summary>
    /// Payment status (e.g., "Paid", "Pending", "Refunded")
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Payment method (e.g., "COD", "Card", "Wallet")
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Amount paid
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}

/// <summary>
/// Shipment tracking context for delivery inquiries.
/// PHASE 3: Event-Aware, Order-Context Intelligence.
/// </summary>
public class ShipmentTrackingContext
{
    /// <summary>
    /// Whether a shipment exists for this order
    /// </summary>
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    /// <summary>
    /// Carrier name (e.g., "Bosta", "Aramex", "DHL")
    /// </summary>
    [JsonPropertyName("carrier")]
    public string? Carrier { get; set; }

    /// <summary>
    /// Tracking number from carrier
    /// </summary>
    [JsonPropertyName("trackingNumber")]
    public string? TrackingNumber { get; set; }

    /// <summary>
    /// Current shipment status (human-readable)
    /// e.g., "Picked Up", "In Transit", "Out for Delivery", "Delivered"
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Estimated delivery date
    /// </summary>
    [JsonPropertyName("estimatedDelivery")]
    public DateTime? EstimatedDelivery { get; set; }

    /// <summary>
    /// Shipment timeline events
    /// </summary>
    [JsonPropertyName("timeline")]
    public List<ShipmentEventContext> Timeline { get; set; } = new();

    /// <summary>
    /// Whether the shipment failed
    /// </summary>
    [JsonPropertyName("failed")]
    public bool Failed { get; set; }

    /// <summary>
    /// Failure reason (if failed)
    /// </summary>
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }

    /// <summary>
    /// Last known location
    /// </summary>
    [JsonPropertyName("lastLocation")]
    public string? LastLocation { get; set; }
}

/// <summary>
/// Shipment timeline event.
/// </summary>
public class ShipmentEventContext
{
    /// <summary>
    /// Event status (human-readable)
    /// e.g., "Order Received", "Picked Up", "In Transit", "Delivered"
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// When the event occurred
    /// </summary>
    [JsonPropertyName("at")]
    public DateTime At { get; set; }

    /// <summary>
    /// Location where the event occurred (if available)
    /// </summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>
    /// Additional notes/description
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Standard order statuses for tracking.
/// </summary>
public static class OrderTrackingStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Confirmed = "Confirmed";
    public const string Shipped = "Shipped";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";
    public const string Returned = "Returned";
    public const string Refunded = "Refunded";
}

/// <summary>
/// Standard shipment statuses for tracking.
/// </summary>
public static class ShipmentTrackingStatuses
{
    public const string OrderReceived = "Order Received";
    public const string Processing = "Processing";
    public const string PickedUp = "Picked Up";
    public const string InTransit = "In Transit";
    public const string OutForDelivery = "Out for Delivery";
    public const string Delivered = "Delivered";
    public const string DeliveryFailed = "Delivery Failed";
    public const string ReturnedToSender = "Returned to Sender";
}

/// <summary>
/// Standard payment statuses.
/// </summary>
public static class PaymentTrackingStatuses
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Refunded = "Refunded";
    public const string PartiallyRefunded = "Partially Refunded";
}
