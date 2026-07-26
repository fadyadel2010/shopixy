using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Core.DTOs.Marketplace;

/// <summary>
/// Search request for marketplace services.
/// Phase DS-1: Hardened with validation, max query length, and sort whitelist.
/// </summary>
public sealed record MarketplaceSearchRequest
{
    [StringLength(200, ErrorMessage = "Query must not exceed 200 characters")]
    public string? Query { get; init; }

    /// <summary>
    /// Category filter — exact match on service category (e.g., "Marketing", "Design").
    /// Maps to Elasticsearch keyword field CategoryId.
    /// </summary>
    [StringLength(100)]
    public string? Category { get; init; }

    // Legacy alias — kept for backward compatibility. Prefer Category.
    [StringLength(100)]
    public string? CategoryId { get; init; }

    [Range(typeof(decimal), "0", "5")]
    public decimal? MinRating { get; init; }

    [Range(0, int.MaxValue)]
    public int? MinCompletedRequests { get; init; }

    [RegularExpression("^(New|Rising|Trusted|TopRated|Standard)$")]
    public string? TrustBadge { get; init; }

    [Range(1, 100000, ErrorMessage = "Page must be between 1 and 100000")]
    public int Page { get; init; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100")]
    public int PageSize { get; init; } = 20;

    [RegularExpression(
        "^(relevance|rating|newest|price_low|price_high)$",
        ErrorMessage = "Sort is invalid")]
    public string Sort { get; init; } = "relevance";

    /// <summary>
    /// Resolves the effective category — prefers Category, falls back to CategoryId.
    /// </summary>
    public string? EffectiveCategory => !string.IsNullOrWhiteSpace(Category) ? Category.Trim() : CategoryId;
}
