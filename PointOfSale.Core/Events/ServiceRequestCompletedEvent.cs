namespace PointOfSale.Core.Events;

public record ServiceRequestCompletedEvent
{
    public Guid ServiceRequestId { get; init; }
    public Guid TenantId { get; init; }
    public Guid ServiceId { get; init; }
    public DateTime CompletedAt { get; init; }
}
