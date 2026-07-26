namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable audit archive for failed marketplace outbox rows that must remain
/// recoverable while the live outbox row continues to block readiness gates.
/// </summary>
public class MarketplaceEventOutboxDeadLetterArchive : BaseEntity
{
    private MarketplaceEventOutboxDeadLetterArchive()
    {
    }

    public Guid OriginalOutboxMessageId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string ClrType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public Guid AggregateRootId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime ArchivedAtUtc { get; private set; }
    public string ArchiveReason { get; private set; } = string.Empty;

    public static MarketplaceEventOutboxDeadLetterArchive FromOutboxMessage(
        MarketplaceEventOutboxMessage message,
        string archiveReason)
    {
        ArgumentNullException.ThrowIfNull(message);

        var reason = string.IsNullOrWhiteSpace(archiveReason)
            ? "Failed marketplace outbox retention archive"
            : archiveReason.Trim();

        var now = DateTime.UtcNow;

        return new MarketplaceEventOutboxDeadLetterArchive
        {
            Id = Guid.NewGuid(),
            OriginalOutboxMessageId = message.Id,
            EventId = message.EventId,
            EventType = message.EventType,
            ClrType = message.ClrType,
            PayloadJson = message.PayloadJson,
            EntityId = message.EntityId,
            AggregateRootId = message.AggregateRootId,
            TenantId = message.TenantId,
            CorrelationId = message.CorrelationId,
            Version = message.Version,
            OccurredAtUtc = message.OccurredAtUtc,
            FailedAtUtc = message.UpdatedAt ?? message.CreatedAt,
            AttemptCount = message.AttemptCount,
            LastError = message.LastError,
            ArchivedAtUtc = now,
            ArchiveReason = reason.Length > 200 ? reason[..200] : reason,
            CreatedAt = now,
            IsDeleted = false
        };
    }
}
