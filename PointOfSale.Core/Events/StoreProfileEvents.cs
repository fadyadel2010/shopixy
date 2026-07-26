namespace PointOfSale.Core.Events;

/// <summary>
/// Event raised when a store profile (tenant metadata) is updated.
/// Used to keep public projections like affiliate marketplace in sync.
/// </summary>
public class StoreProfileUpdatedEvent : IntegrationEvent
{
    public string StoreName { get; set; } = string.Empty;
    public string StoreSlug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
}
