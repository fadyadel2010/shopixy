using System.Text.Json;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.DTOs.Marketplace;

public sealed class MarketplaceCmsPageDto
{
    public string Slug { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public JsonElement Content { get; set; }
    public JsonElement? LocalizedContent { get; set; }
    public MarketplaceCmsSeoDto Seo { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class MarketplaceCmsSeoDto
{
    public string? MetaTitle { get; set; }
    public LocalizedField MetaTitleLocalized { get; set; } = new();
    public string? MetaDescription { get; set; }
    public LocalizedField MetaDescriptionLocalized { get; set; } = new();
    public string? OpenGraphImageUrl { get; set; }
}

public static class MarketplaceCmsApiErrorResponse
{
    public static ApiErrorResponse PageNotFound(string? traceId = null) =>
        new("MARKETPLACE_CMS_PAGE_NOT_FOUND", "Page not found", traceId);

    public static ApiErrorResponse InvalidSlug(string? traceId = null) =>
        new("INVALID_MARKETPLACE_CMS_SLUG", "Invalid page slug", traceId);
}
