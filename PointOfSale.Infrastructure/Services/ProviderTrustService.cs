using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Read-only service for provider trust queries.
/// Public trust authority is recomposed from SQL ServiceReview truth.
/// </summary>
public class ProviderTrustService : IProviderTrustService
{
    private readonly ApplicationDbContext _context;

    public ProviderTrustService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProviderTrustAggregate?> GetTrustAggregateAsync(
        Guid providerProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.Set<MarketplaceProfile>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(profile =>
                profile.Id == providerProfileId &&
                !profile.IsDeleted,
                cancellationToken);

        if (profile == null)
        {
            return null;
        }

        return await ComposeSqlTrustAggregateAsync(
            profile.TenantId,
            profile.Id,
            cancellationToken);
    }

    public async Task<TrustBadge> GetTrustBadgeAsync(
        Guid providerProfileId,
        CancellationToken cancellationToken = default)
    {
        var aggregate = await GetTrustAggregateAsync(providerProfileId, cancellationToken);
        return aggregate?.ComputeBadge() ?? TrustBadge.New;
    }

    public async Task<Dictionary<int, int>> GetRatingDistributionAsync(
        Guid providerProfileId,
        CancellationToken cancellationToken = default)
    {
        var aggregate = await GetTrustAggregateAsync(providerProfileId, cancellationToken);
        return aggregate?.GetRatingDistribution() ?? new Dictionary<int, int>
        {
            { 5, 0 },
            { 4, 0 },
            { 3, 0 },
            { 2, 0 },
            { 1, 0 }
        };
    }

    public async Task<IEnumerable<ProviderTrustAggregate>> GetTopRatedProvidersAsync(
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        var trustRows = await _context.Set<ServiceReview>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _context.Set<ServiceRequest>().AsNoTracking().IgnoreQueryFilters(),
                review => review.ServiceRequestId,
                request => request.Id,
                (review, request) => new { Review = review, Request = request })
            .Where(row =>
                !row.Review.IsDeleted &&
                !row.Request.IsDeleted &&
                row.Request.Status == ServiceRequestStatus.CompletedByBuyer &&
                row.Review.TenantId == row.Request.TenantId &&
                row.Review.ServiceId == row.Request.ServiceId &&
                row.Review.ReviewerUserId == row.Request.BuyerUserId &&
                row.Review.ProviderUserId == row.Request.ProviderUserId)
            .GroupBy(row => row.Request.ProviderProfileId)
            .Select(group => new
            {
                ProviderProfileId = group.Key,
                TotalReviews = group.Count(),
                SumRatings = group.Sum(row => row.Review.Rating)
            })
            .Where(row => row.TotalReviews >= 5)
            .ToListAsync(cancellationToken);

        var result = new List<ProviderTrustAggregate>();
        foreach (var row in trustRows
            .OrderByDescending(row => Math.Round((decimal)row.SumRatings / row.TotalReviews, 2))
            .ThenByDescending(row => row.TotalReviews)
            .Take(count))
        {
            var aggregate = await GetTrustAggregateAsync(row.ProviderProfileId, cancellationToken);
            if (aggregate != null)
            {
                result.Add(aggregate);
            }
        }

        return result;
    }

    private async Task<ProviderTrustAggregate> ComposeSqlTrustAggregateAsync(
        Guid tenantId,
        Guid providerProfileId,
        CancellationToken cancellationToken)
    {
        var reviews = await _context.Set<ServiceReview>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _context.Set<ServiceRequest>().AsNoTracking().IgnoreQueryFilters(),
                review => review.ServiceRequestId,
                request => request.Id,
                (review, request) => new { Review = review, Request = request })
            .GroupJoin(
                _context.Set<MarketingReviewEvidence>().AsNoTracking().IgnoreQueryFilters().Where(evidence => !evidence.IsDeleted),
                row => row.Review.Id,
                evidence => evidence.ServiceReviewId,
                (row, evidence) => new { row.Review, row.Request, Evidence = evidence.FirstOrDefault() })
            .Where(row =>
                !row.Review.IsDeleted &&
                !row.Request.IsDeleted &&
                row.Request.ProviderProfileId == providerProfileId &&
                row.Request.Status == ServiceRequestStatus.CompletedByBuyer &&
                row.Review.TenantId == row.Request.TenantId &&
                row.Review.ServiceRequestId == row.Request.Id &&
                row.Review.ServiceId == row.Request.ServiceId &&
                row.Review.ReviewerUserId == row.Request.BuyerUserId &&
                row.Review.ProviderUserId == row.Request.ProviderUserId)
            .Select(row => new
            {
                row.Review.Rating,
                row.Review.CreatedAt,
                IsMarketingReview = row.Evidence != null,
                IsPosVerified = row.Evidence != null && row.Evidence.EvidenceType == EvidenceType.PosVerified,
                OrdersCount = row.Evidence == null ? 0 : row.Evidence.OrdersCount ?? 0,
                RevenueAmount = row.Evidence == null ? 0m : row.Evidence.RevenueAmount ?? 0m
            })
            .ToListAsync(cancellationToken);

        return ProviderTrustAggregate.FromSqlReviewTruth(
            tenantId,
            providerProfileId,
            reviews.Count,
            reviews.Sum(review => review.Rating),
            reviews.Count(review => review.Rating == 5),
            reviews.Count(review => review.Rating == 4),
            reviews.Count(review => review.Rating == 3),
            reviews.Count(review => review.Rating == 2),
            reviews.Count(review => review.Rating == 1),
            reviews.Count == 0 ? null : reviews.Max(review => review.CreatedAt),
            reviews.Count(review => review.IsMarketingReview),
            reviews.Count(review => review.IsPosVerified),
            reviews.Sum(review => review.OrdersCount),
            reviews.Sum(review => review.RevenueAmount));
    }
}
