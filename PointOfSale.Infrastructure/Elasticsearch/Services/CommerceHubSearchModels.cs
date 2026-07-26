using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public sealed class CommerceHubSearchRequest
{
    public string? Query { get; set; }
    public IReadOnlyCollection<string> QueryExpansions { get; set; } = Array.Empty<string>();
    public CommerceHubSearchIntentContext Intent { get; set; } = CommerceHubSearchIntentContext.Unknown();
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public bool? InStock { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public IReadOnlyCollection<Guid> ExcludedTenantIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyCollection<CommerceHubSearchFilter> Filters { get; set; } = Array.Empty<CommerceHubSearchFilter>();
    public IReadOnlyCollection<CommerceHubSearchFacetDefinition> FacetDefinitions { get; set; } = Array.Empty<CommerceHubSearchFacetDefinition>();
}

public sealed class CommerceHubSearchSuggestionsRequest
{
    public string? Query { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public int Limit { get; set; } = 8;
    public IReadOnlyCollection<Guid> ExcludedTenantIds { get; set; } = Array.Empty<Guid>();
}

public sealed class CommerceHubSearchAutocompleteRequest
{
    public string? Query { get; set; }
    public IReadOnlyCollection<string> QueryExpansions { get; set; } = Array.Empty<string>();
    public CommerceHubSearchIntentContext Intent { get; set; } = CommerceHubSearchIntentContext.Unknown();
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public IReadOnlyCollection<Guid> ExcludedTenantIds { get; set; } = Array.Empty<Guid>();
    public int ProductLimit { get; set; } = 5;
    public int CategoryLimit { get; set; } = 3;
    public int SuggestionLimit { get; set; } = 5;
}

public enum CommerceHubSearchIntentType
{
    Unknown = 0,
    Product = 1,
    Category = 2,
    Price = 3
}

public sealed class CommerceHubSearchIntentContext
{
    public CommerceHubSearchIntentType Type { get; set; } = CommerceHubSearchIntentType.Unknown;
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public IReadOnlyCollection<string> CategoryTerms { get; set; } = Array.Empty<string>();

    public static CommerceHubSearchIntentContext Unknown() => new();
}

public sealed class CommerceHubSearchFilter
{
    public string Key { get; set; } = string.Empty;
    public IReadOnlyCollection<string> Terms { get; set; } = Array.Empty<string>();
    public double? ExactNumber { get; set; }
    public bool? ExactBoolean { get; set; }
    public double? GreaterThan { get; set; }
    public double? GreaterThanOrEqualTo { get; set; }
    public double? LessThan { get; set; }
    public double? LessThanOrEqualTo { get; set; }

    public bool HasRange =>
        GreaterThan.HasValue ||
        GreaterThanOrEqualTo.HasValue ||
        LessThan.HasValue ||
        LessThanOrEqualTo.HasValue;
}

public sealed class CommerceHubSearchFacetDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public int SortOrder { get; set; }

    public bool IsNumeric =>
        string.Equals(Type, "number", StringComparison.OrdinalIgnoreCase);
}

public sealed class CommerceHubSearchReadResult
{
    public List<CommerceHubProductDocument> Items { get; set; } = new();
    public long TotalCount { get; set; }
    public Dictionary<string, CommerceHubSearchAggregationResult> Aggregations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CommerceHubSearchCategoryBucket> CategoryBuckets { get; set; } = new();
    public CommerceHubSearchStatsResult? PriceStats { get; set; }
    public CommerceHubZeroResultSuggestionsResult? ZeroResultSuggestions { get; set; }
}

public sealed class CommerceHubSearchAggregationResult
{
    public string Type { get; set; } = string.Empty;
    public List<CommerceHubSearchAggregationBucket> Buckets { get; set; } = new();
    public CommerceHubSearchStatsResult? Stats { get; set; }
}

public sealed class CommerceHubSearchAggregationBucket
{
    public string Value { get; set; } = string.Empty;
    public long Count { get; set; }
}

public sealed class CommerceHubSearchCategoryBucket
{
    public Guid CategoryId { get; set; }
    public long Count { get; set; }
}

public sealed class CommerceHubSearchStatsResult
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Avg { get; set; }
    public long Count { get; set; }
}

public sealed class CommerceHubSearchSuggestionsResult
{
    public string Query { get; set; } = string.Empty;
    public List<CommerceHubSearchSuggestionProduct> Products { get; set; } = new();
    public List<string> PopularTerms { get; set; } = new();
    public List<string> SimilarTerms { get; set; } = new();
}

public sealed class CommerceHubSearchAutocompleteResult
{
    public string Query { get; set; } = string.Empty;
    public List<CommerceHubSearchAutocompleteProduct> Products { get; set; } = new();
    public List<CommerceHubSearchAutocompleteCategoryBucket> Categories { get; set; } = new();
    public List<string> SuggestedTerms { get; set; } = new();
}

public sealed class CommerceHubZeroResultSuggestionsResult
{
    public List<CommerceHubSearchSuggestionProduct> Products { get; set; } = new();
    public List<string> PopularTerms { get; set; } = new();
    public List<string> SimilarTerms { get; set; } = new();
    public List<CommerceHubSearchCategorySuggestion> Categories { get; set; } = new();
    public Dictionary<string, CommerceHubSearchAggregationResult> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CommerceHubSearchSuggestionProduct
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Image { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string StoreName { get; set; } = string.Empty;
}

public sealed class CommerceHubSearchAutocompleteProduct
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductNameHighlight { get; set; }
    public string? Image { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EGP";
    public string StoreName { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryNameHighlight { get; set; }
}

public sealed class CommerceHubSearchAutocompleteCategoryBucket
{
    public Guid CategoryId { get; set; }
    public long Count { get; set; }
}

public sealed class CommerceHubSearchCategorySuggestion
{
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public long Count { get; set; }
}
