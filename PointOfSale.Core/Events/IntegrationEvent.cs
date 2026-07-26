namespace PointOfSale.Core.Events;

/// <summary>
/// Marker interface for integration events containing sensitive data that should bypass the outbox.
/// Events implementing this interface can be published via PublishSensitiveAsync to prevent
/// sensitive data from being persisted in outbox tables.
/// 
/// SECURITY: Only use for events containing PII, payment proof, or other sensitive data
/// that must not be stored in the database. This bypasses transactional outbox guarantees.
/// </summary>
public interface ISensitiveIntegrationEvent;

/// <summary>
/// Marker interface for integration events that are operational side effects,
/// notifications, analytics, or recovery triggers rather than business-state truth.
/// Use this when an event name contains words such as Order, Payment, Customer,
/// or Stock but must not be treated as an authoritative cross-system mutation.
/// </summary>
public interface INonAuthoritativeIntegrationEvent;

/// <summary>
/// Marker interface for authoritative integration events that represent critical business state changes.
/// Events implementing this interface MUST be published within a database transaction boundary
/// to ensure atomicity between the business state mutation and the outbox entry.
/// 
/// CRITICAL: Authoritative events are the source of truth for downstream systems.
/// They must be published BEFORE the source transaction commits to guarantee exactly-once delivery.
/// 
/// Examples: OrderCreated, PaymentCaptured, InventoryReserved, RefundProcessed
/// Non-examples: NotificationSent, AnalyticsTracked, CacheInvalidated
/// </summary>
public interface IAuthoritativeIntegrationEvent;

/// <summary>
/// Implement on authoritative events whose business aggregate is not safely inferred
/// from generic property-name ordering.
/// CRITICAL: This interface extends IAuthoritativeIntegrationEvent to enforce that only
/// authoritative events can provide explicit aggregate scoping.
/// </summary>
public interface IAggregateScopedIntegrationEvent : IAuthoritativeIntegrationEvent
{
    Guid GetAuthoritativeAggregateId();
}

/// <summary>
/// Base class for all integration events used in event-driven communication.
/// All events are now tenant-aware to support multi-tenant SaaS architecture.
/// </summary>
public abstract class IntegrationEvent
{
    private int _version = 1;

    /// <summary>
    /// Unique identifier for this event instance
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Backward compatibility for Id
    /// </summary>
    public Guid Id => EventId;
    
    /// <summary>
    /// UTC timestamp when the event occurred
    /// </summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Backward compatibility for CreatedAt
    /// </summary>
    public DateTime CreatedAt => OccurredAtUtc;
    
    /// <summary>
    /// Name of the event type
    /// </summary>
    public string EventType => GetType().Name;
    
    /// <summary>
    /// Correlation ID for tracing related events
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Idempotency key to uniquely identify the business action
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Schema version of the event
    /// </summary>
    public int Version
    {
        get => _version;
        set => _version = value;
    }

    /// <summary>
    /// Backward- and forward-compatible alias for Version.
    /// New consumers should prefer EventVersion while older payloads can still use Version.
    /// </summary>
    public int EventVersion
    {
        get => _version;
        set => _version = value;
    }

    /// <summary>
    /// User ID who triggered the event
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Tenant ID that owns this event's data.
    /// CRITICAL: Events must NEVER be tenant-agnostic going forward.
    /// This enables safe consumption by MongoDB, Redis, Elasticsearch, and other downstream systems.
    /// </summary>
    public Guid? TenantId { get; set; }
}

