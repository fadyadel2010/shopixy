using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.DTOs.MarketplaceReviews;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminProductReviewService
{
    Task<SuperAdminProductReviewPageDto> GetReviewsAsync(
        SuperAdminProductReviewQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminProductReviewDetailsDto?> GetReviewDetailsAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminProductReviewDetailsDto> ApproveAsync(
        Guid reviewId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminProductReviewDetailsDto> RejectAsync(
        Guid reviewId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminProductReviewService : ISuperAdminProductReviewService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceEventOutboxService _eventOutbox;

    public SuperAdminProductReviewService(
        ApplicationDbContext dbContext,
        IMarketplaceEventOutboxService eventOutbox)
    {
        _dbContext = dbContext;
        _eventOutbox = eventOutbox;
    }

    public async Task<SuperAdminProductReviewPageDto> GetReviewsAsync(
        SuperAdminProductReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.ProductReviews
            .AsNoTracking()
            .Where(review => !review.IsDeleted);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(review => review.Status == query.Status.Value);
        }

        if (query.Rating.HasValue)
        {
            baseQuery = baseQuery.Where(review => review.Rating == query.Rating.Value);
        }

        if (query.ProductId.HasValue)
        {
            baseQuery = baseQuery.Where(review => review.ProductId == query.ProductId.Value);
        }

        if (query.UserId.HasValue)
        {
            baseQuery = baseQuery.Where(review => review.UserId == query.UserId.Value);
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var page = query.Page <= 0 ? SuperAdminProductReviewQuery.DefaultPage : query.Page;
        var pageSize = query.PageSize <= 0
            ? SuperAdminProductReviewQuery.DefaultPageSize
            : Math.Min(query.PageSize, 100);

        var reviews = await baseQuery
            .OrderByDescending(review => review.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var productNames = await GetProductNamesAsync(reviews.Select(review => review.ProductId).Distinct().ToList(), cancellationToken);
        var customers = await GetCustomersAsync(reviews.Select(review => review.UserId).Distinct().ToList(), cancellationToken);

        return new SuperAdminProductReviewPageDto
        {
            Items = reviews.Select(review => MapListItem(review, productNames, customers)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<SuperAdminProductReviewDetailsDto?> GetReviewDetailsAsync(
        Guid reviewId,
        CancellationToken cancellationToken = default)
    {
        var review = await _dbContext.ProductReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == reviewId, cancellationToken);

        return review == null
            ? null
            : await BuildDetailsAsync(review, cancellationToken);
    }

    public async Task<SuperAdminProductReviewDetailsDto> ApproveAsync(
        Guid reviewId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var review = await _dbContext.ProductReviews
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == reviewId, cancellationToken);

        if (review == null)
        {
            throw new MarketplaceProductReviewException(
                "REVIEW_NOT_FOUND",
                "Review was not found.",
                StatusCodes.Status404NotFound);
        }

        var previousStatus = review.Status;
        if (review.Approve())
        {
            await _eventOutbox.EnqueueAsync(
                new ReviewApprovedEvent
                {
                    CorrelationId = correlationId,
                    EntityId = review.Id,
                    ProductId = review.ProductId,
                    UserId = review.UserId,
                    Rating = review.Rating,
                    IsVerifiedPurchase = review.IsVerifiedPurchase,
                    PreviousStatus = previousStatus,
                    ApprovedAtUtc = DateTime.UtcNow
                },
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await BuildDetailsAsync(review, cancellationToken);
    }

    public async Task<SuperAdminProductReviewDetailsDto> RejectAsync(
        Guid reviewId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var review = await _dbContext.ProductReviews
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == reviewId, cancellationToken);

        if (review == null)
        {
            throw new MarketplaceProductReviewException(
                "REVIEW_NOT_FOUND",
                "Review was not found.",
                StatusCodes.Status404NotFound);
        }

        var previousStatus = review.Status;
        if (review.Reject())
        {
            await _eventOutbox.EnqueueAsync(
                new ReviewRejectedEvent
                {
                    CorrelationId = correlationId,
                    EntityId = review.Id,
                    ProductId = review.ProductId,
                    UserId = review.UserId,
                    PreviousStatus = previousStatus,
                    RejectedAtUtc = DateTime.UtcNow
                },
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await BuildDetailsAsync(review, cancellationToken);
    }

    private async Task<SuperAdminProductReviewDetailsDto> BuildDetailsAsync(
        ProductReview review,
        CancellationToken cancellationToken)
    {
        var productNames = await GetProductNamesAsync([review.ProductId], cancellationToken);
        var customers = await GetCustomersAsync([review.UserId], cancellationToken);
        var history = await BuildUserHistoryAsync(review.UserId, cancellationToken);

        customers.TryGetValue(review.UserId, out var customer);

        return new SuperAdminProductReviewDetailsDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            ProductName = productNames.TryGetValue(review.ProductId, out var productName) ? productName : "Unknown Product",
            UserId = review.UserId,
            CustomerName = customer?.Name ?? "Unknown Customer",
            CustomerEmail = customer?.Email ?? string.Empty,
            Rating = review.Rating,
            Title = review.Title,
            Comment = review.Comment,
            IsVerifiedPurchase = review.IsVerifiedPurchase,
            Status = review.Status,
            CreatedAtUtc = review.CreatedAt,
            UpdatedAtUtc = review.UpdatedAt,
            UserHistory = history
        };
    }

    private async Task<SuperAdminCustomerReviewHistoryDto> BuildUserHistoryAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var customer = await _dbContext.CommerceHubCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == userId, cancellationToken);

        var reviews = await _dbContext.ProductReviews
            .AsNoTracking()
            .Where(review => !review.IsDeleted && review.UserId == userId)
            .OrderByDescending(review => review.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        var allStatusCounts = await _dbContext.ProductReviews
            .AsNoTracking()
            .Where(review => !review.IsDeleted && review.UserId == userId)
            .GroupBy(review => review.Status)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var productNames = await GetProductNamesAsync(reviews.Select(review => review.ProductId).Distinct().ToList(), cancellationToken);

        return new SuperAdminCustomerReviewHistoryDto
        {
            UserId = userId,
            CustomerName = customer?.Name ?? "Unknown Customer",
            CustomerEmail = customer?.Email ?? string.Empty,
            TotalReviews = allStatusCounts.Sum(item => item.Count),
            ApprovedReviews = allStatusCounts.FirstOrDefault(item => item.Key == ProductReviewStatus.Approved)?.Count ?? 0,
            PendingReviews = allStatusCounts.FirstOrDefault(item => item.Key == ProductReviewStatus.Pending)?.Count ?? 0,
            RejectedReviews = allStatusCounts.FirstOrDefault(item => item.Key == ProductReviewStatus.Rejected)?.Count ?? 0,
            RecentReviews = reviews.Select(review => new SuperAdminCustomerReviewHistoryEntryDto
            {
                ReviewId = review.Id,
                ProductId = review.ProductId,
                ProductName = productNames.TryGetValue(review.ProductId, out var productName) ? productName : "Unknown Product",
                Rating = review.Rating,
                Status = review.Status,
                IsVerifiedPurchase = review.IsVerifiedPurchase,
                CreatedAtUtc = review.CreatedAt,
                UpdatedAtUtc = review.UpdatedAt
            }).ToList()
        };
    }

    private async Task<Dictionary<Guid, string>> GetProductNamesAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _dbContext.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(product => !product.IsDeleted && productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, product => product.Name, cancellationToken);
    }

    private async Task<Dictionary<Guid, CommerceHubCustomer>> GetCustomersAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, CommerceHubCustomer>();
        }

        return await _dbContext.CommerceHubCustomers
            .AsNoTracking()
            .Where(customer => !customer.IsDeleted && customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, cancellationToken);
    }

    private static SuperAdminProductReviewListItemDto MapListItem(
        ProductReview review,
        IReadOnlyDictionary<Guid, string> productNames,
        IReadOnlyDictionary<Guid, CommerceHubCustomer> customers)
    {
        customers.TryGetValue(review.UserId, out var customer);

        return new SuperAdminProductReviewListItemDto
        {
            Id = review.Id,
            ProductId = review.ProductId,
            ProductName = productNames.TryGetValue(review.ProductId, out var productName) ? productName : "Unknown Product",
            UserId = review.UserId,
            CustomerName = customer?.Name ?? "Unknown Customer",
            CustomerEmail = customer?.Email ?? string.Empty,
            Rating = review.Rating,
            Title = review.Title,
            Comment = review.Comment,
            IsVerifiedPurchase = review.IsVerifiedPurchase,
            Status = review.Status,
            CreatedAtUtc = review.CreatedAt,
            UpdatedAtUtc = review.UpdatedAt
        };
    }
}
