using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Records successful consumer processing to guarantee idempotency on retries/redelivery.
/// </summary>
public class ProcessedEvent : BaseEntity
{
    public Guid EventId { get; private set; }

    [Required]
    [MaxLength(200)]
    public string ConsumerName { get; private set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string EventType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }
    public Guid? TenantId { get; private set; }

    [Required]
    [MaxLength(200)]
    public string CorrelationId { get; private set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; private set; }

    private ProcessedEvent()
    {
    }

    public static ProcessedEvent Create(
        Guid eventId,
        string consumerName,
        string eventType,
        Guid entityId,
        Guid? tenantId,
        string correlationId)
    {
        return new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ConsumerName = consumerName,
            EventType = eventType,
            EntityId = entityId,
            TenantId = tenantId,
            CorrelationId = correlationId,
            ProcessedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }
}
