using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

/// <summary>
/// MongoDB read model for fast provider trust queries.
/// Updated via ProviderTrustUpdatedEvent.
/// </summary>
public class ProviderTrustSnapshotDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("tenantId")]
    [BsonRepresentation(BsonType.String)]
    public Guid TenantId { get; set; }

    [BsonElement("providerProfileId")]
    [BsonRepresentation(BsonType.String)]
    public Guid ProviderProfileId { get; set; }

    [BsonElement("totalReviews")]
    public int TotalReviews { get; set; }

    [BsonElement("averageRating")]
    public decimal AverageRating { get; set; }

    [BsonElement("trustBadge")]
    [BsonRepresentation(BsonType.String)]
    public string TrustBadge { get; set; } = string.Empty;

    [BsonElement("ratingDistribution")]
    public Dictionary<string, int> RatingDistribution { get; set; } = new();

    [BsonElement("lastReviewAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastReviewAt { get; set; }

    [BsonElement("lastUpdatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdatedAt { get; set; }

    [BsonElement("lastEventId")]
    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonElement("lastEventOccurredAtUtc")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    [BsonElement("lastEventVersion")]
    public int LastEventVersion { get; set; }

    // Phase MR-1: Marketing trust metrics
    [BsonElement("marketingReviewCount")]
    public int MarketingReviewCount { get; set; }

    [BsonElement("posVerifiedCount")]
    public int PosVerifiedCount { get; set; }

    [BsonElement("marketingWeightedScore")]
    public decimal MarketingWeightedScore { get; set; }
}
