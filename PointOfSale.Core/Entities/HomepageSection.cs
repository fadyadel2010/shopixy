namespace PointOfSale.Core.Entities;

public sealed class HomepageSection
{
    public Guid Id { get; set; }
    public Guid SectionGroupId { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Data { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public bool IsDraft { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? DraftGroupId { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
