using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PointOfSale.API.Services.Security;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Cms;

public interface ICmsPageContentValidator
{
    CmsPageContentValidationResult ValidateAndNormalize(
        CmsPageContentType contentType,
        JsonElement content,
        int schemaVersion);
}

public sealed record CmsPageContentValidationResult(
    CmsPageContentType ContentType,
    int SchemaVersion,
    string NormalizedContent);

public sealed class CmsPageContentValidator : ICmsPageContentValidator
{
    private const int MaxBlocks = 100;
    private const int MaxFaqItems = 50;

    private static readonly Regex SlugishBlockTypeRegex = new(
        "^[a-z0-9-]{2,50}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SupportedBlockTypes = new(StringComparer.Ordinal)
    {
        "text",
        "image",
        "faq"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CmsPageContentValidationResult ValidateAndNormalize(
        CmsPageContentType contentType,
        JsonElement content,
        int schemaVersion)
    {
        if (schemaVersion < 1)
        {
            throw new InvalidOperationException("Schema version must be greater than zero.");
        }

        return contentType switch
        {
            CmsPageContentType.RichText => new CmsPageContentValidationResult(
                contentType,
                schemaVersion,
                NormalizeRichText(content)),
            CmsPageContentType.Blocks => new CmsPageContentValidationResult(
                contentType,
                schemaVersion,
                NormalizeBlocks(content)),
            _ => throw new InvalidOperationException("Unsupported CMS page content type.")
        };
    }

    private static string NormalizeRichText(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Rich text pages require HTML string content.");
        }

        var html = content.GetString() ?? string.Empty;
        return SanitizeHtml(html.Trim());
    }

    private static string NormalizeBlocks(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Blocks pages require a JSON array content payload.");
        }

        var blocks = new JsonArray();

        foreach (var block in content.EnumerateArray())
        {
            if (blocks.Count >= MaxBlocks)
            {
                throw new InvalidOperationException($"CMS pages are limited to {MaxBlocks} content blocks.");
            }

            if (block.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Each block must be a JSON object.");
            }

            if (!block.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("Each block must include a string 'type'.");
            }

            var type = (typeElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!SlugishBlockTypeRegex.IsMatch(type))
            {
                throw new InvalidOperationException("Block type names must be lowercase kebab-case identifiers.");
            }

            if (!SupportedBlockTypes.Contains(type))
            {
                throw new InvalidOperationException($"Block type '{type}' is not supported.");
            }

            if (!block.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Block '{type}' must include an object 'data' payload.");
            }

            var normalizedData = NormalizeBlockData(type, dataElement);
            blocks.Add(new JsonObject
            {
                ["type"] = type,
                ["data"] = normalizedData
            });
        }

        return blocks.ToJsonString(JsonOptions);
    }

    private static JsonObject NormalizeBlockData(string type, JsonElement data)
    {
        return type switch
        {
            "text" => NormalizeTextBlock(data),
            "image" => NormalizeImageBlock(data),
            "faq" => NormalizeFaqBlock(data),
            _ => throw new InvalidOperationException($"Block type '{type}' is not supported.")
        };
    }

    private static JsonObject NormalizeTextBlock(JsonElement data)
    {
        var text = ReadOptionalString(data, "text");
        var alignment = ReadOptionalString(data, "alignment");

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Text blocks require a non-empty 'text' field.");
        }

        var normalized = new JsonObject
        {
            ["text"] = NormalizeText(text, "Text block content", 10000)
        };

        if (!string.IsNullOrWhiteSpace(alignment))
        {
            var normalizedAlignment = alignment.Trim().ToLowerInvariant();
            if (normalizedAlignment is not ("left" or "center" or "right"))
            {
                throw new InvalidOperationException("Text block alignment must be left, center, or right.");
            }

            normalized["alignment"] = normalizedAlignment;
        }

        return normalized;
    }

    private static JsonObject NormalizeImageBlock(JsonElement data)
    {
        var imageUrl = ReadOptionalString(data, "imageUrl") ?? ReadOptionalString(data, "url");
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new InvalidOperationException("Image blocks require an 'imageUrl'.");
        }

        return new JsonObject
        {
            ["imageUrl"] = NormalizeSafeUrl(imageUrl, "Image block URL"),
            ["alt"] = NormalizeText(ReadOptionalString(data, "alt") ?? string.Empty, "Image alt text", 300),
            ["caption"] = NormalizeText(ReadOptionalString(data, "caption") ?? string.Empty, "Image caption", 500)
        };
    }

    private static JsonObject NormalizeFaqBlock(JsonElement data)
    {
        if (!data.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("FAQ blocks require an 'items' array.");
        }

        var items = new JsonArray();
        foreach (var item in itemsElement.EnumerateArray())
        {
            if (items.Count >= MaxFaqItems)
            {
                throw new InvalidOperationException($"FAQ blocks are limited to {MaxFaqItems} items.");
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Each FAQ item must be an object.");
            }

            var question = ReadOptionalString(item, "question");
            var answer = ReadOptionalString(item, "answer");
            if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
            {
                throw new InvalidOperationException("Each FAQ item must include both question and answer.");
            }

            items.Add(new JsonObject
            {
                ["question"] = NormalizeText(question, "FAQ question", 500),
                ["answer"] = NormalizeText(answer, "FAQ answer", 5000)
            });
        }

        return new JsonObject
        {
            ["items"] = items
        };
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"'{propertyName}' must be a string.");
        }

        return value.GetString();
    }

    private static string SanitizeHtml(string html)
    {
        return SafeRichTextSanitizer.Sanitize(html);
    }

    public static string NormalizeSafeUrl(string value, string fieldName, int maxLength = 1000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{fieldName} is invalid.");
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.Contains('\\'))
            {
                throw new InvalidOperationException($"{fieldName} must be a safe relative path or an http(s) URL.");
            }

            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException($"{fieldName} must be a safe relative path or an http(s) URL.");
        }

        return trimmed;
    }

    private static string NormalizeText(string value, string fieldName, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{fieldName} is invalid.");
        }

        return trimmed;
    }
}
