using PointOfSale.Core.Enums;

namespace PointOfSale.Core.DTOs.MarketplaceReviews;

public sealed class SuperAdminProductReviewQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;

    public int Page { get; set; } = DefaultPage;
    public int PageSize { get; set; } = DefaultPageSize;
    public ProductReviewStatus? Status { get; set; }
    public int? Rating { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? UserId { get; set; }

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

        if (Rating.HasValue && (Rating.Value < 1 || Rating.Value > 5))
        {
            return "Rating filter must be between 1 and 5.";
        }

        return null;
    }
}

public sealed class SuperAdminProductReviewListItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }
    public ProductReviewStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class SuperAdminProductReviewPageDto
{
    public List<SuperAdminProductReviewListItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public sealed class SuperAdminCustomerReviewHistoryDto
{
    public Guid UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int TotalReviews { get; set; }
    public int ApprovedReviews { get; set; }
    public int PendingReviews { get; set; }
    public int RejectedReviews { get; set; }
    public List<SuperAdminCustomerReviewHistoryEntryDto> RecentReviews { get; set; } = new();
}

public sealed class SuperAdminCustomerReviewHistoryEntryDto
{
    public Guid ReviewId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public ProductReviewStatus Status { get; set; }
    public bool IsVerifiedPurchase { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class SuperAdminProductReviewDetailsDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsVerifiedPurchase { get; set; }
    public ProductReviewStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public SuperAdminCustomerReviewHistoryDto UserHistory { get; set; } = new();
}

public sealed class SuperAdminReviewModerationRequest
{
    public string? Reason { get; set; }
}
