using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.CommerceHub.Services;

/// <summary>
/// Production-grade attribute validation and normalization for CommerceHub listings.
/// 
/// Responsibilities:
/// 1. Key normalization: "Screen Size (inches)" → "screen_size_inches"
/// 2. Value normalization: lowercase select values for search consistency
/// 3. Backend validation: required fields, type checking, option matching
/// 4. Schema snapshot: captures attribute schema at publish time
/// 5. Numeric enforcement: ensures number fields store actual doubles
/// 6. Option normalization: compares case-insensitively, stores lowercased
/// 
/// ES Indexing Contract:
/// - All attributes stored under flat "attributes" object
/// - strings → keyword type
/// - numbers → double type
/// - No nested objects, no arrays
/// </summary>
public interface ICommerceHubAttributeService
{
    /// <summary>
    /// Validates and normalizes attributes against the category's attribute schema.
    /// Returns (normalizedJson, errors). If errors is non-empty, validation failed.
    /// </summary>
    Task<AttributeValidationResult> ValidateAndNormalizeAsync(
        Guid categoryId,
        string? rawAttributesJson,
        CancellationToken cancellationToken = default);
}

public class AttributeValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public string? NormalizedJson { get; set; }
    public List<string> Errors { get; set; } = new();
}

public partial class CommerceHubAttributeService : ICommerceHubAttributeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubAttributeService> _logger;

    public CommerceHubAttributeService(
        ApplicationDbContext dbContext,
        ILogger<CommerceHubAttributeService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AttributeValidationResult> ValidateAndNormalizeAsync(
        Guid categoryId,
        string? rawAttributesJson,
        CancellationToken cancellationToken = default)
    {
        var result = new AttributeValidationResult();

        if (!await CommerceHubCategoryLineageAuthority.IsLiveAsync(_dbContext, categoryId, cancellationToken))
        {
            result.Errors.Add("CommerceHub category is not currently available for attribute validation.");
            return result;
        }

        // Load attribute definitions for this category
        var attributeDefs = await _dbContext.CommerceHubCategoryAttributes
            .Where(a => a.CategoryId == categoryId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(cancellationToken);

        // If no attribute definitions exist for this category, any attributes are ignored
        if (attributeDefs.Count == 0)
        {
            result.NormalizedJson = null;
            return result;
        }

        // Parse raw input
        Dictionary<string, JsonElement> rawAttributes;
        if (string.IsNullOrWhiteSpace(rawAttributesJson))
        {
            rawAttributes = new Dictionary<string, JsonElement>();
        }
        else
        {
            try
            {
                rawAttributes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawAttributesJson)
                    ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"Invalid attributes JSON: {ex.Message}");
                return result;
            }
        }

        // Build a case-insensitive lookup from raw attributes
        var rawLookup = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in rawAttributes)
        {
            rawLookup[kvp.Key] = kvp.Value;
        }

        // Validate and normalize
        var normalized = new Dictionary<string, object>();

        foreach (var def in attributeDefs)
        {
            var normalizedKey = NormalizeKey(def.Name);

            // Try to find the value by original name or normalized key
            JsonElement rawValue = default;
            bool found = rawLookup.TryGetValue(def.Name, out rawValue)
                      || rawLookup.TryGetValue(normalizedKey, out rawValue);

            if (!found || rawValue.ValueKind == JsonValueKind.Null ||
                (rawValue.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(rawValue.GetString())))
            {
                if (def.IsRequired)
                {
                    result.Errors.Add($"Required attribute \"{def.Name}\" is missing.");
                }
                continue;
            }

            // Type validation + normalization
            switch (def.Type.ToLowerInvariant())
            {
                case "select":
                    var selectResult = ValidateSelectValue(def, rawValue);
                    if (selectResult.Error != null)
                    {
                        result.Errors.Add(selectResult.Error);
                    }
                    else if (selectResult.Value != null)
                    {
                        normalized[normalizedKey] = selectResult.Value;
                    }
                    break;

                case "number":
                    var numResult = ConvertToTypedValue(rawValue, "number");
                    if (numResult.Error != null)
                    {
                        result.Errors.Add($"\"{def.Name}\" {numResult.Error}");
                    }
                    else if (numResult.Value != null)
                    {
                        normalized[normalizedKey] = numResult.Value;
                    }
                    break;

                case "text":
                default:
                    var textResult = ConvertToTypedValue(rawValue, "text");
                    if (textResult.Value != null)
                    {
                        normalized[normalizedKey] = textResult.Value;
                    }
                    else if (def.IsRequired)
                    {
                        result.Errors.Add($"\"{def.Name}\" is required but empty.");
                    }
                    break;
            }
        }

        // Fail closed for unknown attributes so merchant payloads cannot introduce
        // unsupported filter/search metadata outside the current SQL schema.
        var knownKeys = attributeDefs.Select(d => NormalizeKey(d.Name)).ToHashSet();
        foreach (var key in rawLookup.Keys)
        {
            var normalizedInputKey = NormalizeKey(key);
            if (!knownKeys.Contains(normalizedInputKey))
            {
                _logger.LogWarning(
                    "Unknown attribute key \"{Key}\" submitted for category {CategoryId} — ignored",
                    key, categoryId);
                result.Errors.Add($"Attribute \"{key}\" is not supported for this CommerceHub category.");
            }
        }

        if (result.Errors.Count > 0)
        {
            return result;
        }

        result.NormalizedJson = normalized.Count > 0
            ? JsonSerializer.Serialize(normalized, JsonOptions)
            : null;

        return result;
    }

    // ═══════════════════════════════════════════════════════════
    // REUSABLE HELPER UTILITIES
    // ═══════════════════════════════════════════════════════════

    // ─── 1. NormalizeKey ─────────────────────────────────────
    // "Screen Size (inches)" → "screen_size_inches"
    // "Brand" → "brand"
    // "RAM (GB)" → "ram_gb"

    /// <summary>
    /// Normalizes an attribute key to snake_case for search/filter consistency.
    /// Rules: lowercase, spaces/hyphens → underscore, strip special chars.
    /// </summary>
    public static string NormalizeKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        // Lowercase
        var key = name.Trim().ToLowerInvariant();

        // Replace spaces, hyphens, dots with underscores
        key = Regex.Replace(key, @"[\s\-\.]+", "_");

        // Remove non-alphanumeric except underscores
        key = Regex.Replace(key, @"[^a-z0-9_]", "");

        // Collapse multiple underscores
        key = Regex.Replace(key, @"_{2,}", "_");

        // Trim leading/trailing underscores
        key = key.Trim('_');

        return key;
    }

    // ─── 2. NormalizeValue ───────────────────────────────────
    /// <summary>
    /// Normalizes an attribute value for search consistency.
    /// Strings → lowercase, trimmed. Numbers → preserved as-is.
    /// </summary>
    public static string NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Trim().ToLowerInvariant();
    }

    // ─── 3. ValidateSelectValue ─────────────────────────────
    /// <summary>
    /// Validates a select-type attribute against allowed options.
    /// Returns normalized lowercase value or error.
    /// </summary>
    private static (string? Value, string? Error) ValidateSelectValue(
        CommerceHubCategoryAttribute def, JsonElement rawValue)
    {
        var selectValue = rawValue.ValueKind == JsonValueKind.String
            ? rawValue.GetString()?.Trim()
            : rawValue.ToString().Trim();

        if (string.IsNullOrEmpty(selectValue))
        {
            if (def.IsRequired)
                return (null, $"\"{def.Name}\" requires a value.");
            return (null, null);
        }

        // Validate against allowed options (case-insensitive)
        if (!string.IsNullOrEmpty(def.OptionsJson))
        {
            var options = JsonSerializer.Deserialize<List<string>>(def.OptionsJson) ?? new();
            var matched = options.FirstOrDefault(o =>
                string.Equals(o, selectValue, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                return (null,
                    $"\"{def.Name}\" value \"{selectValue}\" is not valid. " +
                    $"Allowed: {string.Join(", ", options)}");
            }

            // Store the canonical matched value (lowercased for ES keyword match)
            return (matched.ToLowerInvariant(), null);
        }

        return (selectValue.ToLowerInvariant(), null);
    }

    // ─── 4. ConvertToTypedValue ─────────────────────────────
    /// <summary>
    /// Converts a raw JSON value to the correct typed value for storage.
    /// Number types: ensures actual double (not string).
    /// Text types: ensures lowercase string.
    /// </summary>
    public static (object? Value, string? Error) ConvertToTypedValue(JsonElement rawValue, string type)
    {
        switch (type.ToLowerInvariant())
        {
            case "number":
                if (rawValue.ValueKind == JsonValueKind.Number)
                {
                    return (rawValue.GetDouble(), null);
                }
                if (rawValue.ValueKind == JsonValueKind.String &&
                    double.TryParse(rawValue.GetString(), out var numValue))
                {
                    return (numValue, null);
                }
                return (null, $"must be a number. Got: \"{rawValue}\"");

            case "text":
            case "select":
            default:
                var textValue = rawValue.ValueKind == JsonValueKind.String
                    ? rawValue.GetString()?.Trim()
                    : rawValue.ToString().Trim();

                if (!string.IsNullOrEmpty(textValue))
                {
                    return (textValue.ToLowerInvariant(), null);
                }
                return (null, null);
        }
    }

    // ─── 5. ValidateOptions ─────────────────────────────────
    /// <summary>
    /// Validates that a value exists in the allowed options list (case-insensitive).
    /// Returns the canonical (original-case) matched option or null.
    /// </summary>
    public static string? ValidateOption(string? value, string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(optionsJson))
            return null;

        var options = JsonSerializer.Deserialize<List<string>>(optionsJson) ?? new();
        return options.FirstOrDefault(o =>
            string.Equals(o, value, StringComparison.OrdinalIgnoreCase));
    }

    // ─── 6. NormalizeOptionsForDisplay ───────────────────────
    /// <summary>
    /// Returns options normalized for search (lowercase).
    /// Use for ES aggregation bucket matching.
    /// </summary>
    public static List<string> NormalizeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return new();

        var options = JsonSerializer.Deserialize<List<string>>(optionsJson) ?? new();
        return options.Select(o => o.Trim().ToLowerInvariant()).Distinct().ToList();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
