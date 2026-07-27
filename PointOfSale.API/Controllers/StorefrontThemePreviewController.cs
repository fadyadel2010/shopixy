using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/theme-preview")]
[AllowAnonymous]
[AllowAnonymousTenant]
public sealed class StorefrontThemePreviewController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly IStorefrontThemePreviewService _themePreviewService;
    private readonly ILogger<StorefrontThemePreviewController> _logger;

    public StorefrontThemePreviewController(
        ITenantContext tenantContext,
        IStorefrontThemePreviewService themePreviewService,
        ILogger<StorefrontThemePreviewController> logger)
    {
        _tenantContext = tenantContext;
        _themePreviewService = themePreviewService;
        _logger = logger;
    }

    [HttpGet("config")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(StorefrontStoreConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        if (_tenantContext.TenantId is not Guid tenantId ||
            tenantId == Guid.Empty ||
            !HttpContext.Items.TryGetValue(ThemePreviewAuthorityMiddleware.ThemeIdItemKey, out var themeIdValue) ||
            themeIdValue is not string themeId ||
            string.IsNullOrWhiteSpace(themeId))
        {
            return Unauthorized(new ApiErrorResponse(
                "THEME_PREVIEW_INVALID",
                "Theme preview capability is invalid or expired.",
                traceId));
        }

        try
        {
            var result = await _themePreviewService.GetConfigAsync(
                tenantId,
                themeId,
                cancellationToken);

            if (result.Found && result.Config != null)
            {
                return Ok(result.Config);
            }

            var error = new ApiErrorResponse(
                result.ErrorCode ?? "STOREFRONT_CONFIG_NOT_FOUND",
                result.ErrorMessage ?? "Store configuration was not found.",
                traceId);

            if (result.ErrorCode == "STOREFRONT_CURRENCY_NOT_CONFIGURED")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, error);
            }

            return NotFound(error);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Theme preview config failed | TenantId: {TenantId} | ThemeId: {ThemeId} | TraceId: {TraceId}",
                tenantId,
                themeId,
                traceId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }
}
