using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Outbox message for durable payment cancellation operations.
/// 
/// PHASE: Payment Cancellation Hardening
/// 
/// Flow:
/// 1. Order cancellation + OutboxMessage inserted in same transaction
/// 2. Background worker reads unprocessed messages
/// 3. Worker calls IPaymentLifecycleService.HandlePaymentResultAsync
/// 4. On success, marks ProcessedAtUtc
/// 
/// This guarantees:
/// - No lost payment cancellations (survives crashes)
/// - Retries with exponential backoff
/// - Ordered processing (CreatedAtUtc)
/// </summary>
public class PaymentCancellationOutboxMessage : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    [Timestamp]
    public uint RowVersion { get; set; }
    
    /// <summary>
    /// The order being cancelled.
    /// </summary>
    public Guid OrderId { get; set; }
    
    /// <summary>
    /// Optional specific payment intent to cancel.
    /// NULL means cancel all payment intents for the order.
    /// </summary>
    public Guid? PaymentIntentId { get; set; }
    
    /// <summary>
    /// When true, skips payment intent resolution and operates only on order-level state.
    /// Set to true for order-only cancellations where no payment intents exist.
    /// </summary>
    public bool SkipPaymentIntentResolution { get; set; }
    
    /// <summary>
    /// Reason for cancellation (from admin request).
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Actor who initiated the cancellation.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Actor { get; set; } = string.Empty;
    
    /// <summary>
    /// When the message was successfully processed.
    /// NULL = pending.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>
    /// Processing lease used to collapse active worker overlap.
    /// Expiry lets a restarted worker resume after a crash.
    /// </summary>
    public DateTime? ClaimedUntilUtc { get; set; }
    
    /// <summary>
    /// Number of processing attempts.
    /// </summary>
    public int AttemptCount { get; set; }
    
    /// <summary>
    /// Last error message if processing failed.
    /// </summary>
    [MaxLength(2000)]
    public string? LastError { get; set; }
    
    /// <summary>
    /// Next retry time for failed messages.
    /// </summary>
    public DateTime? NextRetryAtUtc { get; set; }
    
    /// <summary>
    /// Whether this message is permanently failed (max retries exceeded).
    /// </summary>
    public bool IsFailed { get; set; }
    
    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    public bool TryClaim(int claimDurationSeconds)
    {
        var now = DateTime.UtcNow;
        if (ProcessedAtUtc != null ||
            IsFailed ||
            (ClaimedUntilUtc != null && ClaimedUntilUtc > now) ||
            (NextRetryAtUtc != null && NextRetryAtUtc > now))
        {
            return false;
        }

        ClaimedUntilUtc = now.AddSeconds(claimDurationSeconds);
        UpdatedAt = now;
        return true;
    }

    public void MarkProcessed()
    {
        var now = DateTime.UtcNow;
        ProcessedAtUtc = now;
        ClaimedUntilUtc = null;
        UpdatedAt = now;
    }

    public void ClearClaim()
    {
        ClaimedUntilUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
