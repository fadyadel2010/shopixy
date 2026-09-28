using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Analytics;
using PointOfSale.API.Services.PhoneRisk;
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
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly IAnalyticsSessionJourneyService _sessionJourneyService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminOrdersController> _logger;

    public AdminOrdersController(
        IAdminOrderService adminOrderService,
        IPhoneRiskProfileService phoneRiskProfileService,
        IAnalyticsSessionJourneyService sessionJourneyService,
        ITenantContext tenantContext,
        ILogger<AdminOrdersController> logger)
    {
        _adminOrderService = adminOrderService;
        _phoneRiskProfileService = phoneRiskProfileService;
        _sessionJourneyService = sessionJourneyService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet("{orderId:guid}/customer-trust")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminOrderCustomerTrustDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerTrust(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _phoneRiskProfileService.GetOrderCustomerTrustAsync(
                validation.TenantId!.Value,
                orderId,
                cancellationToken);
            return result == null
                ? NotFound(AdminApiErrorResponse.OrderNotFound(traceId))
                : Ok(result);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "GetCustomerTrust | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}",
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
    }

    [HttpGet("{orderId:guid}/session-intelligence")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(AdminOrderSessionIntelligenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetSessionIntelligence(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var journey = await _sessionJourneyService.GetOrderJourneyAsync(
                validation.TenantId!.Value,
                orderId,
                cancellationToken);
            if (journey == null)
            {
                return NoContent();
            }

            return Ok(new AdminOrderSessionIntelligenceDto
            {
                OrderId = journey.OrderId,
                Source = journey.Source,
                Medium = journey.Medium,
                Device = journey.Device,
                VisitedPages = journey.VisitedPages,
                DurationSeconds = journey.DurationSeconds,
                FirstVisitAtUtc = journey.FirstVisitAtUtc,
                CheckoutVisitAtUtc = journey.CheckoutVisitAtUtc,
                PurchaseCompletedAtUtc = journey.PurchaseCompletedAtUtc,
                SessionsCount = journey.SessionsCount,
                EventCount = journey.EventCount,
            });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "GetSessionIntelligence | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}",
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new AdminApiErrorResponse("SESSION_INTELLIGENCE_UNAVAILABLE", "Session intelligence is temporarily unavailable.", traceId));
        }
    }

    [HttpPost("{orderId:guid}/customer-phone/block")]
    [ProducesResponseType(typeof(AdminPhoneRiskStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BlockCustomerPhone(
        [FromRoute] Guid orderId,
        [FromBody] AdminBlockOrderPhoneRequest request,
        CancellationToken cancellationToken = default)
    {
        return await UpdateOrderPhoneAsync(
            orderId,
            async (tenantId, phoneNumber) => await _phoneRiskProfileService.BlockAsync(
                tenantId,
                phoneNumber,
                request.Reason,
                GetCurrentUserId(),
                cancellationToken),
            cancellationToken);
    }

    [HttpPost("{orderId:guid}/customer-phone/unblock")]
    [ProducesResponseType(typeof(AdminPhoneRiskStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnblockCustomerPhone(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        return await UpdateOrderPhoneAsync(
            orderId,
            async (tenantId, phoneNumber) => await _phoneRiskProfileService.UnblockAsync(
                tenantId,
                phoneNumber,
                cancellationToken),
            cancellationToken);
    }

    [HttpPost("{orderId:guid}/customer-phone/report-spam")]
    [ProducesResponseType(typeof(AdminPhoneRiskStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportCustomerPhoneAsSpam(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var trust = await _phoneRiskProfileService.GetOrderCustomerTrustAsync(
                validation.TenantId!.Value,
                orderId,
                cancellationToken);
            if (trust == null)
            {
                return NotFound(AdminApiErrorResponse.OrderNotFound(traceId));
            }

            if (!trust.HasPhone)
            {
                return Conflict(new AdminApiErrorResponse(
                    "ORDER_PHONE_UNAVAILABLE",
                    "The order does not have an available customer phone number.",
                    traceId));
            }

            var result = await _phoneRiskProfileService.ReportSpamAsync(
                validation.TenantId.Value,
                orderId,
                GetCurrentUserId(),
                cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Conflict(new AdminApiErrorResponse("ORDER_PHONE_UNAVAILABLE", ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "ReportCustomerPhoneAsSpam | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}",
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
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
        [FromQuery] OrderSource? source = null,
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
            Source = OrderSource.Ecommerce,
            FromDate = fromDate,
            ToDate = toDate,
            CustomerId = customerId,
            Search = search,
            SortBy = sortBy,
            SortDirection = sortDirection
        };

        if (source.HasValue && source != OrderSource.Ecommerce)
        {
            return BadRequest(new AdminApiErrorResponse(
                "ORDER_SCOPE_INVALID",
                "Storefront order management only admits Ecommerce orders.",
                traceId));
        }

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

    [HttpGet("{orderId:guid}/checkout-attachments/{attachmentId:guid}/download")]
    [Authorize(Roles = "Owner,Admin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DownloadCheckoutAttachment(
        [FromRoute] Guid orderId,
        [FromRoute] Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var validation = ValidateTenantHeader(traceId);

        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            var result = await _adminOrderService.OpenCheckoutAttachmentDownloadAsync(
                validation.TenantId!.Value,
                orderId,
                attachmentId,
                traceId,
                cancellationToken);

            Response.Headers.CacheControl = "private, no-store, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: false);
        }
        catch (AdminOrderNotFoundException)
        {
            return NotFound(AdminApiErrorResponse.OrderNotFound(traceId));
        }
        catch (AdminOrderAttachmentNotFoundException ex)
        {
            return NotFound(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (AdminOrderAttachmentNotDownloadableException ex)
        {
            return BadRequest(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (AdminOrderAttachmentIntegrityException ex)
        {
            return Conflict(new AdminApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "DownloadCheckoutAttachment | TenantId: {TenantId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | Error | TraceId: {TraceId}",
                validation.TenantId,
                orderId,
                attachmentId,
                traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new AdminApiErrorResponse("ATTACHMENT_DOWNLOAD_UNAVAILABLE", "Attachment download is temporarily unavailable.", traceId));
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

    private async Task<IActionResult> UpdateOrderPhoneAsync(
        Guid orderId,
        Func<Guid, string, Task<AdminPhoneRiskStatusDto>> update,
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
            var trust = await _phoneRiskProfileService.GetOrderCustomerTrustAsync(
                validation.TenantId!.Value,
                orderId,
                cancellationToken);
            if (trust == null)
            {
                return NotFound(AdminApiErrorResponse.OrderNotFound(traceId));
            }

            if (!trust.HasPhone)
            {
                return Conflict(new AdminApiErrorResponse(
                    "ORDER_PHONE_UNAVAILABLE",
                    "The order does not have an available customer phone number.",
                    traceId));
            }

            return Ok(await update(validation.TenantId.Value, trust.PhoneNumber!));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AdminApiErrorResponse("PHONE_NUMBER_INVALID", ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "UpdateOrderPhone | TenantId: {TenantId} | OrderId: {OrderId} | Error | TraceId: {TraceId}",
                validation.TenantId, orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, AdminApiErrorResponse.InternalError(traceId));
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(claimValue, out var userId) ? userId : null;
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
