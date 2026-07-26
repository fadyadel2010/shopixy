namespace PointOfSale.Core.Analytics.Attribution;

/// <summary>
/// Campaign Cost entity for ROI calculations.
/// 
/// PHASE 8B: Campaign Cost & ROI (MVP)
/// 
/// PURPOSE:
/// - Admin-declared marketing spend per campaign/period
/// - POS is the single source of truth for costs
/// - No auto-fetch from Facebook/Google Ads APIs
/// 
/// UNIQUE KEY:
/// (TenantId, Source, Medium, Campaign, PeriodStartUtc)
/// 
/// RULES:
/// - Cost is immutable per period (update replaces, no history loss)
/// - Currency must match tenant currency
/// - Source/Medium/Campaign map to UTM parameters
/// 
/// STORAGE:
/// - PostgreSQL (transactional, ACID)
/// </summary>
public class CampaignCost
{
    /// <summary>
    /// Unique identifier for this cost record.
    /// </summary>
    public Guid CampaignCostId { get; set; }

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Traffic source (e.g., facebook, google).
    /// Maps to utm_source.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Marketing medium (e.g., cpc, social, email).
    /// Maps to utm_medium.
    /// </summary>
    public string Medium { get; set; } = string.Empty;

    /// <summary>
    /// Campaign name (e.g., ramadan_sale, black_friday).
    /// Maps to utm_campaign.
    /// </summary>
    public string Campaign { get; set; } = string.Empty;

    /// <summary>
    /// Start of the period this cost applies to (UTC).
    /// </summary>
    public DateTime PeriodStartUtc { get; set; }

    /// <summary>
    /// End of the period this cost applies to (UTC).
    /// </summary>
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>
    /// Marketing spend for this campaign/period.
    /// </summary>
    public decimal CostAmount { get; set; }

    /// <summary>
    /// Currency code (must match tenant currency).
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional notes about this cost entry.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// When this record was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this record was last updated (UTC).
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Soft-delete flag.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Creates a normalized campaign key for matching.
    /// </summary>
    public string GetCampaignKey() => 
        $"{Source.ToLowerInvariant()}|{Medium.ToLowerInvariant()}|{Campaign.ToLowerInvariant()}";
}
