using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin Orders Controller - Admin/Ops order management.
/// 
/// PHASE 6.1: ADMIN ORDER MANAGEMENT
/// 
/// Base Path: /api/admin/orders
/// 
/// Endpoints:
/// - GET /api/admin/orders (list with filters)
/// - GET /api/admin/orders/{orderId} (details)
/// - PUT /api/admin/orders/{orderId}/status (update status)
/// - POST /api/admin/orders/{orderId}/cancel (cancel order)
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth
/// - MongoDB is NOT used here
/// - All operations are tenant-isolated
/// - Prefer rejecting invalid actions over auto-fixing
/// </summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminOrdersController : ControllerBase
{
    private readonly IAdminOrderService _adminOrderService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminOrdersController> _logger;

    public AdminOrdersController(
        IAdminOrderService adminOrderService,
        ITenantContext tenantContext,
        ILogger<AdminOrdersController> logger)
    {
        _adminOrderService = adminOrderService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// List orders with filtering and pagination.
    /// 
    /// GET /api/admin/orders
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Query Parameters:
    /// - page: int (default 1)
    /// - pageSize: int (default 20, max 50)
    /// - status: OrderStatus? (optional)
    /// - fromDate: DateTime? (optional)
    /// - toDate: DateTime? (optional)
    /// - customerId: Guid? (optional)
    /// - paymentStatus: PaymentStatus? (optional)
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminPagedResult<AdminOrderListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] PaymentStatus? paymentStatus = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = AdminOrderListQuery.DefaultSortBy,
        [FromQuery] string sortDirection = AdminOrderListQuery.DefaultSortDirection,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var query = new AdminOrderListQuery
        {
            Page = page,
            PageSize = pageSize,
            Status = status,
            PaymentStatus = paymentStatus,
            FromDate = fromDate,
            ToDate = toDate,
            CustomerId = customerId,
            Search = search,
            SortBy = sortBy,
            SortDirection = sortDirection
        };

        // Validate query
        var queryError = query.Validate();
        if (queryError != null)
        {
            return BadRequest(new AdminApiErrorResponse("INVALID_QUERY", queryError, traceId));
        }

        _logger.LogInformation(
            "ListOrders | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
            validation.TenantId, page, pageSize, traceId);

        try
        {
            var result = await _adminOrderService.ListOrdersAsync(
                validation.TenantId!.Value,
                query,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListOrders | TenantId: {TenantId} | Error | TraceId: {TraceId}", 
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Get order details including items, payments, and shipment.
    /// 
    /// GET /api/admin/orders/{orderId}
    /// X-Tenant-Id: {tenant-guid}
    /// </summary>
    [HttpGet("{orderId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminOrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderDetails(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "GetOrderDetails | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
            validation.TenantId, orderId, traceId);

        try
        {
            var result = await _adminOrderService.GetOrderDetailsAsync(
                validation.TenantId!.Value,
                orderId,
                traceId,
                cancellationToken);

            if (result == null)
            {
                return NotFound(AdminApiErrorResponse.OrderNotFound(traceId));
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOrderDetails | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}", 
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Update order status (manual admin override).
    /// 
    /// PUT /api/admin/orders/{orderId}/status
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Request Body:
    /// {
    ///   "newStatus": "Cancelled",
    ///   "reason": "Customer support override"
    /// }
    /// </summary>
    [HttpPut("{orderId:guid}/status")]
    [ProducesResponseType(typeof(AdminOrderStatusUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateOrderStatus(
        [FromRoute] Guid orderId,
        [FromBody] AdminUpdateOrderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        var expectedRowVersion = ParseRequiredIfMatch(traceId);
        if (expectedRowVersion.Error != null)
        {
            return expectedRowVersion.Error;
        }

        _logger.LogInformation(
            "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | NewStatus: {NewStatus} | TraceId: {TraceId}",
            validation.TenantId, orderId, request.NewStatus, traceId);

        try
        {
            var result = await _adminOrderService.UpdateOrderStatusAsync(
                validation.TenantId!.Value,
                orderId,
                request,
                expectedRowVersion.RowVersion!.Value,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (AdminOrderNotFoundException)
        {
            return NotFound(AdminApiErrorResponse.OrderNotFound(traceId));
        }
        catch (AdminOrderImmutableException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (AdminOrderConcurrencyException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (InvalidAdminOrderStateException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}", 
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminApiErrorResponse.InternalError(traceId));
        }
    }

    /// <summary>
    /// Cancel an order with side effects.
    /// 
    /// POST /api/admin/orders/{orderId}/cancel
    /// X-Tenant-Id: {tenant-guid}
    /// 
    /// Request Body:
    /// {
    ///   "reason": "Customer requested cancellation"
    /// }
    /// 
    /// Side Effects:
    /// - Releases inventory
    /// - Cancels shipment (if exists)
    /// - Updates payment intent (if pending)
    /// - Emits existing events
    /// </summary>
    [HttpPost("{orderId:guid}/cancel")]
    [ProducesResponseType(typeof(AdminOrderCancellationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelOrder(
        [FromRoute] Guid orderId,
        [FromBody] AdminCancelOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | Reason: {Reason} | TraceId: {TraceId}",
            validation.TenantId, orderId, request.Reason, traceId);

        try
        {
            var result = await _adminOrderService.CancelOrderAsync(
                validation.TenantId!.Value,
                orderId,
                request,
                traceId,
                cancellationToken);

            return Ok(result);
        }
        catch (AdminOrderNotFoundException)
        {
            return NotFound(AdminApiErrorResponse.OrderNotFound(traceId));
        }
        catch (AdminOrderImmutableException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (AdminOrderPaymentRequiresRefundException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CancellationReasonRequiredException ex)
        {
            return BadRequest(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (AdminOrderException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}", 
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                AdminApiErrorResponse.InternalError(traceId));
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
            return (null, BadRequest(AdminApiErrorResponse.TenantHeaderMissing(traceId)));
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private (uint? RowVersion, IActionResult? Error) ParseRequiredIfMatch(string traceId)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var headerValues))
        {
            return (null, StatusCode(
                StatusCodes.Status428PreconditionRequired,
                new AdminApiErrorResponse("IF_MATCH_REQUIRED", "Order lifecycle mutations require current RowVersion evidence.", traceId)));
        }

        var header = headerValues.FirstOrDefault()?.Trim().Trim('"');
        if (!uint.TryParse(header, out var rowVersion) || rowVersion == 0)
        {
            return (null, BadRequest(new AdminApiErrorResponse("INVALID_IF_MATCH", "If-Match must contain the current order RowVersion.", traceId)));
        }

        return (rowVersion, null);
    }

    #endregion
}
