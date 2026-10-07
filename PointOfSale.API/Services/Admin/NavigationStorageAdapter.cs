using System.Text.Json;
using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// The single mechanical storage adapter between the canonical Navigation authoring DTO and
/// the frozen StoreConfig navigation JSON/runtime representation.
/// </summary>
public static class NavigationStorageAdapter
{
    public static Dictionary<string, object> ToStorage(AdminNavigationLinkRequest link, int index)
    {
        var authoringType = NormalizeAuthoringType(link.Type);
        var runtimeType = authoringType is "product" or "collection" or "custom"
            ? "internal"
            : authoringType;

        return new Dictionary<string, object>
        {
            ["label"] = link.Label,
            ["labelAr"] = link.LabelAr ?? string.Empty,
            ["authoringType"] = authoringType,
            ["type"] = runtimeType,
            ["targetId"] = link.TargetId?.ToString("D") ?? string.Empty,
            ["collectionKey"] = link.CollectionKey ?? string.Empty,
            ["path"] = link.Path ?? string.Empty,
            ["slug"] = link.Slug ?? string.Empty,
            ["openInNewTab"] = link.OpenInNewTab,
            ["order"] = index,
            ["enabled"] = link.Enabled
        };
    }

    public static AdminNavigationConfigResponse FromStorage(JsonElement element)
    {
        return new AdminNavigationConfigResponse
        {
            Announcement = element.TryGetProperty("header", out var header) && header.ValueKind == JsonValueKind.Object &&
                header.TryGetProperty("announcement", out var announcement)
                ? announcement.Deserialize<PointOfSale.Core.Models.HeaderAnnouncementConfig>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null,
            Header = element.TryGetProperty("header", out var headerElement)
                ? ExtractLinks(headerElement)
                : new List<AdminNavigationLinkRequest>(),
            Footer = element.TryGetProperty("footer", out var footerElement)
                ? ExtractLinks(footerElement)
                : new List<AdminNavigationLinkRequest>()
        };
    }

    public static string NormalizeAuthoringType(string? type)
    {
        var normalized = type?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized == "internal" ? "custom" : normalized;
    }

    private static List<AdminNavigationLinkRequest> ExtractLinks(JsonElement menuElement)
    {
        JsonElement linksElement;
        if (menuElement.ValueKind == JsonValueKind.Object &&
            menuElement.TryGetProperty("links", out var nestedLinks))
        {
            linksElement = nestedLinks;
        }
        else if (menuElement.ValueKind == JsonValueKind.Array)
        {
            linksElement = menuElement;
        }
        else
        {
            return new List<AdminNavigationLinkRequest>();
        }

        if (linksElement.ValueKind != JsonValueKind.Array)
        {
            return new List<AdminNavigationLinkRequest>();
        }

        return linksElement
            .EnumerateArray()
            .Select((link, sourceIndex) => new
            {
                Link = new AdminNavigationLinkRequest
                {
                    Label = GetString(link, "label") ?? string.Empty,
                    LabelAr = GetString(link, "labelAr"),
                    Type = NormalizeAuthoringType(GetString(link, "authoringType") ?? GetString(link, "type")),
                    TargetId = GetGuid(link, "targetId"),
                    CollectionKey = GetString(link, "collectionKey"),
                    Path = GetString(link, "path"),
                    Slug = GetString(link, "slug"),
                    OpenInNewTab = GetBool(link, "openInNewTab", false),
                    Order = GetInt(link, "order", sourceIndex),
                    Enabled = GetBool(link, "enabled", true)
                },
                SourceIndex = sourceIndex
            })
            .OrderBy(item => item.Link.Order)
            .ThenBy(item => item.SourceIndex)
            .Select(item => item.Link)
            .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Guid? GetGuid(JsonElement element, string propertyName) =>
        Guid.TryParse(GetString(element, propertyName), out var value) && value != Guid.Empty
            ? value
            : null;

    private static bool GetBool(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return defaultValue;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var value)
            ? value
            : defaultValue;
}
