namespace PointOfSale.Core.Analytics.Attribution;

/// <summary>
/// Campaign ROI result model for display.
/// 
/// PHASE 8B: Campaign Cost & ROI (MVP)
/// 
/// PURPOSE:
/// - Server-side computed ROI result
/// - All calculations done in backend
/// - Frontend is PURE RENDERER
/// 
/// CALCULATION RULES:
/// - Revenue = SUM(Order.Total) for attributed orders
/// - Orders = COUNT(attributed orders)
/// - Profit = Revenue - Cost
/// - ROI% = (Profit / Cost) * 100
/// 
/// SAFETY RULES:
/// - If Cost = 0 → ROI = null (not infinite)
/// - Revenue-only campaigns still appear (Cost = 0)
/// - Cost-only campaigns still appear (Revenue = 0)
/// </summary>
public class CampaignRoiResult
{
    /// <summary>
    /// Traffic source (e.g., facebook, google).
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Marketing medium (e.g., cpc, social).
    /// </summary>
    public string Medium { get; set; } = string.Empty;

    /// <summary>
    /// Campaign name.
    /// </summary>
    public string Campaign { get; set; } = string.Empty;

    /// <summary>
    /// Total marketing cost from admin-declared data.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// Total revenue from attributed orders.
    /// </summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// Count of attributed orders.
    /// </summary>
    public int Orders { get; set; }

    /// <summary>
    /// Calculated profit (Revenue - Cost).
    /// </summary>
    public decimal Profit { get; set; }

    /// <summary>
    /// Calculated ROI percentage.
    /// Null if Cost = 0 (to avoid infinity).
    /// </summary>
    public decimal? RoiPercentage { get; set; }

    /// <summary>
    /// Attribution model used for revenue calculation.
    /// </summary>
    public AttributionModel Model { get; set; }

    /// <summary>
    /// Average order value for this campaign.
    /// </summary>
    public decimal AverageOrderValue => Orders > 0 ? Revenue / Orders : 0;

    /// <summary>
    /// Cost per acquisition (Cost / Orders).
    /// Null if Orders = 0.
    /// </summary>
    public decimal? CostPerAcquisition => Orders > 0 ? Cost / Orders : null;

    /// <summary>
    /// Currency code for monetary values.
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Indicates if this campaign has positive ROI.
    /// Null if ROI cannot be calculated.
    /// </summary>
    public bool? IsProfitable => RoiPercentage.HasValue ? RoiPercentage > 0 : null;

    /// <summary>
    /// Get ROI color for UI rendering.
    /// Green = positive, Red = negative, Gray = neutral/unknown.
    /// </summary>
    public string GetRoiColor()
    {
        if (!RoiPercentage.HasValue) return "gray";
        if (RoiPercentage > 0) return "green";
        if (RoiPercentage < 0) return "red";
        return "gray"; // Exactly 0
    }
}
