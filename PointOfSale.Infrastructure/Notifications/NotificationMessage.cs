namespace PointOfSale.Infrastructure.Notifications;

/// <summary>
/// Notification message model for all notification channels.
/// Phase 5.3: Tracking & Notifications
/// 
/// This is a channel-agnostic message structure that can be adapted
/// to Email, SMS, WhatsApp, Webhooks, etc.
/// 
/// IMPORTANT:
/// - Always provide TenantId for multi-tenant isolation
/// - Payload is extensible for channel-specific data
/// - OccurredAtUtc is the event timestamp, not send timestamp
/// </summary>
public class NotificationMessage
{
    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Associated order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Associated shipment ID.
    /// </summary>
    public Guid ShipmentId { get; set; }

    /// <summary>
    /// Notification type identifier.
    /// Examples: ShipmentCreated, ShipmentShipped, ShipmentDelivered, ShipmentCancelled
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Target channel for this notification.
    /// Examples: Email, SMS, WhatsApp, Webhook, Log
    /// </summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Recipient identifier (email address, phone number, webhook URL, etc.).
    /// </summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>
    /// Channel-specific payload data.
    /// </summary>
    public Dictionary<string, string> Payload { get; set; } = new();

    /// <summary>
    /// When the triggering event occurred.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Unique event ID for idempotency tracking.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Order number for display purposes.
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Shipment number for display purposes.
    /// </summary>
    public string ShipmentNumber { get; set; } = string.Empty;
}

/// <summary>
/// Static helper class for notification types.
/// </summary>
public static class NotificationTypes
{
    public const string ShipmentCreated = "ShipmentCreated";
    public const string ShipmentPacked = "ShipmentPacked";
    public const string ShipmentShipped = "ShipmentShipped";
    public const string ShipmentDelivered = "ShipmentDelivered";
    public const string ShipmentCancelled = "ShipmentCancelled";
}

/// <summary>
/// Static helper class for notification channels.
/// </summary>
public static class NotificationChannels
{
    public const string Email = "Email";
    public const string Sms = "SMS";
    public const string WhatsApp = "WhatsApp";
    public const string Webhook = "Webhook";
    public const string Log = "Log";
}
