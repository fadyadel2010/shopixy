using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable evidence record attached 1-to-1 with a ServiceReview
/// when the reviewed service is in the Marketing category.
///
/// DOMAIN INVARIANTS:
/// 1. Required if service.Category == "Marketing"
/// 2. Forbidden if service.Category != "Marketing"
/// 3. Immutable after creation (no Update method)
/// 4. OrdersCount/RevenueAmount are server-computed, never client-supplied
/// 5. EvidenceType is server-determined:
///    - PosVerified: OrdersCount AND RevenueAmount are non-null
///    - SelfReported: OrdersCount AND RevenueAmount are null
/// 6. EndDate must be >= StartDate
/// 7. StartDate cannot be in the future
/// </summary>
public class MarketingReviewEvidence : TenantEntity
{
    public Guid ServiceReviewId { get; private set; }
    public string CampaignName { get; private set; } = string.Empty;
    public MarketingPlatform Platform { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }

    /// <summary>
    /// Number of POS orders placed during the campaign period.
    /// NULL for SelfReported evidence. Server-computed only.
    /// </summary>
    public int? OrdersCount { get; private set; }

    /// <summary>
    /// Total revenue from POS orders during the campaign period.
    /// NULL for SelfReported evidence. Server-computed only.
    /// Precision: 18,2 (matches existing financial fields).
    /// </summary>
    public decimal? RevenueAmount { get; private set; }

    /// <summary>
    /// Server-determined classification of the evidence.
    /// PosVerified only if OrdersCount AND RevenueAmount are non-null.
    /// </summary>
    public EvidenceType EvidenceType { get; private set; }

    public ServiceReview? ServiceReview { get; private set; }

    private MarketingReviewEvidence() { }

    /// <summary>
    /// Creates self-reported evidence (no POS metrics).
    /// Called when POS verification is not available or not applicable.
    /// </summary>
    public static MarketingReviewEvidence CreateSelfReported(
        Guid tenantId,
        Guid serviceReviewId,
        string campaignName,
        MarketingPlatform platform,
        DateTime startDate,
        DateTime endDate)
    {
        ValidateDates(startDate, endDate);
        ValidateCampaignName(campaignName);

        return new MarketingReviewEvidence
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceReviewId = serviceReviewId,
            CampaignName = campaignName.Trim(),
            Platform = platform,
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            OrdersCount = null,
            RevenueAmount = null,
            EvidenceType = EvidenceType.SelfReported,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates POS-verified evidence with server-computed metrics.
    /// Called only when the backend has verified orders/revenue from POS data.
    /// </summary>
    public static MarketingReviewEvidence CreatePosVerified(
        Guid tenantId,
        Guid serviceReviewId,
        string campaignName,
        MarketingPlatform platform,
        DateTime startDate,
        DateTime endDate,
        int ordersCount,
        decimal revenueAmount)
    {
        ValidateDates(startDate, endDate);
        ValidateCampaignName(campaignName);

        if (ordersCount < 0)
            throw new ArgumentException("OrdersCount cannot be negative", nameof(ordersCount));

        if (revenueAmount < 0)
            throw new ArgumentException("RevenueAmount cannot be negative", nameof(revenueAmount));

        return new MarketingReviewEvidence
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceReviewId = serviceReviewId,
            CampaignName = campaignName.Trim(),
            Platform = platform,
            StartDate = startDate.Date,
            EndDate = endDate.Date,
            OrdersCount = ordersCount,
            RevenueAmount = revenueAmount,
            EvidenceType = EvidenceType.PosVerified,
            CreatedAt = DateTime.UtcNow
        };
    }

    // --- Domain Guards ---

    private static void ValidateDates(DateTime startDate, DateTime endDate)
    {
        if (endDate.Date < startDate.Date)
            throw new ArgumentException("EndDate must be on or after StartDate", nameof(endDate));

        if (startDate.Date > DateTime.UtcNow.Date)
            throw new ArgumentException("StartDate cannot be in the future", nameof(startDate));
    }

    private static void ValidateCampaignName(string campaignName)
    {
        if (string.IsNullOrWhiteSpace(campaignName))
            throw new ArgumentException("CampaignName is required", nameof(campaignName));

        if (campaignName.Length > 200)
            throw new ArgumentException("CampaignName must not exceed 200 characters", nameof(campaignName));
    }

    // NOTE: No Update method — MarketingReviewEvidence is IMMUTABLE after creation.
    // This is enforced by:
    // 1. Domain: No mutation methods exposed
    // 2. Database: Can add CHECK constraint or trigger if needed
    // 3. Application: Service layer never calls any setter
}
