using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.Localization;

public static class MarketplaceLocalization
{
    public const string English = "en";
    public const string Arabic = "ar";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string ResolveLanguage(string? requestedLanguage, StringValues acceptLanguage)
    {
        return StorefrontLanguageResolver.Resolve(requestedLanguage, acceptLanguage);
    }

    public static string NormalizeLanguage(string? value)
    {
        return StorefrontLanguageResolver.Normalize(value);
    }

    public static LocalizedField MergeField(
        string? serializedField,
        string? fallbackEnglish,
        string? fallbackArabic = null)
    {
        var parsed = DeserializeField(serializedField) ?? new LocalizedField();

        if (string.IsNullOrWhiteSpace(parsed.En))
        {
            parsed.En = NormalizeOptionalText(fallbackEnglish);
        }

        if (string.IsNullOrWhiteSpace(parsed.Ar))
        {
            parsed.Ar = NormalizeOptionalText(fallbackArabic);
        }

        return parsed;
    }

    public static string? SerializeField(LocalizedField? field, int? maxLength = null)
    {
        if (field == null)
        {
            return null;
        }

        var normalized = new LocalizedField
        {
            En = NormalizeOptionalText(field.En, maxLength),
            Ar = NormalizeOptionalText(field.Ar, maxLength)
        };

        return normalized.HasValue
            ? JsonSerializer.Serialize(normalized, JsonOptions)
            : null;
    }

    public static string ResolveText(
        LocalizedField? field,
        string language,
        string? fallback = null)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        var normalizedFallback = NormalizeOptionalText(fallback);

        if (normalizedLanguage == Arabic)
        {
            return NormalizeOptionalText(field?.Ar)
                ?? NormalizeOptionalText(field?.En)
                ?? normalizedFallback
                ?? string.Empty;
        }

        return NormalizeOptionalText(field?.En)
            ?? NormalizeOptionalText(field?.Ar)
            ?? normalizedFallback
            ?? string.Empty;
    }

    public static string? SerializeLocalizedContent(
        CmsPageContentType contentType,
        string englishContent,
        string? arabicContent)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WritePropertyName(English);
        WriteContentValue(writer, contentType, englishContent);

        if (!string.IsNullOrWhiteSpace(arabicContent))
        {
            writer.WritePropertyName(Arabic);
            WriteContentValue(writer, contentType, arabicContent);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JsonElement? ParseLocalizedContent(string? serializedContent)
    {
        if (string.IsNullOrWhiteSpace(serializedContent))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(serializedContent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonElement ResolveLocalizedContent(
        CmsPageContentType contentType,
        JsonElement? localizedContent,
        string fallbackContent,
        string language)
    {
        var normalizedLanguage = NormalizeLanguage(language);
        if (localizedContent is JsonElement { ValueKind: JsonValueKind.Object } root)
        {
            if (TryResolveLocalizedValue(root, normalizedLanguage, out var localizedValue))
            {
                return JsonSerializer.SerializeToElement(localizedValue);
            }

            var fallbackLanguage = normalizedLanguage == Arabic ? English : Arabic;
            if (TryResolveLocalizedValue(root, fallbackLanguage, out var fallbackValue))
            {
                return JsonSerializer.SerializeToElement(fallbackValue);
            }
        }

        return ParseLegacyContent(contentType, fallbackContent);
    }

    public static JsonElement ResolveLocalizedSectionData(JsonElement data, string language)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return data;
        }

        var normalizedLanguage = NormalizeLanguage(language);
        var fallbackLanguage = normalizedLanguage == Arabic ? English : Arabic;
        var resolved = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in data.EnumerateObject())
        {
            if (property.Name.EndsWith("Localized", StringComparison.Ordinal))
            {
                continue;
            }

            var localizedKey = $"{property.Name}Localized";
            if (data.TryGetProperty(localizedKey, out var localizedProperty) &&
                localizedProperty.ValueKind == JsonValueKind.Object)
            {
                if (TryResolveLocalizedValue(localizedProperty, normalizedLanguage, out var localizedValue) ||
                    TryResolveLocalizedValue(localizedProperty, fallbackLanguage, out localizedValue))
                {
                    resolved[property.Name] = ConvertJsonElement(localizedValue);
                    resolved[localizedKey] = ConvertJsonElement(localizedProperty);
                    continue;
                }
            }

            resolved[property.Name] = ConvertJsonElement(property.Value);
        }

        foreach (var property in data.EnumerateObject().Where(property => property.Name.EndsWith("Localized", StringComparison.Ordinal)))
        {
            if (!resolved.ContainsKey(property.Name))
            {
                resolved[property.Name] = ConvertJsonElement(property.Value);
            }
        }

        return JsonSerializer.SerializeToElement(resolved, JsonOptions);
    }

    public static LocalizedField? DeserializeField(string? serializedField)
    {
        if (string.IsNullOrWhiteSpace(serializedField))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LocalizedField>(serializedField, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? NormalizeOptionalText(string? value, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return maxLength.HasValue && trimmed.Length > maxLength.Value
            ? trimmed[..maxLength.Value]
            : trimmed;
    }

    private static bool TryResolveLocalizedValue(
        JsonElement container,
        string language,
        out JsonElement value)
    {
        value = default;
        return container.TryGetProperty(language, out value) &&
               value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static JsonElement ParseLegacyContent(CmsPageContentType contentType, string content)
    {
        return contentType switch
        {
            CmsPageContentType.RichText => JsonSerializer.SerializeToElement(content ?? string.Empty),
            CmsPageContentType.Blocks => JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(content) ? "[]" : content),
            _ => JsonSerializer.SerializeToElement(string.Empty)
        };
    }

    private static void WriteContentValue(
        Utf8JsonWriter writer,
        CmsPageContentType contentType,
        string content)
    {
        if (contentType == CmsPageContentType.RichText)
        {
            writer.WriteStringValue(content);
            return;
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "[]" : content);
        document.RootElement.WriteTo(writer);
    }

    private static object? ConvertJsonElement(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<object>(value.GetRawText(), JsonOptions),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var integerValue)
                ? integerValue
                : value.TryGetDecimal(out var decimalValue)
                    ? decimalValue
                    : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
