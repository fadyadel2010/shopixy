using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// PaymentWebhookLog entity - stores raw webhook payloads for audit.
/// 
/// Phase 4.1: Payments API
/// 
/// Purpose:
/// - Audit trail for all webhook events
/// - Idempotency tracking via ProviderEventId
/// - Debugging failed webhook processing
/// </summary>
public class PaymentWebhookLog : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant isolation
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Payment provider (e.g., stripe, paypal, tap)
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// Provider's unique event ID (for idempotency)
    /// </summary>
    public string ProviderEventId { get; set; } = string.Empty;
    
    /// <summary>
    /// Event type from provider (e.g., payment_intent.succeeded)
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// Related PaymentIntent ID if resolved
    /// </summary>
    public Guid? PaymentIntentId { get; set; }
    public PaymentIntent? PaymentIntent { get; set; }
    
    /// <summary>
    /// Related Order ID if resolved
    /// </summary>
    public Guid? OrderId { get; set; }
    
    /// <summary>
    /// Raw webhook payload (JSON)
    /// </summary>
    public string RawPayload { get; set; } = string.Empty;
    
    /// <summary>
    /// Webhook signature for verification audit
    /// </summary>
    public string? Signature { get; set; }
    
    /// <summary>
    /// Whether signature was verified
    /// </summary>
    public bool SignatureVerified { get; set; }
    
    /// <summary>
    /// Processing status
    /// </summary>
    public WebhookProcessingStatus ProcessingStatus { get; set; } = WebhookProcessingStatus.Pending;
    
    /// <summary>
    /// When processing started
    /// </summary>
    public DateTime? ProcessedAt { get; set; }
    
    /// <summary>
    /// Processing error if failed
    /// </summary>
    public string? ProcessingError { get; set; }
    
    /// <summary>
    /// Processing duration in ms
    /// </summary>
    public long? ProcessingDurationMs { get; set; }
    
    /// <summary>
    /// IP address of webhook caller
    /// </summary>
    public string? SourceIp { get; set; }
    
    /// <summary>
    /// User agent of webhook caller
    /// </summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Webhook processing status
/// </summary>
public enum WebhookProcessingStatus
{
    /// <summary>Webhook received, not yet processed</summary>
    Pending = 0,
    
    /// <summary>Processing in progress</summary>
    Processing = 1,
    
    /// <summary>Successfully processed</summary>
    Completed = 2,
    
    /// <summary>Processing failed</summary>
    Failed = 3,
    
    /// <summary>Skipped (duplicate event)</summary>
    Skipped = 4
}
