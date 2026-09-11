using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/commercehub/refunds")]
public class CommerceHubRefundsController : ControllerBase
{
    private readonly ICommerceHubRefundService _refundService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CommerceHubRefundsController> _logger;

    public CommerceHubRefundsController(
        ICommerceHubRefundService refundService,
        ITenantContext tenantContext,
        ILogger<CommerceHubRefundsController> logger)
    {
        _refundService = refundService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubRefundDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRefunds(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        if (!TryGetTenantId(traceId, out var tenantId, out var failureResult))
        {
            return failureResult!;
        }

        var refunds = await _refundService.GetRefundsAsync(tenantId, cancellationToken);
        return Ok(refunds);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommerceHubRefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommerceHubRefundDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public IActionResult CreateRefund(
        [FromBody] CommerceHubCreateRefundRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        if (!TryGetTenantId(traceId, out var tenantId, out var failureResult))
        {
            return failureResult!;
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required.", traceId));
        }

        return RefundWorkflowOnly(traceId);

    }

    [HttpPost("{refundId:guid}/confirm")]
    [ProducesResponseType(typeof(CommerceHubRefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> ConfirmRefund(
        Guid refundId,
        [FromBody] CommerceHubConfirmRefundRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        return Task.FromResult<IActionResult>(RefundWorkflowOnly(traceId));
    }

    [HttpPost("{refundId:guid}/fail")]
    [ProducesResponseType(typeof(CommerceHubRefundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> FailRefund(
        Guid refundId,
        [FromBody] CommerceHubFailRefundRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        return Task.FromResult<IActionResult>(RefundWorkflowOnly(traceId));
    }

    private bool TryGetTenantId(string traceId, out Guid tenantId, out IActionResult? failureResult)
    {
        if (_tenantContext.TenantId.HasValue)
        {
            tenantId = _tenantContext.TenantId.Value;
            failureResult = null;
            return true;
        }

        tenantId = Guid.Empty;
        failureResult = BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Store tenant context is required.", traceId));
        return false;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private ObjectResult RefundWorkflowOnly(string traceId) =>
        StatusCode(
            StatusCodes.Status410Gone,
            new ApiErrorResponse(
                "REFUND_WORKFLOW_REQUIRED",
                "Direct CommerceHub refund mutation is disabled. Create a customer refund request and complete it through the Super Admin approval and execution workflow.",
                traceId));

    private async Task<IActionResult> UpdateRefundAsync<TRequest>(
        Guid refundId,
        TRequest request,
        CancellationToken cancellationToken,
        Func<Guid, TRequest, string, CancellationToken, Task<CommerceHubRefundDto>> operation)
    {
        var traceId = GetTraceId();
        if (!TryGetTenantId(traceId, out var tenantId, out var failureResult))
        {
            return failureResult!;
        }

        try
        {
            var refund = await operation(tenantId, request, traceId, cancellationToken);
            return Ok(refund);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "CommerceHub refund transition blocked | RefundId: {RefundId} | Code: {Code} | TraceId: {TraceId}",
                refundId,
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub refund transition failed unexpectedly | RefundId: {RefundId} | TraceId: {TraceId}",
                refundId,
                traceId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("INTERNAL_ERROR", "Failed to update refund.", traceId));
        }
    }
}
