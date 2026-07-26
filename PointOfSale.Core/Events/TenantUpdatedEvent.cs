namespace PointOfSale.Core.Events;

/// <summary>
/// Event published when tenant-facing store metadata changes and downstream read models
/// must refresh denormalized store information.
/// </summary>
public class TenantUpdatedEvent : IntegrationEvent
{
    public string StoreName { get; set; } = string.Empty;
}
