using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Core.DTOs.MarketplaceReviews;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.MarketplaceReviews;

public interface IMarketplaceProductReviewService
{
    Task<MarketplaceProductReviewDto> CreateAsync(
        Guid customerId,
        MarketplaceCreateProductReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<MarketplaceProductReviewDto> UpdateAsync(
        Guid customerId,
        Guid reviewId,
        MarketplaceUpdateProductReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid customerId,
        Guid reviewId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<MarketplaceProductReviewPageDto> GetApprovedReviewsAsync(
        Guid productId,
        MarketplaceProductReviewsQuery query,
        CancellationToken cancellationToken = default);

    Task<MarketplaceProductRatingSummaryDto> GetRatingSummaryAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<MarketplaceCustomerProductReviewContextDto> GetCustomerReviewContextAsync(
        Guid productId,
        Guid? customerId,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceProductReviewService : IMarketplaceProductReviewService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceEventOutboxService _eventOutbox;
    private readonly ILogger<MarketplaceProductReviewService> _logger;

    public MarketplaceProductReviewService(
        ApplicationDbContext dbContext,
        IMarketplaceEventOutboxService eventOutbox,
        ILogger<MarketplaceProductReviewService> logger)
    {
        _dbContext = dbContext;
        _eventOutbox = eventOutbox;
        _logger = logger;
    }

    public async Task<MarketplaceProductReviewDto> CreateAsync(
        Guid customerId,
        MarketplaceCreateProductReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new MarketplaceProductReviewException("INVALID_REQUEST", "Review request is required.", StatusCodes.Status400BadRequest);
        }

        var customer = await GetCustomerAsync(customerId, cancellationToken);
        await EnsureMarketplaceProductIsPublishedAsync(request.ProductId, cancellationToken);

        var hasVerifiedPurchase = await HasVerifiedPurchaseAsync(customerId, request.ProductId, cancellationToken);
        if (!hasVerifiedPurchase)
        {
            throw new MarketplaceProductReviewException(
                "PURCHASE_REQUIRED",
                "Only customers with a delivered purchase can review this product.",
                StatusCodes.Status403Forbidden);
        }

        var duplicateExists = await _dbContext.ProductReviews
            .AnyAsync(
                review => !review.IsDeleted &&
                          review.ProductId == request.ProductId &&
                          review.UserId == customerId,
                cancellationToken);

        if (duplicateExists)
        {
            throw new MarketplaceProductReviewException(
                "REVIEW_ALREADY_EXISTS",
                "You have already reviewed this product.",
                StatusCodes.Status409Conflict);
        }

        var review = ProductReview.Create(
            request.ProductId,
            customerId,
            request.Rating,
            request.Title,
            request.Comment,
            hasVerifiedPurchase);

        _dbContext.ProductReviews.Add(review);

        await _eventOutbox.EnqueueAsync(
            new ReviewCreatedEvent
            {
                CorrelationId = correlationId,
                EntityId = review.Id,
                ProductId = review.ProductId,
                UserId = review.UserId,
                Rating = review.Rating,
                Title = review.Title,
                IsVerifiedPurchase = review.IsVerifiedPurchase,
                Status = review.Status
            },
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new MarketplaceProductReviewException(
                "REVIEW_ALREADY_EXISTS",
                "You have already reviewed this product.",
                StatusCodes.Status409Conflict);
        }

        _logger.LogInformation(
            "Marketplace product review created | ReviewId: {ReviewId} | ProductId: {ProductId} | CustomerId: {CustomerId} | VerifiedPurchase: {VerifiedPurchase} | CorrelationId: {CorrelationId}",
            review.Id,
            review.ProductId,
            review.UserId,
            review.IsVerifiedPurchase,
            correlationId);

        return MapReview(review, customer.Name);
    }

    public async Task<MarketplaceProductReviewDto> UpdateAsync(
        Guid customerId,
        Guid reviewId,
        MarketplaceUpdateProductReviewRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new MarketplaceProductReviewException("INVALID_REQUEST", "Review request is required.", StatusCodes.Status400BadRequest);
        }

        var customer = await GetCustomerAsync(customerId, cancellationToken);
        var review = await _dbContext.ProductReviews
            .FirstOrDefaultAsync(
                item => !item.IsDeleted &&
                        item.Id == reviewId &&
                        item.UserId == customerId,
                cancellationToken);

        if (review == null)
        {
            throw new MarketplaceProductReviewException("REVIEW_NOT_FOUND", "Review was not found.", StatusCodes.Status404NotFound);
        }

        var previousStatus = review.Status;
        var hasVerifiedPurchase = await HasVerifiedPurchaseAsync(customerId, review.ProductId, cancellationToken);
        var changed = review.Update(
            request.Rating,
            request.Title,
            request.Comment,
            hasVerifiedPurchase);

        if (!changed)
        {
            return MapReview(review, customer.Name);
        }

        await _eventOutbox.EnqueueAsync(
            new ReviewUpdatedEvent
            {
                CorrelationId = correlationId,
                EntityId = review.Id,
                ProductId = review.ProductId,
                UserId = review.UserId,
                Rating = review.Rating,
                Title = review.Title,
                IsVerifiedPurchase = review.IsVerifiedPurchase,
                PreviousStatus = previousStatus
            },
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Marketplace product review updated | ReviewId: {ReviewId} | ProductId: {ProductId} | CustomerId: {CustomerId} | PreviousStatus: {PreviousStatus} | CorrelationId: {CorrelationId}",
            review.Id,
            review.ProductId,
            review.UserId,
            previousStatus,
            correlationId);

        return MapReview(review, customer.Name);
    }

    public async Task DeleteAsync(
        Guid customerId,
        Guid reviewId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var review = await _dbContext.ProductReviews
            .FirstOrDefaultAsync(
                item => !item.IsDeleted &&
                        item.Id == reviewId &&
                        item.UserId == customerId,
                cancellationToken);

        if (review == null)
        {
            throw new MarketplaceProductReviewException("REVIEW_NOT_FOUND", "Review was not found.", StatusCodes.Status404NotFound);
        }

        var previousStatus = review.Status;
        if (!review.Delete())
        {
            return;
        }

        await _eventOutbox.EnqueueAsync(
            new ReviewDeletedEvent
            {
                CorrelationId = correlationId,
                EntityId = review.Id,
                ProductId = review.ProductId,
                UserId = review.UserId,
                PreviousStatus = previousStatus
            },
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Marketplace product review deleted | ReviewId: {ReviewId} | ProductId: {ProductId} | CustomerId: {CustomerId} | PreviousStatus: {PreviousStatus} | CorrelationId: {CorrelationId}",
            review.Id,
            review.ProductId,
            review.UserId,
            previousStatus,
            correlationId);
    }

    public async Task<MarketplaceProductReviewPageDto> GetApprovedReviewsAsync(
        Guid productId,
        MarketplaceProductReviewsQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new MarketplaceProductReviewsQuery();

        if (!await IsMarketplaceProductPublishedAsync(productId, cancellationToken))
        {
            return CreateEmptyReviewPage(query);
        }

        var reviewsQuery = _dbContext.ProductReviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review =>
                !review.IsDeleted &&
                review.ProductId == productId &&
                review.Status == ProductReviewStatus.Approved);

        if (query.VerifiedOnly)
        {
            reviewsQuery = reviewsQuery.Where(review => review.IsVerifiedPurchase);
        }

        if (query.WithCommentsOnly)
        {
            reviewsQuery = reviewsQuery.Where(review => review.Comment != string.Empty);
        }

        var totalCount = await reviewsQuery.CountAsync(cancellationToken);
        var page = query.Page <= 0 ? MarketplaceProductReviewsQuery.DefaultPage : query.Page;
        var pageSize = query.PageSize <= 0
            ? MarketplaceProductReviewsQuery.DefaultPageSize
            : Math.Min(query.PageSize, 100);

        var reviews = await ApplyReviewSort(reviewsQuery, query.NormalizeSort())
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new MarketplaceProductReviewPageDto
        {
            Items = reviews.Select(review => MapReview(review, review.User?.Name)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<MarketplaceProductRatingSummaryDto> GetRatingSummaryAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await BuildSqlRatingSummaryAsync(productId, cancellationToken);
    }

    public async Task<MarketplaceCustomerProductReviewContextDto> GetCustomerReviewContextAsync(
        Guid productId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        await EnsureMarketplaceProductIsPublishedAsync(productId, cancellationToken);

        if (!customerId.HasValue)
        {
            return new MarketplaceCustomerProductReviewContextDto
            {
                ProductId = productId,
                IsAuthenticated = false,
                HasVerifiedPurchase = false,
                HasExistingReview = false,
                CanSubmitReview = false,
                CanEditReview = false,
                EligibilityMessage = "Sign in to write a review."
            };
        }

        var customer = await _dbContext.CommerceHubCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == customerId.Value, cancellationToken);

        if (customer == null)
        {
            return new MarketplaceCustomerProductReviewContextDto
            {
                ProductId = productId,
                IsAuthenticated = false,
                HasVerifiedPurchase = false,
                HasExistingReview = false,
                CanSubmitReview = false,
                CanEditReview = false,
                EligibilityMessage = "Sign in to write a review."
            };
        }

        var hasVerifiedPurchase = await HasVerifiedPurchaseAsync(customerId.Value, productId, cancellationToken);
        var existingReview = await _dbContext.ProductReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                review => !review.IsDeleted &&
                          review.ProductId == productId &&
                          review.UserId == customerId.Value,
                cancellationToken);

        var hasExistingReview = existingReview != null;

        return new MarketplaceCustomerProductReviewContextDto
        {
            ProductId = productId,
            IsAuthenticated = true,
            HasVerifiedPurchase = hasVerifiedPurchase,
            HasExistingReview = hasExistingReview,
            CanSubmitReview = hasVerifiedPurchase && !hasExistingReview,
            CanEditReview = hasExistingReview,
            EligibilityMessage = BuildEligibilityMessage(hasVerifiedPurchase, existingReview),
            ExistingReview = existingReview == null ? null : MapReview(existingReview, customer.Name)
        };
    }

    private async Task<CommerceHubCustomer> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.CommerceHubCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == customerId, cancellationToken);

        if (customer == null)
        {
            throw new MarketplaceProductReviewException(
                "CUSTOMER_NOT_FOUND",
                "Customer account was not found.",
                StatusCodes.Status404NotFound);
        }

        return customer;
    }

    private async Task EnsureMarketplaceProductIsPublishedAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            throw new MarketplaceProductReviewException("PRODUCT_REQUIRED", "ProductId is required.", StatusCodes.Status400BadRequest);
        }

        var isPublished = await IsMarketplaceProductPublishedAsync(productId, cancellationToken);

        if (!isPublished)
        {
            throw new MarketplaceProductReviewException(
                "PRODUCT_NOT_FOUND",
                "Marketplace product was not found.",
                StatusCodes.Status404NotFound);
        }
    }

    private async Task<bool> IsMarketplaceProductPublishedAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                listing => !listing.IsDeleted &&
                           listing.ProductId == productId &&
                           listing.IsPublished,
                cancellationToken);
    }

    private async Task<bool> HasVerifiedPurchaseAsync(Guid customerId, Guid productId, CancellationToken cancellationToken)
    {
        return await (from item in _dbContext.CommerceHubSubOrderItems.AsNoTracking()
                      join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking()
                          on item.SubOrderId equals subOrder.Id
                      join order in _dbContext.CommerceHubMarketplaceOrders.AsNoTracking()
                          on subOrder.MarketplaceOrderId equals order.Id
                      where !item.IsDeleted
                            && !subOrder.IsDeleted
                            && !order.IsDeleted
                            && order.CustomerId == customerId
                            && item.ProductId == productId
                            && (item.Status == CommerceHubOrderItemStatus.Delivered || item.DeliveredAt != null)
                      select item.Id)
            .AnyAsync(cancellationToken);
    }

    private static IQueryable<ProductReview> ApplyReviewSort(IQueryable<ProductReview> query, string sort)
    {
        return sort switch
        {
            "highest" => query.OrderByDescending(item => item.Rating).ThenByDescending(item => item.CreatedAt),
            "lowest" => query.OrderBy(item => item.Rating).ThenByDescending(item => item.CreatedAt),
            _ => query.OrderByDescending(item => item.CreatedAt)
        };
    }

    private async Task<MarketplaceProductRatingSummaryDto> BuildSqlRatingSummaryAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        if (!await IsMarketplaceProductPublishedAsync(productId, cancellationToken))
        {
            return CreateEmptySummary(productId);
        }

        var reviews = await _dbContext.ProductReviews
            .AsNoTracking()
            .Where(review =>
                !review.IsDeleted &&
                review.ProductId == productId &&
                review.Status == ProductReviewStatus.Approved)
            .Select(review => new { review.Rating, review.CreatedAt, review.UpdatedAt })
            .ToListAsync(cancellationToken);

        var breakdown = new Dictionary<int, int>();
        for (var rating = 1; rating <= 5; rating++)
        {
            breakdown[rating] = reviews.Count(item => item.Rating == rating);
        }

        return new MarketplaceProductRatingSummaryDto
        {
            ProductId = productId,
            AverageRating = reviews.Count == 0
                ? 0m
                : Math.Round(reviews.Average(item => (decimal)item.Rating), 2, MidpointRounding.AwayFromZero),
            TotalReviews = reviews.Count,
            RatingBreakdown = breakdown,
            LastUpdatedAtUtc = reviews.Count == 0
                ? DateTime.UtcNow
                : reviews.Max(review => review.UpdatedAt ?? review.CreatedAt)
        };
    }

    private static MarketplaceProductReviewDto MapReview(ProductReview review, string? reviewerDisplayName)
    {
        return new MarketplaceProductReviewDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            UserId = review.UserId,
            ReviewerDisplayName = reviewerDisplayName,
            Rating = review.Rating,
            Title = review.Title,
            Comment = review.Comment,
            IsVerifiedPurchase = review.IsVerifiedPurchase,
            Status = review.Status,
            CreatedAtUtc = review.CreatedAt,
            UpdatedAtUtc = review.UpdatedAt
        };
    }

    private static MarketplaceProductRatingSummaryDto CreateEmptySummary(Guid productId)
    {
        return new MarketplaceProductRatingSummaryDto
        {
            ProductId = productId,
            AverageRating = 0m,
            TotalReviews = 0,
            RatingBreakdown = EnsureRatingBreakdown(null),
            LastUpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static MarketplaceProductReviewPageDto CreateEmptyReviewPage(MarketplaceProductReviewsQuery query)
    {
        return new MarketplaceProductReviewPageDto
        {
            Page = query.Page <= 0 ? MarketplaceProductReviewsQuery.DefaultPage : query.Page,
            PageSize = query.PageSize <= 0 ? MarketplaceProductReviewsQuery.DefaultPageSize : Math.Min(query.PageSize, 100)
        };
    }

    private static string BuildEligibilityMessage(bool hasVerifiedPurchase, ProductReview? existingReview)
    {
        if (existingReview != null)
        {
            return existingReview.Status switch
            {
                ProductReviewStatus.Pending => "Your review is awaiting approval. You can still edit it if needed.",
                ProductReviewStatus.Rejected => "Your review was rejected. Update it to send it back for moderation.",
                _ => "You have already reviewed this product. You can edit or delete your review any time."
            };
        }

        return hasVerifiedPurchase
            ? "You can review this product because your purchase was delivered."
            : "Only customers with a delivered purchase can review this product.";
    }

    private static Dictionary<int, int> EnsureRatingBreakdown(Dictionary<int, int>? source)
    {
        var result = new Dictionary<int, int>();
        for (var rating = 1; rating <= 5; rating++)
        {
            result[rating] = source != null && source.TryGetValue(rating, out var count) ? count : 0;
        }

        return result;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
    }

}
