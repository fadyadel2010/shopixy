using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable post-commit handoff for marketplace read-model projections.
/// The consumer stages this row inside the authoritative SQL transaction,
/// then the projection processor executes Mongo/secondary writes after commit.
/// </summary>
public class MarketplaceProjectionOutboxMessage : BaseEntity, ITenantOwned
{
    public Guid EventId { get; private set; }

    [Required]
    [MaxLength(200)]
    public string ConsumerName { get; private set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string EventType { get; private set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string ClrType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }
    public Guid TenantId { get; set; }

    [Required]
    [MaxLength(200)]
    public string CorrelationId { get; private set; } = string.Empty;

    public int EventVersion { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    [Required]
    public string PayloadJson { get; private set; } = string.Empty;

    public DateTime? ProcessedAtUtc { get; private set; }
    public DateTime? ClaimedUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }

    [Timestamp]
    public uint RowVersion { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; private set; }

    public DateTime? NextRetryAtUtc { get; private set; }
    public bool IsFailed { get; private set; }

    private MarketplaceProjectionOutboxMessage()
    {
    }

    public static MarketplaceProjectionOutboxMessage Create<TEvent>(
        TEvent @event,
        string consumerName)
        where TEvent : class, IMarketplaceDomainEvent
    {
        var eventType = typeof(TEvent).FullName ?? typeof(TEvent).Name;
        var clrType = typeof(TEvent).AssemblyQualifiedName ?? eventType;

        return new MarketplaceProjectionOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = @event.EventId,
            ConsumerName = consumerName,
            EventType = eventType,
            ClrType = clrType,
            EntityId = @event.EntityId,
            TenantId = @event.TenantId ?? throw new InvalidOperationException(
                $"Marketplace projection outbox requires TenantId for {typeof(TEvent).Name}."),
            CorrelationId = @event.CorrelationId,
            EventVersion = @event.EventVersion,
            OccurredAtUtc = @event.OccurredAtUtc,
            PayloadJson = JsonSerializer.Serialize(@event, typeof(TEvent)),
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

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
        UpdatedAt = now;
        return true;
    }

    public void MarkProcessed()
    {
        ProcessedAtUtc = DateTime.UtcNow;
        ClaimedUntilUtc = null;
        LastError = null;
        NextRetryAtUtc = null;
        IsFailed = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordFailure(string error, int maxRetries = 5)
    {
        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        ClaimedUntilUtc = null;
        UpdatedAt = DateTime.UtcNow;

        if (AttemptCount >= maxRetries)
        {
            IsFailed = true;
            NextRetryAtUtc = null;
        }
        else
        {
            var delayMinutes = Math.Min(60d, Math.Pow(2, Math.Min(AttemptCount - 1, 6)));
            NextRetryAtUtc = DateTime.UtcNow.AddMinutes(delayMinutes);
        }
    }
}
