using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

public static partial class CommerceHubSearchDocumentMapper
{
    public static CommerceHubProductSearchDocument Map(CommerceHubProductDocument document)
    {
        return new CommerceHubProductSearchDocument
        {
            Id = document.Id,
            TenantId = document.TenantId,
            StoreId = document.TenantId,
            ProductId = document.ProductId,
            VariantId = document.VariantId,
            ProductName = document.ProductName,
            ProductNameNormalized = NormalizeText(document.ProductName),
            Description = string.IsNullOrWhiteSpace(document.Description) ? null : document.Description.Trim(),
            CategoryId = document.CategoryId,
            CategoryName = string.IsNullOrWhiteSpace(document.CategoryName) ? null : document.CategoryName.Trim(),
            CategoryPath = BuildCategoryPath(document.CategoryPath),
            Price = Convert.ToDouble(document.Price, CultureInfo.InvariantCulture),
            PriceSource = string.IsNullOrWhiteSpace(document.PriceSource)
                ? "ProductVariantEffectiveOnlinePrice"
                : document.PriceSource.Trim(),
            OverridePresent = document.OverridePresent,
            Currency = string.IsNullOrWhiteSpace(document.Currency)
                ? "EGP"
                : document.Currency.Trim().ToUpperInvariant(),
            InStock = document.InStock,
            TotalStock = Convert.ToDouble(document.AvailableStock, CultureInfo.InvariantCulture),
            Rating = Convert.ToDouble(document.AverageRating, CultureInfo.InvariantCulture),
            ReviewCount = document.ReviewCount,
            PopularityScore = document.PopularityScore,
            LastClickAtUtc = document.LastClickAtUtc,
            StoreName = document.StoreName,
            Attributes = MapAttributes(document.Attributes),
            IsPublished = document.IsPublished,
            CreatedAtUtc = document.CreatedAtUtc,
            UpdatedAtUtc = document.UpdatedAtUtc
        };
    }

    public static Dictionary<string, object> MapAttributes(BsonDocument? attributes)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (attributes == null || attributes.ElementCount == 0)
        {
            return result;
        }

        foreach (var element in attributes.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Name))
            {
                continue;
            }

            var value = ConvertBsonValue(element.Value);
            if (value != null)
            {
                result[element.Name] = value;
            }
        }

        return result;
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = MultiWhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
        return normalized;
    }

    public static List<string> BuildCategoryPath(string? categoryPath)
    {
        if (string.IsNullOrWhiteSpace(categoryPath))
        {
            return new List<string>();
        }

        return categoryPath
            .Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => NormalizeCategorySegment(segment))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeCategorySegment(string value)
    {
        var normalized = NormalizeText(value);
        normalized = normalized.Replace(" ", "_", StringComparison.Ordinal);
        return normalized;
    }

    private static object? ConvertBsonValue(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.String => value.AsString.Trim().ToLowerInvariant(),
            BsonType.Int32 => Convert.ToDouble(value.AsInt32, CultureInfo.InvariantCulture),
            BsonType.Int64 => Convert.ToDouble(value.AsInt64, CultureInfo.InvariantCulture),
            BsonType.Double => value.AsDouble,
            BsonType.Decimal128 => Convert.ToDouble(value.AsDecimal, CultureInfo.InvariantCulture),
            BsonType.Boolean => value.AsBoolean,
            _ => value.ToString()
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespaceRegex();
}
