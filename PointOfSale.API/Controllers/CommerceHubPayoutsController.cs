using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/commercehub/payouts")]
public class CommerceHubPayoutsController : ControllerBase
{
    private readonly ICommerceHubPayoutService _payoutService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CommerceHubPayoutsController> _logger;

    public CommerceHubPayoutsController(
        ICommerceHubPayoutService payoutService,
        ITenantContext tenantContext,
        ILogger<CommerceHubPayoutsController> logger)
    {
        _payoutService = payoutService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CommerceHubPayoutDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPayouts(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        if (!TryGetTenantId(traceId, out var tenantId, out var failureResult))
        {
            return failureResult!;
        }

        var payouts = await _payoutService.GetPayoutsAsync(tenantId, cancellationToken);
        return Ok(payouts);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommerceHubPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePayout(
        [FromBody] CommerceHubCreatePayoutRequestDto request,
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

        try
        {
            var payout = await _payoutService.CreatePayoutAsync(
                tenantId,
                request,
                traceId,
                cancellationToken);

            return Ok(payout);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "CommerceHub payout creation blocked | Code: {Code} | TraceId: {TraceId}",
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
        catch (UserGovernanceException ex)
        {
            _logger.LogWarning(
                "CommerceHub payout creation blocked by governance | Code: {Code} | TraceId: {TraceId}",
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub payout creation failed unexpectedly | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("INTERNAL_ERROR", "Failed to create payout.", traceId));
        }
    }

    [HttpPost("{payoutId:guid}/processing")]
    [ProducesResponseType(typeof(CommerceHubPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> MarkProcessing(
        Guid payoutId,
        [FromBody] CommerceHubUpdatePayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        _ = (payoutId, request, cancellationToken);
        return PayoutExecutionWorkflowRequiredAsync();
    }

    [HttpPost("{payoutId:guid}/complete")]
    [ProducesResponseType(typeof(CommerceHubPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> MarkCompleted(
        Guid payoutId,
        [FromBody] CommerceHubUpdatePayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        _ = (payoutId, request, cancellationToken);
        return PayoutExecutionWorkflowRequiredAsync();
    }

    [HttpPost("{payoutId:guid}/fail")]
    [ProducesResponseType(typeof(CommerceHubPayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public Task<IActionResult> MarkFailed(
        Guid payoutId,
        [FromBody] CommerceHubUpdatePayoutRequestDto request,
        CancellationToken cancellationToken)
    {
        _ = (payoutId, request, cancellationToken);
        return PayoutExecutionWorkflowRequiredAsync();
    }

    private Task<IActionResult> PayoutExecutionWorkflowRequiredAsync()
    {
        var traceId = GetTraceId();
        return Task.FromResult<IActionResult>(StatusCode(
            StatusCodes.Status409Conflict,
            new ApiErrorResponse(
                "PAYOUT_EXECUTION_WORKFLOW_REQUIRED",
                "Direct CommerceHub payout execution mutation is disabled. Payout processing, completion, failure, and transfer evidence recognition must use the Super Admin payout execution workflow.",
                traceId)));
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
}
