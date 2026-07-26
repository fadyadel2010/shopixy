using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/commission")]
public sealed class SuperAdminCommissionController : SuperAdminBaseController
{
    private readonly ISuperAdminCommissionManagementService _commissionManagementService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminCommissionController> _logger;

    public SuperAdminCommissionController(
        ISuperAdminCommissionManagementService commissionManagementService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminCommissionController> logger)
    {
        _commissionManagementService = commissionManagementService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRules(CancellationToken cancellationToken)
    {
        var rules = await _commissionManagementService.GetRulesAsync(cancellationToken);
        return OkResponse(rules);
    }

    [HttpGet("resolve-preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SuperAdminErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolvePreview(
        [FromQuery] Guid? vendorId,
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _commissionManagementService.ResolvePreviewAsync(vendorId, categoryId, cancellationToken);

            _logger.LogInformation(
                "SuperAdmin commission resolve preview | VendorId: {VendorId} | CategoryId: {CategoryId} | Rate: {Rate} | Source: {Source} | RuleId: {RuleId} | Priority: {Priority}",
                vendorId,
                categoryId,
                response.Rate,
                response.Source,
                response.RuleId,
                response.Priority);

            return OkResponse(response);
        }
        catch (SuperAdminCommissionValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] SuperAdminUpsertCommissionRuleRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Request body is required.");
        }

        try
        {
            var response = await _commissionManagementService.CreateRuleAsync(request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "COMMISSION_CREATED",
                entityType: "CommissionRule",
                entityId: response.Id.ToString(),
                description: "Super Admin created a commission rule.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin commission rule created | RuleId: {RuleId} | ScopeType: {ScopeType} | ScopeId: {ScopeId} | Priority: {Priority}",
                response.Id,
                response.ScopeType,
                response.ScopeId,
                response.Priority);

            return OkResponse(response);
        }
        catch (SuperAdminCommissionValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] SuperAdminUpsertCommissionRuleRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Request body is required.");
        }

        try
        {
            var response = await _commissionManagementService.UpdateRuleAsync(id, request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "COMMISSION_UPDATED",
                entityType: "CommissionRule",
                entityId: response.Id.ToString(),
                description: "Super Admin updated a commission rule.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin commission rule updated | RuleId: {RuleId} | ScopeType: {ScopeType} | ScopeId: {ScopeId} | Priority: {Priority}",
                response.Id,
                response.ScopeType,
                response.ScopeId,
                response.Priority);

            return OkResponse(response);
        }
        catch (SuperAdminCommissionValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _commissionManagementService.DeactivateRuleAsync(id, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "COMMISSION_DEACTIVATED",
                entityType: "CommissionRule",
                entityId: response.Id.ToString(),
                description: "Super Admin deactivated a commission rule.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin commission rule deactivated | RuleId: {RuleId}",
                response.Id);

            return OkResponse(response);
        }
        catch (SuperAdminCommissionValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
