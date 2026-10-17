using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Attribution;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin controller for Attribution Engine analytics.
/// 
/// ENDPOINTS:
/// - GET /api/admin/analytics/attribution/orders - Orders with attribution
/// - GET /api/admin/analytics/attribution/summary - Revenue by source
/// - GET /api/admin/analytics/attribution/campaigns - Campaign ROI
/// - GET /api/admin/analytics/attribution/models - Compare First vs Last touch
/// 
/// SECURITY:
/// - All endpoints require POS Admin authentication
/// - Tenant-isolated (uses ITenantContext)
/// - Read-only (attribution data is immutable)
/// </summary>
[ApiController]
[Route("api/admin/analytics/attribution")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAnalyticsAttributionController : ControllerBase
{
    private readonly IOrderAttributionStore _attributionStore;
    private readonly IAttributionContextStore _contextStore;
    private readonly ITenantContext _tenantContext;

    public AdminAnalyticsAttributionController(
        IOrderAttributionStore attributionStore,
        IAttributionContextStore contextStore,
        ITenantContext tenantContext)
    {
        _attributionStore = attributionStore;
        _contextStore = contextStore;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Gets orders with their attribution data.
    /// </summary>
    [HttpGet("orders")]
    public async Task<IActionResult> GetOrdersWithAttribution(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? source,
        [FromQuery] string? campaign,
        [FromQuery] string? period,
        [FromQuery] AttributionModel model = AttributionModel.LastTouch,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(model)) return BadRequest(new { message = "Unsupported attribution model" });
        var tenantId = _tenantContext.RequiredTenantId;

        var (startUtc, endUtc, _) = ParsePeriod(period, startDate, endDate);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        var page = await _attributionStore.GetAttributionPageAsync(
            tenantId, startUtc, endUtc, model, source, campaign, skip, take, cancellationToken);

        var response = new OrderAttributionsResponse
        {
            StartDate = startUtc,
            EndDate = endUtc,
            Model = model.ToString(),
            TotalCount = checked((int)page.TotalCount),
            Orders = page.Items.Select(a => new OrderAttributionDto
            {
                OrderId = a.OrderId,
                Source = a.Source,
                Medium = a.Medium,
                Campaign = a.Campaign,
                Content = a.Content,
                Term = a.Term,
                OrderValue = a.OrderValue,
                Currency = a.Currency,
                AttributedAt = a.AttributedAtUtc,
                TimeToConversionMinutes = a.TimeToConversion.HasValue
                    ? (int)a.TimeToConversion.Value.TotalMinutes
                    : null
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets revenue summary by source.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetRevenueSummary(
        [FromQuery] string? period,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] AttributionModel model = AttributionModel.LastTouch,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(model)) return BadRequest(new { message = "Unsupported attribution model" });
        var tenantId = _tenantContext.RequiredTenantId;

        // Parse period or use date range
        var (startUtc, endUtc, periodLabel) = ParsePeriod(period, startDate, endDate);

        var revenueBySource = await _attributionStore.GetRevenueBySourceAsync(
            tenantId, startUtc, endUtc, model, cancellationToken);

        var response = new RevenueSummaryResponse
        {
            Period = periodLabel,
            StartDate = startUtc,
            EndDate = endUtc,
            Model = model.ToString(),
            TotalRevenue = revenueBySource.Sum(r => r.TotalRevenue),
            TotalOrders = revenueBySource.Sum(r => r.OrderCount),
            RevenueBySource = revenueBySource.Select(r => new SourceRevenueDto
            {
                Source = r.Source,
                Medium = r.Medium,
                Revenue = r.TotalRevenue,
                Orders = r.OrderCount,
                AverageOrderValue = r.AverageOrderValue,
                PercentageOfTotal = revenueBySource.Sum(x => x.TotalRevenue) > 0
                    ? Math.Round(r.TotalRevenue / revenueBySource.Sum(x => x.TotalRevenue) * 100, 2)
                    : 0
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets campaign ROI data.
    /// </summary>
    [HttpGet("campaigns")]
    public async Task<IActionResult> GetCampaignRoi(
        [FromQuery] string? period,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] AttributionModel model = AttributionModel.LastTouch,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(model)) return BadRequest(new { message = "Unsupported attribution model" });
        var tenantId = _tenantContext.RequiredTenantId;

        var (startUtc, endUtc, periodLabel) = ParsePeriod(period, startDate, endDate);

        var revenueByCampaign = await _attributionStore.GetRevenueByCampaignAsync(
            tenantId, startUtc, endUtc, model, cancellationToken);

        var response = new CampaignRoiResponse
        {
            Period = periodLabel,
            StartDate = startUtc,
            EndDate = endUtc,
            Model = model.ToString(),
            Campaigns = revenueByCampaign.Select(c => new CampaignRoiDto
            {
                Campaign = c.Campaign,
                Source = c.Source,
                Medium = c.Medium,
                Revenue = c.TotalRevenue,
                Orders = c.OrderCount,
                AverageOrderValue = c.AverageOrderValue
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Compares FirstTouch vs LastTouch attribution models.
    /// </summary>
    [HttpGet("models")]
    public async Task<IActionResult> CompareModels(
        [FromQuery] string? period,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var (startUtc, endUtc, periodLabel) = ParsePeriod(period, startDate, endDate);

        // Get revenue by source for both models
        var firstTouchData = await _attributionStore.GetRevenueBySourceAsync(
            tenantId, startUtc, endUtc, AttributionModel.FirstTouch, cancellationToken);

        var lastTouchData = await _attributionStore.GetRevenueBySourceAsync(
            tenantId, startUtc, endUtc, AttributionModel.LastTouch, cancellationToken);

        var response = new ModelComparisonResponse
        {
            Period = periodLabel,
            StartDate = startUtc,
            EndDate = endUtc,
            FirstTouch = new ModelSummaryDto
            {
                Model = "FirstTouch",
                TotalRevenue = firstTouchData.Sum(r => r.TotalRevenue),
                TotalOrders = firstTouchData.Sum(r => r.OrderCount),
                TopSources = firstTouchData
                    .OrderByDescending(r => r.TotalRevenue)
                    .Take(5)
                    .Select(r => new SourceRevenueDto
                    {
                        Source = r.Source,
                        Medium = r.Medium,
                        Revenue = r.TotalRevenue,
                        Orders = r.OrderCount,
                        AverageOrderValue = r.AverageOrderValue,
                        PercentageOfTotal = firstTouchData.Sum(x => x.TotalRevenue) > 0
                            ? Math.Round(r.TotalRevenue / firstTouchData.Sum(x => x.TotalRevenue) * 100, 2)
                            : 0
                    }).ToList()
            },
            LastTouch = new ModelSummaryDto
            {
                Model = "LastTouch",
                TotalRevenue = lastTouchData.Sum(r => r.TotalRevenue),
                TotalOrders = lastTouchData.Sum(r => r.OrderCount),
                TopSources = lastTouchData
                    .OrderByDescending(r => r.TotalRevenue)
                    .Take(5)
                    .Select(r => new SourceRevenueDto
                    {
                        Source = r.Source,
                        Medium = r.Medium,
                        Revenue = r.TotalRevenue,
                        Orders = r.OrderCount,
                        AverageOrderValue = r.AverageOrderValue,
                        PercentageOfTotal = lastTouchData.Sum(x => x.TotalRevenue) > 0
                            ? Math.Round(r.TotalRevenue / lastTouchData.Sum(x => x.TotalRevenue) * 100, 2)
                            : 0
                    }).ToList()
            }
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets attribution context history for a session (debugging/analysis).
    /// </summary>
    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionAttribution(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var contexts = await _contextStore.GetAllContextsForSessionAsync(
            tenantId, sessionId, cancellationToken);

        if (contexts.Count == 0)
        {
            return NotFound(new { message = "No attribution data found for this session" });
        }

        var response = new SessionAttributionResponse
        {
            SessionId = sessionId,
            Touchpoints = contexts.Select(c => new TouchpointDto
            {
                Source = c.Source,
                Medium = c.Medium,
                Campaign = c.Campaign,
                Content = c.Content,
                Term = c.Term,
                Referrer = c.Referrer,
                LandingPageUrl = c.LandingPageUrl,
                CapturedAt = c.CapturedAtUtc
            }).ToList(),
            FirstTouch = contexts.FirstOrDefault() != null ? new TouchpointDto
            {
                Source = contexts.First().Source,
                Medium = contexts.First().Medium,
                Campaign = contexts.First().Campaign,
                CapturedAt = contexts.First().CapturedAtUtc
            } : null,
            LastTouch = contexts.LastOrDefault() != null ? new TouchpointDto
            {
                Source = contexts.Last().Source,
                Medium = contexts.Last().Medium,
                Campaign = contexts.Last().Campaign,
                CapturedAt = contexts.Last().CapturedAtUtc
            } : null
        };

        return Ok(response);
    }

    #region Helpers

    private static (DateTime startUtc, DateTime endUtc, string label) ParsePeriod(
        string? period, DateTime? startDate, DateTime? endDate)
    {
        var now = DateTime.UtcNow;

        var range = period?.ToLowerInvariant() switch
        {
            "today" => (now.Date, now.Date.AddDays(1), "today"),
            "yesterday" => (now.Date.AddDays(-1), now.Date, "yesterday"),
            "last_7_days" => (now.Date.AddDays(-6), now.Date.AddDays(1), "last_7_days"),
            "last_30_days" => (now.Date.AddDays(-29), now.Date.AddDays(1), "last_30_days"),
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

#region Response DTOs

public class OrderAttributionsResponse
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Model { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public List<OrderAttributionDto> Orders { get; set; } = new();
}

public class OrderAttributionDto
{
    public Guid OrderId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string? Campaign { get; set; }
    public string? Content { get; set; }
    public string? Term { get; set; }
    public decimal OrderValue { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime AttributedAt { get; set; }
    public int? TimeToConversionMinutes { get; set; }
}

public class RevenueSummaryResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public List<SourceRevenueDto> RevenueBySource { get; set; } = new();
}

public class SourceRevenueDto
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Orders { get; set; }
    public decimal AverageOrderValue { get; set; }
    public decimal PercentageOfTotal { get; set; }
}

public class CampaignRoiResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Model { get; set; } = string.Empty;
    public List<CampaignRoiDto> Campaigns { get; set; } = new();
}

public class CampaignRoiDto
{
    public string Campaign { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Orders { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class ModelComparisonResponse
{
    public string Period { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public ModelSummaryDto FirstTouch { get; set; } = new();
    public ModelSummaryDto LastTouch { get; set; } = new();
}

public class ModelSummaryDto
{
    public string Model { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public List<SourceRevenueDto> TopSources { get; set; } = new();
}

public class SessionAttributionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<TouchpointDto> Touchpoints { get; set; } = new();
    public TouchpointDto? FirstTouch { get; set; }
    public TouchpointDto? LastTouch { get; set; }
}

public class TouchpointDto
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string? Campaign { get; set; }
    public string? Content { get; set; }
    public string? Term { get; set; }
    public string? Referrer { get; set; }
    public string? LandingPageUrl { get; set; }
    public DateTime CapturedAt { get; set; }
}

#endregion
