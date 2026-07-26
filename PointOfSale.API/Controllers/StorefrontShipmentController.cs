using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Shipping;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Shipment Controller - Customer-facing shipment operations.
/// 
/// Route: GET /api/storefront/orders/{orderId}/shipment
/// 
/// PHASE 5.2: SHIPMENT LIFECYCLE
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// </summary>
[ApiController]
[Route("api/storefront/orders")]
public class StorefrontShipmentController : ControllerBase
{
    private readonly IShipmentService _shipmentService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontShipmentController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontShipmentController(
        IShipmentService shipmentService,
        ApplicationDbContext dbContext,
        ILogger<StorefrontShipmentController> logger)
    {
        _shipmentService = shipmentService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets shipment details for an order.
    /// Returns tracking info only after current customer ownership is proven by
    /// the backend. Guest tracking is intentionally served by the separate
    /// session-bound token endpoint in StorefrontOrdersReadController.
    /// </summary>
    [HttpGet("{orderId:guid}/shipment")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(StorefrontShipmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipmentByOrder(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantValidation = PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }

        var tenantId = tenantValidation.TenantId!.Value;

        if (!TryGetAuthenticatedCustomerOwnership(out var customerId, out var tokenTenantId) ||
            tokenTenantId != tenantId)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var ownsOrder = await _dbContext.Orders
            .AsNoTracking()
            .AnyAsync(
                order => order.Id == orderId &&
                         order.TenantId == tenantId &&
                         order.OnlineCustomerId == customerId &&
                         !order.IsDeleted,
                cancellationToken);

        // Return the same non-enumerating result for a missing order and an
        // order owned by somebody else. Route state never grants shipment access.
        if (!ownsOrder)
        {
            return NotFound(new ApiErrorResponse(
                "SHIPMENT_NOT_FOUND",
                "Shipment not found",
                traceId));
        }

        try
        {
            var shipment = await _shipmentService.GetShipmentByOrderIdAsync(
                tenantId,
                orderId,
                traceId,
                cancellationToken);

            if (shipment == null)
            {
                return NotFound(new ApiErrorResponse(
                    "SHIPMENT_NOT_FOUND",
                    $"No shipment found for order {orderId}",
                    traceId));
            }

            _logger.LogDebug(
                "GetShipmentByOrder | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Status: {Status} | TraceId: {TraceId}",
                tenantId, orderId, shipment.ShipmentId, shipment.Status, traceId);

            return Ok(shipment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting shipment by order | OrderId: {OrderId} | TraceId: {TraceId}",
                orderId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private bool TryGetAuthenticatedCustomerOwnership(out Guid customerId, out Guid tenantId)
    {
        customerId = Guid.Empty;
        tenantId = Guid.Empty;

        return Guid.TryParse(User.FindFirst("CustomerId")?.Value, out customerId) &&
               customerId != Guid.Empty &&
               Guid.TryParse(User.FindFirst("TenantId")?.Value, out tenantId) &&
               tenantId != Guid.Empty;
    }
}
