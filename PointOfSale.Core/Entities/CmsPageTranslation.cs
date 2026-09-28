using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public sealed class CmsPageTranslation : ITenantOwned
{
    public Guid CmsPageVersionId { get; set; }
    public Guid TenantId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public int SchemaVersion { get; set; } = 1;
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? OpenGraphImageUrl { get; set; }

    public CmsPage PageVersion { get; set; } = null!;
}
