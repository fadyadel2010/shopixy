using System.Text.Json;
using System.Text.Json.Serialization;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Storefront;

public sealed class CmsPageDto
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CmsPageContentType ContentType { get; set; } = CmsPageContentType.RichText;
    public JsonElement Content { get; set; }
    public int SchemaVersion { get; set; }
    public CmsPageSeoDto Seo { get; set; } = new();
    public string RequestedLanguage { get; set; } = "en";
    public string Language { get; set; } = "en";
    public bool FallbackApplied { get; set; }
    public List<string> AvailableLanguages { get; set; } = new();
    public string CanonicalPath { get; set; } = string.Empty;
    public Dictionary<string, string> AlternatePaths { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class CmsPageSeoDto
{
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? OpenGraphImageUrl { get; set; }
}

public sealed class CmsPageListItemDto
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string RequestedLanguage { get; set; } = "en";
    public string Language { get; set; } = "en";
    public bool FallbackApplied { get; set; }
    public List<string> AvailableLanguages { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public class NavigationDto
{
    public NavigationMenuDto Header { get; set; } = new();
    public NavigationMenuDto Footer { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; }
}

public class NavigationMenuDto
{
    public PointOfSale.Core.Models.HeaderAnnouncementConfig? Announcement { get; set; }
    public List<NavigationLinkDto> Links { get; set; } = new();
}

public class NavigationLinkDto
{
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
    public string Type { get; set; } = "internal";
    public string? Path { get; set; }
    public string? Slug { get; set; }
    public bool OpenInNewTab { get; set; }

    [JsonIgnore]
    public string AuthoringType { get; set; } = string.Empty;

    [JsonIgnore]
    public Guid? TargetId { get; set; }

    [JsonIgnore]
    public string? CollectionKey { get; set; }
}

public static class CmsApiErrorResponse
{
    public static ApiErrorResponse PageNotFound(string? traceId = null) =>
        new("CMS_PAGE_NOT_FOUND", "Page not found", traceId);

    public static ApiErrorResponse InvalidSlug(string? traceId = null) =>
        new("INVALID_SLUG", "Invalid page slug", traceId);
}
