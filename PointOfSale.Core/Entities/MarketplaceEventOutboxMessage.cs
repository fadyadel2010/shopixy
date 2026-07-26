using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Events.Marketplace;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Transactional outbox record for marketplace backbone events.
/// </summary>
public class MarketplaceEventOutboxMessage : BaseEntity
{
    public Guid EventId { get; private set; }

    [Required]
    [MaxLength(200)]
    public string EventType { get; private set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string ClrType { get; private set; } = string.Empty;

    [Required]
    public string PayloadJson { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }
    public Guid AggregateRootId { get; private set; }
    public Guid? TenantId { get; private set; }

    [Required]
    [MaxLength(200)]
    public string CorrelationId { get; private set; } = string.Empty;

    public int Version { get; private set; }
    
    [Timestamp]
    public uint RowVersion { get; set; }
    
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? ClaimedUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }

    [MaxLength(2000)]
    public string? LastError { get; private set; }

    public DateTime? NextRetryAtUtc { get; private set; }
    public bool IsFailed { get; private set; }

    private MarketplaceEventOutboxMessage()
    {
    }

    public static MarketplaceEventOutboxMessage Create<TEvent>(TEvent @event, string payloadJson)
        where TEvent : class, IMarketplaceDomainEvent
    {
        ValidateEventMetadata(@event, payloadJson);

        return new MarketplaceEventOutboxMessage
        {
            Id = @event.EventId,
            EventId = @event.EventId,
            EventType = typeof(TEvent).Name,
            ClrType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name,
            PayloadJson = payloadJson,
            EntityId = @event.EntityId,
            AggregateRootId = @event.AggregateRootId ?? @event.EntityId,
            TenantId = @event.TenantId,
            CorrelationId = @event.CorrelationId,
            Version = @event.Version,
            OccurredAtUtc = @event.OccurredAtUtc,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    private static void ValidateEventMetadata<TEvent>(TEvent @event, string payloadJson)
        where TEvent : class, IMarketplaceDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("Payload JSON cannot be null or whitespace.", nameof(payloadJson));
        }

        if (@event.EventId == Guid.Empty)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a non-empty event id.");
        }

        if (@event.EntityId == Guid.Empty)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a non-empty entity id.");
        }

        if (@event.AggregateRootId == Guid.Empty)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} cannot use an empty aggregate root id.");
        }

        if (!@event.TenantId.HasValue)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a tenant id.");
        }

        if (@event.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} cannot use an empty tenant id.");
        }

        if (string.IsNullOrWhiteSpace(@event.CorrelationId))
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a non-empty correlation id.");
        }

        EnsureUtcTimestamp(@event.OccurredAtUtc, typeof(TEvent).Name);

        if (@event.Version <= 0)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a positive version.");
        }

        if (@event.EventVersion <= 0)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a positive event version.");
        }

        if (@event.Version != @event.EventVersion)
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} version and event version must match before entering the marketplace outbox.");
        }
    }

    private static void EnsureUtcTimestamp(DateTime occurredAtUtc, string eventType)
    {
        if (occurredAtUtc == default)
        {
            throw new InvalidOperationException($"{eventType} requires a non-default occurrence timestamp.");
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException($"{eventType} occurrence timestamp must be UTC.");
        }
    }

    public void MarkPublished()
    {
        PublishedAtUtc = DateTime.UtcNow;
        UpdatedAt = PublishedAtUtc;
        LastError = null;
        NextRetryAtUtc = null;
        IsFailed = false;
        ClaimedUntilUtc = null;
    }

    public bool TryClaim(int claimDurationSeconds = 30)
    {
        var now = DateTime.UtcNow;
        if (PublishedAtUtc != null ||
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

    public void RecordFailure(string error, int maxRetries = 5)
    {
        AttemptCount++;
        var normalizedError = string.IsNullOrEmpty(error) ? "Unknown error" : error;
        LastError = normalizedError.Length > 2000 ? normalizedError[..2000] : normalizedError;
        UpdatedAt = DateTime.UtcNow;
        ClaimedUntilUtc = null;

        if (AttemptCount >= maxRetries)
        {
            IsFailed = true;
            NextRetryAtUtc = null;
            return;
        }

        var delayMinutes = Math.Pow(2, AttemptCount - 1);
        NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
    }

    public void RecordNonTerminalProjectionFailure(string error)
    {
        AttemptCount++;
        var normalizedError = string.IsNullOrEmpty(error) ? "Unknown error" : error;
        LastError = normalizedError.Length > 2000 ? normalizedError[..2000] : normalizedError;
        UpdatedAt = DateTime.UtcNow;
        ClaimedUntilUtc = null;
        IsFailed = false;

        var delayMinutes = Math.Min(60d, Math.Pow(2, Math.Min(AttemptCount - 1, 6)));
        NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
    }

    public void ResetForRetry()
    {
        if (PublishedAtUtc != null)
        {
            return;
        }

        IsFailed = false;
        AttemptCount = 0;
        LastError = null;
        NextRetryAtUtc = null;
        ClaimedUntilUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RefreshForAuthoritativeRetry(
        string eventType,
        string clrType,
        string payloadJson,
        string? resetReason = null)
    {
        if (PublishedAtUtc != null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type cannot be null or whitespace.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(clrType))
        {
            throw new ArgumentException("CLR type cannot be null or whitespace.", nameof(clrType));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("Payload JSON cannot be null or whitespace.", nameof(payloadJson));
        }

        EventType = eventType.Trim();
        ClrType = clrType.Trim();
        PayloadJson = payloadJson;
        IsFailed = false;
        AttemptCount = 0;
        LastError = string.IsNullOrWhiteSpace(resetReason)
            ? null
            : resetReason.Length > 2000
                ? resetReason[..2000]
                : resetReason;
        NextRetryAtUtc = null;
        ClaimedUntilUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompensated(string error)
    {
        var now = DateTime.UtcNow;
        var normalizedError = string.IsNullOrEmpty(error) ? "Unknown error" : error;
        PublishedAtUtc = now;
        UpdatedAt = now;
        ClaimedUntilUtc = null;
        NextRetryAtUtc = null;
        IsFailed = true;
        LastError = normalizedError.Length > 2000 ? normalizedError[..2000] : normalizedError;
    }
}
