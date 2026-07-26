using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Localization;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.Services.Homepage;

public interface IHomepageSectionSchemaRegistry
{
    IReadOnlyList<HomepageSectionSchemaDto> GetSchemas();
    HomepageSectionValidationResult ValidateAndNormalize(string? type, JsonElement data, int? schemaVersion = null);
}

public sealed class HomepageSectionManagementException : Exception
{
    public HomepageSectionManagementException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}

public sealed record HomepageSectionValidationResult(
    string Type,
    int SchemaVersion,
    string NormalizedData,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<Guid> CategoryIds);

public static class HomepageSectionTypes
{
    public const string Hero = "hero";
    public const string Products = "products";
    public const string Banner = "banner";
    public const string Categories = "categories";
}

public sealed class HomepageSectionSchemaRegistry : IHomepageSectionSchemaRegistry
{
    private const int CurrentSchemaVersion = 1;
    private static readonly string[] HeroKeys = ["title", "subtitle", "imageUrl", "ctaText", "ctaLink"];
    private static readonly string[] HeroLocalizedKeys = ["titleLocalized", "subtitleLocalized", "ctaTextLocalized"];
    private static readonly string[] ProductKeys = ["title", "subtitle", "source", "maxItems", "productIds", "layout"];
    private static readonly string[] ProductLocalizedKeys = ["titleLocalized", "subtitleLocalized"];
    private static readonly string[] BannerKeys = ["title", "subtitle", "imageUrl", "ctaText", "ctaLink"];
    private static readonly string[] BannerLocalizedKeys = ["titleLocalized", "subtitleLocalized", "ctaTextLocalized"];
    private static readonly string[] CategoryKeys = ["title", "subtitle", "source", "maxItems", "categoryIds"];
    private static readonly string[] CategoryLocalizedKeys = ["titleLocalized", "subtitleLocalized"];

    private readonly IReadOnlyList<HomepageSectionSchemaDto> _schemas =
    [
        new HomepageSectionSchemaDto
        {
            Type = HomepageSectionTypes.Hero,
            DisplayName = "Hero",
            Description = "Primary homepage spotlight with image and call to action.",
            Version = CurrentSchemaVersion,
            Fields =
            [
                Field("title", "Title", "text", true, "Primary hero headline.", isLocalized: true),
                Field("subtitle", "Subtitle", "textarea", true, "Support copy under the headline.", isLocalized: true),
                Field("imageUrl", "Image URL", "image", true, "Hero artwork or marketing visual."),
                Field("ctaText", "CTA Text", "text", true, "Primary action label.", isLocalized: true),
                Field("ctaLink", "CTA Link", "link", true, "Relative path or full URL.")
            ]
        },
        new HomepageSectionSchemaDto
        {
            Type = HomepageSectionTypes.Products,
            DisplayName = "Products",
            Description = "All, random, or curated product shelf rendered as a premium grid or carousel.",
            Version = CurrentSchemaVersion,
            Fields =
            [
                Field("title", "Title", "text", true, "Section heading shown above the products.", isLocalized: true),
                Field("subtitle", "Subtitle", "textarea", false, "Optional supporting copy.", isLocalized: true),
                Field("source", "Source", "select", true, "Choose whether this shelf uses all products, a stable random mix, or a curated list.", ["all", "random", "curated"]),
                Field("maxItems", "Items to Show", "number", false, "Optional item limit. Leave empty to use the storefront default."),
                Field("productIds", "Curated Product IDs", "guid-list", false, "Only used when Source is set to curated. Leave empty for all products or random products."),
                Field("layout", "Layout", "select", true, "Presentation style.", ["grid", "carousel"])
            ]
        },
        new HomepageSectionSchemaDto
        {
            Type = HomepageSectionTypes.Banner,
            DisplayName = "Banner",
            Description = "Promotional banner section with CTA.",
            Version = CurrentSchemaVersion,
            Fields =
            [
                Field("title", "Title", "text", true, "Banner headline.", isLocalized: true),
                Field("subtitle", "Subtitle", "textarea", true, "Short promotional message.", isLocalized: true),
                Field("imageUrl", "Image URL", "image", true, "Campaign artwork."),
                Field("ctaText", "CTA Text", "text", true, "Primary action label.", isLocalized: true),
                Field("ctaLink", "CTA Link", "link", true, "Relative path or full URL.")
            ]
        },
        new HomepageSectionSchemaDto
        {
            Type = HomepageSectionTypes.Categories,
            DisplayName = "Categories",
            Description = "All, random, or curated category grid for discovery navigation.",
            Version = CurrentSchemaVersion,
            Fields =
            [
                Field("title", "Title", "text", true, "Section heading shown above the categories.", isLocalized: true),
                Field("subtitle", "Subtitle", "textarea", false, "Optional supporting copy.", isLocalized: true),
                Field("source", "Source", "select", true, "Choose whether this grid uses all categories, a stable random mix, or a curated list.", ["all", "random", "curated"]),
                Field("maxItems", "Items to Show", "number", false, "Optional item limit. Leave empty to use the storefront default."),
                Field("categoryIds", "Curated Category IDs", "guid-list", false, "Only used when Source is set to curated. Leave empty for all categories or random categories.")
            ]
        }
    ];

    public IReadOnlyList<HomepageSectionSchemaDto> GetSchemas() => _schemas;

    public HomepageSectionValidationResult ValidateAndNormalize(string? type, JsonElement data, int? schemaVersion = null)
    {
        var normalizedType = NormalizeType(type);
        var normalizedSchemaVersion = NormalizeSchemaVersion(schemaVersion);

        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                "Section data must be a JSON object.");
        }

        return normalizedType switch
        {
            HomepageSectionTypes.Hero => ValidateHero(data, normalizedSchemaVersion),
            HomepageSectionTypes.Products => ValidateProducts(data, normalizedSchemaVersion),
            HomepageSectionTypes.Banner => ValidateBanner(data, normalizedSchemaVersion),
            HomepageSectionTypes.Categories => ValidateCategories(data, normalizedSchemaVersion),
            _ => throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_TYPE_INVALID",
                "Unsupported homepage section type.")
        };
    }

    private static HomepageSectionValidationResult ValidateHero(JsonElement data, int schemaVersion)
    {
        RejectUnknownKeys(data, HeroKeys, HeroLocalizedKeys);
        var title = ReadRequiredLocalizedBackedString(data, "title");
        var subtitle = ReadRequiredLocalizedBackedString(data, "subtitle");
        var ctaText = ReadRequiredLocalizedBackedString(data, "ctaText");

        var normalized = new Dictionary<string, object?>
        {
            ["title"] = title.Value,
            ["subtitle"] = subtitle.Value,
            ["imageUrl"] = ReadRequiredString(data, "imageUrl"),
            ["ctaText"] = ctaText.Value,
            ["ctaLink"] = ReadRequiredString(data, "ctaLink")
        };

        AddLocalizedField(normalized, "title", title.Localized);
        AddLocalizedField(normalized, "subtitle", subtitle.Localized);
        AddLocalizedField(normalized, "ctaText", ctaText.Localized);

        return new HomepageSectionValidationResult(
            HomepageSectionTypes.Hero,
            schemaVersion,
            JsonSerializer.Serialize(normalized),
            [],
            []);
    }

    private static HomepageSectionValidationResult ValidateProducts(JsonElement data, int schemaVersion)
    {
        RejectUnknownKeys(data, ProductKeys, ProductLocalizedKeys);

        var productIds = ReadOptionalGuidArray(data, "productIds");
        var source = ReadCollectionSource(data, "source", productIds.Count > 0 ? "curated" : "all");
        var maxItems = ReadOptionalPositiveInteger(data, "maxItems", fallbackValue: 8, maxValue: 24);
        var layout = ReadRequiredString(data, "layout");
        var title = ReadRequiredLocalizedBackedString(data, "title");
        var subtitle = ReadOptionalLocalizedBackedString(data, "subtitle");
        if (!string.Equals(layout, "grid", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(layout, "carousel", StringComparison.OrdinalIgnoreCase))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                "Products section layout must be either 'grid' or 'carousel'.");
        }

        if (string.Equals(source, "curated", StringComparison.Ordinal) && productIds.Count == 0)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                "Curated products require at least one product ID.");
        }

        var normalized = new Dictionary<string, object?>
        {
            ["title"] = title.Value,
            ["subtitle"] = subtitle.Value,
            ["source"] = source,
            ["maxItems"] = maxItems,
            ["productIds"] = productIds.Select(id => id.ToString("D")).ToList(),
            ["layout"] = layout.ToLowerInvariant()
        };

        AddLocalizedField(normalized, "title", title.Localized);
        AddLocalizedField(normalized, "subtitle", subtitle.Localized);

        return new HomepageSectionValidationResult(
            HomepageSectionTypes.Products,
            schemaVersion,
            JsonSerializer.Serialize(normalized),
            productIds,
            []);
    }

    private static HomepageSectionValidationResult ValidateBanner(JsonElement data, int schemaVersion)
    {
        RejectUnknownKeys(data, BannerKeys, BannerLocalizedKeys);
        var title = ReadRequiredLocalizedBackedString(data, "title");
        var subtitle = ReadRequiredLocalizedBackedString(data, "subtitle");
        var ctaText = ReadRequiredLocalizedBackedString(data, "ctaText");

        var normalized = new Dictionary<string, object?>
        {
            ["title"] = title.Value,
            ["subtitle"] = subtitle.Value,
            ["imageUrl"] = ReadRequiredString(data, "imageUrl"),
            ["ctaText"] = ctaText.Value,
            ["ctaLink"] = ReadRequiredString(data, "ctaLink")
        };

        AddLocalizedField(normalized, "title", title.Localized);
        AddLocalizedField(normalized, "subtitle", subtitle.Localized);
        AddLocalizedField(normalized, "ctaText", ctaText.Localized);

        return new HomepageSectionValidationResult(
            HomepageSectionTypes.Banner,
            schemaVersion,
            JsonSerializer.Serialize(normalized),
            [],
            []);
    }

    private static HomepageSectionValidationResult ValidateCategories(JsonElement data, int schemaVersion)
    {
        RejectUnknownKeys(data, CategoryKeys, CategoryLocalizedKeys);

        var categoryIds = ReadOptionalGuidArray(data, "categoryIds");
        var source = ReadCollectionSource(data, "source", categoryIds.Count > 0 ? "curated" : "all");
        var maxItems = ReadOptionalPositiveInteger(data, "maxItems", fallbackValue: 6, maxValue: 18);
        var title = ReadRequiredLocalizedBackedString(data, "title");
        var subtitle = ReadOptionalLocalizedBackedString(data, "subtitle");

        if (string.Equals(source, "curated", StringComparison.Ordinal) && categoryIds.Count == 0)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                "Curated categories require at least one category ID.");
        }

        var normalized = new Dictionary<string, object?>
        {
            ["title"] = title.Value,
            ["subtitle"] = subtitle.Value,
            ["source"] = source,
            ["maxItems"] = maxItems,
            ["categoryIds"] = categoryIds.Select(id => id.ToString("D")).ToList()
        };

        AddLocalizedField(normalized, "title", title.Localized);
        AddLocalizedField(normalized, "subtitle", subtitle.Localized);

        return new HomepageSectionValidationResult(
            HomepageSectionTypes.Categories,
            schemaVersion,
            JsonSerializer.Serialize(normalized),
            [],
            categoryIds);
    }

    private static string NormalizeType(string? type)
    {
        var normalizedType = type?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_TYPE_INVALID",
                "Section type is required.");
        }

        return normalizedType;
    }

    private static int NormalizeSchemaVersion(int? schemaVersion)
    {
        var normalizedVersion = schemaVersion ?? CurrentSchemaVersion;
        if (normalizedVersion != CurrentSchemaVersion)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_SCHEMA_VERSION_INVALID",
                $"Schema version '{normalizedVersion}' is not supported for homepage sections.");
        }

        return normalizedVersion;
    }

    private static void RejectUnknownKeys(
        JsonElement data,
        IReadOnlyCollection<string> allowedKeys,
        IReadOnlyCollection<string>? localizedKeys = null)
    {
        var validKeys = allowedKeys
            .Concat(localizedKeys ?? Array.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in data.EnumerateObject())
        {
            if (!validKeys.Contains(property.Name))
            {
                throw new HomepageSectionManagementException(
                    StatusCodes.Status400BadRequest,
                    "HOMEPAGE_SECTION_DATA_INVALID",
                    $"Unknown property '{property.Name}' was provided for this section type.");
            }
        }
    }

    private static string ReadRequiredString(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' is required and must be a string.");
        }

        var value = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' is required and cannot be empty.");
        }

        return value;
    }

    private static string? ReadOptionalString(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' must be a string when provided.");
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static LocalizedStringReadResult ReadRequiredLocalizedBackedString(JsonElement data, string propertyName)
    {
        var localized = ReadLocalizedField(data, propertyName);
        var englishValue = MarketplaceLocalization.NormalizeOptionalText(localized?.En) ?? ReadRequiredString(data, propertyName);
        if (string.IsNullOrWhiteSpace(englishValue))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' is required and cannot be empty.");
        }

        if (localized != null)
        {
            localized.En = englishValue;
        }

        return new LocalizedStringReadResult(englishValue, localized?.HasValue == true ? localized : null);
    }

    private static LocalizedStringReadResult ReadOptionalLocalizedBackedString(JsonElement data, string propertyName)
    {
        var localized = ReadLocalizedField(data, propertyName);
        var englishValue = MarketplaceLocalization.NormalizeOptionalText(localized?.En) ?? ReadOptionalString(data, propertyName);

        if (localized != null && !string.IsNullOrWhiteSpace(englishValue))
        {
            localized.En = englishValue;
        }

        return new LocalizedStringReadResult(englishValue, localized?.HasValue == true ? localized : null);
    }

    private static LocalizedField? ReadLocalizedField(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty($"{propertyName}Localized", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}Localized' must be an object when provided.");
        }

        var localized = new LocalizedField
        {
            En = ReadLocalizedTextValue(property, MarketplaceLocalization.English, propertyName),
            Ar = ReadLocalizedTextValue(property, MarketplaceLocalization.Arabic, propertyName)
        };

        return localized.HasValue ? localized : null;
    }

    private static string? ReadLocalizedTextValue(
        JsonElement localizedObject,
        string propertyName,
        string fieldName)
    {
        if (!localizedObject.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{fieldName}Localized.{propertyName}' must be a string when provided.");
        }

        return MarketplaceLocalization.NormalizeOptionalText(property.GetString());
    }

    private static List<Guid> ReadOptionalGuidArray(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' must be an array of GUID strings when provided.");
        }

        var values = new List<Guid>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(item.GetString(), out var id) ||
                id == Guid.Empty)
            {
                throw new HomepageSectionManagementException(
                    StatusCodes.Status400BadRequest,
                    "HOMEPAGE_SECTION_DATA_INVALID",
                    $"'{propertyName}' must contain valid GUID strings.");
            }

            if (!values.Contains(id))
            {
                values.Add(id);
            }
        }

        return values;
    }

    private static string ReadCollectionSource(JsonElement data, string propertyName, string fallbackValue)
    {
        var source = ReadOptionalString(data, propertyName)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(source))
        {
            return fallbackValue;
        }

        if (string.Equals(source, "all", StringComparison.Ordinal) ||
            string.Equals(source, "random", StringComparison.Ordinal) ||
            string.Equals(source, "curated", StringComparison.Ordinal))
        {
            return source;
        }

        throw new HomepageSectionManagementException(
            StatusCodes.Status400BadRequest,
            "HOMEPAGE_SECTION_DATA_INVALID",
            $"'{propertyName}' must be one of: all, random, curated.");
    }

    private static int ReadOptionalPositiveInteger(
        JsonElement data,
        string propertyName,
        int fallbackValue,
        int maxValue)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return fallbackValue;
        }

        if (property.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(property.GetString()))
        {
            return fallbackValue;
        }

        int value;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
        {
            value = numericValue;
        }
        else if (property.ValueKind == JsonValueKind.String &&
                 int.TryParse(property.GetString()?.Trim(), out var stringValue))
        {
            value = stringValue;
        }
        else
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' must be a positive whole number when provided.");
        }

        if (value <= 0 || value > maxValue)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                $"'{propertyName}' must be between 1 and {maxValue}.");
        }

        return value;
    }

    private static HomepageSectionSchemaFieldDto Field(
        string key,
        string label,
        string kind,
        bool required,
        string helpText,
        List<string>? options = null,
        bool isLocalized = false)
    {
        return new HomepageSectionSchemaFieldDto
        {
            Key = key,
            Label = label,
            Kind = kind,
            Required = required,
            IsLocalized = isLocalized,
            HelpText = helpText,
            Options = options ?? []
        };
    }

    private static void AddLocalizedField(
        IDictionary<string, object?> normalized,
        string key,
        LocalizedField? localized)
    {
        if (localized?.HasValue == true)
        {
            normalized[$"{key}Localized"] = localized;
        }
    }

    private sealed record LocalizedStringReadResult(string? Value, LocalizedField? Localized);
}
