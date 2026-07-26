using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable audit and idempotency record for shipping carrier webhooks.
/// Stores the normalized event identifier and payload hash so duplicate or
/// tampered webhook deliveries can be rejected before shipment state changes.
/// </summary>
public class ShippingWebhookLog : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string CarrierCode { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Tracking number extracted from the webhook payload.
    /// Nullable because validation webhooks, malformed deliveries, or non-shipment events
    /// may legitimately arrive without a tracking number.
    /// </summary>
    public string? TrackingNumber { get; set; }

    public Guid? ShipmentId { get; set; }

    public Guid? OrderId { get; set; }

    /// <summary>
    /// Redacted payload summary for audit purposes.
    /// Full payloads containing PII should NOT be stored here.
    /// Format: First 500 chars of payload with PII fields redacted.
    /// </summary>
    public string PayloadSummary { get; set; } = string.Empty;

    public string? Signature { get; set; }

    public bool SignatureVerified { get; set; }

    public string ValidationMethod { get; set; } = string.Empty;

    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Pending;

    public DateTime? ProcessedAt { get; set; }

    public string? ProcessingError { get; set; }

    public long? ProcessingDurationMs { get; set; }

    /// <summary>
    /// Redacted source IP (network prefix only, e.g., "203.0.113.0/24").
    /// Full IP addresses are PII and should not be stored indefinitely.
    /// </summary>
    public string? SourceIpPrefix { get; set; }

    /// <summary>
    /// Truncated user agent (first 100 chars, version info removed).
    /// Full user agents can contain identifying information.
    /// </summary>
    public string? UserAgentSummary { get; set; }
}
