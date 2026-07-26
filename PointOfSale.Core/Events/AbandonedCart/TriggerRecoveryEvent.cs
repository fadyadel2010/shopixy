using PointOfSale.Core.Events;

namespace PointOfSale.Core.Events.AbandonedCart;

/// <summary>
/// Event emitted when an abandoned cart is ready for recovery workflow.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// EMITTED BY:
/// - AbandonedCartScannerJob (background job)
/// 
/// CONSUMED BY:
/// - RecoveryOrchestrationConsumer (calls n8n webhook)
/// 
/// IDEMPOTENCY:
/// - Only emitted once per cart (RecoveryStatus != Triggered check)
/// - Consumer checks status before calling n8n
/// </summary>
public class TriggerRecoveryEvent : IntegrationEvent
{
    // NOTE: TenantId is inherited from IntegrationEvent base class
    
    /// <summary>
    /// Abandoned cart record ID.
    /// </summary>
    public Guid AbandonedCartId { get; set; }
    
    /// <summary>
    /// Cart identifier.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Storefront session identifier for guest recovery.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Authenticated customer ID when available.
    /// </summary>
    public Guid? CustomerId { get; set; }
    
    /// <summary>
    /// Customer phone number for WhatsApp.
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// Customer email (optional).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Verified contact channel selected for recovery.
    /// Expected values: "phone" or "email".
    /// </summary>
    public string? ContactChannel { get; set; }
    
    /// <summary>
    /// Snapshot of cart contents.
    /// </summary>
    public string CartSnapshotJson { get; set; } = "{}";
    
    /// <summary>
    /// Delay before first message (from tenant settings).
    /// </summary>
    public int FirstMessageDelayMinutes { get; set; }
    
    /// <summary>
    /// Delay before second message (from tenant settings).
    /// </summary>
    public int SecondMessageDelayMinutes { get; set; }
    
    /// <summary>
    /// Working hours start (from tenant settings).
    /// </summary>
    public TimeSpan WorkingHoursFrom { get; set; }
    
    /// <summary>
    /// Working hours end (from tenant settings).
    /// </summary>
    public TimeSpan WorkingHoursTo { get; set; }
    
    /// <summary>
    /// Store name for branding in messages.
    /// </summary>
    public string StoreName { get; set; } = string.Empty;
    
    /// <summary>
    /// Storefront base URL for constructing full recovery links.
    /// Example: https://mystore.example.com
    /// </summary>
    public string StorefrontBaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Signed recovery link for cart restoration.
    /// Format: https://storefront/cart/recover/{token}
    /// </summary>
    public string RecoveryLink { get; set; } = string.Empty;
}
