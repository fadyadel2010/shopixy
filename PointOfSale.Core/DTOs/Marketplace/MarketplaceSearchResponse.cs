namespace PointOfSale.Core.DTOs.Marketplace;

public record MarketplaceSearchResponse
{
    public List<MarketplaceServiceResult> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public record MarketplaceServiceResult
{
    public Guid ServiceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? CategoryId { get; init; }
    public Guid ProviderProfileId { get; init; }
    public string ProviderDisplayName { get; init; } = string.Empty;
    public decimal AverageRating { get; init; }
    public int TotalReviews { get; init; }
    public string TrustBadge { get; init; } = string.Empty;
    public int TotalRequests { get; init; }
    public int CompletedRequests { get; init; }
    public decimal CompletionRate { get; init; }
    public DateTime CreatedAt { get; init; }
    // Phase FE-3
    public decimal? StartingPrice { get; init; }
    public string? PricingType { get; init; }
    public string? Currency { get; init; }
    public string? ServiceImages { get; init; }
}
