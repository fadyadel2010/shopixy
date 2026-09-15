using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

/// <summary>
/// Outbox message for guaranteed delivery of financial postings.
/// 
/// PHASE 3.5: Transactional outbox pattern.
/// 
/// Flow:
/// 1. Business operation + OutboxMessage inserted in same transaction
/// 2. Background worker reads unprocessed messages
/// 3. Worker calls CommerceAccountingService
/// 4. On success, marks ProcessedAtUtc
/// 
/// This guarantees:
/// - No lost events (survives crashes)
/// - Exactly-once semantics (idempotency key)
/// - Version-aware processing for mutable aggregates
/// </summary>
public class AccountingOutboxMessage : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Type of business event.
    /// Examples: "PosSale", "PosPurchase", "OnlinePayment", "CodDelivered"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Accounting source type for recovery, reconciliation, and journal coverage checks.
    /// This intentionally mirrors JournalEntry.SourceType for events that become ledger postings.
    /// </summary>
    [MaxLength(50)]
    public string SourceType { get; set; } = string.Empty;
    
    /// <summary>
    /// ID of the business entity (Invoice, Order, Payment, etc.)
    /// </summary>
    public Guid BusinessEntityId { get; set; }
    
    /// <summary>
    /// Fully qualified type name of the entity for deserialization.
    /// </summary>
    [MaxLength(255)]
    public string? EntityTypeName { get; set; }
    
    /// <summary>
    /// Serialized entity data for processing.
    /// Captured at time of business transaction.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;
    
    /// <summary>
    /// Idempotency key for the posting.
    /// Computed at business transaction time.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string IdempotencyKey { get; set; } = string.Empty;
    
    /// <summary>
    /// When the business event occurred.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Authoritative aggregate mutation version captured when the outbox row was staged.
    /// NULL means the event is idempotent but not part of a mutable aggregate version stream.
    /// </summary>
    public int? AggregateVersion { get; set; }
    
    /// <summary>
    /// When the message was successfully processed.
    /// NULL = pending.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }
    
    /// <summary>
    /// Journal ID after successful posting.
    /// </summary>
    public Guid? JournalId { get; set; }
    
    /// <summary>
    /// Number of processing attempts.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Lease held by a background worker while it is posting this message.
    /// Expired leases are claimable again.
    /// </summary>
    public DateTime? ClaimedUntilUtc { get; set; }
    
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
    /// Correlation ID captured at enqueue time for consistent tracing.
    /// </summary>
    [MaxLength(100)]
    public string? CorrelationId { get; set; }
    
    /// <summary>
    /// Creates a new outbox message.
    /// </summary>
    public static AccountingOutboxMessage Create(
        Guid tenantId,
        string eventType,
        Guid businessEntityId,
        string entityTypeName,
        string payloadJson,
        string idempotencyKey,
        string? correlationId = null,
        int? aggregateVersion = null,
        string? sourceType = null)
    {
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        var normalizedSourceType = string.IsNullOrWhiteSpace(sourceType)
            ? ResolveDefaultSourceType(eventType, entityTypeName)
            : sourceType.Trim();
        
        return new AccountingOutboxMessage
        {
            TenantId = tenantId,
            EventType = eventType,
            SourceType = normalizedSourceType,
            BusinessEntityId = businessEntityId,
            EntityTypeName = entityTypeName,
            PayloadJson = payloadJson,
            IdempotencyKey = idempotencyKey,
            CorrelationId = normalizedCorrelationId,
            OccurredAtUtc = DateTime.UtcNow,
            AggregateVersion = aggregateVersion,
            AttemptCount = 0
        };
    }

    private static string ResolveDefaultSourceType(string eventType, string entityTypeName)
    {
        if (string.Equals(eventType, "CommerceHubLedgerPosting", StringComparison.Ordinal))
        {
            return "CommerceHubFinancialTransaction";
        }

        if (string.Equals(eventType, "PosSale", StringComparison.Ordinal))
        {
            return "SalesInvoice";
        }

        if (string.Equals(eventType, "PosPurchase", StringComparison.Ordinal))
        {
            return "PurchaseInvoice";
        }

        if (string.Equals(eventType, "PosRefund", StringComparison.Ordinal))
        {
            return "Return";
        }

        // T-08: COGS outbox source types mirror their journal source types so
        // journal-coverage reconciliation can match event to ledger posting.
        if (string.Equals(eventType, "EcommerceCogs", StringComparison.Ordinal))
        {
            return "Order_COGS";
        }

        if (string.Equals(eventType, "EcommerceCogsReversal", StringComparison.Ordinal))
        {
            return "Order_COGS_Reversal";
        }

        if (string.Equals(eventType, "CommerceHubCogs", StringComparison.Ordinal))
        {
            return "CommerceHub_COGS";
        }

        if (string.Equals(eventType, "CommerceHubCogsReversal", StringComparison.Ordinal))
        {
            return "CommerceHub_COGS_Reversal";
        }

        // Fallback logic with truncation to respect [MaxLength(50)] on SourceType
        string fallback;
        if (string.IsNullOrWhiteSpace(entityTypeName))
        {
            fallback = eventType;
        }
        else if (entityTypeName.Length <= 50)
        {
            fallback = entityTypeName;
        }
        else
        {
            fallback = eventType;
        }

        // Ensure the result never exceeds 50 characters
        return fallback.Length <= 50 ? fallback : fallback.Substring(0, 50);
    }
    
    /// <summary>
    /// Marks the message as successfully processed.
    /// </summary>
    public void MarkProcessed(Guid journalId)
    {
        ProcessedAtUtc = DateTime.UtcNow;
        JournalId = journalId;
        LastError = null;
        ClaimedUntilUtc = null;
        NextRetryAtUtc = null;
        IsFailed = false;
    }

    /// <summary>
    /// Attempts to lease this message before any external accounting side effect.
    /// </summary>
    public bool TryClaim(int claimDurationSeconds = 60)
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
        return true;
    }
    
    /// <summary>
    /// Records a failed attempt with exponential backoff.
    /// </summary>
    public void RecordFailure(string error, int maxRetries = 5)
    {
        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        ClaimedUntilUtc = null;
        
        if (AttemptCount >= maxRetries)
        {
            IsFailed = true;
            NextRetryAtUtc = null;
        }
        else
        {
            // Exponential backoff: 1min, 2min, 4min, 8min, 16min
            var delayMinutes = Math.Pow(2, AttemptCount - 1);
            NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
        }
    }

    /// <summary>
    /// Records a failed attempt for financial truth events that must not disappear
    /// into a terminal dead-letter state while business state remains committed.
    /// </summary>
    public void RecordNonTerminalFinancialFailure(string error)
    {
        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        ClaimedUntilUtc = null;
        IsFailed = false;

        var delayMinutes = Math.Min(60d, Math.Pow(2, Math.Min(AttemptCount - 1, 6)));
        NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
    }

    public void ResetForRetry()
    {
        if (ProcessedAtUtc != null)
        {
            return;
        }

        IsFailed = false;
        AttemptCount = 0;
        ClaimedUntilUtc = null;
        LastError = null;
        NextRetryAtUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
