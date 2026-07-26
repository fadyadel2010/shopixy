using Nest;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

/// <summary>
/// Canonical Elasticsearch document for marketplace service discovery.
/// Contains everything needed to rank and display - no joins, no lookups.
/// </summary>
[ElasticsearchType(IdProperty = nameof(ServiceId))]
public class MarketplaceServiceDocument
{
    [Keyword]
    public Guid ServiceId { get; set; }

    [Keyword]
    public Guid TenantId { get; set; }

    [Text(Analyzer = "standard")]
    public string Title { get; set; } = string.Empty;

    [Text(Analyzer = "standard")]
    public string Description { get; set; } = string.Empty;

    [Keyword]
    public string? CategoryId { get; set; }

    [Text(Analyzer = "standard")]
    public string? CategoryPath { get; set; }

    [Keyword]
    public Guid ProviderProfileId { get; set; }

    [Text(Analyzer = "standard")]
    public string ProviderDisplayName { get; set; } = string.Empty;

    [Object]
    public TrustMetrics Trust { get; set; } = new();

    [Object]
    public ServiceStatistics ServiceStats { get; set; } = new();

    [Date]
    public DateTime CreatedAt { get; set; }

    [Boolean]
    public bool IsActive { get; set; }

    // Phase FE-3: Pricing & Media
    [Number(NumberType.Double)]
    public decimal? StartingPrice { get; set; }

    [Keyword]
    public string? PricingType { get; set; }

    [Keyword]
    public string? Currency { get; set; }

    [Text(Index = false)]
    public string? ServiceImages { get; set; }

    public class TrustMetrics
    {
        [Number(NumberType.Double)]
        public decimal AverageRating { get; set; }

        [Number(NumberType.Integer)]
        public int TotalReviews { get; set; }

        [Keyword]
        public string TrustBadge { get; set; } = "New";

        // Phase MR-1: Marketing trust metrics
        [Number(NumberType.Integer)]
        public int MarketingReviewCount { get; set; }

        [Number(NumberType.Integer)]
        public int PosVerifiedCount { get; set; }

        [Number(NumberType.Double)]
        public decimal MarketingWeightedScore { get; set; }

        [Keyword]
        public Guid? LastEventId { get; set; }

        [Date]
        public DateTime? LastEventOccurredAtUtc { get; set; }

        [Number(NumberType.Integer)]
        public int LastEventVersion { get; set; }
    }

    public class ServiceStatistics
    {
        [Number(NumberType.Integer)]
        public int TotalRequests { get; set; }

        [Number(NumberType.Integer)]
        public int CompletedRequests { get; set; }

        [Number(NumberType.Double)]
        public decimal CompletionRate { get; set; }
    }
}
