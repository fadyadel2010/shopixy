using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.Localization;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.MarketplaceFooter;

namespace PointOfSale.API.Controllers;

[ApiController]
[AllowAnonymousTenant]
[Route("api/marketplace/footer")]
[AllowAnonymous]
public sealed class MarketplaceFooterController : ControllerBase
{
    private readonly IMarketplaceFooterReadService _footerReadService;
    private readonly ILogger<MarketplaceFooterController> _logger;

    public MarketplaceFooterController(
        IMarketplaceFooterReadService footerReadService,
        ILogger<MarketplaceFooterController> logger)
    {
        _footerReadService = footerReadService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(MarketplaceFooterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetFooter(
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var language = MarketplaceLocalization.ResolveLanguage(lang, Request.Headers.AcceptLanguage);
            var footer = await _footerReadService.GetFooterAsync(language, cancellationToken);
            return Ok(footer);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Marketplace footer request canceled | RequestId: {RequestId} | Path: {Path}",
                HttpContext.TraceIdentifier,
                HttpContext.Request.Path.Value ?? "unknown");
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace footer read failed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(HttpContext.TraceIdentifier));
        }
    }
}
