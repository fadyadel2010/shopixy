using System.Text.Json;

namespace PointOfSale.API.DTOs.CommerceHub;

public class CommerceHubProductsQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public bool? InStock { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Search { get; set; }
    public string? Sort { get; set; }
}

public class CommerceHubSearchQuery
{
    public string? Q { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public bool? InStock { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Filters { get; set; }
}

public class CommerceHubSearchSuggestionsQuery
{
    public string? Q { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public int Limit { get; set; } = 8;
}

public class CommerceHubSearchAutocompleteQuery
{
    public string? Q { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? TenantId { get; set; }
    public int ProductLimit { get; set; } = 5;
    public int CategoryLimit { get; set; } = 3;
    public int SuggestionLimit { get; set; } = 5;
    public int PopularLimit { get; set; } = 5;
}

public class CommerceHubPopularSearchesQuery
{
    public int Days { get; set; } = 7;
    public int Limit { get; set; } = 10;
}

public class CommerceHubSearchClickRequest
{
    public string? Query { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public int Position { get; set; }
    public string? SessionId { get; set; }
    public Guid? TenantId { get; set; }
}

public class CommerceHubSearchImpressionRequest
{
    public string? Query { get; set; }
    public List<Guid> ProductIds { get; set; } = new();
    public string? SessionId { get; set; }
    public DateTime? Timestamp { get; set; }
    public Guid? TenantId { get; set; }
}

public class CommerceHubSearchFilterEventRequest
{
    public string FilterKey { get; set; } = string.Empty;
    public string FilterValue { get; set; } = string.Empty;
    public string? Query { get; set; }
    public string? SessionId { get; set; }
    public DateTime? Timestamp { get; set; }
    public Guid? TenantId { get; set; }
}

public class CommerceHubProductDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public decimal StoreShippingFee { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Image { get; set; }
    public decimal Price { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryPath { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool InStock { get; set; }
    public decimal AvailableStock { get; set; }
    public decimal TotalStock { get; set; }
    public bool IsPublished { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
}

public class CommerceHubProductVariantAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CommerceHubProductVariantDto
{
    public Guid Id { get; set; }
    public string? SKU { get; set; }
    public decimal Price { get; set; }
    public decimal? OnlinePrice { get; set; }
    public decimal EffectivePrice => (OnlinePrice.HasValue && OnlinePrice.Value > 0) ? OnlinePrice.Value : Price;
    public Guid? MediaId { get; set; }
    public string? ImageUrl { get; set; }
    public int TotalStock { get; set; }
    public bool InStock => TotalStock > 0;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public List<CommerceHubProductVariantAttributeDto> Attributes { get; set; } = new();
}

public class CommerceHubVariantResolutionRequest
{
    public Dictionary<string, string> SelectedOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CommerceHubVariantResolutionResponse
{
    public Guid ProductId { get; set; }
    public Guid ListingId { get; set; }
    public Guid ResolvedVariantId { get; set; }
    public CommerceHubProductVariantDto Variant { get; set; } = new();
}

public class CommerceHubProductMediaDto
{
    public Guid Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public int Position { get; set; }
}

public class CommerceHubProductSeoDto
{
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}

public class CommerceHubProductFaqDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public class CommerceHubProductDetailDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public decimal StoreShippingFee { get; set; }
    public Guid ProductId { get; set; }
    public Guid InitialVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Image { get; set; }
    public decimal Price { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryPath { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool InStock { get; set; }
    public int StockQuantity { get; set; }
    public bool IsPublished { get; set; }
    public bool IsAvailable { get; set; }
    public bool HasVariants { get; set; }
    public Guid? DefaultVariantId { get; set; }
    public List<string> Features { get; set; } = new();
    public List<CommerceHubProductFaqDto> Faqs { get; set; } = new();
    public CommerceHubProductSeoDto Seo { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public bool AiGenerated { get; set; }
    public JsonElement? AiMetadata { get; set; }
    public List<CommerceHubProductMediaDto> Media { get; set; } = new();
    public List<string> Images { get; set; } = new();
    public List<CommerceHubProductVariantDto> Variants { get; set; } = new();
}

public class CommerceHubProductListResponse
{
    public List<CommerceHubProductDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
}

public class CommerceHubSearchResponse
{
    public List<CommerceHubProductDto> Items { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string Intent { get; set; } = "unknown";
    public CommerceHubSearchFacetsDto Facets { get; set; } = new();
    public Dictionary<string, CommerceHubAggregationDto> Aggregations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CommerceHubZeroResultSuggestionsDto? ZeroResultSuggestions { get; set; }
}

public class CommerceHubSearchFacetsDto
{
    public List<CommerceHubSearchFacetCategoryDto> Categories { get; set; } = new();
    public CommerceHubFacetRangeDto Price { get; set; } = new();
    public Dictionary<string, CommerceHubAttributeFacetDto> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CommerceHubSearchFacetCategoryDto
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public long Count { get; set; }
    public List<CommerceHubSearchFacetCategoryDto> Children { get; set; } = new();
}

public class CommerceHubFacetRangeDto
{
    public double? Min { get; set; }
    public double? Max { get; set; }
}

public class CommerceHubAttributeFacetDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public long ValueCount { get; set; }
    public List<CommerceHubAggregationBucketDto> Values { get; set; } = new();
    public CommerceHubFacetRangeDto? Range { get; set; }
}

public class CommerceHubAggregationDto
{
    public string Type { get; set; } = string.Empty;
    public List<CommerceHubAggregationBucketDto> Values { get; set; } = new();
    public CommerceHubAggregationStatsDto? Stats { get; set; }
}

public class CommerceHubAggregationBucketDto
{
    public string Value { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class CommerceHubAggregationStatsDto
{
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Avg { get; set; }
    public long Count { get; set; }
}

public class CommerceHubSearchSuggestionsResponse
{
    public string Query { get; set; } = string.Empty;
    public List<CommerceHubSearchSuggestionProductDto> Products { get; set; } = new();
    public List<string> PopularTerms { get; set; } = new();
    public List<string> SimilarTerms { get; set; } = new();
}

public class CommerceHubAutocompleteResponse
{
    public string Query { get; set; } = string.Empty;
    public string Intent { get; set; } = "unknown";
    public List<CommerceHubAutocompleteProductDto> Products { get; set; } = new();
    public List<CommerceHubAutocompleteCategoryDto> Categories { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
    public List<string> Popular { get; set; } = new();
}

public class CommerceHubAutocompleteProductDto
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

public class CommerceHubAutocompleteCategoryDto
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class CommerceHubPopularSearchesResponse
{
    public int Days { get; set; }
    public List<CommerceHubPopularSearchQueryDto> Items { get; set; } = new();
}

public class CommerceHubPopularSearchQueryDto
{
    public string Query { get; set; } = string.Empty;
    public long SearchCount { get; set; }
    public long ZeroResultCount { get; set; }
    public long ClickCount { get; set; }
    public double ClickThroughRate { get; set; }
}

public class CommerceHubZeroResultSuggestionsDto
{
    public List<CommerceHubSearchSuggestionProductDto> Products { get; set; } = new();
    public List<string> PopularTerms { get; set; } = new();
    public List<string> SimilarTerms { get; set; } = new();
    public List<CommerceHubSearchCategorySuggestionDto> Categories { get; set; } = new();
    public Dictionary<string, CommerceHubAggregationDto> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CommerceHubSearchSuggestionProductDto
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

public class CommerceHubSearchCategorySuggestionDto
{
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public long Count { get; set; }
}
