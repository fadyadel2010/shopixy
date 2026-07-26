using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.Localization;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.Services.MarketplaceCms;

namespace PointOfSale.API.Controllers;

[ApiController]
[AllowAnonymousTenant]
[Route("api/marketplace/pages")]
[AllowAnonymous]
public sealed class MarketplaceCmsPagesController : ControllerBase
{
    private readonly IMarketplaceCmsPageReadService _marketplaceCmsPageReadService;
    private readonly IMarketplaceCmsPagePreviewTokenService _marketplaceCmsPagePreviewTokenService;
    private readonly ILogger<MarketplaceCmsPagesController> _logger;

    public MarketplaceCmsPagesController(
        IMarketplaceCmsPageReadService marketplaceCmsPageReadService,
        IMarketplaceCmsPagePreviewTokenService marketplaceCmsPagePreviewTokenService,
        ILogger<MarketplaceCmsPagesController> logger)
    {
        _marketplaceCmsPageReadService = marketplaceCmsPageReadService;
        _marketplaceCmsPagePreviewTokenService = marketplaceCmsPagePreviewTokenService;
        _logger = logger;
    }

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(MarketplaceCmsPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPage(
        string slug,
        [FromQuery] bool preview = false,
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();

        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 160)
        {
            return BadRequest(MarketplaceCmsApiErrorResponse.InvalidSlug(traceId));
        }

        try
        {
            var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(slug);
            var language = MarketplaceLocalization.ResolveLanguage(lang, Request.Headers.AcceptLanguage);

            if (preview)
            {
                if (!TryValidatePreviewToken(normalizedSlug, out var pageGroupId, out var pageVersion))
                {
                    return Forbid();
                }

                var previewPage = await _marketplaceCmsPageReadService.GetPreviewPageByIdAsync(
                    pageGroupId,
                    pageVersion,
                    language,
                    cancellationToken);
                if (previewPage == null ||
                    !string.Equals(previewPage.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(MarketplaceCmsApiErrorResponse.PageNotFound(traceId));
                }

                Response.Headers["X-Preview-Mode"] = "true";
                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                return Ok(previewPage);
            }

            var page = await _marketplaceCmsPageReadService.GetPublishedPageAsync(normalizedSlug, language, cancellationToken);
            return page == null
                ? NotFound(MarketplaceCmsApiErrorResponse.PageNotFound(traceId))
                : Ok(page);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Marketplace CMS page validation failed | Slug: {Slug}", slug);
            return BadRequest(new ApiErrorResponse
            {
                Code = "INVALID_MARKETPLACE_CMS_SLUG",
                Message = "Invalid request.",
                TraceId = traceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace CMS page read failed | Slug: {Slug}", slug);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private bool TryValidatePreviewToken(string slug, out Guid pageGroupId, out int pageVersion)
    {
        pageGroupId = Guid.Empty;
        pageVersion = 0;

        if (!Request.Query.TryGetValue("previewToken", out var previewToken) ||
            string.IsNullOrWhiteSpace(previewToken))
        {
            return false;
        }

        return _marketplaceCmsPagePreviewTokenService.TryValidatePreviewToken(
            previewToken.ToString(),
            slug,
            out pageGroupId,
            out pageVersion);
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
