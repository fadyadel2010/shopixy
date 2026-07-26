using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Analytics;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/analytics/storefront-dashboard")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminStorefrontAnalyticsDashboardController : ControllerBase
{
    private static readonly string[] SupportedRanges = ["today", "7d", "30d", "this_month"];

    private readonly IStorefrontAnalyticsDashboardService _dashboardService;
    private readonly ITenantContext _tenantContext;

    public AdminStorefrontAnalyticsDashboardController(
        IStorefrontAnalyticsDashboardService dashboardService,
        ITenantContext tenantContext)
    {
        _dashboardService = dashboardService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(StorefrontAnalyticsDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string range = "7d",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var response = await _dashboardService.GetDashboardAsync(tenantId, range, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest(new
            {
                message = $"Invalid range. Supported values: {string.Join(", ", SupportedRanges)}"
            });
        }
    }
}
