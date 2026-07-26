using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.Services.Security;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.MarketplaceCms;

public interface IMarketplaceCmsPageContentValidator
{
    MarketplaceCmsPageContentValidationResult ValidateAndNormalize(
        CmsPageContentType contentType,
        JsonElement content);
}

public sealed record MarketplaceCmsPageContentValidationResult(
    CmsPageContentType ContentType,
    string NormalizedContent);

public sealed class MarketplaceCmsPageContentValidator : IMarketplaceCmsPageContentValidator
{
    private static readonly Regex SlugishBlockTypeRegex = new(
        "^[a-z0-9-]{2,50}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public MarketplaceCmsPageContentValidationResult ValidateAndNormalize(
        CmsPageContentType contentType,
        JsonElement content)
    {
        return contentType switch
        {
            CmsPageContentType.RichText => new MarketplaceCmsPageContentValidationResult(
                contentType,
                NormalizeRichText(content)),
            CmsPageContentType.Blocks => new MarketplaceCmsPageContentValidationResult(
                contentType,
                NormalizeBlocks(content)),
            _ => throw new InvalidOperationException("Unsupported marketplace CMS page content type.")
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
            _ => throw new InvalidOperationException($"Unsupported marketplace CMS block type '{type}'.")
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
            ["text"] = text.Trim()
        };

        if (!string.IsNullOrWhiteSpace(alignment))
        {
            normalized["alignment"] = alignment.Trim().ToLowerInvariant();
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
            ["imageUrl"] = CmsPageContentValidator.NormalizeSafeUrl(imageUrl, "Image block URL"),
            ["alt"] = ReadOptionalString(data, "alt")?.Trim() ?? string.Empty,
            ["caption"] = ReadOptionalString(data, "caption")?.Trim() ?? string.Empty
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
                ["question"] = question.Trim(),
                ["answer"] = answer.Trim()
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
}
