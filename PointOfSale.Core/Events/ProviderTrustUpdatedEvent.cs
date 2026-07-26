using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events;

/// <summary>
/// Published when provider trust aggregate is updated.
/// Triggers read model projection updates.
/// </summary>
public class ProviderTrustUpdatedEvent : IntegrationEvent, IAuthoritativeIntegrationEvent, IAggregateScopedIntegrationEvent
{
    public Guid ProviderProfileId { get; init; }
    public int TotalReviews { get; init; }
    public decimal AverageRating { get; init; }
    public TrustBadge TrustBadge { get; init; }
    public Dictionary<int, int> RatingDistribution { get; init; } = new();
    public DateTime? LastReviewAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // Phase MR-1: Marketing trust metrics
    public int MarketingReviewCount { get; init; }
    public int PosVerifiedCount { get; init; }
    public decimal MarketingWeightedScore { get; init; }

    public Guid GetAuthoritativeAggregateId() => ProviderProfileId;
}
