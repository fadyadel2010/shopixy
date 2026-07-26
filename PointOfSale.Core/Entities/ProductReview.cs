using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class ProductReview : BaseEntity
{
    private const int MinRating = 1;
    private const int MaxRating = 5;
    private const int MaxTitleLength = 200;
    private const int MaxCommentLength = 4000;

    public Guid ProductId { get; private set; }
    public Guid UserId { get; private set; }
    public int Rating { get; private set; }
    public string? Title { get; private set; }
    public string Comment { get; private set; } = string.Empty;
    public bool IsVerifiedPurchase { get; private set; }
    public ProductReviewStatus Status { get; private set; } = ProductReviewStatus.Pending;

    public Product? Product { get; private set; }
    public CommerceHubCustomer? User { get; private set; }

    private ProductReview()
    {
    }

    public static ProductReview Create(
        Guid productId,
        Guid userId,
        int rating,
        string? title,
        string comment,
        bool isVerifiedPurchase)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId is required.", nameof(productId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        ValidateRating(rating);

        return new ProductReview
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            UserId = userId,
            Rating = rating,
            Title = NormalizeTitle(title),
            Comment = NormalizeComment(comment),
            IsVerifiedPurchase = isVerifiedPurchase,
            Status = ProductReviewStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool Update(
        int rating,
        string? title,
        string comment,
        bool isVerifiedPurchase)
    {
        EnsureNotDeleted();
        ValidateRating(rating);

        var normalizedTitle = NormalizeTitle(title);
        var normalizedComment = NormalizeComment(comment);

        var changed =
            Rating != rating ||
            !string.Equals(Title, normalizedTitle, StringComparison.Ordinal) ||
            !string.Equals(Comment, normalizedComment, StringComparison.Ordinal) ||
            IsVerifiedPurchase != isVerifiedPurchase;

        if (!changed)
        {
            return false;
        }

        Rating = rating;
        Title = normalizedTitle;
        Comment = normalizedComment;
        IsVerifiedPurchase = isVerifiedPurchase;
        Status = ProductReviewStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool Approve()
    {
        EnsureNotDeleted();

        if (Status == ProductReviewStatus.Approved)
        {
            return false;
        }

        Status = ProductReviewStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool Reject()
    {
        EnsureNotDeleted();

        if (Status == ProductReviewStatus.Rejected)
        {
            return false;
        }

        Status = ProductReviewStatus.Rejected;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool MarkPending()
    {
        EnsureNotDeleted();

        if (Status == ProductReviewStatus.Pending)
        {
            return false;
        }

        Status = ProductReviewStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool Delete()
    {
        if (IsDeleted)
        {
            return false;
        }

        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("Deleted reviews cannot be modified.");
        }
    }

    private static void ValidateRating(int rating)
    {
        if (rating < MinRating || rating > MaxRating)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), $"Rating must be between {MinRating} and {MaxRating}.");
        }
    }

    private static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = title.Trim();
        if (normalized.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title cannot exceed {MaxTitleLength} characters.", nameof(title));
        }

        return normalized;
    }

    private static string NormalizeComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException("Comment is required.", nameof(comment));
        }

        var normalized = comment.Trim();
        if (normalized.Length > MaxCommentLength)
        {
            throw new ArgumentException($"Comment cannot exceed {MaxCommentLength} characters.", nameof(comment));
        }

        return normalized;
    }
}
