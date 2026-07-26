using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public sealed class MarketplaceCmsPage
{
    public Guid Id { get; set; }
    public Guid PageGroupId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? TitleJson { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ContentJson { get; set; }
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public int Version { get; set; } = 1;
    public bool IsDraft { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaTitleJson { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaDescriptionJson { get; set; }
    public string? OpenGraphImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
