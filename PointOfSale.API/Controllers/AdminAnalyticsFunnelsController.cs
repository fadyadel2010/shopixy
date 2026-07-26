using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Analytics.Funnels;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin controller for realtime conversion funnels.
/// 
/// FUNNELS:
/// - Purchase Funnel: PageView → ProductView → AddToCart → BeginCheckout → PurchaseCompleted
/// - Discovery Funnel: PageView → ProductImpression → ProductView → AddToCart
/// - Checkout Funnel: BeginCheckout → PurchaseCompleted
/// - Cart Funnel: ProductView → AddToCart → BeginCheckout
/// - Search Funnel: Search → ProductView → AddToCart → Purchase
/// 
/// ENDPOINTS:
/// - GET /api/admin/analytics/funnels         - List available funnels
/// - GET /api/admin/analytics/funnels/{key}   - Funnel definition
/// - GET /api/admin/analytics/funnels/{key}/realtime - Compute funnel
/// - GET /api/admin/analytics/funnels/all/realtime   - Compute all funnels
/// 
/// SECURITY:
/// - Admin authentication required
/// - Tenant-isolated
/// - Read-only (no data mutation)
/// </summary>
[ApiController]
[Route("api/admin/analytics/funnels")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAnalyticsFunnelsController : ControllerBase
{
    private readonly IAnalyticsFunnelService _funnelService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAnalyticsFunnelsController> _logger;

    // Supported time windows
    private static readonly int[] SupportedWindows = { 15, 30, 60, 180, 360, 720, 1440 };

    public AdminAnalyticsFunnelsController(
        IAnalyticsFunnelService funnelService,
        ITenantContext tenantContext,
        ILogger<AdminAnalyticsFunnelsController> logger)
    {
        _funnelService = funnelService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Lists all available funnel definitions.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<FunnelDefinitionResponse>), StatusCodes.Status200OK)]
    public IActionResult GetFunnelDefinitions()
    {
        var definitions = _funnelService.GetFunnelDefinitions();
        var response = definitions.Select(MapToResponse).ToList();
        return Ok(response);
    }

    /// <summary>
    /// Gets a specific funnel definition.
    /// </summary>
    [HttpGet("{funnelKey}")]
    [ProducesResponseType(typeof(FunnelDefinitionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetFunnelDefinition([FromRoute] string funnelKey)
    {
        var definition = _funnelService.GetFunnelDefinition(funnelKey);

        if (definition == null)
        {
            return NotFound(new { error = $"Funnel '{funnelKey}' not found" });
        }

        return Ok(MapToResponse(definition));
    }

    /// <summary>
    /// Computes a funnel in realtime.
    /// </summary>
    /// <param name="funnelKey">Funnel key (e.g., "purchase_funnel")</param>
    /// <param name="windowMinutes">Time window: 15, 30, 60, 180, 360, 720, or 1440 (24h)</param>
    [HttpGet("{funnelKey}/realtime")]
    [ProducesResponseType(typeof(FunnelResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ComputeFunnel(
        [FromRoute] string funnelKey,
        [FromQuery] int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Validate window
        if (!SupportedWindows.Contains(windowMinutes))
        {
            return BadRequest(new 
            { 
                error = $"Invalid windowMinutes. Supported values: {string.Join(", ", SupportedWindows)}" 
            });
        }

        var result = await _funnelService.ComputeFunnelAsync(
            tenantId, funnelKey, windowMinutes, cancellationToken);

        if (result == null)
        {
            return NotFound(new { error = $"Funnel '{funnelKey}' not found" });
        }

        _logger.LogDebug(
            "[Admin Funnels] Computed | TenantId: {TenantId} | Funnel: {Funnel} | Window: {Window}min",
            tenantId, funnelKey, windowMinutes);

        return Ok(MapToResponse(result));
    }

    /// <summary>
    /// Computes all funnels in realtime.
    /// </summary>
    /// <param name="windowMinutes">Time window: 15, 30, 60, 180, 360, 720, or 1440 (24h)</param>
    [HttpGet("all/realtime")]
    [ProducesResponseType(typeof(List<FunnelResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ComputeAllFunnels(
        [FromQuery] int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Validate window
        if (!SupportedWindows.Contains(windowMinutes))
        {
            return BadRequest(new 
            { 
                error = $"Invalid windowMinutes. Supported values: {string.Join(", ", SupportedWindows)}" 
            });
        }

        var results = await _funnelService.ComputeAllFunnelsAsync(
            tenantId, windowMinutes, cancellationToken);

        _logger.LogDebug(
            "[Admin Funnels] Computed all | TenantId: {TenantId} | Window: {Window}min | Count: {Count}",
            tenantId, windowMinutes, results.Count);

        return Ok(results.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets a comparison of a funnel across multiple time windows.
    /// </summary>
    [HttpGet("{funnelKey}/compare")]
    [ProducesResponseType(typeof(FunnelComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompareFunnel(
        [FromRoute] string funnelKey,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var definition = _funnelService.GetFunnelDefinition(funnelKey);
        if (definition == null)
        {
            return NotFound(new { error = $"Funnel '{funnelKey}' not found" });
        }

        // Compute for multiple windows
        var windows = new[] { 15, 60, 1440 }; // 15min, 1h, 24h
        var results = new List<FunnelResultResponse>();

        foreach (var window in windows)
        {
            var result = await _funnelService.ComputeFunnelAsync(
                tenantId, funnelKey, window, cancellationToken);
            
            if (result != null)
            {
                results.Add(MapToResponse(result));
            }
        }

        return Ok(new FunnelComparisonResponse
        {
            FunnelKey = funnelKey,
            FunnelName = definition.Name,
            Windows = results
        });
    }

    /// <summary>
    /// Gets top viewed pages for a time window.
    /// </summary>
    [HttpGet("pages/top")]
    [ProducesResponseType(typeof(TopPagesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopPages(
        [FromQuery] int windowMinutes = 60,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var pages = await _funnelService.GetTopPagesAsync(tenantId, windowMinutes, limit, cancellationToken);
        
        return Ok(new TopPagesResponse
        {
            WindowMinutes = windowMinutes,
            TotalPageViews = pages.Sum(p => p.ViewCount),
            Pages = pages.Select(p => new TopPageStatsDto
            {
                PagePath = p.PagePath,
                ViewCount = p.ViewCount,
                Percentage = p.PercentageOfTotal
            }).ToList()
        });
    }

    /// <summary>
    /// Gets top viewed products for a time window.
    /// </summary>
    [HttpGet("products/top-viewed")]
    [ProducesResponseType(typeof(TopProductsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopViewedProducts(
        [FromQuery] int windowMinutes = 60,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var products = await _funnelService.GetTopViewedProductsAsync(tenantId, windowMinutes, limit, cancellationToken);
        
        return Ok(new TopProductsResponse
        {
            WindowMinutes = windowMinutes,
            TotalCount = products.Sum(p => p.Count),
            Products = products.Select(p => new TopProductStatsDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                Count = p.Count
            }).ToList()
        });
    }

    /// <summary>
    /// Gets top added-to-cart products for a time window.
    /// </summary>
    [HttpGet("products/top-cart")]
    [ProducesResponseType(typeof(TopProductsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopAddedToCartProducts(
        [FromQuery] int windowMinutes = 60,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var products = await _funnelService.GetTopAddedToCartProductsAsync(tenantId, windowMinutes, limit, cancellationToken);
        
        return Ok(new TopProductsResponse
        {
            WindowMinutes = windowMinutes,
            TotalCount = products.Sum(p => p.Count),
            Products = products.Select(p => new TopProductStatsDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                Count = p.Count
            }).ToList()
        });
    }

    #region Mapping

    private static FunnelDefinitionResponse MapToResponse(AnalyticsFunnelDefinition definition)
    {
        return new FunnelDefinitionResponse
        {
            FunnelKey = definition.FunnelKey,
            Name = definition.Name,
            Description = definition.Description,
            IsSystem = definition.IsSystem,
            StepCount = definition.Steps.Count,
            Steps = definition.Steps.Select(s => new FunnelStepDefinitionResponse
            {
                Order = s.Order,
                Name = s.Name,
                EventType = s.EventType.ToString()
            }).ToList()
        };
    }

    private static FunnelResultResponse MapToResponse(AnalyticsFunnelResult result)
    {
        return new FunnelResultResponse
        {
            FunnelKey = result.FunnelKey,
            FunnelName = result.FunnelName,
            WindowMinutes = result.WindowMinutes,
            WindowLabel = GetWindowLabel(result.WindowMinutes),
            ComputedAtUtc = result.ComputedAtUtc,
            EntryCount = result.EntryCount,
            CompletionCount = result.CompletionCount,
            OverallConversionRate = result.OverallConversionRate,
            OverallDropOffRate = result.OverallDropOffRate,
            Steps = result.Steps.Select(s => new FunnelStepResultResponse
            {
                Order = s.Order,
                Name = s.Name,
                EventType = s.EventType.ToString(),
                Count = s.Count,
                ConversionFromPrevious = s.ConversionFromPrevious,
                DropOffFromPrevious = s.DropOffFromPrevious,
                ConversionFromEntry = s.ConversionFromEntry,
                DropOffCount = s.DropOffCount,
                TopPages = s.TopPages?.Select(p => new TopPageStatsDto 
                { 
                    PagePath = p.PagePath, 
                    ViewCount = p.ViewCount, 
                    Percentage = p.PercentageOfTotal 
                }).ToList(),
                TopProducts = s.TopProducts?.Select(p => new TopProductStatsDto 
                { 
                    ProductId = p.ProductId, 
                    ProductName = p.ProductName, 
                    Count = p.Count 
                }).ToList()
            }).ToList()
        };
    }

    private static string GetWindowLabel(int minutes)
    {
        return minutes switch
        {
            15 => "Last 15 minutes",
            30 => "Last 30 minutes",
            60 => "Last hour",
            180 => "Last 3 hours",
            360 => "Last 6 hours",
            720 => "Last 12 hours",
            1440 => "Last 24 hours",
            _ => $"Last {minutes} minutes"
        };
    }

    #endregion
}

#region DTOs

public class FunnelDefinitionResponse
{
    public string FunnelKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public int StepCount { get; set; }
    public List<FunnelStepDefinitionResponse> Steps { get; set; } = new();
}

public class FunnelStepDefinitionResponse
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}

public class FunnelResultResponse
{
    public string FunnelKey { get; set; } = string.Empty;
    public string FunnelName { get; set; } = string.Empty;
    public int WindowMinutes { get; set; }
    public string WindowLabel { get; set; } = string.Empty;
    public DateTime ComputedAtUtc { get; set; }
    public long EntryCount { get; set; }
    public long CompletionCount { get; set; }
    public decimal OverallConversionRate { get; set; }
    public decimal OverallDropOffRate { get; set; }
    public List<FunnelStepResultResponse> Steps { get; set; } = new();
}

public class FunnelStepResultResponse
{
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public long Count { get; set; }
    public decimal ConversionFromPrevious { get; set; }
    public decimal DropOffFromPrevious { get; set; }
    public decimal ConversionFromEntry { get; set; }
    public long DropOffCount { get; set; }
    public List<TopPageStatsDto>? TopPages { get; set; }
    public List<TopProductStatsDto>? TopProducts { get; set; }
}

public class TopPagesResponse
{
    public int WindowMinutes { get; set; }
    public long TotalPageViews { get; set; }
    public List<TopPageStatsDto> Pages { get; set; } = new();
}

public class TopPageStatsDto
{
    public string PagePath { get; set; } = string.Empty;
    public long ViewCount { get; set; }
    public decimal Percentage { get; set; }
}

public class TopProductsResponse
{
    public int WindowMinutes { get; set; }
    public long TotalCount { get; set; }
    public List<TopProductStatsDto> Products { get; set; } = new();
}

public class TopProductStatsDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class FunnelComparisonResponse
{
    public string FunnelKey { get; set; } = string.Empty;
    public string FunnelName { get; set; } = string.Empty;
    public List<FunnelResultResponse> Windows { get; set; } = new();
}

#endregion
