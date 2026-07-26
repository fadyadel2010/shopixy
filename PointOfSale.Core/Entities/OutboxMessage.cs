namespace PointOfSale.Core.Entities;

/// <summary>
/// Transactional outbox message for guaranteed event delivery.
/// Written in same transaction as domain changes.
/// Phase: Abandoned Cart Hardening
/// 
/// DESIGN:
/// - Events are written to this table in the same transaction as the domain change
/// - Background job reads unprocessed messages and publishes to RabbitMQ
/// - After successful publish, message is marked as processed
/// - Processed messages are cleaned up after retention period
/// 
/// GUARANTEES:
/// - At-least-once delivery (consumer must be idempotent)
/// - No event loss on crash
/// - Ordered delivery per aggregate
/// </summary>
public class OutboxMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Full type name of the event (e.g., "PointOfSale.Core.Events.AbandonedCart.CartActivityEvent").
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// JSON-serialized event payload.
    /// </summary>
    public string EventPayload { get; set; } = "{}";
    
    /// <summary>
    /// Tenant ID for tenant-scoped events.
    /// </summary>
    public Guid? TenantId { get; set; }
    
    /// <summary>
    /// Aggregate/entity ID for ordering and debugging.
    /// </summary>
    public Guid? AggregateId { get; set; }
    
    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Monotonic aggregate version for critical integration events.
    /// Used by the outbox publisher to prevent stale events from overtaking newer
    /// state across POS, Storefront, Marketplace, Affiliate, and Accounting.
    /// </summary>
    public int EventVersion { get; set; }
    
    /// <summary>
    /// When the message was successfully published.
    /// NULL = not yet processed.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }
    
    /// <summary>
    /// Number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    /// <summary>
    /// Last error message if publish failed.
    /// </summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// True when a critical authoritative event has exhausted automatic retries
    /// and must be repaired or explicitly replayed by an operator.
    /// </summary>
    public bool RequiresOperatorReconciliation { get; set; }

    /// <summary>
    /// When the message first entered operator reconciliation.
    /// </summary>
    public DateTime? OperatorReconciliationRequiredAtUtc { get; set; }

    /// <summary>
    /// Human-readable reason the message is blocking its aggregate stream.
    /// </summary>
    public string? OperatorReconciliationReason { get; set; }
    
    /// <summary>
    /// When the last error occurred.
    /// </summary>
    public DateTime? LastErrorAtUtc { get; set; }
    
    /// <summary>
    /// Maximum retries before giving up.
    /// </summary>
    public const int MaxRetries = 5;
    
    /// <summary>
    /// Returns true if this message has exceeded retry limit.
    /// </summary>
    public bool IsExhausted => RetryCount >= MaxRetries;
    
    /// <summary>
    /// Returns true if this message has been processed.
    /// </summary>
    public bool IsProcessed => ProcessedAtUtc.HasValue;
}
