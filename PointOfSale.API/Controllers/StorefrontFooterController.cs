using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/footer")]
[AllowAnonymous]
public sealed class StorefrontFooterController : ControllerBase
{
    private readonly IStorefrontFooterReadService _footerReadService;
    private readonly ILogger<StorefrontFooterController> _logger;

    public StorefrontFooterController(
        IStorefrontFooterReadService footerReadService,
        ILogger<StorefrontFooterController> logger)
    {
        _footerReadService = footerReadService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(StorefrontFooterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetFooter(
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        try
        {
            var language = MarketplaceLocalization.ResolveLanguage(lang, Request.Headers.AcceptLanguage);
            var footer = await _footerReadService.GetFooterAsync(tenantValidation.TenantId!.Value, language, cancellationToken);
            return Ok(footer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetFooter failed | TenantId: {TenantId}", tenantValidation.TenantId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }
}
