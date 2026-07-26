namespace PointOfSale.Core.Events;

public record ServiceActivationChangedEvent
{
    public Guid ServiceId { get; init; }
    public Guid TenantId { get; init; }
    public bool IsActive { get; init; }
    public DateTime ChangedAt { get; init; }
}
