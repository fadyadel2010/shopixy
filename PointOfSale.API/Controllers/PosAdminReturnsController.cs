using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/pos-admin/returns")]
[Authorize(Roles = "Owner,Admin,Manager")]
public sealed class PosAdminReturnsController : ControllerBase
{
    private readonly IReturnManagementService _returnManagementService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PosAdminReturnsController> _logger;

    public PosAdminReturnsController(
        IReturnManagementService returnManagementService,
        ITenantContext tenantContext,
        ILogger<PosAdminReturnsController> logger)
    {
        _returnManagementService = returnManagementService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpPost("{returnId:guid}/schedule-pickup")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> SchedulePickup(
        Guid returnId,
        [FromBody] AdminReturnStatusUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorStatusActionAsync(
            returnId,
            request,
            expectedStatus => _returnManagementService.SchedulePickupAsync(
                returnId,
                _tenantContext.RequiredTenantId,
                expectedStatus,
                request?.TimestampUtc,
                request?.TrackingNumber,
                request?.CourierName,
                request?.Notes,
                CurrentActorId,
                CurrentActorEmail,
                HttpContext.TraceIdentifier,
                source: "PosAdminAPI",
                cancellationToken: cancellationToken));

    [HttpPost("{returnId:guid}/mark-picked-up")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> MarkPickedUp(
        Guid returnId,
        [FromBody] AdminReturnStatusUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorStatusActionAsync(
            returnId,
            request,
            expectedStatus => _returnManagementService.MarkPickedUpAsync(
                returnId,
                _tenantContext.RequiredTenantId,
                expectedStatus,
                request?.TimestampUtc,
                request?.TrackingNumber,
                request?.CourierName,
                request?.Notes,
                CurrentActorId,
                CurrentActorEmail,
                HttpContext.TraceIdentifier,
                source: "PosAdminAPI",
                cancellationToken: cancellationToken));

    [HttpPost("{returnId:guid}/mark-in-transit")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> MarkInTransit(
        Guid returnId,
        [FromBody] AdminReturnStatusUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorStatusActionAsync(
            returnId,
            request,
            expectedStatus => _returnManagementService.MarkInTransitAsync(
                returnId,
                _tenantContext.RequiredTenantId,
                expectedStatus,
                request?.TimestampUtc,
                request?.TrackingNumber,
                request?.CourierName,
                request?.Notes,
                CurrentActorId,
                CurrentActorEmail,
                HttpContext.TraceIdentifier,
                source: "PosAdminAPI",
                cancellationToken: cancellationToken));

    [HttpPost("{returnId:guid}/mark-delivered")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> MarkDelivered(
        Guid returnId,
        [FromBody] AdminReturnStatusUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorStatusActionAsync(
            returnId,
            request,
            expectedStatus => _returnManagementService.MarkDeliveredAsync(
                returnId,
                _tenantContext.RequiredTenantId,
                expectedStatus,
                request?.TimestampUtc,
                request?.TrackingNumber,
                request?.CourierName,
                request?.Notes,
                CurrentActorId,
                CurrentActorEmail,
                HttpContext.TraceIdentifier,
                source: "PosAdminAPI",
                cancellationToken: cancellationToken));

    [HttpPost("{returnId:guid}/mark-failed")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> MarkFailed(
        Guid returnId,
        [FromBody] AdminReturnStatusUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorStatusActionAsync(
            returnId,
            request,
            expectedStatus => _returnManagementService.MarkFailedAsync(
                returnId,
                _tenantContext.RequiredTenantId,
                expectedStatus,
                request?.TimestampUtc,
                request?.TrackingNumber,
                request?.CourierName,
                request?.Notes,
                CurrentActorId,
                CurrentActorEmail,
                HttpContext.TraceIdentifier,
                source: "PosAdminAPI",
                cancellationToken: cancellationToken));

    [HttpPost("{returnId:guid}/confirm-received")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ConfirmReceived(
        Guid returnId,
        [FromBody] AdminConfirmReturnReceiptRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteVendorActionAsync(
            returnId,
            () => _returnManagementService.ConfirmReceiptAsync(
                returnId,
                _tenantContext.RequiredTenantId,
                request?.ConfirmedAtUtc,
                request?.Notes,
                request?.ResellableQuantity,
                request?.DamagedQuantity,
                CurrentActorId,
                CurrentActorEmail,
                HttpContext.TraceIdentifier,
                source: "PosAdminAPI",
                cancellationToken: cancellationToken));

    [HttpPost("{returnId:guid}/confirm-receipt")]
    [ProducesResponseType(typeof(CommerceHubReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ConfirmReceipt(
        Guid returnId,
        [FromBody] AdminConfirmReturnReceiptRequest request,
        CancellationToken cancellationToken = default) =>
        ConfirmReceived(returnId, request, cancellationToken);

    private Guid? CurrentActorId
    {
        get
        {
            var rawValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(rawValue, out var actorId) ? actorId : null;
        }
    }

    private string? CurrentActorEmail =>
        User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;

    private Task<IActionResult> ExecuteVendorStatusActionAsync(
        Guid returnId,
        AdminReturnStatusUpdateRequest? request,
        Func<CommerceHubReturnStatus, Task<CommerceHubReturnDetailsDto>> action)
    {
        if (request?.ExpectedStatus is null)
        {
            return Task.FromResult<IActionResult>(StatusCode(
                StatusCodes.Status428PreconditionRequired,
                new
                {
                    code = "RETURN_EXPECTED_STATUS_REQUIRED",
                    message = "The current return status is required before changing return logistics."
                }));
        }

        return ExecuteVendorActionAsync(returnId, () => action(request.ExpectedStatus.Value));
    }

    private async Task<IActionResult> ExecuteVendorActionAsync(
        Guid returnId,
        Func<Task<CommerceHubReturnDetailsDto>> action)
    {
        try
        {
            var returnDetails = await _returnManagementService.GetReturnDetailsAsync(returnId, HttpContext.RequestAborted);
            if (returnDetails == null || returnDetails.VendorId != _tenantContext.RequiredTenantId)
            {
                return NotFound();
            }

            var result = await action();
            return Ok(result);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "POS-admin return action blocked | ReturnId: {ReturnId} | Code: {Code}",
                returnId,
                ex.Code);

            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }
}
