using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public sealed class MarketplaceFooterLink
{
    public Guid Id { get; set; }
    public Guid SectionId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? LabelJson { get; set; }
    public FooterLinkType LinkType { get; set; } = FooterLinkType.Internal;
    public string? Url { get; set; }
    public string? CmsPageSlug { get; set; }
    public bool OpenInNewTab { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public MarketplaceFooterSection? Section { get; set; }
}
