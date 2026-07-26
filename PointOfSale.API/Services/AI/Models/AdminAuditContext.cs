using System.Text.Json.Serialization;

namespace PointOfSale.API.Services.AI.Models;

// =============================================================================
// PHASE 4: Admin Audit Context
// 
// These models provide audit/event data to the AI for admin-facing explanations.
// The AI helps admins understand:
// - WHY something happened
// - WHO/WHAT triggered it
// - Whether it was automatic or manual
// 
// CRITICAL RULES:
// - AI can only EXPLAIN events
// - AI cannot SUGGEST actions (rollback, retry, edit)
// - AI must be neutral (no blame)
// - AI must avoid technical jargon
// =============================================================================

/// <summary>
/// Admin audit context for explaining system events.
/// PHASE 4: Audit & Explanation Intelligence (Admin).
/// </summary>
public class AdminAuditContext
{
    /// <summary>
    /// Who/what caused the event.
    /// Values: "system", "carrier", "admin", "payment", "customer"
    /// </summary>
    [JsonPropertyName("actor")]
    public string Actor { get; set; } = "system";

    /// <summary>
    /// The event that occurred.
    /// </summary>
    [JsonPropertyName("event")]
    public AuditEventContext Event { get; set; } = new();

    /// <summary>
    /// Order context (from Phase 3)
    /// </summary>
    [JsonPropertyName("order")]
    public OrderTrackingContext? Order { get; set; }

    /// <summary>
    /// Shipment context (from Phase 3)
    /// </summary>
    [JsonPropertyName("shipment")]
    public ShipmentTrackingContext? Shipment { get; set; }

    /// <summary>
    /// Source of the event.
    /// Values: "Webhook", "AdminAction", "BackgroundJob", "Integration", "API"
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "System";

    /// <summary>
    /// The admin's question to explain.
    /// </summary>
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Additional context for the explanation.
    /// </summary>
    [JsonPropertyName("additionalContext")]
    public Dictionary<string, string>? AdditionalContext { get; set; }
}

/// <summary>
/// Audit event context describing what happened.
/// </summary>
public class AuditEventContext
{
    /// <summary>
    /// Type of event.
    /// Examples: "ShipmentStatusChanged", "PaymentReceived", "OrderCancelled"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable event description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Previous state (for state change events)
    /// </summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>
    /// New state (for state change events)
    /// </summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    [JsonPropertyName("at")]
    public DateTime At { get; set; }

    /// <summary>
    /// Reason for the event (if available)
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Admin ID if this was an admin action
    /// </summary>
    [JsonPropertyName("adminId")]
    public Guid? AdminId { get; set; }

    /// <summary>
    /// Admin name if this was an admin action
    /// </summary>
    [JsonPropertyName("adminName")]
    public string? AdminName { get; set; }

    /// <summary>
    /// Related entity ID (order, shipment, etc.)
    /// </summary>
    [JsonPropertyName("entityId")]
    public Guid? EntityId { get; set; }

    /// <summary>
    /// Related entity type
    /// </summary>
    [JsonPropertyName("entityType")]
    public string? EntityType { get; set; }
}

/// <summary>
/// Well-known actor types for audit events.
/// </summary>
public static class AuditActors
{
    public const string System = "system";
    public const string Carrier = "carrier";
    public const string Admin = "admin";
    public const string Payment = "payment";
    public const string Customer = "customer";
    public const string Integration = "integration";
}

/// <summary>
/// Well-known event sources.
/// </summary>
public static class AuditSources
{
    public const string Webhook = "Webhook";
    public const string AdminAction = "AdminAction";
    public const string BackgroundJob = "BackgroundJob";
    public const string Integration = "Integration";
    public const string API = "API";
    public const string System = "System";
}

/// <summary>
/// Well-known audit event types.
/// </summary>
public static class AuditEventTypes
{
    // Order events
    public const string OrderCreated = "OrderCreated";
    public const string OrderConfirmed = "OrderConfirmed";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderStatusChanged = "OrderStatusChanged";

    // Shipment events
    public const string ShipmentCreated = "ShipmentCreated";
    public const string ShipmentStatusChanged = "ShipmentStatusChanged";
    public const string ShipmentDelivered = "ShipmentDelivered";
    public const string ShipmentFailed = "ShipmentFailed";

    // Payment events
    public const string PaymentReceived = "PaymentReceived";
    public const string PaymentFailed = "PaymentFailed";
    public const string RefundIssued = "RefundIssued";

    // Admin events
    public const string ManualOverride = "ManualOverride";
    public const string AdminEdit = "AdminEdit";
}
