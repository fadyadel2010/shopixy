using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/promo-codes")]
public sealed class SuperAdminPromoCodesController : SuperAdminBaseController
{
    private readonly ISuperAdminPromoCodeManagementService _promoCodeManagementService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ILogger<SuperAdminPromoCodesController> _logger;

    public SuperAdminPromoCodesController(
        ISuperAdminPromoCodeManagementService promoCodeManagementService,
        ISuperAdminAuditService auditService,
        ILogger<SuperAdminPromoCodesController> logger)
    {
        _promoCodeManagementService = promoCodeManagementService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPromoCodes(CancellationToken cancellationToken)
    {
        var promoCodes = await _promoCodeManagementService.GetPromoCodesAsync(cancellationToken);
        return OkResponse(promoCodes);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] SuperAdminUpsertPromoCodeRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "PROMO_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var response = await _promoCodeManagementService.CreatePromoCodeAsync(request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "PROMO_CODE_CREATED",
                entityType: "PromoCode",
                entityId: response.Id.ToString(),
                description: "Super Admin created a promo code.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin promo code created | PromoId: {PromoId} | Code: {Code}",
                response.Id,
                response.Code);

            return OkResponse(response);
        }
        catch (SuperAdminPromoCodeValidationException exception)
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
        [FromBody] SuperAdminUpsertPromoCodeRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "PROMO_REQUEST_INVALID",
                "Request body is required.");
        }

        try
        {
            var response = await _promoCodeManagementService.UpdatePromoCodeAsync(id, request, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "PROMO_CODE_UPDATED",
                entityType: "PromoCode",
                entityId: response.Id.ToString(),
                description: "Super Admin updated a promo code.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "SuperAdmin promo code updated | PromoId: {PromoId} | Code: {Code}",
                response.Id,
                response.Code);

            return OkResponse(response);
        }
        catch (SuperAdminPromoCodeValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _promoCodeManagementService.ActivatePromoCodeAsync(id, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "PROMO_CODE_ACTIVATED",
                entityType: "PromoCode",
                entityId: response.Id.ToString(),
                description: "Super Admin activated a promo code.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(response);
        }
        catch (SuperAdminPromoCodeValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _promoCodeManagementService.DeactivatePromoCodeAsync(id, CurrentSuperAdminId, cancellationToken);

            await _auditService.LogActionAsync(
                action: "PROMO_CODE_DEACTIVATED",
                entityType: "PromoCode",
                entityId: response.Id.ToString(),
                description: "Super Admin deactivated a promo code.",
                success: true,
                details: response,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(response);
        }
        catch (SuperAdminPromoCodeValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(
        [FromBody] SuperAdminPromoCodePreviewRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "PROMO_PREVIEW_INVALID",
                "Request body is required.");
        }

        try
        {
            var response = await _promoCodeManagementService.PreviewAsync(request, cancellationToken);
            return OkResponse(response);
        }
        catch (SuperAdminPromoCodeValidationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
