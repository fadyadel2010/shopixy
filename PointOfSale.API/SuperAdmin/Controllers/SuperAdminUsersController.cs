using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/users")]
public sealed class SuperAdminUsersController : SuperAdminBaseController
{
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminUsersController> _logger;

    public SuperAdminUsersController(
        IUserGovernanceService userGovernanceService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminUsersController> logger)
    {
        _userGovernanceService = userGovernanceService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = SuperAdminUserGovernanceListQuery.DefaultPage,
        [FromQuery] int pageSize = SuperAdminUserGovernanceListQuery.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] UserGovernanceRole? role = null,
        [FromQuery] UserGovernanceStatus? status = null,
        [FromQuery] UserGovernanceRiskLevel? riskLevel = null,
        [FromQuery] DateTime? createdFromUtc = null,
        [FromQuery] DateTime? createdToUtc = null,
        [FromQuery] bool? hasDebt = null,
        CancellationToken cancellationToken = default)
    {
        var query = new SuperAdminUserGovernanceListQuery
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            Role = role,
            Status = status,
            RiskLevel = riskLevel,
            CreatedFromUtc = createdFromUtc,
            CreatedToUtc = createdToUtc,
            HasDebt = hasDebt
        };

        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "INVALID_QUERY",
                validationError);
        }

        var result = await _userGovernanceService.GetUsersAsync(query, cancellationToken);
        var response = new SuperAdminUsersListResponseDto
        {
            Users = result.Items
        };

        var meta = new Dictionary<string, object?>
        {
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
            ["totalPages"] = result.TotalPages
        };

        await _auditService.LogActionAsync(
            action: "USER_GOVERNANCE_LIST_VIEWED",
            entityType: "UserGovernance",
            entityId: "user-list",
            description: "Super Admin viewed the user governance console.",
            success: true,
            details: new
            {
                query.Page,
                query.PageSize,
                query.Search,
                query.Role,
                query.Status,
                query.RiskLevel,
                query.CreatedFromUtc,
                query.CreatedToUtc,
                query.HasDebt,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(response, meta);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(
        Guid id,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _userGovernanceService.GetUserDetailsAsync(id, role, cancellationToken);
        if (user == null)
        {
            return ErrorResponse(
                StatusCodes.Status404NotFound,
                "USER_NOT_FOUND",
                "Governed user was not found.");
        }

        await _auditService.LogActionAsync(
            action: "USER_GOVERNANCE_DETAILS_VIEWED",
            entityType: "UserGovernance",
            entityId: id.ToString(),
            description: "Super Admin opened governed user details.",
            success: true,
            details: new
            {
                role = user.Role,
                status = user.Status,
                riskLevel = user.RiskLevel
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(user);
    }

    [HttpGet("{id:guid}/risk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRisk(
        Guid id,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default)
    {
        var risk = await _userGovernanceService.GetRiskAsync(id, role, cancellationToken);
        if (risk == null)
        {
            return ErrorResponse(
                StatusCodes.Status404NotFound,
                "USER_NOT_FOUND",
                "Governed user was not found.");
        }

        return OkResponse(risk);
    }

    [HttpPost("{id:guid}/suspend")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Suspend(
        Guid id,
        [FromBody] SuperAdminUserGovernanceActionRequestDto request,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(id, role, UserGovernanceStatus.Suspended, request, cancellationToken);

    [HttpPost("{id:guid}/ban")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Ban(
        Guid id,
        [FromBody] SuperAdminUserGovernanceActionRequestDto request,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(id, role, UserGovernanceStatus.Banned, request, cancellationToken);

    [HttpPost("{id:guid}/activate")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> Activate(
        Guid id,
        [FromBody] SuperAdminUserGovernanceActionRequestDto request,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(id, role, UserGovernanceStatus.Active, request, cancellationToken);

    [HttpPost("{id:guid}/financial-lock")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> FinancialLock(
        Guid id,
        [FromBody] SuperAdminUserGovernanceActionRequestDto request,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(id, role, UserGovernanceStatus.FinanciallyLocked, request, cancellationToken);

    [HttpPost("{id:guid}/financial-unlock")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> FinancialUnlock(
        Guid id,
        [FromBody] SuperAdminUserGovernanceActionRequestDto request,
        [FromQuery] UserGovernanceRole? role = null,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(id, role, UserGovernanceStatus.Active, request, cancellationToken);

    [HttpPost("{id:guid}/vendor-verification")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> SetVendorVerification(
        Guid id,
        [FromBody] SuperAdminVendorCertificationActionRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorCertificationAsync(
            id,
            request,
            isVerification: true,
            cancellationToken);

    [HttpPost("{id:guid}/marketplace-approval")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public Task<IActionResult> SetVendorMarketplaceApproval(
        Guid id,
        [FromBody] SuperAdminVendorCertificationActionRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorCertificationAsync(
            id,
            request,
            isVerification: false,
            cancellationToken);

    [HttpGet("blacklist")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBlacklist(
        [FromQuery] int page = SuperAdminUserGovernanceListQuery.DefaultPage,
        [FromQuery] int pageSize = SuperAdminUserGovernanceListQuery.DefaultPageSize,
        [FromQuery] UserGovernanceBlacklistType? type = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        if (page <= 0)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "INVALID_QUERY",
                "Page must be greater than zero.");
        }

        if (pageSize <= 0 || pageSize > SuperAdminUserGovernanceListQuery.MaxPageSize)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "INVALID_QUERY",
                $"PageSize must be between 1 and {SuperAdminUserGovernanceListQuery.MaxPageSize}.");
        }

        var result = await _userGovernanceService.GetBlacklistAsync(
            type,
            activeOnly,
            search,
            page,
            pageSize,
            cancellationToken);

        return OkResponse(
            new SuperAdminBlacklistListResponseDto
            {
                Entries = result.Items
            },
            new Dictionary<string, object?>
            {
                ["page"] = result.Page,
                ["pageSize"] = result.PageSize,
                ["totalCount"] = result.TotalCount,
                ["totalPages"] = result.TotalPages
            });
    }

    [HttpPost("blacklist")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertBlacklist(
        [FromBody] SuperAdminUpsertBlacklistRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for blacklist changes.");
        }

        try
        {
            var entry = await _userGovernanceService.UpsertBlacklistAsync(
                request,
                CurrentSuperAdminId.Value,
                cancellationToken);

            return OkResponse(entry);
        }
        catch (UserGovernanceException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("blacklist/{entryId:guid}/revoke")]
    [EnableRateLimiting(SuperAdminSecurityConstants.GovernanceWriteRateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeBlacklist(
        Guid entryId,
        [FromBody] SuperAdminUserGovernanceActionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for blacklist changes.");
        }

        try
        {
            var entry = await _userGovernanceService.RevokeBlacklistAsync(
                entryId,
                request.Reason,
                CurrentSuperAdminId.Value,
                cancellationToken);

            return OkResponse(entry);
        }
        catch (UserGovernanceException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    private async Task<IActionResult> ExecuteActionAsync(
        Guid id,
        UserGovernanceRole? role,
        UserGovernanceStatus targetStatus,
        SuperAdminUserGovernanceActionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for governance actions.");
        }

        try
        {
            var result = await _userGovernanceService.ApplyActionAsync(
                id,
                role,
                targetStatus,
                request.Reason,
                CurrentSuperAdminId.Value,
                CurrentSuperAdminEmail,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: $"USER_GOVERNANCE_{targetStatus.ToString().ToUpperInvariant()}",
                entityType: "UserGovernance",
                entityId: id.ToString(),
                description: $"Super Admin changed governed user status to {targetStatus}.",
                success: true,
                details: new
                {
                    role = result.Role,
                    status = result.Status,
                    riskLevel = result.RiskLevel
                },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "User governance action applied | UserId: {UserId} | Role: {Role} | TargetStatus: {TargetStatus} | SuperAdminId: {SuperAdminId}",
                id,
                result.Role,
                targetStatus,
                CurrentSuperAdminId);

            return OkResponse(result);
        }
        catch (UserGovernanceException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    private async Task<IActionResult> ExecuteVendorCertificationAsync(
        Guid vendorId,
        SuperAdminVendorCertificationActionRequestDto request,
        bool isVerification,
        CancellationToken cancellationToken)
    {
        if (!CurrentSuperAdminId.HasValue || CurrentSuperAdminId.Value == Guid.Empty)
        {
            return ErrorResponse(
                StatusCodes.Status403Forbidden,
                "SUPER_ADMIN_CONTEXT_REQUIRED",
                "A valid Super Admin identity is required for vendor certification actions.");
        }

        try
        {
            var result = isVerification
                ? await _userGovernanceService.SetVendorVerificationAsync(
                    vendorId,
                    request.Enabled,
                    request.Reason,
                    CurrentSuperAdminId.Value,
                    CurrentSuperAdminEmail,
                    cancellationToken)
                : await _userGovernanceService.SetVendorMarketplaceApprovalAsync(
                    vendorId,
                    request.Enabled,
                    request.Reason,
                    CurrentSuperAdminId.Value,
                    CurrentSuperAdminEmail,
                    cancellationToken);

            await _auditService.LogActionAsync(
                action: isVerification
                    ? request.Enabled ? "VENDOR_VERIFIED" : "VENDOR_VERIFICATION_REVOKED"
                    : request.Enabled ? "VENDOR_MARKETPLACE_APPROVED" : "VENDOR_MARKETPLACE_REJECTED",
                entityType: "VendorGovernance",
                entityId: vendorId.ToString(),
                description: isVerification
                    ? "Super Admin changed vendor verification authority."
                    : "Super Admin changed vendor marketplace approval authority.",
                success: true,
                details: new
                {
                    result.IsKycVerified,
                    result.IsMarketplaceApproved,
                    request.Reason
                },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(result);
        }
        catch (UserGovernanceException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
