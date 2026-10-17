using PointOfSale.Core.Analytics.Attribution;

namespace PointOfSale.Infrastructure.CampaignCost;

/// <summary>
/// Service interface for Campaign Cost operations.
/// 
/// PHASE 8B: Campaign Cost & ROI (MVP)
/// 
/// PURPOSE:
/// - CRUD operations for campaign costs
/// - PostgreSQL-backed storage
/// - Tenant-isolated
/// 
/// RULES:
/// - (TenantId, Source, Medium, Campaign, Period) must be unique
/// - Update replaces existing cost for same key
/// - Currency must match tenant currency
/// </summary>
public interface ICampaignCostService
{
    /// <summary>
    /// Creates or updates a campaign cost entry.
    /// If a matching entry exists for the same campaign/period, it is updated.
    /// </summary>
    Task<Core.Analytics.Attribution.CampaignCost> CreateOrUpdateAsync(
        Guid tenantId,
        CreateCampaignCostRequest request,
        CancellationToken cancellationToken = default);

    Task<Core.Analytics.Attribution.CampaignCost?> ReplaceAsync(
        Guid tenantId, Guid id, CreateCampaignCostRequest request,
        DateTime? expectedUpdatedAtUtc = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all campaign costs for a tenant within a date range.
    /// </summary>
    Task<List<Core.Analytics.Attribution.CampaignCost>> GetByPeriodAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific campaign cost by ID.
    /// </summary>
    Task<Core.Analytics.Attribution.CampaignCost?> GetByIdAsync(
        Guid tenantId,
        Guid campaignCostId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a campaign cost entry (soft delete).
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid campaignCostId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets campaign costs matching specific UTM parameters.
    /// Used for ROI calculations.
    /// </summary>
    Task<List<Core.Analytics.Attribution.CampaignCost>> GetByCampaignAsync(
        Guid tenantId,
        string? source,
        string? medium,
        string? campaign,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request DTO for creating/updating campaign costs.
/// </summary>
public class CreateCampaignCostRequest
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Campaign { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Notes { get; set; }
}
