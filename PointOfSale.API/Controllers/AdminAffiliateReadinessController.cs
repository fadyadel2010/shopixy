using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Models.Affiliate;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-23: Affiliate Readiness Check API.
/// 
/// Provides a single endpoint for frontend to check if affiliate features are available.
/// Used to show/hide affiliate UI elements and display setup guidance.
/// 
/// ARCHITECTURE:
/// - Read-only endpoint (no side effects)
/// - Tenant-scoped (checks current tenant's readiness)
/// - No caching (always fresh check)
/// - Returns detailed status for UX guidance
/// </summary>
[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/affiliate")]
public class AdminAffiliateReadinessController : ControllerBase
{
    private readonly IAffiliateReadinessService _affiliateReadiness;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAffiliateReadinessController> _logger;

    public AdminAffiliateReadinessController(
        IAffiliateReadinessService affiliateReadiness,
        ITenantContext tenantContext,
        ILogger<AdminAffiliateReadinessController> logger)
    {
        _affiliateReadiness = affiliateReadiness;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/affiliate/readiness
    /// 
    /// Checks if the tenant has completed all prerequisites to use affiliate features.
    /// 
    /// Returns:
    /// - isReady: true if all prerequisites met
    /// - storefrontLive: true if storefront is live
    /// - hasPaymentMethod: true if at least one active payment method exists
    /// - hasShippingMethod: true if at least one active shipping method exists
    /// - missingRequirements: array of human-readable missing items
    /// 
    /// Frontend should:
    /// - Disable affiliate toggle if isReady = false
    /// - Show warning card with missing requirements
    /// - Provide action buttons to navigate to setup pages
    /// </summary>
    [HttpGet("readiness")]
    public async Task<ActionResult<AffiliateReadinessResult>> CheckReadiness(CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var result = await _affiliateReadiness.CheckAsync(tenantId, ct);

        _logger.LogInformation(
            "[AF-23] Readiness check | TenantId: {TenantId} | IsReady: {IsReady}",
            tenantId, result.IsReady);

        return Ok(result);
    }
}
