using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.API.Services.Homepage;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/homepage/sections")]
public sealed class SuperAdminHomepageSectionsController : SuperAdminBaseController
{
    private readonly ISuperAdminHomepageSectionManagementService _homepageSectionManagementService;
    private readonly IHomepageTemplateSeeder _homepageTemplateSeeder;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminHomepageSectionsController> _logger;

    public SuperAdminHomepageSectionsController(
        ISuperAdminHomepageSectionManagementService homepageSectionManagementService,
        IHomepageTemplateSeeder homepageTemplateSeeder,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminHomepageSectionsController> logger)
    {
        _homepageSectionManagementService = homepageSectionManagementService;
        _homepageTemplateSeeder = homepageTemplateSeeder;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSections(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? mode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryParseMode(mode, out var readMode))
            {
                return ErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "HOMEPAGE_SECTION_MODE_INVALID",
                    "Mode must be either 'published' or 'preview'.");
            }

            var sections = await _homepageSectionManagementService.GetSectionsAsync(tenantId, readMode, cancellationToken);
            return OkResponse(sections);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpGet("schemas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSchemas()
    {
        return OkResponse(_homepageSectionManagementService.GetSchemas());
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] HomepageSectionWriteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var section = await _homepageSectionManagementService.CreateSectionAsync(request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_SECTION_CREATED",
                entityType: "HomepageSection",
                entityId: section.Id.ToString(),
                description: "Super Admin created a homepage section.",
                success: true,
                details: section,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage section created | SectionId: {SectionId} | Type: {Type} | TenantId: {TenantId}",
                section.Id,
                section.Type,
                section.TenantId);

            return OkResponse(section);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] HomepageSectionWriteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var section = await _homepageSectionManagementService.UpdateSectionAsync(id, request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_SECTION_UPDATED",
                entityType: "HomepageSection",
                entityId: section.Id.ToString(),
                description: "Super Admin updated a homepage section.",
                success: true,
                details: section,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage section updated | SectionId: {SectionId} | Type: {Type} | TenantId: {TenantId}",
                section.Id,
                section.Type,
                section.TenantId);

            return OkResponse(section);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var section = await _homepageSectionManagementService.DeleteSectionAsync(id, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_SECTION_DELETED",
                entityType: "HomepageSection",
                entityId: section.Id.ToString(),
                description: "Super Admin deleted a homepage section.",
                success: true,
                details: section,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(section);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("reorder")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder(
        [FromBody] ReorderHomepageSectionsRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REORDER_INVALID",
                "Request body is required.");
        }

        try
        {
            var sections = await _homepageSectionManagementService.ReorderSectionsAsync(request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_SECTION_REORDERED",
                entityType: "HomepageSection",
                entityId: request.TenantId?.ToString() ?? "global",
                description: "Super Admin reordered homepage sections.",
                success: true,
                details: sections,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(sections);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Publish(
        [FromBody] PublishHomepageSectionsRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var sections = await _homepageSectionManagementService.PublishSectionsAsync(
                request,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_SECTION_PUBLISHED",
                entityType: "HomepageSection",
                entityId: request.TenantId?.ToString() ?? "global",
                description: "Super Admin published homepage draft changes.",
                success: true,
                details: sections,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage sections published | TenantId: {TenantId} | Count: {Count}",
                request.TenantId,
                sections.Count);

            return OkResponse(sections);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPut("draft")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveDraft(
        [FromBody] SaveHomepageDraftRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var sections = await _homepageSectionManagementService.SaveDraftAsync(
                request,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_DRAFT_SAVED",
                entityType: "HomepageSection",
                entityId: request.TenantId?.ToString() ?? "global",
                description: "Super Admin saved homepage draft changes.",
                success: true,
                details: sections,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage draft saved | TenantId: {TenantId} | Count: {Count}",
                request.TenantId,
                sections.Count);

            return OkResponse(sections);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateStarterTemplate(
        [FromBody] GenerateHomepageTemplateRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var sections = await _homepageTemplateSeeder.GenerateDraftTemplateAsync(
                request.TenantId,
                request.OverwriteExisting,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: request.OverwriteExisting ? "HOMEPAGE_TEMPLATE_RESET" : "HOMEPAGE_TEMPLATE_GENERATED",
                entityType: "HomepageSection",
                entityId: request.TenantId?.ToString() ?? "global",
                description: request.OverwriteExisting
                    ? "Super Admin reset the homepage draft to the starter template."
                    : "Super Admin generated the homepage starter template.",
                success: true,
                details: sections,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage starter template generated | TenantId: {TenantId} | OverwriteExisting: {OverwriteExisting} | Count: {Count}",
                request.TenantId,
                request.OverwriteExisting,
                sections.Count);

            return OkResponse(sections);
        }
        catch (HomepageSectionManagementException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    private static bool TryParseMode(string? mode, out HomepageSectionReadMode readMode)
    {
        if (string.IsNullOrWhiteSpace(mode) ||
            string.Equals(mode, "preview", StringComparison.OrdinalIgnoreCase))
        {
            readMode = HomepageSectionReadMode.Preview;
            return true;
        }

        if (string.Equals(mode, "published", StringComparison.OrdinalIgnoreCase))
        {
            readMode = HomepageSectionReadMode.Published;
            return true;
        }

        readMode = HomepageSectionReadMode.Preview;
        return false;
    }
}
