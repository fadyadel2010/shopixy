using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.CampaignCost;

/// <summary>
/// Service for calculating Campaign ROI.
/// 
/// PHASE 8B: Campaign Cost & ROI (MVP)
/// 
/// CALCULATION FLOW:
/// 1. Fetch attribution revenue per campaign (from Phase 7)
/// 2. Fetch campaign costs per campaign (from this phase)
/// 3. Join by (source, medium, campaign)
/// 4. Calculate ROI server-side
/// 
/// ROI FORMULA:
/// - Profit = Revenue - Cost
/// - ROI% = (Profit / Cost) * 100
/// 
/// SAFETY RULES:
/// - Cost = 0 → ROI = null (not infinite)
/// - Revenue-only campaigns still appear
/// - Cost-only campaigns still appear
/// </summary>
public interface ICampaignRoiService
{
    /// <summary>
    /// Calculates ROI for all campaigns in the given period.
    /// </summary>
    Task<List<CampaignRoiResult>> CalculateAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        AttributionModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ROI summary by source (aggregated).
    /// </summary>
    Task<List<SourceRoiResult>> GetRoiBySourceAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        AttributionModel model,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// ROI result aggregated by source.
/// </summary>
public class SourceRoiResult
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public decimal Profit { get; set; }
    public decimal? RoiPercentage { get; set; }
    public decimal AverageOrderValue => TotalOrders > 0 ? TotalRevenue / TotalOrders : 0;
    public string Currency { get; set; } = "USD";
}

public class CampaignRoiService : ICampaignRoiService
{
    private readonly ICampaignCostService _campaignCostService;
    private readonly IOrderAttributionStore _attributionStore;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CampaignRoiService> _logger;

    public CampaignRoiService(
        ICampaignCostService campaignCostService,
        IOrderAttributionStore attributionStore,
        ApplicationDbContext dbContext,
        ILogger<CampaignRoiService> logger)
    {
        _campaignCostService = campaignCostService;
        _attributionStore = attributionStore;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<CampaignRoiResult>> CalculateAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        // 1. Get attribution revenue by campaign
        var campaignRevenue = await _attributionStore.GetRevenueByCampaignAsync(
            tenantId, from, to, model, cancellationToken);

        // 2. Get campaign costs for the period
        var campaignCosts = await _campaignCostService.GetByPeriodAsync(
            tenantId, from, to, cancellationToken);
        var tenantCurrency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);
        campaignCosts = FilterCurrencyMismatches(tenantId, campaignCosts, tenantCurrency);

        // 3. Build lookup for costs by campaign key
        var costLookup = new Dictionary<string, List<Core.Analytics.Attribution.CampaignCost>>();
        foreach (var cost in campaignCosts)
        {
            var key = $"{cost.Source}|{cost.Medium}|{cost.Campaign}";
            if (!costLookup.ContainsKey(key))
            {
                costLookup[key] = new List<Core.Analytics.Attribution.CampaignCost>();
            }
            costLookup[key].Add(cost);
        }

        // 4. Build results - start with attribution data
        var results = new Dictionary<string, CampaignRoiResult>();
        // Add all campaigns that have revenue
        foreach (var rev in campaignRevenue)
        {
            var key = $"{rev.Source.ToLowerInvariant()}|{rev.Medium.ToLowerInvariant()}|{(rev.Campaign ?? "").ToLowerInvariant()}";
            
            if (!results.ContainsKey(key))
            {
                results[key] = new CampaignRoiResult
                {
                    Source = rev.Source,
                    Medium = rev.Medium,
                    Campaign = rev.Campaign ?? "",
                    Revenue = 0,
                    Cost = 0,
                    Orders = 0,
                    Model = model
                };
            }

            results[key].Revenue += rev.TotalRevenue;
            results[key].Orders += rev.OrderCount;
        }

        // Add costs from declared campaign costs
        foreach (var costEntry in campaignCosts)
        {
            var key = costEntry.GetCampaignKey();
            if (!results.ContainsKey(key))
            {
                // Campaign with cost but no revenue yet
                results[key] = new CampaignRoiResult
                {
                    Source = costEntry.Source,
                    Medium = costEntry.Medium,
                    Campaign = costEntry.Campaign,
                    Revenue = 0,
                    Cost = 0,
                    Orders = 0,
                    Model = model
                };
            }

            // Sum up costs for overlapping periods
            results[key].Cost += costEntry.CostAmount;
        }

        // 5. Calculate ROI for each result
        foreach (var result in results.Values)
        {
            result.Currency = tenantCurrency;
            result.Profit = result.Revenue - result.Cost;
            
            // ROI% = (Profit / Cost) * 100
            // Safety: If Cost = 0, ROI is null (not infinite)
            if (result.Cost > 0)
            {
                result.RoiPercentage = Math.Round((result.Profit / result.Cost) * 100, 2);
            }
            else
            {
                result.RoiPercentage = null;
            }
        }

        _logger.LogInformation(
            "[CampaignROI] Calculated ROI for {Count} campaigns, model={Model}, period={From}-{To}",
            results.Count, model, from, to);

        // Sort by revenue descending
        return results.Values
            .OrderByDescending(r => r.Revenue)
            .ToList();
    }

    public async Task<List<SourceRoiResult>> GetRoiBySourceAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        AttributionModel model,
        CancellationToken cancellationToken = default)
    {
        // 1. Get revenue by source from attribution
        var sourceRevenue = await _attributionStore.GetRevenueBySourceAsync(
            tenantId, from, to, model, cancellationToken);

        // 2. Get campaign costs and aggregate by source
        var campaignCosts = await _campaignCostService.GetByPeriodAsync(
            tenantId, from, to, cancellationToken);
        var tenantCurrency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);
        campaignCosts = FilterCurrencyMismatches(tenantId, campaignCosts, tenantCurrency);

        var costBySource = campaignCosts
            .GroupBy(c => new { Source = c.Source, Medium = c.Medium })
            .ToDictionary(
                g => $"{g.Key.Source}|{g.Key.Medium}",
                g => new { Cost = g.Sum(c => c.CostAmount), Currency = g.First().Currency });

        // 3. Build results
        var results = new Dictionary<string, SourceRoiResult>();
        // Add sources with revenue
        foreach (var rev in sourceRevenue)
        {
            var key = $"{rev.Source.ToLowerInvariant()}|{rev.Medium.ToLowerInvariant()}";
            
            if (!results.ContainsKey(key))
            {
                results[key] = new SourceRoiResult
                {
                    Source = rev.Source,
                    Medium = rev.Medium,
                    TotalRevenue = 0,
                    TotalCost = 0,
                    TotalOrders = 0
                };
            }

            results[key].TotalRevenue += rev.TotalRevenue;
            results[key].TotalOrders += rev.OrderCount;
        }

        // Add costs
        foreach (var kvp in costBySource)
        {
            var parts = kvp.Key.Split('|');
            if (!results.ContainsKey(kvp.Key))
            {
                results[kvp.Key] = new SourceRoiResult
                {
                    Source = parts[0],
                    Medium = parts[1],
                    TotalRevenue = 0,
                    TotalCost = 0,
                    TotalOrders = 0
                };
            }

            results[kvp.Key].TotalCost += kvp.Value.Cost;
        }

        // Calculate ROI
        foreach (var result in results.Values)
        {
            result.Currency = tenantCurrency;
            result.Profit = result.TotalRevenue - result.TotalCost;
            
            if (result.TotalCost > 0)
            {
                result.RoiPercentage = Math.Round((result.Profit / result.TotalCost) * 100, 2);
            }
            else
            {
                result.RoiPercentage = null;
            }
        }

        return results.Values
            .OrderByDescending(r => r.TotalRevenue)
            .ToList();
    }

    private async Task<string> ResolveTenantCurrencyAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var currency = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new InvalidOperationException($"Tenant {tenantId:D} has no configured currency.");
        }

        return currency.Trim().ToUpperInvariant();
    }

    private List<Core.Analytics.Attribution.CampaignCost> FilterCurrencyMismatches(
        Guid tenantId,
        List<Core.Analytics.Attribution.CampaignCost> costs,
        string tenantCurrency)
    {
        var valid = costs
            .Where(cost => string.Equals(
                cost.Currency,
                tenantCurrency,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (valid.Count != costs.Count)
        {
            _logger.LogWarning(
                "[CampaignROI] Excluded {Count} campaign costs with non-authoritative currency | TenantId: {TenantId} | Currency: {Currency}",
                costs.Count - valid.Count,
                tenantId,
                tenantCurrency);
        }

        return valid;
    }
}
