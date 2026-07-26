namespace PointOfSale.Core.Events;

public record ServiceCreatedEvent
{
    public Guid ServiceId { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProfileId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Category { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    // Phase FE-3
    public decimal? StartingPrice { get; init; }
    public string? PricingType { get; init; }
    public string? Currency { get; init; }
    public string? ServiceImages { get; init; }
}
