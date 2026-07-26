using System.Collections;
using System.Text.Json;
using PointOfSale.API.DTOs.Analytics;

namespace PointOfSale.API.Services.Analytics;

public interface IAnalyticsPayloadGuard
{
    AnalyticsPayloadInspectionResult Inspect(AnalyticsPayloadDto? payload);
}

public sealed record AnalyticsPayloadInspectionResult(
    bool IsValid,
    int SerializedSizeBytes,
    string Reason)
{
    public static AnalyticsPayloadInspectionResult Valid(int serializedSizeBytes) =>
        new(true, serializedSizeBytes, string.Empty);

    public static AnalyticsPayloadInspectionResult Invalid(int serializedSizeBytes, string reason) =>
        new(false, serializedSizeBytes, reason);
}

public sealed class AnalyticsPayloadGuard : IAnalyticsPayloadGuard
{
    private const int MaxPayloadBytes = 16 * 1024;
    private const int MaxSearchQueryLength = 500;
    private const int MaxProductNameLength = 500;
    private const int MaxProductSkuLength = 100;
    private const int MaxFilterKeyLength = 100;
    private const int MaxFilterValueLength = 500;
    private const int MaxFiltersCount = 20;
    private const int MaxExtraCount = 50;
    private const int MaxExtraDepth = 4;
    private const int MaxExtraNodes = 128;
    private const int MaxExtraCollectionItems = 25;
    private const int MaxExtraStringLength = 512;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AnalyticsPayloadInspectionResult Inspect(AnalyticsPayloadDto? payload)
    {
        if (payload == null)
        {
            return AnalyticsPayloadInspectionResult.Valid(0);
        }

        int serializedSizeBytes;
        try
        {
            serializedSizeBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions).Length;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return AnalyticsPayloadInspectionResult.Invalid(0, "payload_not_serializable");
        }

        if (serializedSizeBytes > MaxPayloadBytes)
        {
            return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "payload_size_exceeded");
        }

        if (payload.SearchQuery?.Length > MaxSearchQueryLength)
        {
            return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "search_query_too_long");
        }

        if (payload.ProductName?.Length > MaxProductNameLength)
        {
            return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "product_name_too_long");
        }

        if (payload.ProductSku?.Length > MaxProductSkuLength)
        {
            return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "product_sku_too_long");
        }

        if (payload.Filters?.Count > MaxFiltersCount)
        {
            return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "filters_count_exceeded");
        }

        if (payload.Filters != null)
        {
            foreach (var filter in payload.Filters)
            {
                if (string.IsNullOrWhiteSpace(filter.Key))
                {
                    return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "filter_key_missing");
                }

                if (filter.Key.Length > MaxFilterKeyLength)
                {
                    return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "filter_key_too_long");
                }

                if ((filter.Value?.Length ?? 0) > MaxFilterValueLength)
                {
                    return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "filter_value_too_long");
                }
            }
        }

        if (payload.Extra?.Count > MaxExtraCount)
        {
            return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "extra_field_count_exceeded");
        }

        if (payload.Extra == null || payload.Extra.Count == 0)
        {
            return AnalyticsPayloadInspectionResult.Valid(serializedSizeBytes);
        }

        var visitedNodes = 0;
        foreach (var entry in payload.Extra)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "extra_key_missing");
            }

            if (entry.Key.Length > MaxFilterKeyLength)
            {
                return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, "extra_key_too_long");
            }

            if (!TryValidateExtraValue(entry.Value, 1, ref visitedNodes, out var reason))
            {
                return AnalyticsPayloadInspectionResult.Invalid(serializedSizeBytes, reason);
            }
        }

        return AnalyticsPayloadInspectionResult.Valid(serializedSizeBytes);
    }

    private static bool TryValidateExtraValue(
        object? value,
        int depth,
        ref int visitedNodes,
        out string reason)
    {
        if (depth > MaxExtraDepth)
        {
            reason = "extra_depth_exceeded";
            return false;
        }

        visitedNodes++;
        if (visitedNodes > MaxExtraNodes)
        {
            reason = "extra_object_size_exceeded";
            return false;
        }

        switch (value)
        {
            case null:
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
            case Guid:
            case DateTime:
            case DateTimeOffset:
                reason = string.Empty;
                return true;

            case string s when s.Length <= MaxExtraStringLength:
                reason = string.Empty;
                return true;

            case string:
                reason = "extra_string_too_long";
                return false;

            case JsonElement jsonElement:
                return TryValidateJsonElement(jsonElement, depth, ref visitedNodes, out reason);

            case IDictionary<string, object?> dictionary:
                if (dictionary.Count > MaxExtraCollectionItems)
                {
                    reason = "extra_object_size_exceeded";
                    return false;
                }

                foreach (var pair in dictionary)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        reason = "extra_key_missing";
                        return false;
                    }

                    if (pair.Key.Length > MaxFilterKeyLength)
                    {
                        reason = "extra_key_too_long";
                        return false;
                    }

                    if (!TryValidateExtraValue(pair.Value, depth + 1, ref visitedNodes, out reason))
                    {
                        return false;
                    }
                }

                reason = string.Empty;
                return true;

            case IEnumerable sequence when value is not string:
                var count = 0;
                foreach (var item in sequence)
                {
                    count++;
                    if (count > MaxExtraCollectionItems)
                    {
                        reason = "extra_object_size_exceeded";
                        return false;
                    }

                    if (!TryValidateExtraValue(item, depth + 1, ref visitedNodes, out reason))
                    {
                        return false;
                    }
                }

                reason = string.Empty;
                return true;

            default:
                reason = "unsupported_extra_value_type";
                return false;
        }
    }

    private static bool TryValidateJsonElement(
        JsonElement element,
        int depth,
        ref int visitedNodes,
        out string reason)
    {
        // Enforce depth limit
        if (depth > MaxExtraDepth)
        {
            reason = "extra_depth_exceeded";
            return false;
        }

        // Note: visitedNodes is NOT incremented here because TryValidateExtraValue
        // already incremented it before delegating to this method. Incrementing
        // here would cause double-counting for JsonElement roots.

        // Check visited nodes counter (already incremented by caller)
        if (visitedNodes > MaxExtraNodes)
        {
            reason = "extra_object_size_exceeded";
            return false;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var propertyCount = 0;
                foreach (var property in element.EnumerateObject())
                {
                    propertyCount++;
                    if (propertyCount > MaxExtraCollectionItems)
                    {
                        reason = "extra_object_size_exceeded";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(property.Name))
                    {
                        reason = "extra_key_missing";
                        return false;
                    }

                    if (property.Name.Length > MaxFilterKeyLength)
                    {
                        reason = "extra_key_too_long";
                        return false;
                    }

                    // Increment for each child property value
                    visitedNodes++;
                    if (visitedNodes > MaxExtraNodes)
                    {
                        reason = "extra_object_size_exceeded";
                        return false;
                    }

                    if (!TryValidateJsonElement(property.Value, depth + 1, ref visitedNodes, out reason))
                    {
                        return false;
                    }
                }

                reason = string.Empty;
                return true;
            }

            case JsonValueKind.Array:
            {
                var count = 0;
                foreach (var item in element.EnumerateArray())
                {
                    count++;
                    if (count > MaxExtraCollectionItems)
                    {
                        reason = "extra_object_size_exceeded";
                        return false;
                    }

                    // Increment for each array item
                    visitedNodes++;
                    if (visitedNodes > MaxExtraNodes)
                    {
                        reason = "extra_object_size_exceeded";
                        return false;
                    }

                    if (!TryValidateJsonElement(item, depth + 1, ref visitedNodes, out reason))
                    {
                        return false;
                    }
                }

                reason = string.Empty;
                return true;
            }

            case JsonValueKind.String:
                reason = element.GetString()?.Length > MaxExtraStringLength
                    ? "extra_string_too_long"
                    : string.Empty;
                return string.IsNullOrEmpty(reason);

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                reason = string.Empty;
                return true;

            default:
                reason = "unsupported_extra_value_type";
                return false;
        }
    }
}
