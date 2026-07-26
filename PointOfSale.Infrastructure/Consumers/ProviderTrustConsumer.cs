using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumes ServiceReviewCreatedEvent and updates ProviderTrustAggregate.
/// Uses optimistic concurrency to handle concurrent reviews.
/// 
/// PHASE: Marketplace Audit Hardening
/// - Stages ProviderTrustUpdatedEvent through IEventPublisher before SaveChanges
///   so aggregate, idempotency marker, and outbox intent commit atomically.
/// - Uses Task.Delay instead of Thread.Sleep for async-friendly retry.
/// - Detaches conflicting entries before retry to avoid stale state.
/// </summary>
public class ProviderTrustConsumer : IConsumer<ServiceReviewCreatedEvent>
{
    private readonly ApplicationDbContext _context;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProviderTrustConsumer> _logger;

    public ProviderTrustConsumer(
        ApplicationDbContext context,
        IEventPublisher eventPublisher,
        ILogger<ProviderTrustConsumer> logger)
    {
        _context = context;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ServiceReviewCreatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Processing ServiceReviewCreatedEvent for Provider {ProviderProfileId}, Rating {Rating}",
            message.ProviderProfileId,
            message.Rating
        );

        var sqlReview = await LoadSqlReviewAuthorityAsync(message.ReviewId, context.CancellationToken);
        if (sqlReview == null)
        {
            _logger.LogWarning(
                "Ignoring ServiceReviewCreatedEvent because current SQL review/request authority rejected it | ReviewId: {ReviewId} | ProviderProfileId: {ProviderProfileId}",
                message.ReviewId,
                message.ProviderProfileId);
            return;
        }

        // Check idempotency
        // NOTE: IgnoreQueryFilters because consumer runs without tenant context
        var alreadyProcessed = await _context.Set<ProcessedTrustEvent>()
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EventId == sqlReview.ReviewId);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Event {EventId} already processed, skipping",
                message.ReviewId
            );
            return;
        }

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Load or create aggregate
                // NOTE: IgnoreQueryFilters because consumer runs without tenant context
                var aggregate = await _context.Set<ProviderTrustAggregate>()
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.ProviderProfileId == sqlReview.ProviderProfileId && !t.IsDeleted);

                if (aggregate == null)
                {
                    aggregate = ProviderTrustAggregate.Create(sqlReview.TenantId, sqlReview.ProviderProfileId);
                    _context.Set<ProviderTrustAggregate>().Add(aggregate);
                }

                // Apply review (Phase MR-1: pass marketing evidence if present)
                aggregate.ApplyReview(
                    sqlReview.Rating,
                    sqlReview.IsMarketingReview,
                    sqlReview.EvidenceType,
                    sqlReview.OrdersCount,
                    sqlReview.RevenueAmount);

                // Record event as processed
                var processedEvent = ProcessedTrustEvent.Create(
                    sqlReview.TenantId,
                    sqlReview.ReviewId,
                    nameof(ServiceReviewCreatedEvent),
                    sqlReview.ProviderProfileId
                );
                _context.Set<ProcessedTrustEvent>().Add(processedEvent);

                await _eventPublisher.PublishAsync(new ProviderTrustUpdatedEvent
                {
                    TenantId = sqlReview.TenantId,
                    ProviderProfileId = sqlReview.ProviderProfileId,
                    EventVersion = aggregate.Version,
                    OccurredAtUtc = aggregate.UpdatedAt ?? DateTime.UtcNow,
                    TotalReviews = aggregate.TotalReviews,
                    AverageRating = aggregate.AverageRating,
                    TrustBadge = aggregate.ComputeBadge(),
                    RatingDistribution = aggregate.GetRatingDistribution(),
                    LastReviewAt = aggregate.LastReviewAt,
                    UpdatedAt = aggregate.UpdatedAt ?? DateTime.UtcNow,
                    // Phase MR-1
                    MarketingReviewCount = aggregate.MarketingReviewCount,
                    PosVerifiedCount = aggregate.PosVerifiedCount,
                    MarketingWeightedScore = aggregate.MarketingWeightedScore
                }, context.CancellationToken);

                // Save aggregate, idempotency marker, and authoritative outbox row atomically.
                await _context.SaveChangesAsync(context.CancellationToken);

                _logger.LogInformation(
                    "Updated trust aggregate for Provider {ProviderProfileId}: {TotalReviews} reviews, {AverageRating} avg",
                    sqlReview.ProviderProfileId,
                    aggregate.TotalReviews,
                    aggregate.AverageRating
                );

                return; // Success
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                        "Concurrency conflict updating trust aggregate for Provider {ProviderProfileId}, attempt {Attempt}/{MaxRetries}",
                    sqlReview.ProviderProfileId,
                    attempt,
                    maxRetries
                );

                if (attempt == maxRetries)
                {
                    _logger.LogError(
                        "Failed to update trust aggregate after {MaxRetries} attempts for Provider {ProviderProfileId}",
                        maxRetries,
                        sqlReview.ProviderProfileId
                    );
                    throw;
                }

                // Detach all tracked entities and retry with fresh state
                // This avoids stale concurrency tokens from the failed attempt
                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                // Async-friendly delay (P1-5: replaced Thread.Sleep)
                await Task.Delay(100 * attempt);
            }
        }
    }

    private async Task<SqlReviewAuthority?> LoadSqlReviewAuthorityAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        return await _context.Set<ServiceReview>()
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
                row.Review.Id == reviewId &&
                !row.Review.IsDeleted &&
                !row.Request.IsDeleted &&
                row.Request.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer &&
                row.Review.TenantId == row.Request.TenantId &&
                row.Review.ServiceRequestId == row.Request.Id &&
                row.Review.ServiceId == row.Request.ServiceId &&
                row.Review.ReviewerUserId == row.Request.BuyerUserId &&
                row.Review.ProviderUserId == row.Request.ProviderUserId)
            .Select(row => new SqlReviewAuthority(
                row.Review.Id,
                row.Review.TenantId,
                row.Request.ProviderProfileId,
                row.Review.Rating,
                row.Evidence != null,
                row.Evidence == null ? null : row.Evidence.EvidenceType,
                row.Evidence == null ? null : row.Evidence.OrdersCount,
                row.Evidence == null ? null : row.Evidence.RevenueAmount))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record SqlReviewAuthority(
        Guid ReviewId,
        Guid TenantId,
        Guid ProviderProfileId,
        int Rating,
        bool IsMarketingReview,
        Core.Enums.EvidenceType? EvidenceType,
        int? OrdersCount,
        decimal? RevenueAmount);
}
