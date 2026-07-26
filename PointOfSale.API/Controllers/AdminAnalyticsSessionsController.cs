using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/analytics/sessions")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAnalyticsSessionsController : ControllerBase
{
    private readonly IAnalyticsSessionJourneyService _journeyService;
    private readonly ITenantContext _tenantContext;

    public AdminAnalyticsSessionsController(
        IAnalyticsSessionJourneyService journeyService,
        ITenantContext tenantContext)
    {
        _journeyService = journeyService;
        _tenantContext = tenantContext;
    }

    [HttpGet("{sessionId}/journey")]
    public async Task<IActionResult> GetJourney(
        string sessionId,
        [FromQuery] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            return BadRequest(new { message = "orderId is required" });
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var journey = await _journeyService.GetJourneyAsync(
            tenantId,
            orderId,
            sessionId,
            cancellationToken);

        if (journey == null)
        {
            return NotFound(new { message = "No session journey found for this order" });
        }

        return Ok(journey);
    }
}
