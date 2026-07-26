using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Helpers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Orders Read Controller.
/// 
/// PHASE 10.5.A: Order Lifecycle State Machine
/// 
/// ARCHITECTURAL RULES:
/// - MongoDB remains the served storefront order read model
/// - Customer-critical reads can use SQL authority metadata to prove existence/ownership
///   and trigger projection repair before Mongo is trusted
/// - Tenant-isolated
/// - Supports authenticated and guest order tracking
/// 
/// Endpoints:
/// - GET /storefront/orders/{orderId} - Get order details
/// - GET /storefront/orders/{orderId}/track - Get order tracking
/// - GET /storefront/orders/{orderId}/track-guest - Get guest order tracking with renewable token
/// - GET /storefront/account/orders - Get customer order history
/// - GET /storefront/orders/by-number/{orderNumber} - Get by order number
/// - GET /storefront/orders/track-guest - Track guest order by email+number (rate limited)
/// </summary>
[ApiController]
[Route("api/storefront")]
public class StorefrontOrdersReadController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IOrderReadRepository _orderRead;
    private readonly IStorefrontOrderProjectionRepairService _orderProjectionRepairService;
    private readonly IGuestOrderTrackingRateLimiter _guestTrackingRateLimiter;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly IEmailSender _emailSender;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StorefrontOrdersReadController> _logger;

    public StorefrontOrdersReadController(
        ApplicationDbContext dbContext,
        IOrderReadRepository orderRead,
        IStorefrontOrderProjectionRepairService orderProjectionRepairService,
        IGuestOrderTrackingRateLimiter guestTrackingRateLimiter,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        IEmailSender emailSender,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StorefrontOrdersReadController> logger)
    {
        _dbContext = dbContext;
        _orderRead = orderRead;
        _orderProjectionRepairService = orderProjectionRepairService;
        _guestTrackingRateLimiter = guestTrackingRateLimiter;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _emailSender = emailSender;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Get order details by ID (authenticated customer).
    /// </summary>
    [HttpGet("orders/{orderId:guid}")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(StorefrontOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrderById(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        var authority = await LoadOrderAuthoritySnapshotAsync(tenantId.Value, orderId, cancellationToken);
        if (authority == null || authority.OnlineCustomerId != customerId.Value)
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        _logger.LogInformation(
            "[StorefrontOrders] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
            "GET_ORDER",
            tenantId,
            customerId,
            orderId,
            traceId);

        if (!await TryRepairOrderProjectionAsync(tenantId.Value, orderId, traceId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var order = await _orderRead.GetByIdAsync(tenantId.Value, orderId, cancellationToken);
        if (order == null)
        {
            _logger.LogError(
                "[StorefrontOrders] SQL order authority exists but projection remained unavailable after repair | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                customerId,
                orderId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        if (!BelongsToAuthenticatedStorefrontCustomer(order, customerId.Value))
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        return Ok(MapToDto(order));
    }

    /// <summary>
    /// Get order tracking information by ID (authenticated customer).
    /// </summary>
    [HttpGet("orders/{orderId:guid}/track")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(StorefrontOrderTrackingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrderTracking(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        var authority = await LoadOrderAuthoritySnapshotAsync(tenantId.Value, orderId, cancellationToken);
        if (authority == null || authority.OnlineCustomerId != customerId.Value)
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        _logger.LogInformation(
            "[StorefrontOrders] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
            "GET_ORDER_TRACKING",
            tenantId,
            customerId,
            orderId,
            traceId);

        if (!await TryRepairOrderProjectionAsync(tenantId.Value, orderId, traceId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var order = await _orderRead.GetOrderTrackingAsync(tenantId.Value, orderId, cancellationToken);
        if (order == null)
        {
            _logger.LogError(
                "[StorefrontOrders] SQL order authority exists but tracking projection remained unavailable after repair | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                customerId,
                orderId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        if (!BelongsToAuthenticatedStorefrontCustomer(order, customerId.Value))
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        return Ok(MapToTrackingDto(order));
    }

    /// <summary>
    /// Get order tracking information for a guest order.
    /// Requires a renewable order/session-bound tracking token and current X-Session-Id.
    /// </summary>
    [HttpGet("orders/{orderId:guid}/track-guest")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(StorefrontOrderTrackingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetGuestOrderTracking(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitResult = await _guestTrackingRateLimiter.CheckAsync(clientIp);

        if (!rateLimitResult.Allowed)
        {
            Response.Headers["Retry-After"] = rateLimitResult.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(
                "RATE_LIMITED",
                "Too many tracking requests. Please try again later.",
                traceId));
        }

        var tenantId = StorefrontTenantContextResolver.GetTenantId(HttpContext);
        if (!tenantId.HasValue)
        {
            return BadRequest(new ApiErrorResponse(
                "TENANT_REQUIRED",
                "Tenant ID is required",
                traceId));
        }

        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        var authority = await LoadOrderAuthoritySnapshotAsync(tenantId.Value, orderId, cancellationToken);
        if (authority == null || !authority.IsGuestCheckout)
        {
            RecordGuestTrackingDenied(tenantId.Value, "order_not_found_or_not_guest");
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        if (string.IsNullOrWhiteSpace(authority.SessionId) ||
            string.IsNullOrWhiteSpace(sessionId) ||
            !string.Equals(authority.SessionId, sessionId, StringComparison.Ordinal))
        {
            RecordGuestTrackingDenied(tenantId.Value, "session_mismatch");
            return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(
                "SESSION_MISMATCH",
                "This order can only be tracked from the checkout session that created it.",
                traceId));
        }

        var trackingToken = ReadTrackingToken();
        if (!ValidateGuestTrackingToken(tenantId.Value, authority.OrderId, authority.SessionId, trackingToken))
        {
            RecordGuestTrackingDenied(
                tenantId.Value,
                string.IsNullOrWhiteSpace(trackingToken) ? "missing_token" : "invalid_token");

            return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(
                "TRACKING_TOKEN_REQUIRED",
                "A valid guest tracking token is required.",
                traceId));
        }

        if (!await TryRepairOrderProjectionAsync(tenantId.Value, orderId, traceId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var order = await _orderRead.GetOrderTrackingAsync(tenantId.Value, orderId, cancellationToken);
        if (order == null)
        {
            _logger.LogError(
                "[StorefrontOrders] SQL guest order authority exists but tracking projection remained unavailable after repair | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                orderId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var renewedToken = _checkoutSessionTokenService.CreateGuestTrackingToken(
            tenantId.Value,
            authority.OrderId,
            sessionId);
        var response = MapToTrackingDto(order);
        SetTrackingCookie(renewedToken);
        response.RenewedTrackingToken = renewedToken;
        _reliabilityTelemetry.RecordGuestRecoverySuccess(tenantId.Value, "order_tracking");

        return Ok(response);
    }

    // NOTE: Customer order history is handled by StorefrontAccountController.GetOrders
    // at /api/storefront/account/orders to avoid route duplication.

    /// <summary>
    /// Get order by order number (authenticated customer).
    /// </summary>
    [HttpGet("orders/by-number/{orderNumber}")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(StorefrontOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrderByNumber(
        string orderNumber,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        var authority = await LoadOrderAuthorityByNumberSnapshotAsync(
            tenantId.Value,
            orderNumber,
            cancellationToken);
        if (authority == null || authority.OnlineCustomerId != customerId.Value)
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        _logger.LogInformation(
            "[StorefrontOrders] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderNumber: {OrderNumber} | TraceId: {TraceId}",
            "GET_ORDER_BY_NUMBER",
            tenantId,
            customerId,
            orderNumber,
            traceId);

        if (!await TryRepairOrderByNumberProjectionAsync(tenantId.Value, orderNumber, traceId, cancellationToken))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var order = await _orderRead.GetByOrderNumberAsync(tenantId.Value, orderNumber, cancellationToken);

        if (order == null)
        {
            _logger.LogError(
                "[StorefrontOrders] SQL order authority exists but order-number projection remained unavailable after repair | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderNumber: {OrderNumber} | TraceId: {TraceId}",
                tenantId,
                customerId,
                orderNumber,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        if (!BelongsToAuthenticatedStorefrontCustomer(order, customerId.Value))
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        return Ok(MapToDto(order));
    }

    /// <summary>
    /// Track guest order by email and order number.
    /// No authentication required, but Redis-backed rate limited.
    /// 
    /// SECURITY HARDENING (Audit C-2):
    /// - Rate limited: 10 requests per minute per IP
    /// - Prevents brute-force enumeration of orders
    /// - Protects customer PII (names, addresses, order contents)
    /// </summary>
    [HttpGet("orders/track-guest")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(GuestTrackingRecoveryResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TrackGuestOrder(
        [FromQuery] string email,
        [FromQuery] string orderNumber,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();

        // =====================================================================
        // SECURITY HARDENING (Audit C-2): Rate limit BEFORE any business logic
        // =====================================================================
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var rateLimitResult = await _guestTrackingRateLimiter.CheckAsync(clientIp);

        if (!rateLimitResult.Allowed)
        {
            Response.Headers["Retry-After"] = rateLimitResult.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(
                "RATE_LIMITED",
                "Too many tracking requests. Please try again later.",
                traceId));
        }

        var tenantId = StorefrontTenantContextResolver.GetTenantId(HttpContext);

        if (!tenantId.HasValue)
        {
            return BadRequest(new ApiErrorResponse(
                "TENANT_REQUIRED",
                "Tenant ID is required",
                traceId));
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(orderNumber))
        {
            return BadRequest(new ApiErrorResponse(
                "INVALID_REQUEST",
                "Email and order number are required",
                traceId));
        }

        _logger.LogInformation(
            "[StorefrontOrders] {Action} | TenantId: {TenantId} | Email: {Email} | OrderNumber: {OrderNumber} | TraceId: {TraceId}",
            "REQUEST_GUEST_TRACKING_LINK",
            tenantId,
            MaskEmail(email),
            orderNumber,
            traceId);

        var authority = await LoadGuestOrderAuthorityByNumberAsync(
            tenantId.Value,
            orderNumber,
            cancellationToken);
        if (authority == null)
        {
            RecordGuestTrackingDenied(tenantId.Value, "email_or_order_mismatch");
            return Accepted(CreateGuestTrackingRecoveryResponse());
        }

        if (string.IsNullOrWhiteSpace(authority.CustomerEmail))
        {
            RecordGuestTrackingDenied(tenantId.Value, "missing_customer_email");
            return Accepted(CreateGuestTrackingRecoveryResponse());
        }

        if (!string.Equals(authority.CustomerEmail?.Trim(), email?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            RecordGuestTrackingDenied(tenantId.Value, "email_or_order_mismatch");
            return Accepted(CreateGuestTrackingRecoveryResponse());
        }

        if (!authority.IsGuestCheckout || string.IsNullOrWhiteSpace(authority.SessionId))
        {
            RecordGuestTrackingDenied(
                tenantId.Value,
                !authority.IsGuestCheckout ? "authenticated_order_lookup_denied" : "missing_order_session");
            return Accepted(CreateGuestTrackingRecoveryResponse());
        }

        try
        {
            if (!await TryRepairOrderByNumberProjectionAsync(tenantId.Value, orderNumber, traceId, cancellationToken))
            {
                return Accepted(CreateGuestTrackingRecoveryResponse());
            }

            var trackingToken = _checkoutSessionTokenService.CreateGuestTrackingToken(
                tenantId.Value,
                authority.OrderId,
                authority.SessionId);
            var trackingLink = BuildGuestTrackingMagicLink(authority.OrderId, trackingToken);

            await SendGuestTrackingLinkAsync(authority, trackingLink, cancellationToken);

            _logger.LogInformation(
                "[StorefrontOrders] Guest tracking recovery link sent | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                authority.OrderId,
                traceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "[StorefrontOrders] Failed to send guest tracking recovery link | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                authority.OrderId,
                traceId);
        }

        return Accepted(CreateGuestTrackingRecoveryResponse());
    }

    #region Helper Methods

    private (Guid? CustomerId, Guid? TenantId) GetCustomerAndTenantIds()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return (null, null);
        }

        return (customerId, tenantId);
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private static bool BelongsToAuthenticatedStorefrontCustomer(OrderDocument order, Guid onlineCustomerId) =>
        order.OnlineCustomerId.HasValue &&
        order.OnlineCustomerId.Value != Guid.Empty &&
        onlineCustomerId != Guid.Empty &&
        order.OnlineCustomerId.Value == onlineCustomerId;

    private static bool IsGuestStorefrontOrder(OrderDocument order) =>
        (!order.OnlineCustomerId.HasValue || order.OnlineCustomerId.Value == Guid.Empty) &&
        (!order.CustomerId.HasValue || order.CustomerId.Value == Guid.Empty);

    private async Task<bool> TryRepairOrderProjectionAsync(
        Guid tenantId,
        Guid orderId,
        string traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orderProjectionRepairService.EnsureOrderProjectedAsync(
                tenantId,
                orderId,
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "[StorefrontOrders] Projection repair failed for customer-critical storefront order read | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                orderId,
                traceId);
            return false;
        }
    }

    private async Task<bool> TryRepairOrderByNumberProjectionAsync(
        Guid tenantId,
        string orderNumber,
        string traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orderProjectionRepairService.EnsureOrderByNumberProjectedAsync(
                tenantId,
                orderNumber,
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "[StorefrontOrders] Projection repair failed for customer-critical storefront order-number read | TenantId: {TenantId} | OrderNumber: {OrderNumber} | TraceId: {TraceId}",
                tenantId,
                orderNumber,
                traceId);
            return false;
        }
    }

    private async Task<StorefrontOrderAuthoritySnapshot?> LoadOrderAuthoritySnapshotAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId &&
                order.Id == orderId &&
                order.Source == OrderSource.Ecommerce &&
                !order.IsDeleted)
            .Select(order => new StorefrontOrderAuthoritySnapshot(
                order.Id,
                order.OrderNumber,
                order.OnlineCustomerId,
                order.CustomerId,
                order.SessionId,
                order.GuestEmail))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<StorefrontOrderAuthoritySnapshot?> LoadOrderAuthorityByNumberSnapshotAsync(
        Guid tenantId,
        string orderNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return null;
        }

        var normalizedOrderNumber = orderNumber.Trim();
        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId &&
                order.OrderNumber == normalizedOrderNumber &&
                order.Source == OrderSource.Ecommerce &&
                !order.IsDeleted)
            .Select(order => new StorefrontOrderAuthoritySnapshot(
                order.Id,
                order.OrderNumber,
                order.OnlineCustomerId,
                order.CustomerId,
                order.SessionId,
                order.GuestEmail))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<StorefrontOrderAuthoritySnapshot?> LoadGuestOrderAuthorityByNumberAsync(
        Guid tenantId,
        string orderNumber,
        CancellationToken cancellationToken) =>
        await LoadOrderAuthorityByNumberSnapshotAsync(tenantId, orderNumber, cancellationToken);

    private sealed record StorefrontOrderAuthoritySnapshot(
        Guid OrderId,
        string OrderNumber,
        Guid? OnlineCustomerId,
        Guid? CustomerId,
        string? SessionId,
        string? CustomerEmail)
    {
        public bool IsGuestCheckout =>
            (!OnlineCustomerId.HasValue || OnlineCustomerId.Value == Guid.Empty) &&
            (!CustomerId.HasValue || CustomerId.Value == Guid.Empty);
    }

    private string? ReadTrackingToken()
    {
        // Prefer cookie (rotated token) over legacy headers
        var token = GuestOrderAccessCookie.ReadTrackingToken(Request);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }

        token = Request.Headers["X-Tracking-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }

        token = Request.Headers["X-Guest-Tracking-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }

        return null;
    }

    private void RecordGuestTrackingDenied(Guid tenantId, string reason)
    {
        _reliabilityTelemetry.RecordGuestTrackingUnauthorizedAttempt(tenantId, reason);
        _reliabilityTelemetry.RecordGuestTrackingDenied(tenantId, reason);

        if (reason.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            _reliabilityTelemetry.RecordTrackingTokenInvalidAccess(tenantId, reason);
        }
    }

    private static GuestTrackingRecoveryResponse CreateGuestTrackingRecoveryResponse() =>
        new()
        {
            Message = "If the order details match, a fresh tracking link will be sent to the order email."
        };

    private string BuildGuestTrackingMagicLink(Guid orderId, string trackingToken)
    {
        var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;
        var escapedToken = Uri.EscapeDataString(trackingToken);
        return $"{Request.Scheme}://{Request.Host}{pathBase}/orders/{orderId:D}/track#tracking_token={escapedToken}";
    }

    private async Task SendGuestTrackingLinkAsync(StorefrontOrderAuthoritySnapshot order, string trackingLink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var orderNumber = WebUtility.HtmlEncode(order.OrderNumber);
        var htmlLink = WebUtility.HtmlEncode(trackingLink);
        var subject = $"Your tracking link for order {order.OrderNumber}";
        var htmlBody =
            "<p>Use this secure link to track your order.</p>" +
            $"<p><a href=\"{htmlLink}\">Track order {orderNumber}</a></p>" +
            "<p>This link is short-lived. If it expires, request a fresh tracking link from the store.</p>";
        var textBody =
            $"Use this secure link to track order {order.OrderNumber}: {trackingLink}\n\n" +
            "This link is short-lived. If it expires, request a fresh tracking link from the store.";

        await _emailSender.SendEmailAsync(order.CustomerEmail!, subject, htmlBody, textBody);
    }

    private void SetTrackingCookie(string trackingToken)
    {
        var options = HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Options.IOptions<PointOfSale.API.Configuration.CheckoutSessionSecurityOptions>>()
            ?.Value;
        var environment = HttpContext.RequestServices.GetService<IWebHostEnvironment>();

        GuestOrderAccessCookie.SetTrackingToken(
            HttpContext,
            trackingToken,
            TimeSpan.FromMinutes(options?.GuestTrackingTokenTtlMinutes ?? 20),
            environment?.IsDevelopment() == true);
    }

    /// <summary>
    /// Masks email for structured log output to prevent PII leakage.
    /// SECURITY HARDENING (Audit I-4).
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    private static StorefrontOrderDto MapToDto(OrderDocument order)
    {
        return new StorefrontOrderDto
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ShippingStatus = order.ShippingStatus,
            Subtotal = order.Subtotal,
            PromotionDiscountAmount = order.PromotionDiscountAmount,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            ShippingCost = order.ShippingCost,
            Total = order.Total,
            Currency = order.Currency,
            PaymentMethodName = order.PaymentMethodName,
            ShippingMethodName = order.ShippingMethodName,
            CouponCode = order.CouponCode,
            TrackingNumber = order.TrackingNumber,
            CarrierName = order.CarrierName,
            EstimatedDeliveryDate = order.EstimatedDeliveryDate,
            DeliveredAt = order.DeliveredAt,
            CreatedAt = order.CreatedAt,
            ShippingAddress = order.ShippingAddress != null ? new StorefrontAddressDto
            {
                Street = order.ShippingAddress.Street,
                Street2 = order.ShippingAddress.Street2,
                City = order.ShippingAddress.City,
                State = order.ShippingAddress.State,
                ZipCode = order.ShippingAddress.ZipCode,
                Country = order.ShippingAddress.Country
            } : null,
            Items = order.Items.Select(i => new StorefrontOrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                SKU = i.SKU,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Total = i.Total,
                ImageUrl = i.ImageUrl
            }).ToList()
        };
    }

    private static StorefrontOrderSummaryDto MapToSummaryDto(OrderDocument order)
    {
        return new StorefrontOrderSummaryDto
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ShippingStatus = order.ShippingStatus,
            Total = order.Total,
            Currency = order.Currency,
            ItemCount = order.Items.Count,
            ShippingMethodName = order.ShippingMethodName,
            PaymentMethodName = order.PaymentMethodName,
            CreatedAt = order.CreatedAt,
            // Preview items (first 3)
            Items = order.Items.Take(3).Select(i => new StorefrontOrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                SKU = i.SKU,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Total = i.Total,
                ImageUrl = i.ImageUrl
            }).ToList()
        };
    }

    private static StorefrontOrderTrackingDto MapToTrackingDto(OrderDocument order)
    {
        return new StorefrontOrderTrackingDto
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ShippingStatus = order.ShippingStatus,
            Subtotal = order.Subtotal,
            PromotionDiscountAmount = order.PromotionDiscountAmount,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            ShippingCost = order.ShippingCost,
            Total = order.Total,
            Currency = order.Currency,
            PaymentMethodName = order.PaymentMethodName,
            ShippingMethodName = order.ShippingMethodName,
            TrackingNumber = order.TrackingNumber,
            CarrierName = order.CarrierName,
            TrackingUrl = order.TrackingUrl,
            EstimatedDeliveryDate = order.EstimatedDeliveryDate,
            DeliveredAt = order.DeliveredAt,
            CreatedAt = order.CreatedAt,
            CancellationReason = order.CancellationReason,
            ShippingAddress = order.ShippingAddress != null ? new StorefrontAddressDto
            {
                Street = order.ShippingAddress.Street,
                Street2 = order.ShippingAddress.Street2,
                City = order.ShippingAddress.City,
                State = order.ShippingAddress.State,
                ZipCode = order.ShippingAddress.ZipCode,
                Country = order.ShippingAddress.Country,
                Phone = order.ShippingAddress.Phone
            } : null,
            Items = order.Items.Select(i => new StorefrontOrderItemDto
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                SKU = i.SKU,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Total = i.Total,
                ImageUrl = i.ImageUrl
            }).ToList(),
            Timeline = order.Timeline.OrderByDescending(t => t.At).Select(t => new StorefrontTimelineItemDto
            {
                At = t.At,
                Type = t.Type,
                From = t.From,
                To = t.To,
                Description = t.Description ?? GetDefaultDescription(t.Type, t.To)
            }).ToList()
        };
    }

    private StorefrontOrderTrackingDto MapToMaskedTrackingDto(OrderDocument order)
    {
        return new StorefrontOrderTrackingDto
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ShippingStatus = order.ShippingStatus,
            Currency = order.Currency,
            ShippingMethodName = order.ShippingMethodName,
            EstimatedDeliveryDate = order.EstimatedDeliveryDate,
            DeliveredAt = order.DeliveredAt,
            CreatedAt = order.CreatedAt,
            CancellationReason = null, // SECURITY: Masked - may contain sensitive operator notes
            TrackingAccessVerified = false,
            RequiresTrackingToken = true,
            ShippingAddress = order.ShippingAddress != null ? new StorefrontAddressDto
            {
                City = order.ShippingAddress.City,
                Country = order.ShippingAddress.Country
            } : null,
            // SECURITY: Sanitized timeline - only expose minimal non-sensitive status transitions
            Timeline = order.Timeline.OrderByDescending(t => t.At).Select(t => new StorefrontTimelineItemDto
            {
                At = t.At,
                Type = t.Type,
                From = string.Empty, // Masked - may reveal internal state details
                To = string.Empty,   // Masked - may reveal internal state details
                Description = GetMaskedDescription(t.Type) // Generic description only, no state interpolation
            }).ToList()
        };
    }

    private bool ValidateGuestTrackingToken(Guid tenantId, Guid orderId, string? expectedSessionId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trackingValidation = _checkoutSessionTokenService.ValidateGuestTrackingToken(token, tenantId);
        if (trackingValidation.IsValid &&
            Guid.TryParse(trackingValidation.Subject, out var trackingOrderId) &&
            trackingOrderId == orderId &&
            !string.IsNullOrWhiteSpace(expectedSessionId) &&
            string.Equals(trackingValidation.Nonce, expectedSessionId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string GetMaskedDescription(string type)
    {
        return type switch
        {
            "OrderCreated" => "Order placed",
            "StatusChanged" => "Order status updated",
            "PaymentChanged" => "Payment status updated",
            "ShipmentChanged" => "Shipment status updated",
            "Shipped" => "Order shipped",
            "Delivered" => "Order delivered",
            "Cancelled" => "Order cancelled",
            _ => "Order updated"
        };
    }

    private static string GetDefaultDescription(string type, string to)
    {
        return type switch
        {
            "OrderCreated" => "Order placed",
            "StatusChanged" => $"Order status: {to}",
            "PaymentChanged" => $"Payment: {to}",
            "ShipmentChanged" => $"Shipment: {to}",
            "Shipped" => "Order shipped",
            "Delivered" => "Order delivered",
            "Cancelled" => "Order cancelled",
            _ => type
        };
    }

    #endregion
}

#region DTOs

public class StorefrontOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ShippingStatus { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    /// <summary>
    /// PHASE: Promotions Engine - Promotion discount amount
    /// </summary>
    public decimal PromotionDiscountAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PaymentMethodName { get; set; }
    public string? ShippingMethodName { get; set; }
    public string? CouponCode { get; set; }
    public string? TrackingNumber { get; set; }
    public string? CarrierName { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public StorefrontAddressDto? ShippingAddress { get; set; }
    public List<StorefrontOrderItemDto> Items { get; set; } = new();
}

public class StorefrontOrderSummaryDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ShippingStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string? ShippingMethodName { get; set; }
    public string? PaymentMethodName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<StorefrontOrderItemDto> Items { get; set; } = new();
}

public class StorefrontOrdersListDto
{
    public List<StorefrontOrderSummaryDto> Orders { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class StorefrontOrderItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public string? ImageUrl { get; set; }
}

public class StorefrontAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string? Street2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
}

public class StorefrontOrderTrackingDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ShippingStatus { get; set; } = string.Empty;
    
    // Pricing breakdown
    public decimal Subtotal { get; set; }
    /// <summary>
    /// PHASE: Promotions Engine - Promotion discount amount
    /// </summary>
    public decimal PromotionDiscountAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    
    // Payment and shipping
    public string? PaymentMethodName { get; set; }
    public string? ShippingMethodName { get; set; }
    
    // Tracking
    public string? TrackingNumber { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingUrl { get; set; }
    
    // Dates
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    
    public string? CancellationReason { get; set; }

    public bool TrackingAccessVerified { get; set; } = true;

    public bool RequiresTrackingToken { get; set; }

    public string? RenewedTrackingToken { get; set; }

    // Order details
    public StorefrontAddressDto? ShippingAddress { get; set; }
    public List<StorefrontOrderItemDto> Items { get; set; } = new();
    public List<StorefrontTimelineItemDto> Timeline { get; set; } = new();
}

public class GuestTrackingRecoveryResponse
{
    public string Message { get; set; } = string.Empty;
}

public class StorefrontTimelineItemDto
{
    public DateTime At { get; set; }
    public string Type { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Description { get; set; }
}

#endregion
