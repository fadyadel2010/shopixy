using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/marketplace/pages")]
public sealed class SuperAdminMarketplaceCmsPagesController : SuperAdminBaseController
{
    private readonly IMarketplaceCmsPageService _marketplaceCmsPageService;
    private readonly IMarketplaceCmsPagePreviewTokenService _previewTokenService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminMarketplaceCmsPagesController> _logger;

    public SuperAdminMarketplaceCmsPagesController(
        IMarketplaceCmsPageService marketplaceCmsPageService,
        IMarketplaceCmsPagePreviewTokenService previewTokenService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminMarketplaceCmsPagesController> logger)
    {
        _marketplaceCmsPageService = marketplaceCmsPageService;
        _previewTokenService = previewTokenService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPages(CancellationToken cancellationToken)
    {
        var pages = await _marketplaceCmsPageService.ListPagesAsync(cancellationToken);
        return OkResponse(pages);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPage(Guid id, CancellationToken cancellationToken)
    {
        var page = await _marketplaceCmsPageService.GetPageAsync(id, cancellationToken);
        return page == null
            ? ErrorResponse(StatusCodes.Status404NotFound, "MARKETPLACE_CMS_PAGE_NOT_FOUND", "Marketplace CMS page not found.")
            : OkResponse(page);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] MarketplaceCmsPageRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_REQUEST_INVALID", "Request body is required.");
        }

        var validationError = request.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_VALIDATION_ERROR", validationError);
        }

        try
        {
            var page = await _marketplaceCmsPageService.CreatePageAsync(request, cancellationToken);

            await _auditService.LogActionAsync(
                action: "MARKETPLACE_CMS_PAGE_CREATED",
                entityType: "MarketplaceCmsPage",
                entityId: page.Id.ToString(),
                description: "Super Admin created a marketplace CMS draft page.",
                success: true,
                details: page,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(page);
        }
        catch (SlugConflictException ex)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "MARKETPLACE_CMS_SLUG_EXISTS", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_VALIDATION_ERROR", ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] MarketplaceCmsPageRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_REQUEST_INVALID", "Request body is required.");
        }

        var validationError = request.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_VALIDATION_ERROR", validationError);
        }

        try
        {
            var page = await _marketplaceCmsPageService.UpdatePageAsync(id, request, cancellationToken);

            await _auditService.LogActionAsync(
                action: "MARKETPLACE_CMS_PAGE_UPDATED",
                entityType: "MarketplaceCmsPage",
                entityId: page.Id.ToString(),
                description: "Super Admin updated a marketplace CMS draft page.",
                success: true,
                details: page,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(page);
        }
        catch (SlugConflictException ex)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "MARKETPLACE_CMS_SLUG_EXISTS", ex.Message);
        }
        catch (ConcurrencyConflictException)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "MARKETPLACE_CMS_CONCURRENCY_CONFLICT", "This page was modified by another admin. Refresh and try again.");
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Marketplace CMS page not found.", StringComparison.Ordinal))
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "MARKETPLACE_CMS_PAGE_NOT_FOUND", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_VALIDATION_ERROR", ex.Message);
        }
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var expectedVersion = ReadExpectedVersion();
            var page = await _marketplaceCmsPageService.PublishPageAsync(id, expectedVersion, cancellationToken);

            await _auditService.LogActionAsync(
                action: "MARKETPLACE_CMS_PAGE_PUBLISHED",
                entityType: "MarketplaceCmsPage",
                entityId: page.Id.ToString(),
                description: "Super Admin published a marketplace CMS page.",
                success: true,
                details: page,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(page);
        }
        catch (SlugConflictException ex)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "MARKETPLACE_CMS_SLUG_EXISTS", ex.Message);
        }
        catch (ConcurrencyConflictException)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "MARKETPLACE_CMS_CONCURRENCY_CONFLICT", "This page was modified by another admin. Refresh and try again.");
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "Marketplace CMS page not found.", StringComparison.Ordinal) ||
            string.Equals(ex.Message, "No marketplace CMS draft exists to publish.", StringComparison.Ordinal))
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "MARKETPLACE_CMS_PUBLISH_FAILED", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "MARKETPLACE_CMS_PUBLISH_FAILED", ex.Message);
        }
    }

    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var page = await _marketplaceCmsPageService.GetPageAsync(id, cancellationToken);
        if (page == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "MARKETPLACE_CMS_PAGE_NOT_FOUND", "Marketplace CMS page not found.");
        }

        var version = page.Draft ?? page.Published;
        if (version == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "MARKETPLACE_CMS_PAGE_NOT_FOUND", "Marketplace CMS page not found.");
        }

        var previewToken = _previewTokenService.CreatePreviewToken(page.Id, version.Slug, version.Version);
        var response = new MarketplaceCmsPreviewAccessResponse
        {
            Slug = version.Slug,
            PreviewToken = previewToken.Token,
            ExpiresAtUtc = previewToken.ExpiresAtUtc
        };

        return OkResponse(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var expectedVersion = ReadExpectedVersion();
            await _marketplaceCmsPageService.DeletePageAsync(id, expectedVersion, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return ErrorResponse(StatusCodes.Status409Conflict, "MARKETPLACE_CMS_CONCURRENCY_CONFLICT", "This page was modified by another admin. Refresh and try again.");
        }

        await _auditService.LogActionAsync(
            action: "MARKETPLACE_CMS_PAGE_DELETED",
            entityType: "MarketplaceCmsPage",
            entityId: id.ToString(),
            description: "Super Admin deleted a marketplace CMS page.",
            success: true,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Marketplace CMS page deleted | PageGroupId: {PageGroupId}", id);

        return OkResponse(new { deleted = true, id });
    }

    private int ReadExpectedVersion()
    {
        if (Request.Query.TryGetValue("expectedVersion", out var expectedVersionValue) &&
            int.TryParse(expectedVersionValue.ToString(), out var expectedVersion) &&
            expectedVersion > 0)
        {
            return expectedVersion;
        }

        if (Request.Headers.TryGetValue("If-Match", out var ifMatchValue))
        {
            var normalized = ifMatchValue.ToString().Trim().Trim('"');
            if (int.TryParse(normalized, out expectedVersion) && expectedVersion > 0)
            {
                return expectedVersion;
            }
        }

        throw new ConcurrencyConflictException("MarketplaceCmsPage", Guid.Empty);
    }
}
