using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Store Config Controller - Read-only store configuration for storefront.
/// 
/// Route: GET /api/storefront/store-config
/// 
/// PHASE 7.1: THEME & STORE CONFIG SYSTEM
/// PHASE P3: TRUE DRAFT PREVIEW MODE
/// 
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - This API is READ-ONLY
/// - Store config is managed ONLY in POS Admin
/// - Current tenant-owned PostgreSQL config is authoritative
/// - Redis and MongoDB projections are derivative only
/// - Tenant isolation is mandatory
/// </summary>
[ApiController]
[Route("api/storefront/store-config")]
public class StorefrontStoreConfigController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly IStorefrontThemePreviewTokenService _themePreviewTokenService;
    private readonly ILogger<StorefrontStoreConfigController> _logger;

    public StorefrontStoreConfigController(
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        IStorefrontThemePreviewTokenService themePreviewTokenService,
        ILogger<StorefrontStoreConfigController> logger)
    {
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _themePreviewTokenService = themePreviewTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the store configuration for the storefront.
    /// 
    /// Read Flow (Normal):
    /// 1. Resolve trusted tenant context
    /// 2. Read current tenant-owned SQL StoreConfig and Tenant currency
    /// 3. Map live configuration only
    /// 4. Return backend authority or fail closed
    /// 
    /// SECURITY: preview content is never served from this public endpoint.
    /// Admin preview is available only via /api/admin/store-config/preview.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(StorefrontStoreConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetStoreConfig(
        [FromQuery] string? themePreviewToken = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();

        if (!_tenantContext.TenantId.HasValue || _tenantContext.TenantId.Value == Guid.Empty)
        {
            _logger.LogWarning("Missing verified tenant context | TraceId: {TraceId}", traceId);
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        var tenantId = _tenantContext.TenantId.Value;

        try
        {
            var authority = await _dbContext.StoreConfigs
                .AsNoTracking()
                .Where(config => config.TenantId == tenantId)
                .Join(
                    _dbContext.Tenants.AsNoTracking(),
                    config => config.TenantId,
                    tenant => tenant.Id,
                    (config, tenant) => new { Config = config, Tenant = tenant })
                .SingleOrDefaultAsync(cancellationToken);

            if (authority == null)
            {
                return NotFound(new ApiErrorResponse(
                    "STOREFRONT_CONFIG_NOT_FOUND",
                    "Store configuration was not found.",
                    traceId));
            }

            if (string.IsNullOrWhiteSpace(authority.Tenant.Currency))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse(
                    "STOREFRONT_CURRENCY_NOT_CONFIGURED",
                    "Store currency is not configured.",
                    traceId));
            }

            string? previewThemeId = null;
            if (!string.IsNullOrWhiteSpace(themePreviewToken))
            {
                if (!_themePreviewTokenService.TryValidate(themePreviewToken, tenantId, out previewThemeId))
                {
                    return Unauthorized(new ApiErrorResponse(
                        "THEME_PREVIEW_INVALID",
                        "Theme preview capability is invalid or expired.",
                        traceId));
                }
            }

            var preset = await _dbContext.ThemePresets
                .AsNoTracking()
                .Where(item => item.Key == authority.Config.ThemeLivePresetKey || item.Key == "default")
                .OrderByDescending(item => item.Key == authority.Config.ThemeLivePresetKey)
                .FirstOrDefaultAsync(cancellationToken);

            var config = StorefrontDraftConfigMapper.MapLive(
                authority.Config,
                preset,
                authority.Tenant.Currency.Trim().ToUpperInvariant(),
                CountryCodeHelper.NormalizeOrDefault(authority.Tenant.Country),
                (exception, operation) => _logger.LogWarning(
                    exception,
                    "Failed to map SQL storefront configuration | TenantId: {TenantId} | Operation: {Operation}",
                    tenantId,
                    operation),
                storefrontThemeOverride: previewThemeId);

            if (!string.IsNullOrWhiteSpace(previewThemeId))
            {
                config.StorefrontTheme = previewThemeId;
            }

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting store config | TenantId: {TenantId}", tenantId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
