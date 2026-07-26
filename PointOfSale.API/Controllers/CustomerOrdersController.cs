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
[Route("api/customers/orders")]
public class CustomerOrdersController : ControllerBase
{
    private readonly ICommerceHubCustomerOrderService _orderService;

    public CustomerOrdersController(ICommerceHubCustomerOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerOrderSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] CustomerOrdersQueryDto query,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var validationError = query.Validate();
        if (validationError != null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_PAGINATION", validationError, traceId));
        }

        var response = await _orderService.GetOrdersAsync(customerId.Value, query, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(typeof(CustomerOrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var order = await _orderService.GetOrderAsync(customerId.Value, orderId, cancellationToken);
        if (order == null)
        {
            return NotFound(new ApiErrorResponse("ORDER_NOT_FOUND", "Order not found.", traceId));
        }

        return Ok(order);
    }

    private Guid? ResolveCustomerId()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
