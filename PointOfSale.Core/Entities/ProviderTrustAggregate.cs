using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Aggregated trust data for a provider.
/// Trust is derived from ServiceReviews, never set manually.
/// Uses optimistic concurrency via Version field.
/// </summary>
public class ProviderTrustAggregate : TenantEntity
{
    public Guid ProviderProfileId { get; private set; }
    public int TotalReviews { get; private set; }
    public int SumRatings { get; private set; }
    public decimal AverageRating { get; private set; }
    public int FiveStarCount { get; private set; }
    public int FourStarCount { get; private set; }
    public int ThreeStarCount { get; private set; }
    public int TwoStarCount { get; private set; }
    public int OneStarCount { get; private set; }
    public DateTime? LastReviewAt { get; private set; }
    public int Version { get; private set; }

    // Phase MR-1: Marketing-specific trust metrics
    public int MarketingReviewCount { get; private set; }
    public int PosVerifiedCount { get; private set; }
    public decimal TotalVerifiedRevenue { get; private set; }
    public int TotalVerifiedOrders { get; private set; }
    public decimal MarketingWeightedScore { get; private set; }

    public MarketplaceProfile? ProviderProfile { get; private set; }

    private ProviderTrustAggregate() { }

    public static ProviderTrustAggregate Create(Guid tenantId, Guid providerProfileId)
    {
        return new ProviderTrustAggregate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProviderProfileId = providerProfileId,
            TotalReviews = 0,
            SumRatings = 0,
            AverageRating = 0.00m,
            FiveStarCount = 0,
            FourStarCount = 0,
            ThreeStarCount = 0,
            TwoStarCount = 0,
            OneStarCount = 0,
            MarketingReviewCount = 0,
            PosVerifiedCount = 0,
            TotalVerifiedRevenue = 0,
            TotalVerifiedOrders = 0,
            MarketingWeightedScore = 0,
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ProviderTrustAggregate FromSqlReviewTruth(
        Guid tenantId,
        Guid providerProfileId,
        int totalReviews,
        int sumRatings,
        int fiveStarCount,
        int fourStarCount,
        int threeStarCount,
        int twoStarCount,
        int oneStarCount,
        DateTime? lastReviewAt,
        int marketingReviewCount,
        int posVerifiedCount,
        int totalVerifiedOrders,
        decimal totalVerifiedRevenue)
    {
        var aggregate = Create(tenantId, providerProfileId);
        aggregate.TotalReviews = totalReviews;
        aggregate.SumRatings = sumRatings;
        aggregate.FiveStarCount = fiveStarCount;
        aggregate.FourStarCount = fourStarCount;
        aggregate.ThreeStarCount = threeStarCount;
        aggregate.TwoStarCount = twoStarCount;
        aggregate.OneStarCount = oneStarCount;
        aggregate.AverageRating = totalReviews > 0
            ? Math.Round((decimal)sumRatings / totalReviews, 2)
            : 0.00m;
        aggregate.LastReviewAt = lastReviewAt;
        aggregate.MarketingReviewCount = marketingReviewCount;
        aggregate.PosVerifiedCount = posVerifiedCount;
        aggregate.TotalVerifiedOrders = totalVerifiedOrders;
        aggregate.TotalVerifiedRevenue = totalVerifiedRevenue;
        aggregate.RecalculateMarketingScore();
        return aggregate;
    }

    /// <summary>
    /// Applies a review to the trust aggregate.
    /// Phase MR-1: Extended with optional marketing evidence data.
    /// </summary>
    public void ApplyReview(
        int rating,
        bool isMarketingReview = false,
        EvidenceType? evidenceType = null,
        int? ordersCount = null,
        decimal? revenueAmount = null)
    {
        if (rating < 1 || rating > 5)
            throw new ArgumentException("Rating must be between 1 and 5", nameof(rating));

        // Increment star count
        switch (rating)
        {
            case 5: FiveStarCount++; break;
            case 4: FourStarCount++; break;
            case 3: ThreeStarCount++; break;
            case 2: TwoStarCount++; break;
            case 1: OneStarCount++; break;
        }

        // Update aggregates
        TotalReviews++;
        SumRatings += rating;
        AverageRating = Math.Round((decimal)SumRatings / TotalReviews, 2);

        // Phase MR-1: Marketing metrics
        if (isMarketingReview)
        {
            MarketingReviewCount++;

            if (evidenceType == Enums.EvidenceType.PosVerified 
                && ordersCount.HasValue && revenueAmount.HasValue)
            {
                PosVerifiedCount++;
                TotalVerifiedOrders += ordersCount.Value;
                TotalVerifiedRevenue += revenueAmount.Value;
            }

            RecalculateMarketingScore();
        }

        LastReviewAt = DateTime.UtcNow;
        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Computes MarketingWeightedScore from aggregate metrics.
    /// Formula: Stars * 0.5 + log(Revenue + 1) * 0.3 + log(Orders + 1) * 0.2
    /// Only meaningful when MarketingReviewCount > 0.
    /// </summary>
    private void RecalculateMarketingScore()
    {
        if (MarketingReviewCount == 0)
        {
            MarketingWeightedScore = 0;
            return;
        }

        var starComponent = (double)AverageRating * 0.5;
        var revenueComponent = Math.Log((double)TotalVerifiedRevenue + 1) * 0.3;
        var ordersComponent = Math.Log(TotalVerifiedOrders + 1) * 0.2;

        MarketingWeightedScore = Math.Round((decimal)(starComponent + revenueComponent + ordersComponent), 2);
    }

    /// <summary>
    /// Force reconciles aggregate state from source of truth (Reconciliation Job).
    /// </summary>
    public void Reconcile(
        int totalReviews, 
        int sumRatings, 
        int fiveStarCount, 
        int fourStarCount, 
        int threeStarCount, 
        int twoStarCount, 
        int oneStarCount)
    {
        TotalReviews = totalReviews;
        SumRatings = sumRatings;
        FiveStarCount = fiveStarCount;
        FourStarCount = fourStarCount;
        ThreeStarCount = threeStarCount;
        TwoStarCount = twoStarCount;
        OneStarCount = oneStarCount;
        
        // Recalculate average
        AverageRating = TotalReviews > 0 
            ? Math.Round((decimal)SumRatings / TotalReviews, 2) 
            : 0.00m;

        Version++;
        UpdatedAt = DateTime.UtcNow;
    }

    public TrustBadge ComputeBadge()
    {
        if (TotalReviews < 5)
            return TrustBadge.New;

        if (AverageRating >= 4.6m && TotalReviews >= 50)
            return TrustBadge.TopRated;

        if (AverageRating >= 4.3m && TotalReviews >= 20)
            return TrustBadge.Trusted;

        if (AverageRating >= 4.0m && TotalReviews >= 5)
            return TrustBadge.Rising;

        return TrustBadge.Standard;
    }

    public Dictionary<int, int> GetRatingDistribution()
    {
        return new Dictionary<int, int>
        {
            { 5, FiveStarCount },
            { 4, FourStarCount },
            { 3, ThreeStarCount },
            { 2, TwoStarCount },
            { 1, OneStarCount }
        };
    }
}
