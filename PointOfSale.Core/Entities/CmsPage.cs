using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public sealed class CmsPage : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid PageGroupId { get; set; }
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public int Version { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public bool IsDraft { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? OpenGraphImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<CmsPageTranslation> Translations { get; set; } = new List<CmsPageTranslation>();
}
