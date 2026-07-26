using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// CRITICAL: Trust Integrity Reconciliation Job.
/// Detects and repairs drift between the denormalized ProviderTrustAggregate
/// and the actual source-of-truth ServiceReview records.
/// 
/// Runs periodically to guarantee that trust scores are mathematically correct
/// even in the face of concurrency bugs, lost events, or cosmic rays.
/// </summary>
public class TrustDriftReconciliationJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TrustDriftReconciliationJob> _logger;
    private readonly MarketplaceTelemetry _telemetry;

    public TrustDriftReconciliationJob(
        ApplicationDbContext context,
        ILogger<TrustDriftReconciliationJob> logger,
        MarketplaceTelemetry telemetry)
    {
        _context = context;
        _logger = logger;
        _telemetry = telemetry;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Trust Drift Reconciliation Job...");
        
        int processed = 0;
        int driftDetected = 0;
        int pageSize = 100;
        Guid? lastId = null;

        while (true)
        {
            // Batch process aggregates
            var query = _context.Set<ProviderTrustAggregate>()
                .IgnoreQueryFilters()
                .Include(t => t.ProviderProfile)
                .Where(t => !t.IsDeleted)
                .OrderBy(t => t.Id);

            if (lastId.HasValue)
            {
                // Pagination using ID > lastId
                query = (IOrderedQueryable<ProviderTrustAggregate>)query.Where(t => t.Id > lastId.Value);
            }

            var aggregates = await query
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            if (!aggregates.Any()) break;

            foreach (var aggregate in aggregates)
            {
                lastId = aggregate.Id;
                processed++;

                if (aggregate.ProviderProfile == null)
                {
                    _logger.LogWarning("Orphaned ProviderTrustAggregate found | Id: {Id}", aggregate.Id);
                    continue;
                }

                // 1. Get the Ground Truth (Actual Reviews)
                var reviews = await _context.Set<ServiceReview>()
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Join(
                        _context.Set<ServiceRequest>().AsNoTracking().IgnoreQueryFilters(),
                        review => review.ServiceRequestId,
                        request => request.Id,
                        (review, request) => new { Review = review, Request = request })
                    .Where(row =>
                        row.Request.ProviderProfileId == aggregate.ProviderProfileId &&
                        !row.Review.IsDeleted &&
                        !row.Request.IsDeleted &&
                        row.Request.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer &&
                        row.Review.TenantId == row.Request.TenantId &&
                        row.Review.ServiceRequestId == row.Request.Id &&
                        row.Review.ServiceId == row.Request.ServiceId &&
                        row.Review.ReviewerUserId == row.Request.BuyerUserId &&
                        row.Review.ProviderUserId == row.Request.ProviderUserId)
                    .Select(row => row.Review.Rating)
                    .ToListAsync(cancellationToken);

                // 2. Calculate Truth
                var totalReviews = reviews.Count;
                var sumRatings = reviews.Sum();
                var realAverage = totalReviews > 0 
                    ? Math.Round((decimal)sumRatings / totalReviews, 2) 
                    : 0m;
                
                // 3. Detect Drift
                bool countMismatch = aggregate.TotalReviews != totalReviews;
                bool ratingMismatch = Math.Abs(aggregate.AverageRating - realAverage) > 0.01m;

                if (countMismatch || ratingMismatch)
                {
                    driftDetected++;
                    
                    var fiveStar = reviews.Count(r => r == 5);
                    var fourStar = reviews.Count(r => r == 4);
                    var threeStar = reviews.Count(r => r == 3);
                    var twoStar = reviews.Count(r => r == 2);
                    var oneStar = reviews.Count(r => r == 1);

                    _logger.LogWarning(
                        "TRUST DRIFT DETECTED | Provider: {ProviderId} | Stored: [Count={StoredCount}, Rating={StoredRating:F2}] | Real: [Count={RealCount}, Rating={RealRating:F2}]",
                        aggregate.ProviderProfileId, 
                        aggregate.TotalReviews, aggregate.AverageRating,
                        totalReviews, realAverage);

                    // 4. Repair (Self-Healing)
                    aggregate.Reconcile(totalReviews, sumRatings, fiveStar, fourStar, threeStar, twoStar, oneStar);
                    
                    // Metric
                    _telemetry.RecordTrustDrift(1);
                }
            }

            // Save fixes for this batch
            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation(
            "Trust Reconciliation Complete | Processed: {Processed} | DriftFixed: {Drift}", 
            processed, driftDetected);
    }
}
