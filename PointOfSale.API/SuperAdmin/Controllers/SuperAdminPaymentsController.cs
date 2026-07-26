using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/payments")]
public sealed class SuperAdminPaymentsController : SuperAdminBaseController
{
    private readonly ICommerceHubVendorPaymentsService _vendorPaymentsService;
    private readonly ISuperAdminAuditService _auditService;

    public SuperAdminPaymentsController(
        ICommerceHubVendorPaymentsService vendorPaymentsService,
        ISuperAdminAuditService auditService)
    {
        _vendorPaymentsService = vendorPaymentsService;
        _auditService = auditService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SuperAdminVendorPaymentRequestListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPayments(
        [FromQuery] SuperAdminVendorPaymentRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        var validationError = query.Validate();
        if (validationError != null)
        {
            return ErrorResponse(StatusCodes.Status400BadRequest, "INVALID_QUERY", validationError);
        }

        var result = await _vendorPaymentsService.GetPaymentRequestsAsync(query, cancellationToken);

        await _auditService.LogActionAsync(
            action: "PAYMENT_REQUESTS_VIEWED",
            entityType: "CommerceHubVendorPaymentRequest",
            entityId: "payments",
            description: "Super Admin viewed vendor payment requests.",
            success: true,
            details: new
            {
                query.Page,
                query.PageSize,
                query.Status,
                query.VendorId,
                query.Search,
                result.TotalCount
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(
            new SuperAdminVendorPaymentRequestListResponseDto
            {
                Requests = result.Items
            },
            new Dictionary<string, object?>
            {
                ["page"] = result.Page,
                ["pageSize"] = result.PageSize,
                ["totalCount"] = result.TotalCount,
                ["totalPages"] = result.TotalPages
            });
    }

    [HttpPost("{paymentRequestId:guid}/approve")]
    [ProducesResponseType(typeof(SuperAdminVendorPaymentRequestItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApprovePayment(
        Guid paymentRequestId,
        [FromBody] SuperAdminApproveVendorPaymentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _vendorPaymentsService.ApprovePaymentRequestAsync(
                paymentRequestId,
                request,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "PAYMENT_REQUEST_APPROVED",
                entityType: "CommerceHubVendorPaymentRequest",
                entityId: paymentRequestId.ToString(),
                description: "Super Admin approved a vendor payment request.",
                success: true,
                details: result,
                reference: paymentRequestId.ToString(),
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(result);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("{paymentRequestId:guid}/reject")]
    [ProducesResponseType(typeof(SuperAdminVendorPaymentRequestItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectPayment(
        Guid paymentRequestId,
        [FromBody] SuperAdminRejectVendorPaymentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _vendorPaymentsService.RejectPaymentRequestAsync(
                paymentRequestId,
                request,
                CurrentSuperAdminId,
                cancellationToken);

            await _auditService.LogActionAsync(
                action: "PAYMENT_REQUEST_REJECTED",
                entityType: "CommerceHubVendorPaymentRequest",
                entityId: paymentRequestId.ToString(),
                description: "Super Admin rejected a vendor payment request.",
                success: true,
                details: result,
                reference: paymentRequestId.ToString(),
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(result);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpGet("{paymentRequestId:guid}/proof-url")]
    [ProducesResponseType(typeof(CommerceHubVendorPaymentProofAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProofUrl(
        Guid paymentRequestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _vendorPaymentsService.GetSuperAdminProofAccessAsync(
                paymentRequestId,
                cancellationToken);

            return OkResponse(result);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
