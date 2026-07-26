using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Services.MarketplaceReviews;

public interface IMarketplaceProductReviewModerationService
{
    Task HandleCreatedAsync(ReviewCreatedEvent @event, CancellationToken cancellationToken = default);
    Task HandleUpdatedAsync(ReviewUpdatedEvent @event, CancellationToken cancellationToken = default);
}

public interface IMarketplaceProductReviewProjectionService
{
    Task HandleApprovedAsync(ReviewApprovedEvent @event, CancellationToken cancellationToken = default);
    Task HandleRejectedAsync(ReviewRejectedEvent @event, CancellationToken cancellationToken = default);
    Task HandleUpdatedAsync(ReviewUpdatedEvent @event, CancellationToken cancellationToken = default);
    Task HandleDeletedAsync(ReviewDeletedEvent @event, CancellationToken cancellationToken = default);
}

public sealed class MarketplaceProductReviewModerationService : IMarketplaceProductReviewModerationService
{
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceEventOutboxService _eventOutbox;
    private readonly ILogger<MarketplaceProductReviewModerationService> _logger;

    public MarketplaceProductReviewModerationService(
        ApplicationDbContext dbContext,
        IMarketplaceEventOutboxService eventOutbox,
        ILogger<MarketplaceProductReviewModerationService> logger)
    {
        _dbContext = dbContext;
        _eventOutbox = eventOutbox;
        _logger = logger;
    }

    public Task HandleCreatedAsync(ReviewCreatedEvent @event, CancellationToken cancellationToken = default)
        => ModerateAsync(@event.EntityId, @event.CorrelationId, cancellationToken);

    public Task HandleUpdatedAsync(ReviewUpdatedEvent @event, CancellationToken cancellationToken = default)
        => ModerateAsync(@event.EntityId, @event.CorrelationId, cancellationToken);

    private async Task ModerateAsync(Guid reviewId, string correlationId, CancellationToken cancellationToken)
    {
        var review = await _dbContext.ProductReviews
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == reviewId, cancellationToken);

        if (review == null)
        {
            _logger.LogWarning(
                "Marketplace review moderation skipped because the review no longer exists | ReviewId: {ReviewId} | CorrelationId: {CorrelationId}",
                reviewId,
                correlationId);
            return;
        }

        if (review.Status != ProductReviewStatus.Pending)
        {
            return;
        }

        var suspicious = await IsSuspiciousAsync(review, cancellationToken);
        if (!review.IsVerifiedPurchase || suspicious)
        {
            _logger.LogInformation(
                "Marketplace review left pending for manual moderation | ReviewId: {ReviewId} | VerifiedPurchase: {VerifiedPurchase} | Suspicious: {Suspicious} | CorrelationId: {CorrelationId}",
                review.Id,
                review.IsVerifiedPurchase,
                suspicious,
                correlationId);
            return;
        }

        var previousStatus = review.Status;
        if (!review.Approve())
        {
            return;
        }

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

        _logger.LogInformation(
            "Marketplace review auto-approved | ReviewId: {ReviewId} | ProductId: {ProductId} | UserId: {UserId} | CorrelationId: {CorrelationId}",
            review.Id,
            review.ProductId,
            review.UserId,
            correlationId);
    }

    private async Task<bool> IsSuspiciousAsync(ProductReview review, CancellationToken cancellationToken)
    {
        var burstWindowStartUtc = DateTime.UtcNow.AddMinutes(-10);
        var burstCount = await _dbContext.ProductReviews
            .AsNoTracking()
            .CountAsync(
                item => !item.IsDeleted &&
                        item.Id != review.Id &&
                        item.UserId == review.UserId &&
                        item.CreatedAt >= burstWindowStartUtc,
                cancellationToken);

        if (burstCount >= 3)
        {
            return true;
        }

        var fingerprint = BuildFingerprint(review.Title, review.Comment);
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        var recentReviews = await _dbContext.ProductReviews
            .AsNoTracking()
            .Where(
                item => !item.IsDeleted &&
                        item.Id != review.Id &&
                        item.UserId == review.UserId &&
                        item.CreatedAt >= DateTime.UtcNow.AddDays(-30))
            .Select(item => new { item.ProductId, item.Title, item.Comment })
            .ToListAsync(cancellationToken);

        return recentReviews.Any(item =>
            item.ProductId != review.ProductId &&
            string.Equals(BuildFingerprint(item.Title, item.Comment), fingerprint, StringComparison.Ordinal));
    }

    private static string BuildFingerprint(string? title, string comment)
    {
        var raw = $"{title ?? string.Empty} {comment}";
        var normalized = MultiWhitespaceRegex.Replace(raw.Trim().ToLowerInvariant(), " ");
        normalized = normalized.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("!", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal);

        return normalized;
    }
}

public sealed class MarketplaceProductReviewProjectionService : IMarketplaceProductReviewProjectionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ILogger<MarketplaceProductReviewProjectionService> _logger;

    public MarketplaceProductReviewProjectionService(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        ICommerceHubSearchIndexService searchIndexService,
        ILogger<MarketplaceProductReviewProjectionService> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _searchIndexService = searchIndexService;
        _logger = logger;
    }

    public async Task HandleApprovedAsync(ReviewApprovedEvent @event, CancellationToken cancellationToken = default)
    {
        var review = await _dbContext.ProductReviews
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Id == @event.EntityId, cancellationToken);

        if (review == null || review.Status != ProductReviewStatus.Approved)
        {
            await RemoveReviewDocumentAsync(@event.EntityId, cancellationToken);
            await RebuildAggregatesAsync(@event.ProductId, @event.EventId, cancellationToken);
            return;
        }

        await UpsertReviewDocumentAsync(review, cancellationToken);
        await RebuildAggregatesAsync(@event.ProductId, @event.EventId, cancellationToken);
    }

    public async Task HandleRejectedAsync(ReviewRejectedEvent @event, CancellationToken cancellationToken = default)
    {
        await ReconcileReviewProjectionAsync(@event.EntityId, @event.ProductId, @event.EventId, cancellationToken);
    }

    public async Task HandleUpdatedAsync(ReviewUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        await ReconcileReviewProjectionAsync(@event.EntityId, @event.ProductId, @event.EventId, cancellationToken);
    }

    public async Task HandleDeletedAsync(ReviewDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        await ReconcileReviewProjectionAsync(@event.EntityId, @event.ProductId, @event.EventId, cancellationToken);
    }

    private async Task ReconcileReviewProjectionAsync(
        Guid reviewId,
        Guid eventProductId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var review = await _dbContext.ProductReviews
            .AsNoTracking()
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == reviewId, cancellationToken);

        var productId = review?.ProductId ?? eventProductId;
        if (review is { IsDeleted: false, Status: ProductReviewStatus.Approved })
        {
            await UpsertReviewDocumentAsync(review, cancellationToken);
        }
        else
        {
            await RemoveReviewDocumentAsync(reviewId, cancellationToken);
        }

        await RebuildAggregatesAsync(productId, eventId, cancellationToken);
    }

    private async Task UpsertReviewDocumentAsync(ProductReview review, CancellationToken cancellationToken)
    {
        var document = new MarketplaceProductReviewDocument
        {
            Id = review.Id,
            ProductId = review.ProductId,
            UserId = review.UserId,
            ReviewerDisplayName = review.User?.Name,
            Rating = review.Rating,
            Title = review.Title,
            Comment = review.Comment,
            IsVerifiedPurchase = review.IsVerifiedPurchase,
            CreatedAtUtc = review.CreatedAt,
            UpdatedAtUtc = review.UpdatedAt
        };

        await _mongoDbContext.MarketplaceProductReviews.ReplaceOneAsync(
            Builders<MarketplaceProductReviewDocument>.Filter.Eq(item => item.Id, review.Id),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    private async Task RemoveReviewDocumentAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        await _mongoDbContext.MarketplaceProductReviews.DeleteOneAsync(
            Builders<MarketplaceProductReviewDocument>.Filter.Eq(item => item.Id, reviewId),
            cancellationToken);
    }

    private async Task RebuildAggregatesAsync(Guid productId, Guid eventId, CancellationToken cancellationToken)
    {
        var summaryDocument = await BuildSummaryDocumentAsync(productId, cancellationToken);

        await _mongoDbContext.ProductReviewSummaries.ReplaceOneAsync(
            Builders<ProductReviewSummaryDocument>.Filter.Eq(item => item.ProductId, productId),
            summaryDocument,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

        var listings = await UpdateCommerceHubListingsAsync(productId, summaryDocument, eventId, cancellationToken);
        await SyncSearchAsync(listings, cancellationToken);
    }

    private async Task<ProductReviewSummaryDocument> BuildSummaryDocumentAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var ratings = await _dbContext.ProductReviews
            .AsNoTracking()
            .Where(
                review => !review.IsDeleted &&
                          review.ProductId == productId &&
                          review.Status == ProductReviewStatus.Approved)
            .Select(review => review.Rating)
            .ToListAsync(cancellationToken);

        var breakdown = new Dictionary<int, int>();
        for (var rating = 1; rating <= 5; rating++)
        {
            breakdown[rating] = ratings.Count(item => item == rating);
        }

        var totalReviews = ratings.Count;
        var averageRating = totalReviews == 0
            ? 0m
            : Math.Round(ratings.Average(item => (decimal)item), 2, MidpointRounding.AwayFromZero);

        return new ProductReviewSummaryDocument
        {
            ProductId = productId,
            AverageRating = averageRating,
            TotalReviews = totalReviews,
            RatingBreakdown = breakdown,
            LastUpdatedAtUtc = DateTime.UtcNow
        };
    }

    private async Task<List<CommerceHubProductDocument>> UpdateCommerceHubListingsAsync(
        Guid productId,
        ProductReviewSummaryDocument summaryDocument,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CommerceHubProductDocument>.Filter.Eq(item => item.ProductId, productId);
        var update = Builders<CommerceHubProductDocument>.Update.Combine(
            Builders<CommerceHubProductDocument>.Update.Set(item => item.AverageRating, summaryDocument.AverageRating),
            Builders<CommerceHubProductDocument>.Update.Set(item => item.ReviewCount, summaryDocument.TotalReviews),
            Builders<CommerceHubProductDocument>.Update.Set(item => item.UpdatedAtUtc, DateTime.UtcNow),
            Builders<CommerceHubProductDocument>.Update.Set(item => item.LastEventId, eventId),
            Builders<CommerceHubProductDocument>.Update.Set(item => item.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending),
            Builders<CommerceHubProductDocument>.Update.Set(item => item.SearchLastError, null));

        await _mongoDbContext.CommerceHubProducts.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

        return await _mongoDbContext.CommerceHubProducts
            .Find(filter)
            .ToListAsync(cancellationToken);
    }

    private async Task SyncSearchAsync(
        IReadOnlyCollection<CommerceHubProductDocument> documents,
        CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            var attemptedAtUtc = DateTime.UtcNow;
            var succeeded = false;
            string? error = null;

            try
            {
                var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    document,
                    document.TenantId,
                    cancellationToken);

                succeeded = sqlAuthority == null
                    ? await _searchIndexService.DeleteAsync(
                        document.TenantId,
                        document.Id,
                        cancellationToken: cancellationToken)
                    : await _searchIndexService.IndexAsync(
                        CommerceHubSearchDocumentMapper.Map(sqlAuthority),
                        cancellationToken: cancellationToken);

                if (!succeeded)
                {
                    error = "Search index update returned false.";
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogError(
                    ex,
                    "Marketplace review search sync failed | ListingId: {ListingId} | ProductId: {ProductId}",
                    document.Id,
                    document.ProductId);
            }

            var updates = new List<UpdateDefinition<CommerceHubProductDocument>>
            {
                Builders<CommerceHubProductDocument>.Update.Set(item => item.SearchLastAttemptedAtUtc, attemptedAtUtc),
                Builders<CommerceHubProductDocument>.Update.Set(item => item.SearchLastError, succeeded ? null : error),
                Builders<CommerceHubProductDocument>.Update.Set(
                    item => item.SearchSyncStatus,
                    succeeded ? CommerceHubProductDocument.SearchSyncSynced : CommerceHubProductDocument.SearchSyncFailed)
            };

            if (succeeded)
            {
                updates.Add(Builders<CommerceHubProductDocument>.Update.Set(item => item.SearchLastSucceededAtUtc, attemptedAtUtc));
                updates.Add(Builders<CommerceHubProductDocument>.Update.Set(item => item.LastSyncedAt, attemptedAtUtc));
            }

            await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
                Builders<CommerceHubProductDocument>.Filter.And(
                    Builders<CommerceHubProductDocument>.Filter.Eq(item => item.Id, document.Id),
                    Builders<CommerceHubProductDocument>.Filter.Eq(item => item.TenantId, document.TenantId)),
                Builders<CommerceHubProductDocument>.Update.Combine(updates),
                cancellationToken: cancellationToken);
        }
    }
}
