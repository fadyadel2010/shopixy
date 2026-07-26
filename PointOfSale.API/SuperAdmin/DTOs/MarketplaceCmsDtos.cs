using System.Text.Json;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class MarketplaceCmsPageRequest
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public LocalizedField? TitleLocalized { get; set; }
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public JsonElement Content { get; set; }
    public JsonElement? LocalizedContent { get; set; }
    public MarketplaceCmsPageSeoRequest Seo { get; set; } = new();
    public int? ExpectedVersion { get; set; }

    public string? Validate()
    {
        Seo ??= new MarketplaceCmsPageSeoRequest();

        if (string.IsNullOrWhiteSpace(Slug))
        {
            return "Slug is required";
        }

        if (Slug.Length > 160)
        {
            return "Slug must be less than 160 characters";
        }

        var englishTitle = TitleLocalized?.En ?? Title;
        if (string.IsNullOrWhiteSpace(englishTitle))
        {
            return "English title is required";
        }

        if (englishTitle.Trim().Length > 200)
        {
            return "English title must be less than 200 characters";
        }

        if (Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "Content is required";
        }

        return null;
    }
}

public sealed class MarketplaceCmsPageSeoRequest
{
    public string? MetaTitle { get; set; }
    public LocalizedField? MetaTitleLocalized { get; set; }
    public string? MetaDescription { get; set; }
    public LocalizedField? MetaDescriptionLocalized { get; set; }
    public string? OpenGraphImageUrl { get; set; }
}

public sealed class MarketplaceCmsPageListItemResponse
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public bool HasDraft { get; set; }
    public bool HasPublished { get; set; }
    public bool HasPendingChanges { get; set; }
    public string Status { get; set; } = "Draft";
    public int? DraftVersion { get; set; }
    public int? PublishedVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class MarketplaceCmsPageResponse
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public bool HasPendingChanges { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public MarketplaceCmsPageVersionResponse? Draft { get; set; }
    public MarketplaceCmsPageVersionResponse? Published { get; set; }
}

public sealed class MarketplaceCmsPageVersionResponse
{
    public Guid RecordId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public JsonElement Content { get; set; }
    public JsonElement? LocalizedContent { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public MarketplaceCmsPageSeoRequest Seo { get; set; } = new();
}

public sealed class MarketplaceCmsPreviewAccessResponse
{
    public string Slug { get; set; } = string.Empty;
    public string PreviewToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
