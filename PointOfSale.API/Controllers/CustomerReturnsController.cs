using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.API.Controllers;

[AllowAnonymousTenant]
[ApiController]
[Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
[Route("api/customers/returns")]
public sealed class CustomerReturnsController : ControllerBase
{
    private readonly IReturnManagementService _returnManagementService;

    public CustomerReturnsController(IReturnManagementService returnManagementService)
    {
        _returnManagementService = returnManagementService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerReturnSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetReturns(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var returns = await _returnManagementService.GetCustomerReturnsAsync(customerId.Value, cancellationToken);
        return Ok(returns);
    }

    [HttpGet("{returnId:guid}")]
    [ProducesResponseType(typeof(CustomerReturnDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReturn(Guid returnId, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var returnRequest = await _returnManagementService.GetCustomerReturnAsync(customerId.Value, returnId, cancellationToken);
        if (returnRequest == null)
        {
            return NotFound(new ApiErrorResponse("RETURN_NOT_FOUND", "Return request was not found.", traceId));
        }

        return Ok(returnRequest);
    }

    private Guid? ResolveCustomerId()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
