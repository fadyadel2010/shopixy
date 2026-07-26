namespace PointOfSale.Core.Entities;

/// <summary>
/// Global marketplace category shared across all tenants.
/// </summary>
public class CommerceHubCategory
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string Slug { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public int Level { get; set; }
    public string? IconEmoji { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public CommerceHubCategory? Parent { get; set; }
    public ICollection<CommerceHubCategory> Children { get; set; } = new List<CommerceHubCategory>();
    public ICollection<CommerceHubCategoryAttribute> Attributes { get; set; } = new List<CommerceHubCategoryAttribute>();
}
