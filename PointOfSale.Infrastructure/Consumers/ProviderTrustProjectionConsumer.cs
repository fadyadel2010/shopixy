using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumes ProviderTrustUpdatedEvent and updates MongoDB projection.
/// Provides fast read model for storefront/admin queries.
/// </summary>
public class ProviderTrustProjectionConsumer : IConsumer<ProviderTrustUpdatedEvent>
{
    private readonly IMongoCollection<ProviderTrustSnapshotDocument> _collection;
    private readonly ILogger<ProviderTrustProjectionConsumer> _logger;

    public ProviderTrustProjectionConsumer(
        IMongoDatabase mongoDatabase,
        ILogger<ProviderTrustProjectionConsumer> logger)
    {
        _collection = mongoDatabase.GetCollection<ProviderTrustSnapshotDocument>("provider_trust_snapshots");
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProviderTrustUpdatedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue || message.TenantId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("ProviderTrustUpdatedEvent requires a tenant id for ordered projection.");
        }

        var tenantId = message.TenantId.Value;

        _logger.LogInformation(
            "Updating MongoDB projection for Provider {ProviderProfileId}",
            message.ProviderProfileId
        );

        var occurredAtUtc = message.OccurredAtUtc.Kind == DateTimeKind.Utc
            ? message.OccurredAtUtc
            : DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc);

        var snapshot = new ProviderTrustSnapshotDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProviderProfileId = message.ProviderProfileId,
            TotalReviews = message.TotalReviews,
            AverageRating = message.AverageRating,
            TrustBadge = message.TrustBadge.ToString(),
            RatingDistribution = message.RatingDistribution.ToDictionary(k => k.Key.ToString(), k => k.Value),
            LastReviewAt = message.LastReviewAt,
            LastUpdatedAt = message.UpdatedAt,
            LastEventId = message.EventId,
            LastEventOccurredAtUtc = occurredAtUtc,
            LastEventVersion = message.EventVersion,
            // Phase MR-1
            MarketingReviewCount = message.MarketingReviewCount,
            PosVerifiedCount = message.PosVerifiedCount,
            MarketingWeightedScore = message.MarketingWeightedScore
        };

        var filter = BuildFreshnessFilter(tenantId, message.ProviderProfileId, message.EventId, occurredAtUtc, message.EventVersion);

        try
        {
            var result = await _collection.ReplaceOneAsync(
                filter,
                snapshot,
                new ReplaceOptions { IsUpsert = true },
                context.CancellationToken);

            if (result.MatchedCount == 0 && result.UpsertedId == null)
            {
                _logger.LogInformation(
                    "Provider trust projection ignored stale event | ProviderProfileId: {ProviderProfileId} | EventId: {EventId} | EventVersion: {EventVersion}",
                    message.ProviderProfileId,
                    message.EventId,
                    message.EventVersion);
                return;
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogInformation(
                ex,
                "Provider trust projection ignored stale concurrent event | ProviderProfileId: {ProviderProfileId} | EventId: {EventId} | EventVersion: {EventVersion}",
                message.ProviderProfileId,
                message.EventId,
                message.EventVersion);
            return;
        }

        _logger.LogInformation(
            "MongoDB projection updated for Provider {ProviderProfileId}",
            message.ProviderProfileId
        );
    }

    private static FilterDefinition<ProviderTrustSnapshotDocument> BuildFreshnessFilter(
        Guid tenantId,
        Guid providerProfileId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<ProviderTrustSnapshotDocument>.Filter;

        return filter.And(
            filter.Eq(d => d.TenantId, tenantId),
            filter.Eq(d => d.ProviderProfileId, providerProfileId),
            filter.Ne(d => d.LastEventId, eventId),
            filter.Or(
                filter.Exists(d => d.LastEventOccurredAtUtc, false),
                filter.Eq(d => d.LastEventOccurredAtUtc, null),
                filter.Lt(d => d.LastEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(d => d.LastEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(d => d.LastEventVersion, eventVersion))));
    }
}
