using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.API.CommerceHub.Services;

public interface ICommerceHubSearchQueryParser
{
    IReadOnlyCollection<CommerceHubSearchFilter> Parse(IQueryCollection query, string? filtersJson);
}

public sealed class CommerceHubSearchQueryParser : ICommerceHubSearchQueryParser
{
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "q",
        "categoryId",
        "tenantId",
        "inStock",
        "minPrice",
        "maxPrice",
        "sort",
        "page",
        "pageSize",
        "filters",
        "lang"
    };

    private static readonly string[] RangeSuffixes = [".gte", ".gt", ".lte", ".lt", ".min", ".max", "_gte", "_gt", "_lte", "_lt", "_min", "_max"];

    public IReadOnlyCollection<CommerceHubSearchFilter> Parse(IQueryCollection query, string? filtersJson)
    {
        var builders = new Dictionary<string, FilterBuilder>(StringComparer.OrdinalIgnoreCase);

        ParseFiltersJson(filtersJson, builders);
        ParseQueryString(query, builders);

        return builders
            .Values
            .Select(builder => builder.Build())
            .Where(filter =>
                !string.IsNullOrWhiteSpace(filter.Key) && (
                    filter.Terms.Count > 0 ||
                    filter.ExactNumber.HasValue ||
                    filter.ExactBoolean.HasValue ||
                    filter.HasRange))
            .ToList();
    }

    private static void ParseFiltersJson(
        string? filtersJson,
        Dictionary<string, FilterBuilder> builders)
    {
        if (string.IsNullOrWhiteSpace(filtersJson))
        {
            return;
        }

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(filtersJson);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = CommerceHubAttributeService.NormalizeKey(property.Name);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var builder = GetOrCreateBuilder(builders, key);
                ApplyJsonValue(builder, property.Value);
            }
        }
    }

    private static void ParseQueryString(
        IQueryCollection query,
        Dictionary<string, FilterBuilder> builders)
    {
        foreach (var pair in query)
        {
            if (ReservedKeys.Contains(pair.Key))
            {
                continue;
            }

            if (TryParseRangeKey(pair.Key, out var rangeKey, out var rangeOperator))
            {
                if (TryParseDouble(pair.Value.FirstOrDefault(), out var numericValue))
                {
                    GetOrCreateBuilder(builders, rangeKey).SetRange(rangeOperator, numericValue);
                }

                continue;
            }

            var key = pair.Key.StartsWith("attr.", StringComparison.OrdinalIgnoreCase)
                ? pair.Key["attr.".Length..]
                : pair.Key;

            key = CommerceHubAttributeService.NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var builder = GetOrCreateBuilder(builders, key);
            foreach (var value in pair.Value)
            {
                ApplyStringValue(builder, value);
            }
        }
    }

    private static void ApplyJsonValue(FilterBuilder builder, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    var name = property.Name.Trim().ToLowerInvariant();
                    if (!TryParseDouble(property.Value, out var numericValue))
                    {
                        continue;
                    }

                    switch (name)
                    {
                        case "min":
                        case "gte":
                            builder.SetRange("gte", numericValue);
                            break;
                        case "gt":
                            builder.SetRange("gt", numericValue);
                            break;
                        case "max":
                        case "lte":
                            builder.SetRange("lte", numericValue);
                            break;
                        case "lt":
                            builder.SetRange("lt", numericValue);
                            break;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var element in value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        ApplyStringValue(builder, element.GetString());
                    }
                    else if (TryParseDouble(element, out var numericValue))
                    {
                        builder.AddNumber(numericValue);
                    }
                    else if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        builder.AddBoolean(element.GetBoolean());
                    }
                }
                break;

            case JsonValueKind.String:
                ApplyStringValue(builder, value.GetString());
                break;

            case JsonValueKind.Number:
                if (value.TryGetDouble(out var doubleValue))
                {
                    builder.AddNumber(doubleValue);
                }
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.AddBoolean(value.GetBoolean());
                break;
        }
    }

    private static void ApplyStringValue(FilterBuilder builder, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        var values = rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var value in values)
        {
            if (bool.TryParse(value, out var boolValue))
            {
                builder.AddBoolean(boolValue);
            }
            else if (TryParseDouble(value, out var numericValue))
            {
                builder.AddNumber(numericValue);
            }
            else
            {
                builder.AddTerm(CommerceHubAttributeService.NormalizeValue(value));
            }
        }
    }

    private static bool TryParseRangeKey(string key, out string normalizedKey, out string op)
    {
        normalizedKey = string.Empty;
        op = string.Empty;

        var workingKey = key.StartsWith("attr.", StringComparison.OrdinalIgnoreCase)
            ? key["attr.".Length..]
            : key;

        foreach (var suffix in RangeSuffixes)
        {
            if (!workingKey.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedKey = CommerceHubAttributeService.NormalizeKey(workingKey[..^suffix.Length]);
            op = suffix.ToLowerInvariant() switch
            {
                ".min" or "_min" or ".gte" or "_gte" => "gte",
                ".max" or "_max" or ".lte" or "_lte" => "lte",
                ".gt" or "_gt" => "gt",
                ".lt" or "_lt" => "lt",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(normalizedKey) && !string.IsNullOrWhiteSpace(op);
        }

        return false;
    }

    private static bool TryParseDouble(JsonElement value, out double numericValue)
    {
        numericValue = default;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out numericValue);
    }

    private static bool TryParseDouble(string? value, out double numericValue)
    {
        return double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out numericValue);
    }

    private static FilterBuilder GetOrCreateBuilder(
        Dictionary<string, FilterBuilder> builders,
        string key)
    {
        if (!builders.TryGetValue(key, out var builder))
        {
            builder = new FilterBuilder(key);
            builders[key] = builder;
        }

        return builder;
    }

    private sealed class FilterBuilder
    {
        private readonly HashSet<string> _terms = new(StringComparer.OrdinalIgnoreCase);
        private double? _number;
        private bool? _boolean;

        public FilterBuilder(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public double? GreaterThan { get; private set; }
        public double? GreaterThanOrEqualTo { get; private set; }
        public double? LessThan { get; private set; }
        public double? LessThanOrEqualTo { get; private set; }

        public void AddTerm(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _terms.Add(value);
            }
        }

        public void AddNumber(double value)
        {
            _number = value;
        }

        public void AddBoolean(bool value)
        {
            _boolean = value;
        }

        public void SetRange(string op, double value)
        {
            switch (op)
            {
                case "gt":
                    GreaterThan = value;
                    break;
                case "gte":
                    GreaterThanOrEqualTo = value;
                    break;
                case "lt":
                    LessThan = value;
                    break;
                case "lte":
                    LessThanOrEqualTo = value;
                    break;
            }
        }

        public CommerceHubSearchFilter Build()
        {
            return new CommerceHubSearchFilter
            {
                Key = Key,
                Terms = _terms.ToList(),
                ExactNumber = _number,
                ExactBoolean = _boolean,
                GreaterThan = GreaterThan,
                GreaterThanOrEqualTo = GreaterThanOrEqualTo,
                LessThan = LessThan,
                LessThanOrEqualTo = LessThanOrEqualTo
            };
        }
    }
}
