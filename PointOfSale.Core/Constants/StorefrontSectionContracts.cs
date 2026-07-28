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
    IReadOnlyList<string> Options,
    string RendererConsumer,
    string VisualEffect);

public static class StorefrontSectionContracts
{
    private const string ThemeAuthorityResourceName = "shopixy-theme-authority.json";
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
                if (value is not IEnumerable<object?> guidList || value is string ||
                    guidList.Any(item => item is not string))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be a string array.");
                }
                break;
            case "image-list":
            case "list":
                if (value is not IEnumerable<object?> || value is string)
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' must be an array.");
                }
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

                if (field.Options.Count > 0 &&
                    !field.Options.Contains(stringValue, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Section setting '{field.Key}' for section type '{sectionType}' has unsupported value '{stringValue}'.");
                }
                break;
        }
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
                    field.Options ?? Array.Empty<string>(),
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
        IReadOnlyList<string>? Options,
        string RendererConsumer,
        string VisualEffect);
}
