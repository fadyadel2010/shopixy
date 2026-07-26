using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/refunds")]
public sealed class SuperAdminRefundsController : SuperAdminBaseController
{
    private readonly ISuperAdminRefundsService _refundsService;
    private readonly ILogger<SuperAdminRefundsController> _logger;

    public SuperAdminRefundsController(
        ISuperAdminRefundsService refundsService,
        ILogger<SuperAdminRefundsController> logger)
    {
        _refundsService = refundsService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRefunds(
        [FromQuery] int page = SuperAdminRefundsListQuery.DefaultPage,
        [FromQuery] int pageSize = SuperAdminRefundsListQuery.DefaultPageSize,
        [FromQuery] PointOfSale.Core.Enums.CommerceHubRefundRequestStatus? status = null,
        [FromQuery] Guid? vendorId = null,
        [FromQuery] Guid? orderId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = new SuperAdminRefundsListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            VendorId = vendorId,
            OrderId = orderId,
            FromDate = fromDate,
            ToDate = toDate
        };

        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var result = await _refundsService.GetRefundsAsync(query, cancellationToken);
        return OkResponse(
            new SuperAdminRefundsListResponseDto { Refunds = result.Items },
            new Dictionary<string, object?>
            {
                ["page"] = result.Page,
                ["pageSize"] = result.PageSize,
                ["totalCount"] = result.TotalCount,
                ["totalPages"] = result.TotalPages
            });
    }

    [HttpGet("{refundId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRefund(Guid refundId, CancellationToken cancellationToken = default)
    {
        var refund = await _refundsService.GetRefundDetailsAsync(refundId, cancellationToken);
        if (refund == null)
        {
            return ErrorResponse(StatusCodes.Status404NotFound, "REFUND_NOT_FOUND", "Refund request was not found.");
        }

        return OkResponse(refund);
    }

    [HttpPost("{refundId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ApproveRefund(
        Guid refundId,
        [FromBody] SuperAdminApproveRefundRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            refundId,
            () => _refundsService.ApproveAsync(
                refundId,
                request ?? new SuperAdminApproveRefundRequest(),
                CurrentSuperAdminId,
                CurrentSuperAdminEmail,
                HttpContext.TraceIdentifier,
                cancellationToken));

    [HttpPost("{refundId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RejectRefund(
        Guid refundId,
        [FromBody] SuperAdminRejectRefundRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            refundId,
            () => _refundsService.RejectAsync(
                refundId,
                request ?? new SuperAdminRejectRefundRequest(),
                CurrentSuperAdminId,
                CurrentSuperAdminEmail,
                cancellationToken));

    [HttpPost("{refundId:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RetryRefund(
        Guid refundId,
        [FromBody] SuperAdminRetryRefundRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            refundId,
            () => _refundsService.RetryAsync(
                refundId,
                request ?? new SuperAdminRetryRefundRequest(),
                CurrentSuperAdminId,
                CurrentSuperAdminEmail,
                cancellationToken));

    [HttpPost("{refundId:guid}/execute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ExecuteRefund(
        Guid refundId,
        [FromBody] SuperAdminExecuteRefundRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            refundId,
            () => _refundsService.ExecuteAsync(
                refundId,
                request ?? new SuperAdminExecuteRefundRequest(),
                CurrentSuperAdminId,
                CurrentSuperAdminEmail,
                HttpContext.TraceIdentifier,
                cancellationToken));

    private async Task<IActionResult> ExecuteActionAsync(
        Guid refundId,
        Func<Task<SuperAdminRefundDetailsDto>> action)
    {
        try
        {
            var refund = await action();
            return OkResponse(refund);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "SuperAdmin refund action blocked | RefundId: {RefundId} | Code: {Code}",
                refundId,
                ex.Code);

            return ErrorResponse(ex.StatusCode, ex.Code, ex.Message);
        }
    }
}
