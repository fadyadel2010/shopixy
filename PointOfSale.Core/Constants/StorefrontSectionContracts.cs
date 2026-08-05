using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PointOfSale.Core.Constants;

public sealed record StorefrontSectionContractDefinition(
    string Type,
    IReadOnlyDictionary<string, object?> DefaultSettings,
    IReadOnlyList<StorefrontSectionContractField> Fields);

public sealed record StorefrontSectionContractField(
    string Key,
    string Type,
    object? DefaultValue,
    int? Min,
    int? Max,
    int? MaxItems,
    IReadOnlyList<string> Options,
    string? ItemLabel,
    StorefrontSectionContractItemSchema? ItemSchema,
    string RendererConsumer,
    string VisualEffect);

public sealed record StorefrontSectionContractItemSchema(
    IReadOnlyDictionary<string, object?> DefaultValue,
    IReadOnlyList<StorefrontSectionContractItemField> Fields);

public sealed record StorefrontSectionContractItemField(
    string Key,
    string Type,
    object? DefaultValue,
    int? MaxLength,
    bool AllowEmpty);

public static class StorefrontSectionContracts
{
    private const string ThemeAuthorityResourceName = "shopixy-theme-authority.json";
    private const int DefaultTextMaxLength = 500;
    private const int DefaultLongTextMaxLength = 5000;
    private const int DefaultUrlMaxLength = 2048;
    private static readonly Regex HexColorRegex = new(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    private static readonly SectionContractAuthorityDocument Authority = LoadAuthority();

    public static readonly IReadOnlyList<StorefrontSectionContractDefinition> All = BuildDefinitions(Authority);

    public static readonly IReadOnlySet<string> SupportedTypes = All
        .Select(section => section.Type)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> GetAllowedSettingKeys(string sectionType)
    {
        var section = GetDefinition(sectionType);
        return section.Fields
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GetAllowedSettingKeysBySectionType() =>
        All.ToDictionary(
            section => section.Type,
            section => (IReadOnlySet<string>)section.Fields
                .Select(field => field.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetAllowedStringValues(string sectionType, string key)
    {
        var field = GetDefinition(sectionType).Fields
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

        return field?.Options ?? Array.Empty<string>();
    }

    public static string? GetDefaultString(string sectionType, string key)
    {
        var field = GetDefinition(sectionType).Fields
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

        return field?.DefaultValue as string;
    }

    public static IReadOnlyList<StorefrontSectionContractField> GetFields(string sectionType) =>
        GetDefinition(NormalizeSectionType(sectionType)).Fields;

    public static string NormalizeSectionType(string sectionType)
    {
        return sectionType.Trim().ToLowerInvariant();
    }

    public static bool TryValidateSettingsStrict(
        string sectionType,
        IReadOnlyDictionary<string, object?> settings,
        out IReadOnlyList<string> diagnostics)
    {
        var errors = new List<string>();
        var canonicalType = NormalizeSectionType(sectionType);
        StorefrontSectionContractDefinition definition;

        try
        {
            definition = GetDefinition(canonicalType);
        }
        catch (Exception ex)
        {
            diagnostics = new[] { ex.Message };
            return false;
        }

        var fieldsByKey = definition.Fields.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var key in settings.Keys)
        {
            if (!fieldsByKey.ContainsKey(key))
            {
                errors.Add($"Section setting '{key}' is not supported for section type '{canonicalType}'.");
            }
        }

        foreach (var field in definition.Fields)
        {
            if (!settings.TryGetValue(field.Key, out var value))
            {
                errors.Add($"Section setting '{field.Key}' is required for section type '{canonicalType}'.");
                continue;
            }

            ValidateFieldValue(canonicalType, field, value, errors);
        }

        diagnostics = errors;
        return errors.Count == 0;
    }

    private static StorefrontSectionContractDefinition GetDefinition(string sectionType) =>
        All.FirstOrDefault(section => string.Equals(section.Type, NormalizeSectionType(sectionType), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Storefront section contract '{sectionType}' is not registered.");

    private static void ValidateFieldValue(
        string sectionType,
        StorefrontSectionContractField field,
        object? value,
        ICollection<string> errors)
    {
        value = ConvertJsonValue(value);

        if (value == null)
        {
            errors.Add($"Section setting '{field.Key}' is required for section type '{sectionType}'.");
            return;
        }

        switch (field.Type)
        {
            case "toggle":
                if (value is not bool)
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be boolean.");
                }
                break;
            case "number":
                if (!TryReadNumber(value, out var number))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be number.");
                    break;
                }

                if (field.Min.HasValue && number < field.Min.Value)
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' is below minimum {field.Min.Value}.");
                }

                if (field.Max.HasValue && number > field.Max.Value)
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' is above maximum {field.Max.Value}.");
                }
                break;
            case "guid-list":
                if (!TryReadList(value, out var guidList))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be a string array.");
                    break;
                }

                ValidateListSize(sectionType, field, guidList, errors);
                var guidValues = new List<string>();
                foreach (var item in guidList)
                {
                    if (item is not string stringItem || !Guid.TryParse(stringItem, out _))
                    {
                        errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must contain valid GUID values.");
                        continue;
                    }

                    guidValues.Add(stringItem);
                }

                if (guidValues.Count != guidValues.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must not contain duplicate GUID values.");
                }
                break;
            case "image-list":
                if (!TryReadList(value, out var images))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be an array.");
                    break;
                }

                ValidateListSize(sectionType, field, images, errors);
                foreach (var image in images)
                {
                    if (image is not string imageUrl || !IsSafeAuthoredUrl(imageUrl, field.Type))
                    {
                        errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must contain safe image URLs.");
                    }
                }
                break;
            case "list":
                if (!TryReadList(value, out var items))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be an array.");
                    break;
                }

                ValidateListSize(sectionType, field, items, errors);
                ValidateNestedList(sectionType, field, items, errors);
                break;
            default:
                if (value is not string stringValue)
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be string.");
                    break;
                }

                if (field.Type.Equals("color", StringComparison.OrdinalIgnoreCase) &&
                    !HexColorRegex.IsMatch(stringValue))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be a hex color.");
                }

                var maxLength = field.Type.Equals("textarea", StringComparison.OrdinalIgnoreCase)
                    ? DefaultLongTextMaxLength
                    : field.Type is "url" or "image" or "video"
                        ? DefaultUrlMaxLength
                        : DefaultTextMaxLength;
                if (stringValue.Length > maxLength)
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' exceeds maximum length {maxLength}.");
                }

                if (field.Type is "url" or "image" or "video" && !IsSafeAuthoredUrl(stringValue, field.Type))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be a safe URL or relative path.");
                }

                if (field.Options.Count > 0 &&
                    !field.Options.Contains(stringValue, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' has unsupported value '{stringValue}'.");
                }
                break;
        }
    }

    private static bool TryReadList(object value, out IReadOnlyList<object?> items)
    {
        if (value is string || value is not IEnumerable<object?> enumerable)
        {
            items = Array.Empty<object?>();
            return false;
        }

        items = enumerable.ToArray();
        return true;
    }

    private static void ValidateListSize(
        string sectionType,
        StorefrontSectionContractField field,
        IReadOnlyCollection<object?> items,
        ICollection<string> errors)
    {
        if (field.MaxItems.HasValue && items.Count > field.MaxItems.Value)
        {
            errors.Add(
                $"Section setting '{field.Key}' for section type '{sectionType}' exceeds maximum item count {field.MaxItems.Value}.");
        }
    }

    private static void ValidateNestedList(
        string sectionType,
        StorefrontSectionContractField field,
        IReadOnlyList<object?> items,
        ICollection<string> errors)
    {
        if (field.ItemSchema == null)
        {
            errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' has no semantic item schema.");
            return;
        }

        var fieldsByKey = field.ItemSchema.Fields.ToDictionary(itemField => itemField.Key, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not IReadOnlyDictionary<string, object?> item)
            {
                errors.Add($"Section setting '{field.Key}' item {index} for section type '{sectionType}' must be an object.");
                continue;
            }

            foreach (var key in item.Keys)
            {
                if (!fieldsByKey.ContainsKey(key))
                {
                    errors.Add(
                        $"Section setting '{field.Key}' item {index} for section type '{sectionType}' contains unsupported property '{key}'.");
                }
            }

            foreach (var itemField in field.ItemSchema.Fields)
            {
                if (!item.TryGetValue(itemField.Key, out var itemValue) || itemValue is not string stringValue)
                {
                    errors.Add(
                        $"Section setting '{field.Key}' item {index} property '{itemField.Key}' for section type '{sectionType}' must be string.");
                    continue;
                }

                if (!itemField.AllowEmpty && string.IsNullOrWhiteSpace(stringValue))
                {
                    errors.Add(
                        $"Section setting '{field.Key}' item {index} property '{itemField.Key}' for section type '{sectionType}' must not be empty.");
                }

                if (itemField.MaxLength.HasValue && stringValue.Length > itemField.MaxLength.Value)
                {
                    errors.Add(
                        $"Section setting '{field.Key}' item {index} property '{itemField.Key}' for section type '{sectionType}' exceeds maximum length {itemField.MaxLength.Value}.");
                }

                if (itemField.Type is "url" or "image" or "video" && !IsSafeAuthoredUrl(stringValue, itemField.Type))
                {
                    errors.Add(
                        $"Section setting '{field.Key}' item {index} property '{itemField.Key}' for section type '{sectionType}' must be a safe URL or relative path.");
                }
            }
        }
    }

    private static bool IsSafeAuthoredUrl(string value, string fieldType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Length > DefaultUrlMaxLength || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) || value.Contains('\\'))
        {
            return false;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("#", StringComparison.Ordinal) ||
            value.StartsWith("?", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            return Uri.TryCreate(value, UriKind.Relative, out _) &&
                   !value.Contains(':', StringComparison.Ordinal);
        }

        if (absoluteUri.Scheme is "http" or "https")
        {
            return string.IsNullOrEmpty(absoluteUri.UserInfo);
        }

        return fieldType.Equals("url", StringComparison.OrdinalIgnoreCase) &&
               absoluteUri.Scheme is "mailto" or "tel";
    }

    private static bool TryReadNumber(object value, out double number)
    {
        switch (value)
        {
            case byte byteValue:
                number = byteValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                number = floatValue;
                return true;
            case double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue):
                number = doubleValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static IReadOnlyList<StorefrontSectionContractDefinition> BuildDefinitions(SectionContractAuthorityDocument authority)
    {
        var definitions = authority.SectionContracts
            .Select(section => new StorefrontSectionContractDefinition(
                section.Type,
                ConvertObjectMap(section.DefaultSettings),
                section.Fields.Select(field => new StorefrontSectionContractField(
                    field.Key,
                    field.Type,
                    ConvertJsonValue(field.DefaultValue),
                    field.Min,
                    field.Max,
                    field.MaxItems,
                    field.Options ?? Array.Empty<string>(),
                    field.ItemLabel,
                    field.ItemSchema == null
                        ? null
                        : new StorefrontSectionContractItemSchema(
                            ConvertObjectMap(field.ItemSchema.DefaultValue),
                            field.ItemSchema.Fields.Select(itemField => new StorefrontSectionContractItemField(
                                itemField.Key,
                                itemField.Type,
                                ConvertJsonValue(itemField.DefaultValue),
                                itemField.MaxLength,
                                itemField.AllowEmpty)).ToArray()),
                    field.RendererConsumer,
                    field.VisualEffect)).ToArray()))
            .ToArray();

        var duplicate = definitions
            .GroupBy(section => section.Type, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidOperationException($"Duplicate storefront section contract registered: '{duplicate.Key}'.");
        }

        foreach (var section in definitions)
        {
            if (string.IsNullOrWhiteSpace(section.Type) || section.Fields.Count == 0)
            {
                throw new InvalidOperationException($"Storefront section contract '{section.Type}' has an invalid manifest.");
            }

            var duplicateField = section.Fields
                .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateField != null)
            {
                throw new InvalidOperationException(
                    $"Duplicate field '{duplicateField.Key}' in storefront section contract '{section.Type}'.");
            }
        }

        return definitions;
    }

    private static SectionContractAuthorityDocument LoadAuthority()
    {
        var assembly = typeof(StorefrontThemes).Assembly;
        using var stream = assembly.GetManifestResourceStream(ThemeAuthorityResourceName)
            ?? throw new InvalidOperationException($"Canonical theme authority resource '{ThemeAuthorityResourceName}' was not found.");

        var authority = JsonSerializer.Deserialize<SectionContractAuthorityDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (authority is null || authority.SectionContracts.Count == 0)
        {
            throw new InvalidOperationException("Canonical section contract manifest is invalid.");
        }

        return authority;
    }

    private static IReadOnlyDictionary<string, object?> ConvertObjectMap(IReadOnlyDictionary<string, JsonElement> source) =>
        source.ToDictionary(
            entry => entry.Key,
            entry => ConvertJsonValue(entry.Value),
            StringComparer.OrdinalIgnoreCase);

    private static object? ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var intValue) ? intValue : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.Object => value
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => ConvertJsonValue(property.Value), StringComparer.OrdinalIgnoreCase),
            _ => value.ToString()
        };
    }

    private static object? ConvertJsonValue(object? value) =>
        value is JsonElement element ? ConvertJsonValue(element) : value;

    private sealed record SectionContractAuthorityDocument(
        IReadOnlyList<SectionContractManifest> SectionContracts);

    private sealed record SectionContractManifest(
        string Type,
        IReadOnlyDictionary<string, JsonElement> DefaultSettings,
        IReadOnlyList<SectionContractFieldManifest> Fields);

    private sealed record SectionContractFieldManifest(
        string Key,
        string Type,
        JsonElement DefaultValue,
        int? Min,
        int? Max,
        int? MaxItems,
        IReadOnlyList<string>? Options,
        string? ItemLabel,
        SectionContractItemSchemaManifest? ItemSchema,
        string RendererConsumer,
        string VisualEffect);

    private sealed record SectionContractItemSchemaManifest(
        IReadOnlyDictionary<string, JsonElement> DefaultValue,
        IReadOnlyList<SectionContractItemFieldManifest> Fields);

    private sealed record SectionContractItemFieldManifest(
        string Key,
        string Type,
        JsonElement DefaultValue,
        int? MaxLength,
        bool AllowEmpty);
}
