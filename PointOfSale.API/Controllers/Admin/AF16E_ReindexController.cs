using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Scripts;

namespace PointOfSale.API.Controllers.Admin;

/// <summary>
/// AF-16E: Admin endpoint to trigger affiliate products reindex with StorefrontBaseUrl.
///
/// SECURITY: Admin-only endpoint.
/// USAGE: Call once after deploying AF-16E to backfill StorefrontBaseUrl.
/// </summary>
[ApiController]
[Route("api/admin/af16e-reindex")]
[Authorize(Roles = "Owner,Admin")]
public class AF16E_ReindexController : ControllerBase
{
    private readonly AF16E_ReindexAffiliateProductsWithStorefrontUrl _reindexService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AF16E_ReindexController> _logger;

    public AF16E_ReindexController(
        AF16E_ReindexAffiliateProductsWithStorefrontUrl reindexService,
        ITenantContext tenantContext,
        ILogger<AF16E_ReindexController> logger)
    {
        _reindexService = reindexService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Trigger reindex of all affiliate products with StorefrontBaseUrl.
    /// This is a one-time operation to backfill existing data.
    /// </summary>
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerReindex(CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        _logger.LogWarning("[AF-16E] Tenant-scoped reindex triggered by admin | TenantId: {TenantId}", tenantId);

        var result = await _reindexService.ReindexTenantAsync(tenantId, ct);

        if (result.Success)
        {
            return Ok(new
            {
                success = true,
                message = "Reindex completed successfully",
                updatedProducts = result.UpdatedProducts,
                updatedIntelligenceProducts = result.UpdatedIntelligenceProducts,
                skippedProducts = result.SkippedProducts,
                skippedIntelligenceProducts = result.SkippedIntelligenceProducts,
                failedProducts = result.FailedProducts,
                skippedTenants = result.SkippedTenants,
                tenantId,
                durationSeconds = result.DurationSeconds
            });
        }

        return StatusCode(500, new
        {
            success = false,
            message = "Reindex failed",
            error = result.ErrorMessage,
            updatedProducts = result.UpdatedProducts,
            updatedIntelligenceProducts = result.UpdatedIntelligenceProducts,
            skippedProducts = result.SkippedProducts,
            skippedIntelligenceProducts = result.SkippedIntelligenceProducts,
            failedProducts = result.FailedProducts
        });
    }

    /// <summary>
    /// Get reindex status (for monitoring).
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            message = "AF-16E reindex endpoint is available",
            tenantId = _tenantContext.RequiredTenantId,
            instructions = "POST to /api/admin/af16e-reindex/trigger to start a tenant-scoped reindex"
        });
    }
}
