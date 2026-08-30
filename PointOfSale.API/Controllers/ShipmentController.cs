using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Shipping;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Shipment Controller - Admin/System shipment operations.
/// 
/// Routes:
/// - POST /api/shipments (internal - create shipment)
/// - GET /api/shipments/{shipmentId}
/// - PUT /api/shipments/{shipmentId}/status
/// 
/// PHASE 5.2: SHIPMENT LIFECYCLE
/// 
/// Tenant scope is resolved by TenantResolutionMiddleware from trusted server
/// sources. The compatibility X-Tenant-Id header is never an authority input.
/// - X-Request-Id: string (required for status updates)
/// </summary>
[ApiController]
[Route("api/shipments")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class ShipmentController : ControllerBase
{
    private readonly IShipmentService _shipmentService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ShipmentController> _logger;

    private const string RequestIdHeader = "X-Request-Id";

    public ShipmentController(
        IShipmentService shipmentService,
        ITenantContext tenantContext,
        ILogger<ShipmentController> logger)
    {
        _shipmentService = shipmentService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates a shipment for an order.
    /// Internal use - called by checkout process.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateShipment(
        [FromBody] CreateShipmentRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "CreateShipment | TenantId: {TenantId} | OrderId: {OrderId} | ShippingMethodId: {ShippingMethodId} | TraceId: {TraceId}",
            validation.TenantId, request.OrderId, request.ShippingMethodId, traceId);

        try
        {
            var shipment = await _shipmentService.CreateShipmentAsync(
                validation.TenantId!.Value,
                request.OrderId,
                request.ShippingMethodId,
                traceId,
                cancellationToken);

            return CreatedAtAction(nameof(GetShipment), new { shipmentId = shipment.Id }, shipment);
        }
        catch (ShipmentOrderNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShipmentAlreadyExistsException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (OrderNotReadyForShipmentException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShippingMethodInactiveException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShipmentShippingMethodMismatchException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating shipment | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets a shipment by ID.
    /// </summary>
    [HttpGet("{shipmentId:guid}")]
    [ProducesResponseType(typeof(ShipmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipment(
        [FromRoute] Guid shipmentId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var shipment = await _shipmentService.GetShipmentByIdAsync(
                validation.TenantId!.Value,
                shipmentId,
                traceId,
                cancellationToken);

            if (shipment == null)
            {
                return NotFound(new ApiErrorResponse("SHIPMENT_NOT_FOUND", $"Shipment {shipmentId} not found", traceId));
            }

            return Ok(shipment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting shipment | ShipmentId: {ShipmentId} | TraceId: {TraceId}", shipmentId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Updates shipment status.
    /// Used by admin panel or automation.
    /// </summary>
    [HttpPut("{shipmentId:guid}/status")]
    [ProducesResponseType(typeof(ShipmentStatusUpdateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateShipmentStatus(
        [FromRoute] Guid shipmentId,
        [FromBody] UpdateShipmentStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = ValidateHeaders(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NewStatus: {NewStatus} | TraceId: {TraceId}",
            validation.TenantId, shipmentId, request.NewStatus, traceId);

        try
        {
            var response = await _shipmentService.UpdateStatusAsync(
                validation.TenantId!.Value,
                shipmentId,
                request,
                validation.RequestId!,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (ShipmentNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (InvalidShipmentStateException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShipmentCarrierTrackingAuthorityException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShipmentCarrierStatusAuthorityException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating shipment status | ShipmentId: {ShipmentId} | TraceId: {TraceId}", shipmentId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Header Validation

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        try
        {
            return (_tenantContext.RequiredTenantId, null);
        }
        catch (InvalidOperationException)
        {
            return (null, BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId)));
        }
    }

    private (Guid? TenantId, string? RequestId, IActionResult? Error) ValidateHeaders(string traceId)
    {
        Guid tenantId;
        try
        {
            tenantId = _tenantContext.RequiredTenantId;
        }
        catch (InvalidOperationException)
        {
            return (null, null, BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId)));
        }

        if (!Request.Headers.TryGetValue(RequestIdHeader, out var requestIdHeaderValue) ||
            string.IsNullOrWhiteSpace(requestIdHeaderValue))
        {
            return (null, null, BadRequest(new ApiErrorResponse(
                "REQUEST_ID_MISSING",
                "X-Request-Id header is required for idempotency",
                traceId)));
        }

        return (tenantId, requestIdHeaderValue.ToString()!.Trim(), null);
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
