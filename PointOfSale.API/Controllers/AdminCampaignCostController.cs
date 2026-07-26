using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Infrastructure.CampaignCost;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin controller for Campaign Cost & ROI management.
/// 
/// PHASE 8B: Campaign Cost & ROI (MVP)
/// 
/// ENDPOINTS:
/// Campaign Cost Management:
/// - GET    /api/admin/analytics/campaign-costs         - List all costs
/// - POST   /api/admin/analytics/campaign-costs         - Create/update cost
/// - PUT    /api/admin/analytics/campaign-costs/{id}    - Update cost
/// - DELETE /api/admin/analytics/campaign-costs/{id}    - Delete cost
/// 
/// ROI APIs:
/// - GET    /api/admin/analytics/roi/summary            - ROI by source
/// - GET    /api/admin/analytics/roi/campaigns          - ROI by campaign
/// 
/// SECURITY:
/// - All endpoints require POS Admin authentication
/// - Tenant-isolated (uses ITenantContext)
/// - Server-side only calculations
/// </summary>
[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminCampaignCostController : ControllerBase
{
    private readonly ICampaignCostService _campaignCostService;
    private readonly ICampaignRoiService _roiService;
    private readonly ITenantContext _tenantContext;
    private readonly IAnalyticsTenantCurrencyResolver _currencyResolver;

    public AdminCampaignCostController(
        ICampaignCostService campaignCostService,
        ICampaignRoiService roiService,
        ITenantContext tenantContext,
        IAnalyticsTenantCurrencyResolver currencyResolver)
    {
        _campaignCostService = campaignCostService;
        _roiService = roiService;
        _tenantContext = tenantContext;
        _currencyResolver = currencyResolver;
    }

    #region Campaign Cost CRUD

    /// <summary>
    /// Gets all campaign costs within a date range.
    /// </summary>
    [HttpGet("campaign-costs")]
    public async Task<IActionResult> GetCampaignCosts(
        [FromQuery] string? period,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var (startUtc, endUtc, periodLabel) = ParsePeriod(period, startDate, endDate);

        var costs = await _campaignCostService.GetByPeriodAsync(
            tenantId, startUtc, endUtc, cancellationToken);

        var response = new CampaignCostsResponse
        {
            Period = periodLabel,
            StartDate = startUtc,
            EndDate = endUtc,
            TotalCount = costs.Count,
            TotalCost = costs.Sum(c => c.CostAmount),
            Costs = costs.Select(c => new CampaignCostDto
            {
                CampaignCostId = c.CampaignCostId,
                Source = c.Source,
                Medium = c.Medium,
                Campaign = c.Campaign,
                PeriodStartUtc = c.PeriodStartUtc,
                PeriodEndUtc = c.PeriodEndUtc,
                CostAmount = c.CostAmount,
                Currency = c.Currency,
                Notes = c.Notes,
                CreatedAtUtc = c.CreatedAtUtc,
                UpdatedAtUtc = c.UpdatedAtUtc
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Creates or updates a campaign cost entry.
    /// </summary>
    [HttpPost("campaign-costs")]
    public async Task<IActionResult> CreateCampaignCost(
        [FromBody] CreateCampaignCostDto request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Validation
        if (string.IsNullOrWhiteSpace(request.Source))
            return BadRequest(new { message = "Source is required" });
        if (string.IsNullOrWhiteSpace(request.Medium))
            return BadRequest(new { message = "Medium is required" });
        if (string.IsNullOrWhiteSpace(request.Campaign))
            return BadRequest(new { message = "Campaign is required" });
        if (request.CostAmount < 0)
            return BadRequest(new { message = "Cost amount cannot be negative" });
        if (request.PeriodEndUtc <= request.PeriodStartUtc)
            return BadRequest(new { message = "Period end must be after period start" });

        var currencyResolution = await _currencyResolver.ResolveAsync(
            tenantId,
            [],
            cancellationToken);
        if (string.IsNullOrWhiteSpace(currencyResolution.TenantCurrency))
            return Conflict(new { message = "Tenant currency is not configured" });
        if (!string.IsNullOrWhiteSpace(request.Currency) &&
            !string.Equals(request.Currency.Trim(), currencyResolution.TenantCurrency, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Campaign cost currency must match tenant currency" });

        var cost = await _campaignCostService.CreateOrUpdateAsync(
            tenantId,
            new CreateCampaignCostRequest
            {
                Source = request.Source,
                Medium = request.Medium,
                Campaign = request.Campaign,
                PeriodStartUtc = request.PeriodStartUtc,
                PeriodEndUtc = request.PeriodEndUtc,
                CostAmount = request.CostAmount,
                Currency = currencyResolution.TenantCurrency,
                Notes = request.Notes
            },
            cancellationToken);

        return Ok(new CampaignCostDto
        {
            CampaignCostId = cost.CampaignCostId,
            Source = cost.Source,
            Medium = cost.Medium,
            Campaign = cost.Campaign,
            PeriodStartUtc = cost.PeriodStartUtc,
            PeriodEndUtc = cost.PeriodEndUtc,
            CostAmount = cost.CostAmount,
            Currency = cost.Currency,
            Notes = cost.Notes,
            CreatedAtUtc = cost.CreatedAtUtc,
            UpdatedAtUtc = cost.UpdatedAtUtc
        });
    }

    /// <summary>
    /// Updates a campaign cost entry.
    /// </summary>
    [HttpPut("campaign-costs/{id}")]
    public async Task<IActionResult> UpdateCampaignCost(
        Guid id,
        [FromBody] CreateCampaignCostDto request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Check if exists
        var existing = await _campaignCostService.GetByIdAsync(tenantId, id, cancellationToken);
        if (existing == null)
            return NotFound(new { message = "Campaign cost not found" });

        // Validation
        if (string.IsNullOrWhiteSpace(request.Source))
            return BadRequest(new { message = "Source is required" });
        if (string.IsNullOrWhiteSpace(request.Medium))
            return BadRequest(new { message = "Medium is required" });
        if (string.IsNullOrWhiteSpace(request.Campaign))
            return BadRequest(new { message = "Campaign is required" });
        if (request.CostAmount < 0)
            return BadRequest(new { message = "Cost amount cannot be negative" });
        if (request.PeriodEndUtc <= request.PeriodStartUtc)
            return BadRequest(new { message = "Period end must be after period start" });

        var currencyResolution = await _currencyResolver.ResolveAsync(
            tenantId,
            [],
            cancellationToken);
        if (string.IsNullOrWhiteSpace(currencyResolution.TenantCurrency))
            return Conflict(new { message = "Tenant currency is not configured" });
        if (!string.IsNullOrWhiteSpace(request.Currency) &&
            !string.Equals(request.Currency.Trim(), currencyResolution.TenantCurrency, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Campaign cost currency must match tenant currency" });

        // Delete old and create new (upsert pattern)
        await _campaignCostService.DeleteAsync(tenantId, id, cancellationToken);
        
        var cost = await _campaignCostService.CreateOrUpdateAsync(
            tenantId,
            new CreateCampaignCostRequest
            {
                Source = request.Source,
                Medium = request.Medium,
                Campaign = request.Campaign,
                PeriodStartUtc = request.PeriodStartUtc,
                PeriodEndUtc = request.PeriodEndUtc,
                CostAmount = request.CostAmount,
                Currency = currencyResolution.TenantCurrency,
                Notes = request.Notes
            },
            cancellationToken);

        return Ok(new CampaignCostDto
        {
            CampaignCostId = cost.CampaignCostId,
            Source = cost.Source,
            Medium = cost.Medium,
            Campaign = cost.Campaign,
            PeriodStartUtc = cost.PeriodStartUtc,
            PeriodEndUtc = cost.PeriodEndUtc,
            CostAmount = cost.CostAmount,
            Currency = cost.Currency,
            Notes = cost.Notes,
            CreatedAtUtc = cost.CreatedAtUtc,
            UpdatedAtUtc = cost.UpdatedAtUtc
        });
    }

    /// <summary>
    /// Deletes a campaign cost entry.
    /// </summary>
    [HttpDelete("campaign-costs/{id}")]
    public async Task<IActionResult> DeleteCampaignCost(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        
        var deleted = await _campaignCostService.DeleteAsync(tenantId, id, cancellationToken);
        
        if (!deleted)
            return NotFound(new { message = "Campaign cost not found" });

        return Ok(new { message = "Campaign cost deleted", id });
    }

    #endregion

    #region ROI APIs

    /// <summary>
    /// Gets ROI summary by source.
    /// </summary>
    [HttpGet("roi/summary")]
    public async Task<IActionResult> GetRoiSummary(
        [FromQuery] string? period,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] AttributionModel model = AttributionModel.LastTouch,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var (startUtc, endUtc, periodLabel) = ParsePeriod(period, startDate, endDate);

        var roiBySource = await _roiService.GetRoiBySourceAsync(
            tenantId, startUtc, endUtc, model, cancellationToken);

        var response = new RoiSummaryResponse
        {
            Period = periodLabel,
            StartDate = startUtc,
            EndDate = endUtc,
            Model = model.ToString(),
            TotalCost = roiBySource.Sum(r => r.TotalCost),
            TotalRevenue = roiBySource.Sum(r => r.TotalRevenue),
            TotalOrders = roiBySource.Sum(r => r.TotalOrders),
            TotalProfit = roiBySource.Sum(r => r.Profit),
            OverallRoi = CalculateOverallRoi(roiBySource),
            Sources = roiBySource.Select(r => new SourceRoiDto
            {
                Source = r.Source,
                Medium = r.Medium,
                Cost = r.TotalCost,
                Revenue = r.TotalRevenue,
                Orders = r.TotalOrders,
                Profit = r.Profit,
                RoiPercentage = r.RoiPercentage,
                AverageOrderValue = r.AverageOrderValue,
                Currency = r.Currency
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets ROI by campaign.
    /// </summary>
    [HttpGet("roi/campaigns")]
    public async Task<IActionResult> GetRoiByCampaign(
        [FromQuery] string? period,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] AttributionModel model = AttributionModel.LastTouch,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var (startUtc, endUtc, periodLabel) = ParsePeriod(period, startDate, endDate);

        var roiResults = await _roiService.CalculateAsync(
            tenantId, startUtc, endUtc, model, cancellationToken);

        var response = new CampaignCostRoiResponse
        {
            Period = periodLabel,
            StartDate = startUtc,
            EndDate = endUtc,
            Model = model.ToString(),
            TotalCost = roiResults.Sum(r => r.Cost),
            TotalRevenue = roiResults.Sum(r => r.Revenue),
            TotalOrders = roiResults.Sum(r => r.Orders),
            TotalProfit = roiResults.Sum(r => r.Profit),
            Campaigns = roiResults.Select(r => new CampaignRoiResultDto
            {
                Source = r.Source,
                Medium = r.Medium,
                Campaign = r.Campaign,
                Cost = r.Cost,
                Revenue = r.Revenue,
                Orders = r.Orders,
                Profit = r.Profit,
                RoiPercentage = r.RoiPercentage,
                AverageOrderValue = r.AverageOrderValue,
                CostPerAcquisition = r.CostPerAcquisition,
                Currency = r.Currency,
                RoiColor = r.GetRoiColor()
            }).ToList()
        };

        return Ok(response);
    }

    #endregion

    #region Helpers

    private static decimal? CalculateOverallRoi(List<SourceRoiResult> results)
    {
        var totalCost = results.Sum(r => r.TotalCost);
        var totalRevenue = results.Sum(r => r.TotalRevenue);
        
        if (totalCost <= 0) return null;
        
        var profit = totalRevenue - totalCost;
        return Math.Round((profit / totalCost) * 100, 2);
    }

    private static (DateTime startUtc, DateTime endUtc, string label) ParsePeriod(
        string? period, DateTime? startDate, DateTime? endDate)
    {
        var now = DateTime.UtcNow;

        var range = period?.ToLowerInvariant() switch
        {
            "today" => (now.Date, now.Date.AddDays(1), "today"),
            "yesterday" => (now.Date.AddDays(-1), now.Date, "yesterday"),
            "last_7_days" => (now.Date.AddDays(-7), now.Date.AddDays(1), "last_7_days"),
            "last_30_days" => (now.Date.AddDays(-30), now.Date.AddDays(1), "last_30_days"),
            "this_month" => (new DateTime(now.Year, now.Month, 1), now.Date.AddDays(1), "this_month"),
            "last_month" => (
                new DateTime(now.Year, now.Month, 1).AddMonths(-1),
                new DateTime(now.Year, now.Month, 1),
                "last_month"),
            _ => (
                startDate?.ToUniversalTime().Date ?? now.Date.AddDays(-30),
                endDate?.ToUniversalTime().Date.AddDays(1) ?? now.Date.AddDays(1),
                "custom")
        };

        return BoundRange(range);
    }

    private static (DateTime startUtc, DateTime endUtc, string label) BoundRange(
        (DateTime startUtc, DateTime endUtc, string label) range)
    {
        var latestEndUtc = DateTime.UtcNow.Date.AddDays(1);
        var endUtc = range.endUtc > latestEndUtc ? latestEndUtc : range.endUtc;
        var startUtc = range.startUtc < endUtc.AddDays(-366) ? endUtc.AddDays(-366) : range.startUtc;
        if (endUtc <= startUtc)
        {
            startUtc = endUtc.AddDays(-30);
        }

        return (
            DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(endUtc, DateTimeKind.Utc),
            range.label);
    }

    #endregion
}

#region DTOs

public class CampaignCostsResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalCount { get; set; }
    public decimal TotalCost { get; set; }
    public List<CampaignCostDto> Costs { get; set; } = new();
}

public class CampaignCostDto
{
    public Guid CampaignCostId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Campaign { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal CostAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class CreateCampaignCostDto
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Campaign { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal CostAmount { get; set; }
    public string? Currency { get; set; }
    public string? Notes { get; set; }
}

public class RoiSummaryResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal? OverallRoi { get; set; }
    public List<SourceRoiDto> Sources { get; set; } = new();
}

public class SourceRoiDto
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal Revenue { get; set; }
    public int Orders { get; set; }
    public decimal Profit { get; set; }
    public decimal? RoiPercentage { get; set; }
    public decimal AverageOrderValue { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class CampaignCostRoiResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalProfit { get; set; }
    public List<CampaignRoiResultDto> Campaigns { get; set; } = new();
}

public class CampaignRoiResultDto
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Campaign { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal Revenue { get; set; }
    public int Orders { get; set; }
    public decimal Profit { get; set; }
    public decimal? RoiPercentage { get; set; }
    public decimal AverageOrderValue { get; set; }
    public decimal? CostPerAcquisition { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string RoiColor { get; set; } = "gray";
}

#endregion
