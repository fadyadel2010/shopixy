using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Analytics;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/analytics/storefront-live")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminStorefrontLiveMonitoringController : ControllerBase
{
    private readonly IStorefrontLiveMonitoringService _liveMonitoringService;
    private readonly ITenantContext _tenantContext;

    public AdminStorefrontLiveMonitoringController(
        IStorefrontLiveMonitoringService liveMonitoringService,
        ITenantContext tenantContext)
    {
        _liveMonitoringService = liveMonitoringService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(StorefrontLiveMonitoringResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLiveMonitoring(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var response = await _liveMonitoringService.GetLiveMonitoringAsync(tenantId, cancellationToken);
        return Ok(response);
    }
}
