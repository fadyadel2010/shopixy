using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.API.Controllers;

[AllowAnonymousTenant]
[ApiController]
[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
[Route("api/customers/payout-details")]
public sealed class CustomerPayoutDetailsController : ControllerBase
{
    private readonly ICustomerPayoutDetailsService _payoutDetailsService;
    private readonly ILogger<CustomerPayoutDetailsController> _logger;

    public CustomerPayoutDetailsController(
        ICustomerPayoutDetailsService payoutDetailsService,
        ILogger<CustomerPayoutDetailsController> logger)
    {
        _payoutDetailsService = payoutDetailsService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerPayoutDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPayoutDetails(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var payoutDetails = await _payoutDetailsService.GetPayoutDetailsAsync(customerId.Value, cancellationToken);
        return Ok(payoutDetails);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerPayoutDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePayoutDetails(
        [FromBody] CustomerUpsertPayoutDetailsRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required.", traceId));
        }

        try
        {
            var payoutDetails = await _payoutDetailsService.CreatePayoutDetailsAsync(
                customerId.Value,
                request,
                cancellationToken);

            return Ok(payoutDetails);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "Customer payout details create blocked | CustomerId: {CustomerId} | Code: {Code} | TraceId: {TraceId}",
                customerId.Value,
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpPut("{payoutDetailsId:guid}")]
    [ProducesResponseType(typeof(CustomerPayoutDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePayoutDetails(
        [FromRoute] Guid payoutDetailsId,
        [FromBody] CustomerUpsertPayoutDetailsRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required.", traceId));
        }

        try
        {
            var payoutDetails = await _payoutDetailsService.UpdatePayoutDetailsAsync(
                customerId.Value,
                payoutDetailsId,
                request,
                cancellationToken);

            return Ok(payoutDetails);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "Customer payout details update blocked | CustomerId: {CustomerId} | PayoutDetailsId: {PayoutDetailsId} | Code: {Code} | TraceId: {TraceId}",
                customerId.Value,
                payoutDetailsId,
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    private Guid? ResolveCustomerId()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
