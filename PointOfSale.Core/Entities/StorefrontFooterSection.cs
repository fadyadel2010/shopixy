namespace PointOfSale.Core.Entities;

public sealed class StorefrontFooterSection
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TitleAr { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<StorefrontFooterLink> Links { get; set; } = new();
}
