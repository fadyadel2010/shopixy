using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.AI;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Commerce AI Controller.
/// Phase 9: Admin Insights (Event Collection)
/// </summary>
[ApiController]
[Route("api/storefront/commerce-ai")]
[AllowAnonymous]
public class StorefrontCommerceAiController : ControllerBase
{
    private readonly ICommerceAiInsightsService _insightsService;
    private readonly ITenantContext _tenantContext;

    public StorefrontCommerceAiController(
        ICommerceAiInsightsService insightsService,
        ITenantContext tenantContext)
    {
        _insightsService = insightsService;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Track a Commerce AI event (fire-and-forget).
    /// Used for privacy-safe admin insights.
    /// POST /api/storefront/commerce-ai/event
    /// </summary>
    [HttpPost("event")]
    [EnableRateLimiting("CommerceAiEventPolicy")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TrackEvent(
        [FromBody] CommerceAiEventDto request,
        CancellationToken cancellationToken = default)
    {
        // Tenant context is already guaranteed by TenantResolutionMiddleware for this route
        var tenantId = _tenantContext.RequiredTenantId;

        // 2. Track Event (Fire-and-forget)
        // We do not wait for tracking to complete to keep response fast
        // Exception handling is inside the service
        _ = _insightsService.TrackEventAsync(tenantId, request.Type);

        return Accepted();
    }
}
