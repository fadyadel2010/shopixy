using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Shipments Controller - Admin/Ops shipment management.
/// 
/// PHASE 6.2: ADMIN SHIPMENT OPERATIONS
/// 
/// Base Path: /api/admin/shipments
/// 
/// Endpoints:
/// - GET /api/admin/shipments (list with filters)
/// - GET /api/admin/shipments/{shipmentId} (details)
/// - PUT /api/admin/shipments/{shipmentId}/status (update status)
/// - PUT /api/admin/shipments/{shipmentId}/tracking (update tracking)
/// - POST /api/admin/shipments/{shipmentId}/cancel (cancel shipment)
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth
/// - MongoDB/Elasticsearch are NOT used here
/// - All operations are tenant-isolated via Shipment.TenantId
/// - Prefer rejecting invalid actions over auto-fixing
/// </summary>
[ApiController]
[Route("api/admin/shipments")]
public class AdminShipmentsController : ControllerBase
{
    private readonly IAdminShipmentService _adminShipmentService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminShipmentsController> _logger;

    public AdminShipmentsController(
        IAdminShipmentService adminShipmentService,
        ITenantContext tenantContext,
        ILogger<AdminShipmentsController> logger)
    {
        _adminShipmentService = adminShipmentService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// List shipments with filtering and pagination.
    /// 
    /// GET /api/admin/shipments
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Query Parameters:
    /// - page: int (default 1)
    /// - pageSize: int (default 20, max 50)
    /// - status: ShipmentStatus? (optional)
    /// - carrier: string? (optional)
    /// - fromDate: DateTime? (optional)
    /// - toDate: DateTime? (optional)
    /// - orderId: Guid? (optional)
    /// - isCod: bool? (optional)
    /// - search: string? (optional)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminPagedResult<AdminShipmentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListShipments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ShipmentStatus? status = null,
        [FromQuery] string? carrier = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] Guid? orderId = null,
        [FromQuery] bool? isCod = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var query = new AdminShipmentListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            Carrier = carrier,
            FromDate = fromDate,
            ToDate = toDate,
            OrderId = orderId,
            IsCod = isCod,
            Search = search
        };

        // Validate query
        var queryError = query.Validate();
        if (queryError != null)
        {
            return BadRequest(new AdminShipmentApiErrorResponse("INVALID_QUERY", queryError, traceId));
        }

        _logger.LogInformation(
            "ListShipments | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
            validation.TenantId, page, pageSize, traceId);

        try
        {
            var result = await _adminShipmentService.ListShipmentsAsync(
                validation.TenantId!.Value,
                query,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListShipments | TenantId: {TenantId} | Error | TraceId: {TraceId}", 
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminShipmentApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Get shipment details including order reference and timeline.
    /// 
    /// GET /api/admin/shipments/{shipmentId}
    /// X-Tenant-Id: {tenant-guid}
    /// </summary>
    [HttpGet("{shipmentId:guid}")]
    [ProducesResponseType(typeof(AdminShipmentDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipmentDetails(
        [FromRoute] Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "GetShipmentDetails | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TraceId: {TraceId}",
            validation.TenantId, shipmentId, traceId);

        try
        {
            var result = await _adminShipmentService.GetShipmentDetailsAsync(
                validation.TenantId!.Value,
                shipmentId,
                traceId,
                cancellationToken);

            if (result == null)
            {
                return NotFound(AdminShipmentApiErrorResponse.ShipmentNotFound(traceId));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetShipmentDetails | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Error | TraceId: {TraceId}", 
                validation.TenantId, shipmentId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminShipmentApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Update shipment status (admin override).
    /// 
    /// PUT /api/admin/shipments/{shipmentId}/status
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Request Body:
    /// {
    ///   "newStatus": "Shipped",
    ///   "reason": "Warehouse dispatch",
    ///   "trackingNumber": "ARX123456",
    ///   "carrierName": "Aramex"
    /// }
    /// </summary>
    [HttpPut("{shipmentId:guid}/status")]
    [ProducesResponseType(typeof(AdminShipmentStatusUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateShipmentStatus(
        [FromRoute] Guid shipmentId,
        [FromBody] AdminUpdateShipmentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NewStatus: {NewStatus} | TraceId: {TraceId}",
            validation.TenantId, shipmentId, request.NewStatus, traceId);

        try
        {
            var result = await _adminShipmentService.UpdateShipmentStatusAsync(
                validation.TenantId!.Value,
                shipmentId,
                request,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (AdminShipmentNotFoundException)
        {
            return NotFound(AdminShipmentApiErrorResponse.ShipmentNotFound(traceId));
        }
        catch (AdminShipmentImmutableException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (InvalidAdminShipmentStateException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (TrackingNumberRequiredException ex)
        {
            return BadRequest(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShipmentFinancialAuthorityException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CarrierManagedTrackingAuthorityException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CarrierManagedStatusAuthorityException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Error | TraceId: {TraceId}", 
                validation.TenantId, shipmentId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminShipmentApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Update tracking information only.
    /// 
    /// PUT /api/admin/shipments/{shipmentId}/tracking
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Request Body:
    /// {
    ///   "trackingNumber": "ARX123456",
    ///   "carrierName": "Aramex"
    /// }
    /// </summary>
    [HttpPut("{shipmentId:guid}/tracking")]
    [ProducesResponseType(typeof(AdminTrackingUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTracking(
        [FromRoute] Guid shipmentId,
        [FromBody] AdminUpdateTrackingRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | TraceId: {TraceId}",
            validation.TenantId, shipmentId, request.TrackingNumber, traceId);

        try
        {
            var result = await _adminShipmentService.UpdateTrackingAsync(
                validation.TenantId!.Value,
                shipmentId,
                request,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (AdminShipmentNotFoundException)
        {
            return NotFound(AdminShipmentApiErrorResponse.ShipmentNotFound(traceId));
        }
        catch (AdminShipmentImmutableException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CarrierManagedTrackingAuthorityException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (TrackingNumberRequiredException ex)
        {
            return BadRequest(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Error | TraceId: {TraceId}", 
                validation.TenantId, shipmentId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminShipmentApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Cancel a shipment (admin action).
    /// 
    /// POST /api/admin/shipments/{shipmentId}/cancel
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Request Body:
    /// {
    ///   "reason": "Order cancelled by customer"
    /// }
    /// </summary>
    [HttpPost("{shipmentId:guid}/cancel")]
    [ProducesResponseType(typeof(AdminShipmentCancellationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminShipmentApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelShipment(
        [FromRoute] Guid shipmentId,
        [FromBody] AdminCancelShipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Reason: {Reason} | TraceId: {TraceId}",
            validation.TenantId, shipmentId, request.Reason, traceId);

        try
        {
            if (User.Identity?.IsAuthenticated != true ||
                !Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actorId) || actorId == Guid.Empty)
                return Forbid();
            request.ActorUserId = actorId;
            var result = await _adminShipmentService.CancelShipmentAsync(
                validation.TenantId!.Value,
                shipmentId,
                request,
                traceId,
                cancellationToken);

            return StatusCode(result.Outcome == "Confirmed" ? 200 : result.Outcome == "Requested" ? 202 : 409, result);
        }
        catch (AdminShipmentNotFoundException)
        {
            return NotFound(AdminShipmentApiErrorResponse.ShipmentNotFound(traceId));
        }
        catch (AdminShipmentImmutableException ex)
        {
            return Conflict(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (ShipmentCancellationReasonRequiredException ex)
        {
            return BadRequest(new AdminShipmentApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException) { return BadRequest(new AdminShipmentApiErrorResponse("CANCELLATION_REASON_INVALID", "A reason of at most 500 characters is required.", traceId)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Error | TraceId: {TraceId}", 
                validation.TenantId, shipmentId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminShipmentApiErrorResponse.InternalError(traceId));
        }
    }

    #region Header Validation

    [Authorize(Policy = PointOfSale.Core.Security.PosPermissions.ShippingView)]
    [HttpGet("{shipmentId:guid}/parcel-evidence")]
    public async Task<IActionResult> GetParcelEvidence(Guid shipmentId,
        [FromServices] PointOfSale.Infrastructure.Shipping.ShipmentParcelEvidenceService evidence, CancellationToken ct)
    {
        try { return Ok(await evidence.ReadAsync(_tenantContext.RequiredTenantId, shipmentId, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return Conflict(new { code = ex.Message }); }
    }

    [Authorize(Policy = PointOfSale.Core.Security.PosPermissions.ShippingFulfill)]
    [HttpPut("{shipmentId:guid}/parcel-evidence")]
    public async Task<IActionResult> SaveParcelEvidence(Guid shipmentId,
        [FromBody] PointOfSale.Infrastructure.Shipping.ParcelMeasurement request,
        [FromServices] PointOfSale.Infrastructure.Shipping.ShipmentParcelEvidenceService evidence, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actor) || actor == Guid.Empty)
            return Forbid();
        try { return Ok(await evidence.SaveAsync(_tenantContext.RequiredTenantId, shipmentId, actor, request, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { code = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { code = ex.Message }); }
    }

    [Authorize(Policy = PointOfSale.Core.Security.PosPermissions.ShippingFulfill)]
    [HttpPost("{shipmentId:guid}/parcel-quote")]
    public async Task<IActionResult> QuoteParcel(Guid shipmentId,
        [FromServices] PointOfSale.Infrastructure.Shipping.ICarrierShipmentService carrier, CancellationToken ct)
    {
        var result = await carrier.QuoteExistingShipmentAsync(_tenantContext.RequiredTenantId, shipmentId, ct);
        return StatusCode(result.Success ? 200 : 409, result);
    }

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        try
        {
            return (_tenantContext.RequiredTenantId, null);
        }
        catch (InvalidOperationException)
        {
            return (null, BadRequest(AdminShipmentApiErrorResponse.TenantHeaderMissing(traceId)));
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
