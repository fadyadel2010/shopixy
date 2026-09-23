using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/commercehub/payments")]
public sealed class AdminCommerceHubPaymentsController : ControllerBase
{
    private const long MaxVendorPaymentProofUploadBytes = 50 * 1024 * 1024;

    private readonly ITenantContext _tenantContext;
    private readonly ICommerceHubVendorPaymentsService _vendorPaymentsService;

    public AdminCommerceHubPaymentsController(
        ITenantContext tenantContext,
        ICommerceHubVendorPaymentsService vendorPaymentsService)
    {
        _tenantContext = tenantContext;
        _vendorPaymentsService = vendorPaymentsService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CommerceHubVendorPaymentsDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        try
        {
            var dashboard = await _vendorPaymentsService.GetVendorDashboardAsync(
                _tenantContext.RequiredTenantId,
                cancellationToken);

            return Ok(dashboard);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception);
        }
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxVendorPaymentProofUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxVendorPaymentProofUploadBytes)]
    [ProducesResponseType(typeof(CommerceHubVendorPaymentHistoryItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePaymentRequest(
        [FromForm] CommerceHubCreateVendorPaymentRequestFormDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorPaymentsService.CreateVendorPaymentRequestAsync(
                _tenantContext.RequiredTenantId,
                request,
                cancellationToken);

            return Ok(result);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception);
        }
    }

    [HttpGet("{paymentRequestId:guid}/proof-url")]
    [ProducesResponseType(typeof(CommerceHubVendorPaymentProofAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProofUrl(
        Guid paymentRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _vendorPaymentsService.GetVendorProofAccessAsync(
                _tenantContext.RequiredTenantId,
                paymentRequestId,
                cancellationToken);

            return Ok(result);
        }
        catch (CommerceHubVendorFinancialControlException exception)
        {
            return ErrorResponse(exception);
        }
    }

    private ObjectResult ErrorResponse(CommerceHubVendorFinancialControlException exception)
    {
        return StatusCode(
            exception.StatusCode,
            new ApiErrorResponse(exception.Code, exception.Message, HttpContext.TraceIdentifier));
    }
}
