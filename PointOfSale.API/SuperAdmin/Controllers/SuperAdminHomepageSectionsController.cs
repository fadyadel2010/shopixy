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

            var result = await _homepageSectionManagementService.GetSectionsAsync(tenantId, readMode, cancellationToken);
            return OkResponse(result.Data, WorkspaceMeta(result.WorkspaceVersion));
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
    public IActionResult Create(
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

        return RetiredGranularMutation();
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Update(
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

        return RetiredGranularMutation();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete(Guid id, CancellationToken cancellationToken)
    {
        return RetiredGranularMutation();
    }

    [HttpPost("reorder")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Reorder(
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

        return RetiredGranularMutation();
    }

    [HttpPost("publish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Publish(
        [FromBody] PublishHomepageSectionsRequest? request,
        [FromQuery] string? expectedVersion,
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
            var result = await _homepageSectionManagementService.PublishSectionsAsync(
                request,
                expectedVersion,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_SECTION_PUBLISHED",
                entityType: "HomepageSection",
                entityId: request.TenantId?.ToString() ?? "global",
                description: "Super Admin published homepage draft changes.",
                success: true,
                details: result.Data,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage sections published | TenantId: {TenantId} | Count: {Count}",
                request.TenantId,
                result.Data.Count);

            return OkResponse(result.Data, WorkspaceMeta(result.WorkspaceVersion));
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
        [FromQuery] string? expectedVersion,
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
            var result = await _homepageSectionManagementService.SaveDraftAsync(
                request,
                expectedVersion,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "HOMEPAGE_DRAFT_SAVED",
                entityType: "HomepageSection",
                entityId: request.TenantId?.ToString() ?? "global",
                description: "Super Admin saved homepage draft changes.",
                success: true,
                details: result.Data,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage draft saved | TenantId: {TenantId} | Count: {Count}",
                request.TenantId,
                result.Data.Count);

            return OkResponse(result.Data, WorkspaceMeta(result.WorkspaceVersion));
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
        [FromQuery] string? expectedVersion,
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
            var result = await _homepageTemplateSeeder.GenerateDraftTemplateAsync(
                request.TenantId,
                request.OverwriteExisting,
                expectedVersion,
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
                details: result.Data,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin homepage starter template generated | TenantId: {TenantId} | OverwriteExisting: {OverwriteExisting} | Count: {Count}",
                request.TenantId,
                request.OverwriteExisting,
                result.Data.Count);

            return OkResponse(result.Data, WorkspaceMeta(result.WorkspaceVersion));
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

    private IActionResult RetiredGranularMutation()
    {
        return ErrorResponse(
            StatusCodes.Status409Conflict,
            "HOMEPAGE_AUTHORING_OWNER_RETIRED",
            "Homepage draft changes must be saved through the canonical draft workspace endpoint.");
    }

    private static IReadOnlyDictionary<string, object?> WorkspaceMeta(string workspaceVersion)
    {
        return new Dictionary<string, object?>
        {
            ["workspaceVersion"] = workspaceVersion
        };
    }
}
