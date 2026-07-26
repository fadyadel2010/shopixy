namespace PointOfSale.Core.Entities;

public sealed class MarketplaceFooterSection
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleJson { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<MarketplaceFooterLink> Links { get; set; } = new();
}
