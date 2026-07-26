namespace PointOfSale.Core.Entities;

/// <summary>
/// Tracks processed trust events for idempotency.
/// Prevents duplicate event processing in ProviderTrustConsumer.
/// </summary>
public class ProcessedTrustEvent : TenantEntity
{
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid ProviderProfileId { get; private set; }
    public DateTime ProcessedAt { get; private set; }

    private ProcessedTrustEvent() { }

    public static ProcessedTrustEvent Create(Guid tenantId, Guid eventId, string eventType, Guid providerProfileId)
    {
        return new ProcessedTrustEvent
        {
            Id = eventId, // Use EventId as primary key
            TenantId = tenantId,
            EventId = eventId,
            EventType = eventType,
            ProviderProfileId = providerProfileId,
            ProcessedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }
}
