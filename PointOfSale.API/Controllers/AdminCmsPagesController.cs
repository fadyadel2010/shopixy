using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/cms")]
[Authorize(Roles = "Owner,Admin")]
public sealed class AdminCmsPagesController : ControllerBase
{
    private readonly IAdminCmsService _cmsService;
    private readonly ICmsSeedService _cmsSeedService;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ITenantContext _tenantContext;
    private readonly StorefrontUrlResolver _storefrontUrlResolver;
    private readonly ILogger<AdminCmsPagesController> _logger;

    public AdminCmsPagesController(
        IAdminCmsService cmsService,
        ICmsSeedService cmsSeedService,
        CommerceReliabilityTelemetry telemetry,
        ITenantContext tenantContext,
        StorefrontUrlResolver storefrontUrlResolver,
        ILogger<AdminCmsPagesController> logger)
    {
        _cmsService = cmsService;
        _cmsSeedService = cmsSeedService;
        _telemetry = telemetry;
        _tenantContext = tenantContext;
        _storefrontUrlResolver = storefrontUrlResolver;
        _logger = logger;
    }

    [HttpGet("pages")]
    [HttpGet("/api/admin/pages")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminCmsPageListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListCmsPages(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.ListCmsPagesAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListCmsPages failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("pages/{id:guid}")]
    [HttpGet("/api/admin/pages/{id:guid}")]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCmsPage(Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.GetCmsPageAsync(
                validation.TenantId!.Value,
                id,
                traceId,
                cancellationToken);

            return result == null
                ? NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId))
                : Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCmsPage failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("pages/{id:guid}/versions")]
    [HttpGet("/api/admin/pages/{id:guid}/versions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminCmsPageVersionHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCmsPageVersionHistory(Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.GetCmsPageVersionHistoryAsync(
                validation.TenantId!.Value,
                id,
                traceId,
                cancellationToken);

            return result == null
                ? NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId))
                : Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCmsPageVersionHistory failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("pages/by-slug/{slug}")]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCmsPageBySlug(string slug, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest(AdminCmsApiErrorResponse.InvalidSlug(traceId));
        }

        try
        {
            var result = await _cmsService.GetCmsPageBySlugAsync(
                validation.TenantId!.Value,
                slug,
                traceId,
                cancellationToken);

            return result == null
                ? NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId))
                : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCmsPageBySlug failed | Slug: {Slug} | TenantId: {TenantId}", slug, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("pages")]
    [HttpPost("/api/admin/pages")]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCmsPage(
        [FromBody] AdminCmsPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var validationError = request.Validate();
        if (validationError != null)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(validationError, traceId));
        }

        try
        {
            var result = await _cmsService.CreateCmsPageAsync(
                validation.TenantId!.Value,
                request,
                traceId,
                cancellationToken);

            return CreatedAtAction(nameof(GetCmsPage), new { id = result.Id }, result);
        }
        catch (SlugConflictException ex)
        {
            return Conflict(new AdminCmsApiErrorResponse("SLUG_EXISTS", ex.Message, traceId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateCmsPage failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPut("pages/{id:guid}")]
    [HttpPut("/api/admin/pages/{id:guid}")]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCmsPage(
        Guid id,
        [FromBody] AdminCmsPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var validationError = request.Validate();
        if (validationError != null)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(validationError, traceId));
        }

        if (!request.ExpectedVersion.HasValue || request.ExpectedVersion.Value < 1)
        {
            return CmsVersionPreconditionRequired(traceId);
        }

        try
        {
            var result = await _cmsService.UpdateCmsPageAsync(
                validation.TenantId!.Value,
                id,
                request,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (SlugConflictException ex)
        {
            return Conflict(new AdminCmsApiErrorResponse("SLUG_EXISTS", ex.Message, traceId));
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict updating CMS page | PageId: {PageId}", id);
            return await BuildCmsPageConflictResponse(
                validation.TenantId!.Value,
                id,
                traceId,
                "This page was updated by another admin. Review the latest version before saving again.",
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "CMS page not found.", StringComparison.Ordinal))
        {
            return NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateCmsPage failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("pages/{id:guid}/publish")]
    [HttpPost("/api/admin/pages/{id:guid}/publish")]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PublishCmsPage(Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var expectedVersion = GetExpectedCmsVersion();
        if (!expectedVersion.HasValue)
        {
            return CmsVersionPreconditionRequired(traceId);
        }

        try
        {
            var result = await _cmsService.PublishCmsPageAsync(
                validation.TenantId!.Value,
                id,
                expectedVersion.Value,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (SlugConflictException ex)
        {
            return Conflict(new AdminCmsApiErrorResponse("SLUG_EXISTS", ex.Message, traceId));
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict publishing CMS page | PageId: {PageId}", id);
            return await BuildCmsPageConflictResponse(
                validation.TenantId!.Value,
                id,
                traceId,
                "This page changed before it could be published. Reload the latest version and confirm the publish again.",
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            string.Equals(ex.Message, "CMS page not found.", StringComparison.Ordinal) ||
            string.Equals(ex.Message, "No draft page exists to publish.", StringComparison.Ordinal))
        {
            return NotFound(new AdminCmsApiErrorResponse("PUBLISH_FAILED", ex.Message, traceId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AdminCmsApiErrorResponse("PUBLISH_FAILED", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublishCmsPage failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("pages/{id:guid}/rollback")]
    [HttpPost("/api/admin/pages/{id:guid}/rollback")]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RollbackCmsPage(
        Guid id,
        [FromBody] AdminCmsRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var expectedVersion = GetExpectedCmsVersion();
        if (!expectedVersion.HasValue)
        {
            return CmsVersionPreconditionRequired(traceId);
        }

        if (request.Version < 1)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError("Rollback version is required.", traceId));
        }

        try
        {
            var result = await _cmsService.RollbackCmsPageAsync(
                validation.TenantId!.Value,
                id,
                request.Version,
                expectedVersion.Value,
                request.Reason,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict rolling back CMS page | PageId: {PageId}", id);
            return await BuildCmsPageConflictResponse(
                validation.TenantId!.Value,
                id,
                traceId,
                "This page changed before rollback completed. Reload the latest version and confirm rollback again.",
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "CMS page not found.", StringComparison.Ordinal))
        {
            return NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId));
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "Rollback version was not found.", StringComparison.Ordinal))
        {
            return NotFound(new AdminCmsApiErrorResponse("ROLLBACK_VERSION_NOT_FOUND", ex.Message, traceId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AdminCmsApiErrorResponse("ROLLBACK_FAILED", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RollbackCmsPage failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("pages/{id:guid}/preview")]
    [HttpGet("/api/admin/pages/{id:guid}/preview")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminCmsPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewCmsPage(Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.GetCmsPagePreviewAsync(
                validation.TenantId!.Value,
                id,
                traceId,
                cancellationToken);

            return result == null
                ? NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId))
                : Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreviewCmsPage failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("pages/{id:guid}/preview-link")]
    [HttpPost("/api/admin/pages/{id:guid}/preview-link")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminCmsPreviewAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateCmsPagePreviewLink(Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.CreateCmsPagePreviewAccessAsync(
                validation.TenantId!.Value,
                id,
                traceId,
                cancellationToken);

            if (result != null)
            {
                result.PreviewUrl = await _storefrontUrlResolver.BuildCmsPagePreviewUrlAsync(
                    validation.TenantId.Value,
                    result.Slug,
                    result.PreviewToken,
                    cancellationToken);
            }

            return result == null
                ? NotFound(AdminCmsApiErrorResponse.PageNotFound(traceId))
                : Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateCmsPagePreviewLink failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpDelete("pages/{id:guid}")]
    [HttpDelete("/api/admin/pages/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteCmsPage(Guid id, CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var expectedVersion = GetExpectedCmsVersion();
        if (!expectedVersion.HasValue)
        {
            return CmsVersionPreconditionRequired(traceId);
        }

        try
        {
            await _cmsService.DeleteCmsPageAsync(
                validation.TenantId!.Value,
                id,
                expectedVersion.Value,
                traceId,
                cancellationToken);

            return NoContent();
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict deleting CMS page | PageId: {PageId}", id);
            return await BuildCmsPageConflictResponse(
                validation.TenantId!.Value,
                id,
                traceId,
                "This page changed before it could be deleted. Reload the latest version and confirm deletion again.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteCmsPage failed | PageId: {PageId} | TenantId: {TenantId}", id, validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("seed")]
    [ProducesResponseType(typeof(AdminCmsSeedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SeedDefaultPages(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsSeedService.SeedAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);
            return Ok(result);
        }
        catch (CmsSeedException ex)
        {
            return StatusCode(
                ex.StatusCode,
                new AdminCmsApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SeedDefaultPages failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("navigation")]
    [ProducesResponseType(typeof(AdminNavigationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetNavigation(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.GetNavigationAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);

            ApplyNavigationConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (NavigationAdmissionException ex)
        {
            return BuildNavigationAdmissionResponse(ex, traceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetNavigation failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("navigation/targets")]
    [ProducesResponseType(typeof(AdminNavigationTargetsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNavigationTargets(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _cmsService.GetNavigationTargetsAsync(
                validation.TenantId!.Value,
                traceId,
                cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetNavigationTargets failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPut("navigation")]
    [ProducesResponseType(typeof(AdminNavigationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateNavigation(
        [FromBody] AdminNavigationRequest? request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var precondition = RequireNavigationRowVersion(traceId, request?.RowVersion);
        if (precondition.Error != null)
        {
            return precondition.Error;
        }

        try
        {
            var result = await _cmsService.UpdateNavigationAsync(
                validation.TenantId!.Value,
                request,
                precondition.RowVersion!.Value,
                traceId,
                cancellationToken);

            ApplyNavigationConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (NavigationAdmissionException ex)
        {
            return BuildNavigationAdmissionResponse(ex, traceId);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict updating navigation | TenantId: {TenantId}", validation.TenantId);
            return await BuildNavigationConflictResponse(
                validation.TenantId!.Value,
                traceId,
                "Navigation changed while you were editing it. Reload the latest navigation before saving again.",
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(AdminCmsApiErrorResponse.ValidationError(ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateNavigation failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpPost("navigation/publish")]
    [ProducesResponseType(typeof(AdminNavigationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminCmsApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PublishNavigation(CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var precondition = RequireNavigationRowVersion(traceId);
        if (precondition.Error != null)
        {
            return precondition.Error;
        }

        try
        {
            var result = await _cmsService.PublishNavigationAsync(
                validation.TenantId!.Value,
                precondition.RowVersion!.Value,
                traceId,
                cancellationToken);

            ApplyNavigationConcurrencyHeaders(result.RowVersion);
            return Ok(result);
        }
        catch (NavigationAdmissionException ex)
        {
            return BuildNavigationAdmissionResponse(ex, traceId);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict publishing navigation | TenantId: {TenantId}", validation.TenantId);
            return await BuildNavigationConflictResponse(
                validation.TenantId!.Value,
                traceId,
                "Navigation changed before publish completed. Reload the latest version and confirm the publish again.",
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AdminCmsApiErrorResponse("PUBLISH_FAILED", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublishNavigation failed | TenantId: {TenantId}", validation.TenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminCmsApiErrorResponse.InternalError(traceId));
        }
    }

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        try
        {
            return (_tenantContext.RequiredTenantId, null);
        }
        catch (InvalidOperationException)
        {
            return (null, BadRequest(AdminCmsApiErrorResponse.TenantHeaderMissing(traceId)));
        }
    }

    private int? GetExpectedCmsVersion()
    {
        var raw = Request.Headers["If-Match"].ToString().Trim();
        if (raw.StartsWith("W/\"", StringComparison.OrdinalIgnoreCase) && raw.EndsWith('"'))
        {
            raw = raw[3..^1];
        }
        else if (raw.StartsWith('"') && raw.EndsWith('"'))
        {
            raw = raw[1..^1];
        }

        return int.TryParse(raw, out var version) && version > 0 ? version : null;
    }

    private ObjectResult CmsVersionPreconditionRequired(string traceId)
    {
        return StatusCode(
            StatusCodes.Status428PreconditionRequired,
            new AdminCmsApiErrorResponse(
                "PRECONDITION_REQUIRED",
                "The current CMS page version is required for this mutation.",
                traceId));
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private async Task<ConflictObjectResult> BuildCmsPageConflictResponse(
        Guid tenantId,
        Guid pageId,
        string traceId,
        string message,
        CancellationToken cancellationToken)
    {
        _telemetry.RecordCmsConflictDetected("cms_page");

        object? latestVersion = null;
        try
        {
            latestVersion = await _cmsService.GetCmsPageAsync(
                tenantId,
                pageId,
                traceId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CMS] Failed to fetch latest version for conflict response. PageId: {PageId}, TraceId: {TraceId}", pageId, traceId);
        }

        return Conflict(new AdminCmsApiErrorResponse(
            "CONCURRENCY_CONFLICT",
            message,
            traceId)
        {
            LatestVersion = latestVersion,
        });
    }

    private async Task<ConflictObjectResult> BuildNavigationConflictResponse(
        Guid tenantId,
        string traceId,
        string message,
        CancellationToken cancellationToken)
    {
        _telemetry.RecordCmsConflictDetected("navigation");

        object? latestVersion = null;
        try
        {
            latestVersion = await _cmsService.GetNavigationAsync(
                tenantId,
                traceId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CMS] Failed to fetch latest navigation for conflict response. TraceId: {TraceId}", traceId);
        }

        return Conflict(new AdminCmsApiErrorResponse(
            "CONCURRENCY_CONFLICT",
            message,
            traceId)
        {
            LatestVersion = latestVersion,
        });
    }

    private UnprocessableEntityObjectResult BuildNavigationAdmissionResponse(
        NavigationAdmissionException exception,
        string traceId)
    {
        return UnprocessableEntity(new AdminCmsApiErrorResponse(
            exception.Code,
            exception.Message,
            traceId)
        {
            Navigation = new AdminNavigationAdmissionErrorDetails
            {
                LinkType = exception.LinkType,
                Menu = exception.Menu,
                Index = exception.Index,
                Field = exception.Field,
                TargetId = exception.TargetId
            }
        });
    }

    private (uint? RowVersion, IActionResult? Error) RequireNavigationRowVersion(
        string traceId,
        string? requestRowVersion = null)
    {
        var candidate = !string.IsNullOrWhiteSpace(requestRowVersion)
            ? requestRowVersion
            : Request.Headers["If-Match"].ToString();

        if (string.IsNullOrWhiteSpace(candidate) || !TryParseNavigationRowVersion(candidate, out var rowVersion))
        {
            _telemetry.RecordCmsConflictDetected("navigation-precondition");
            return (
                null,
                StatusCode(
                    StatusCodes.Status428PreconditionRequired,
                    new AdminCmsApiErrorResponse(
                        "PRECONDITION_REQUIRED",
                        "The current navigation RowVersion or If-Match value is required.",
                        traceId)));
        }

        return (rowVersion, null);
    }

    private void ApplyNavigationConcurrencyHeaders(string rowVersion)
    {
        Response.Headers.ETag = $"\"{rowVersion}\"";
        Response.Headers["X-Resource-Version"] = rowVersion;
    }

    private static bool TryParseNavigationRowVersion(string raw, out uint rowVersion)
    {
        rowVersion = 0;
        var normalized = raw.Trim();
        if (normalized.StartsWith("W/\"", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }

        if (normalized.StartsWith('"') && normalized.EndsWith('"') && normalized.Length >= 2)
        {
            normalized = normalized[1..^1];
        }

        return uint.TryParse(normalized, out rowVersion) && rowVersion > 0;
    }
}
