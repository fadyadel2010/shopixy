using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.Cms;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/storefront/pages")]
[AllowAnonymous]
public sealed class StorefrontPagesController : ControllerBase
{
    private readonly ICmsPageReadService _cmsPageReadService;
    private readonly ICmsPagePreviewTokenService _cmsPagePreviewTokenService;
    private readonly ILogger<StorefrontPagesController> _logger;

    public StorefrontPagesController(
        ICmsPageReadService cmsPageReadService,
        ICmsPagePreviewTokenService cmsPagePreviewTokenService,
        ILogger<StorefrontPagesController> logger)
    {
        _cmsPageReadService = cmsPageReadService;
        _cmsPagePreviewTokenService = cmsPagePreviewTokenService;
        _logger = logger;
    }

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(CmsPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
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
            return BadRequest(CmsApiErrorResponse.InvalidSlug(traceId));
        }

        var tenantValidation = await ValidateTenantHeaderAsync(traceId, cancellationToken);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        var tenantId = tenantValidation.TenantId;

        try
        {
            var normalizedSlug = CmsPageReadService.NormalizeSlug(slug);
            var requestedLanguage = StorefrontLanguageResolver.Resolve(lang, Request.Headers.AcceptLanguage);

            if (preview)
            {
                if (!TryValidatePreviewToken(
                        tenantId!.Value,
                        normalizedSlug,
                        out var pageGroupId,
                        out var pageVersion))
                {
                    return Forbid();
                }

                var previewPage = await _cmsPageReadService.GetPreviewPageByIdAsync(
                    tenantId.Value,
                    pageGroupId,
                    pageVersion,
                    cancellationToken,
                    requestedLanguage);

                if (previewPage != null &&
                    !string.Equals(previewPage.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))
                {
                    previewPage = null;
                }

                if (previewPage == null)
                {
                    return NotFound(new ApiErrorResponse
                    {
                        Code = "PAGE_NOT_FOUND",
                        Message = $"Page '{slug}' not found in preview.",
                        TraceId = traceId
                    });
                }

                Response.Headers["X-Preview-Mode"] = "true";
                ApplyLanguageHeaders(previewPage.Language);
                return Ok(previewPage);
            }

            var page = await _cmsPageReadService.GetPublishedPageAsync(
                tenantId!.Value,
                normalizedSlug,
                cancellationToken,
                requestedLanguage);
            if (page != null)
            {
                ApplyLanguageHeaders(page.Language);
            }
            return page == null
                ? NotFound(CmsApiErrorResponse.PageNotFound(traceId))
                : Ok(page);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "GetPage validation failed | Slug: {Slug}", slug);
            return BadRequest(new ApiErrorResponse
            {
                Code = "INVALID_SLUG",
                Message = "Invalid page slug",
                TraceId = traceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPage failed | Slug: {Slug} | TenantId: {TenantId}", slug, tenantId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CmsPageListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListPages([FromQuery] string? lang, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantValidation = await ValidateTenantHeaderAsync(traceId, cancellationToken);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        try
        {
            var requestedLanguage = StorefrontLanguageResolver.Resolve(lang, Request.Headers.AcceptLanguage);
            var pages = await _cmsPageReadService.ListPublishedPagesAsync(
                tenantValidation.TenantId!.Value,
                cancellationToken,
                requestedLanguage);
            var actualLanguages = pages
                .Select(page => page.Language)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (actualLanguages.Length == 1)
            {
                Response.Headers.ContentLanguage = actualLanguages[0];
            }
            Response.Headers.Append("Vary", "Accept-Language");
            return Ok(pages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListPages failed | TenantId: {TenantId}", tenantValidation.TenantId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpGet("/api/storefront/contact")]
    [ProducesResponseType(typeof(CmsPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetContactPage(CancellationToken cancellationToken)
    {
        return GetPage("contact", false, null, cancellationToken);
    }

    private async Task<(Guid? TenantId, IActionResult? Error)> ValidateTenantHeaderAsync(
        string traceId,
        CancellationToken cancellationToken)
    {
        var validation = PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
        return await Task.FromResult((validation.TenantId, validation.Error));
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private bool TryValidatePreviewToken(
        Guid tenantId,
        string slug,
        out Guid pageGroupId,
        out int pageVersion)
    {
        pageGroupId = Guid.Empty;
        pageVersion = 0;

        if (!Request.Query.TryGetValue("previewToken", out var previewToken) ||
            string.IsNullOrWhiteSpace(previewToken))
        {
            return false;
        }

        return _cmsPagePreviewTokenService.TryValidatePreviewToken(
            previewToken.ToString(),
            tenantId,
            slug,
            out pageGroupId,
            out pageVersion);
    }

    private void ApplyLanguageHeaders(string language)
    {
        Response.Headers.ContentLanguage = language;
        Response.Headers.Append("Vary", "Accept-Language");
    }

}
