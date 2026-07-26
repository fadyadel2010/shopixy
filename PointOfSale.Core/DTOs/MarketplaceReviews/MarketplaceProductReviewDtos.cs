using PointOfSale.Core.Enums;

namespace PointOfSale.Core.DTOs.MarketplaceReviews;

public sealed class MarketplaceCreateProductReviewRequest
{
    public Guid ProductId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed class MarketplaceUpdateProductReviewRequest
{
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed class MarketplaceProductReviewsQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;

    public int Page { get; set; } = DefaultPage;
    public int PageSize { get; set; } = DefaultPageSize;
    public string Sort { get; set; } = "latest";
    public bool VerifiedOnly { get; set; }
    public bool WithCommentsOnly { get; set; }

    public string? Validate()
    {
        if (Page <= 0)
        {
            return "Page must be greater than zero.";
        }

        if (PageSize <= 0 || PageSize > 100)
        {
            return "PageSize must be between 1 and 100.";
        }

        if (!IsSupportedSort(Sort))
        {
            return "Sort must be one of: latest, highest, lowest.";
        }

        return null;
    }

    public string NormalizeSort() => NormalizeSort(Sort);

    public static string NormalizeSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort)
            ? "latest"
            : sort.Trim().ToLowerInvariant();

        return normalized switch
        {
            "highest" => "highest",
            "lowest" => "lowest",
            _ => "latest"
        };
    }

    private static bool IsSupportedSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return true;
        }

        return sort.Trim().Equals("latest", StringComparison.OrdinalIgnoreCase)
            || sort.Trim().Equals("highest", StringComparison.OrdinalIgnoreCase)
            || sort.Trim().Equals("lowest", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MarketplaceProductReviewDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public string? ReviewerDisplayName { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }
    public ProductReviewStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class MarketplaceProductReviewPageDto
{
    public List<MarketplaceProductReviewDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public sealed class MarketplaceProductRatingSummaryDto
{
    public Guid ProductId { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public Dictionary<int, int> RatingBreakdown { get; set; } = new();
    public DateTime LastUpdatedAtUtc { get; set; }
}

public sealed class MarketplaceCustomerProductReviewContextDto
{
    public Guid ProductId { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool HasVerifiedPurchase { get; set; }
    public bool HasExistingReview { get; set; }
    public bool CanSubmitReview { get; set; }
    public bool CanEditReview { get; set; }
    public string? EligibilityMessage { get; set; }
    public MarketplaceProductReviewDto? ExistingReview { get; set; }
}
