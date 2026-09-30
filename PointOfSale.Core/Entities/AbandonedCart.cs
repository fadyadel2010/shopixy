namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a cart that may be abandoned.
/// Tracks cart lifecycle from active → abandoned → pending → triggered → recovered/expired.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// LIFECYCLE:
/// 1. Active: Customer is shopping, cart updated recently
/// 2. Abandoned: Cart idle beyond threshold (FirstMessageDelayMinutes)
/// 3. Pending: native WhatsApp dispatch is in progress (HARDENING)
/// 4. Triggered: the first audited recovery message was sent
/// 5. Recovered: Customer completed order (success!)
/// 6. Expired: Recovery window closed without conversion
/// 7. Failed: Max retries exhausted (HARDENING)
/// 
/// IDEMPOTENCY:
/// - CartId is unique per tenant
/// - Upsert on cart activity
/// - Atomic state transitions via SQL UPDATE ... WHERE status = X
/// </summary>
public class AbandonedCart : TenantEntity
{
    /// <summary>
    /// Cart identifier from storefront session.
    /// Unique per tenant.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Storefront session identifier for guest or mixed checkout flows.
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// Customer ID if authenticated, null for guest carts.
    /// </summary>
    public Guid? CustomerId { get; set; }
    
    /// <summary>
    /// Customer phone number (required for WhatsApp recovery).
    /// E.164 format (e.g., +201234567890).
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Customer email (optional, for fallback communication).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Preferred or latest contact channel observed for this cart.
    /// Expected values: "phone", "email", or "phone,email".
    /// </summary>
    public string? ContactChannel { get; set; }

    /// <summary>
    /// Whether the stored phone number is verified and eligible for recovery messaging.
    /// </summary>
    public bool PhoneVerified { get; set; }

    /// <summary>
    /// Whether the stored email address is verified and eligible for recovery messaging.
    /// </summary>
    public bool EmailVerified { get; set; }
    
    /// <summary>
    /// Snapshot of cart contents at last activity.
    /// JSON structure: { items: [...], total: 0, currency: "USD" }
    /// Used for recovery link and message personalization.
    /// </summary>
    public string CartSnapshotJson { get; set; } = "{}";
    
    /// <summary>
    /// Last time customer interacted with cart.
    /// Updated on: item add/remove, quantity change, checkout visit.
    /// Used to calculate abandonment threshold.
    /// </summary>
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When cart was marked as abandoned.
    /// Set when: LastActivityAtUtc + FirstMessageDelayMinutes < now
    /// Null while cart is Active.
    /// </summary>
    public DateTime? AbandonedAtUtc { get; set; }
    
    /// <summary>
    /// Current recovery status.
    /// Controls workflow progression and prevents duplicate triggers.
    /// </summary>
    public Enums.AbandonedCartRecoveryStatus RecoveryStatus { get; set; } = Enums.AbandonedCartRecoveryStatus.Active;
    
    /// <summary>
    /// HARDENING: When cart entered Pending state (native dispatch started).
    /// Used to detect stuck Pending carts (timeout recovery).
    /// </summary>
    public DateTime? PendingAtUtc { get; set; }
    
    /// <summary>
    /// When recovery workflow was triggered (first audited message sent).
    /// Used for idempotency and analytics.
    /// Null until RecoveryStatus = Triggered.
    /// </summary>
    public DateTime? TriggeredAtUtc { get; set; }
    
    /// <summary>
    /// When cart was recovered (order completed).
    /// Used for success metrics and ROI calculation.
    /// Null until RecoveryStatus = Recovered.
    /// </summary>
    public DateTime? RecoveredAtUtc { get; set; }
    
    /// <summary>
    /// Order ID if cart was recovered.
    /// Links abandoned cart to successful conversion.
    /// </summary>
    public Guid? RecoveredOrderId { get; set; }
    
    /// <summary>
    /// HARDENING: When cart was marked as expired.
    /// Set when recovery window closes without conversion.
    /// </summary>
    public DateTime? ExpiredAtUtc { get; set; }
    
    /// <summary>
    /// HARDENING: Number of native dispatch retry attempts.
    /// Incremented on each failure, reset on success.
    /// Max retries defined by MaxRetryCount.
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    /// <summary>
    /// HARDENING: Last error message from native dispatch failure.
    /// Used for debugging and alerting.
    /// </summary>
    public string? LastErrorMessage { get; set; }
    
    /// <summary>
    /// When this record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When this record was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Maximum retry attempts before marking as Failed.
    /// </summary>
    public const int MaxRetryCount = 3;
    
    // Navigation
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual OnlineCustomer? Customer { get; set; }
    public virtual ICollection<AbandonedCartRecoveryLog> RecoveryLogs { get; set; } = new List<AbandonedCartRecoveryLog>();
}
