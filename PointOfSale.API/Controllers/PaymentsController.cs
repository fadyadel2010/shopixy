using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Payments Controller - Payment Intent and COD operations.
/// 
/// Routes:
/// - POST /api/payments/intents/{orderId}
/// - POST /api/payments/cod/confirm/{orderId}
/// - GET /api/payments/orders/{orderId}/status
/// 
/// PHASE: FINAL HARDENING - Unified Tenant Resolution
/// 
/// CRITICAL: This controller now uses ITenantContext exclusively.
/// Tenant resolution is handled by middleware. No manual header parsing.
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID or slug (handled by TenantMiddleware)
/// - X-Request-Id: string (required for idempotency on POST/PUT)
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize(AuthenticationSchemes = "StorefrontJwt,CustomerJwt")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentRequestAuthorizationService _paymentRequestAuthorizationService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PaymentsController> _logger;

    private const string RequestIdHeader = "X-Request-Id";
    private const string SessionIdHeader = "X-Session-Id";
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;

    public PaymentsController(
        IPaymentService paymentService,
        IPaymentRequestAuthorizationService paymentRequestAuthorizationService,
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _paymentRequestAuthorizationService = paymentRequestAuthorizationService;
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates a payment intent for an order.
    /// 
    /// For COD orders: Status = Skipped, order confirmed immediately
    /// For Online orders: Status = Pending, returns redirect URL
    /// </summary>
    /// <param name="orderId">Order ID</param>
    /// <param name="request">Payment intent request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment intent response</returns>
    [AllowAnonymous]
    [HttpPost("intents/{orderId:guid}")]
    [ProducesResponseType(typeof(PaymentIntentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePaymentIntent(
        [FromRoute] Guid orderId,
        [FromBody] CreatePaymentIntentRequestDto? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        
        // HARDENING: Use ITenantContext - middleware handles resolution
        var tenantId = _tenantContext.RequiredTenantId;

        // Validate X-Request-Id for idempotency
        var requestId = GetRequestId(traceId);
        if (requestId == null)
        {
            return BadRequest(new ApiErrorResponse(
                "REQUEST_ID_MISSING",
                "X-Request-Id header is required for idempotency",
                traceId));
        }

        _logger.LogInformation(
            "CreatePaymentIntent | TenantId: {TenantId} | OrderId: {OrderId} | RequestId: {RequestId} | TraceId: {TraceId}",
            tenantId, orderId, requestId, traceId);

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId,
                orderId,
                traceId,
                cancellationToken: cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }

            request ??= new CreatePaymentIntentRequestDto();
            var orderPaymentMethodCode = await _dbContext.Orders
                .Where(order => order.Id == orderId && order.TenantId == tenantId && !order.IsDeleted)
                .Select(order => order.PaymentMethodCode)
                .FirstOrDefaultAsync(cancellationToken);

            var hasManualWalletRequestFields =
                !string.IsNullOrWhiteSpace(request.WalletType) ||
                !string.IsNullOrWhiteSpace(request.WalletPhoneNumber) ||
                !string.IsNullOrWhiteSpace(request.ProofFileKey) ||
                string.Equals(request.Provider, "ManualWallet", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(orderPaymentMethodCode, "MANUALWALLET", StringComparison.OrdinalIgnoreCase) ||
                hasManualWalletRequestFields)
            {
                return BadRequest(new ApiErrorResponse(
                    "MANUAL_WALLET_CHECKOUT_ONLY",
                    "Manual wallet payment intents must be created through the checkout flow.",
                    traceId));
            }

            var response = await _paymentService.CreatePaymentIntentAsync(
                tenantId,
                orderId,
                request,
                promotionCalculationFailed: false, // Direct API call - not from checkout flow
                requestId,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (PaymentOrderNotFoundException ex)
        {
            _logger.LogWarning("Order not found | OrderId: {OrderId} | TraceId: {TraceId}", ex.OrderId, traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentOrderInvalidStateException ex)
        {
            _logger.LogWarning(
                "Order invalid state | OrderId: {OrderId} | Current: {Current} | Expected: {Expected} | TraceId: {TraceId}",
                ex.OrderId, ex.CurrentStatus, ex.ExpectedStatus, traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentProviderNotSupportedException ex)
        {
            _logger.LogWarning("Provider not supported | Provider: {Provider} | TraceId: {TraceId}", ex.Provider, traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentManualWalletNotConfiguredException ex)
        {
            _logger.LogWarning(
                "Manual wallet payment rejected because merchant wallet is not configured | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                orderId,
                traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentManualWalletProofException ex)
        {
            _logger.LogWarning(
                "Manual wallet payment rejected because the proof reference is invalid | OrderId: {OrderId} | TraceId: {TraceId}",
                orderId,
                traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentManualWalletCheckoutOnlyException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentUnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                "Unauthorized access | OrderId: {OrderId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                ex.OrderId, ex.CustomerId, traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, "Order not found", traceId));
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Payment error | OrderId: {OrderId} | TraceId: {TraceId}", orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(ex.ErrorCode, "Payment processing failed. Please try again.", traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error | OrderId: {OrderId} | TraceId: {TraceId}", orderId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Confirms a COD (Cash on Delivery) order.
    /// 
    /// Validates PaymentMethod = COD, confirms order, confirms inventory.
    /// </summary>
    /// <param name="orderId">Order ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>COD confirmation response</returns>
    [AllowAnonymous]
    [HttpPost("cod/confirm/{orderId:guid}")]
    [ProducesResponseType(typeof(CodConfirmResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmCodOrder(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        
        // HARDENING: Use ITenantContext - middleware handles resolution
        var tenantId = _tenantContext.RequiredTenantId;

        // Validate X-Request-Id for idempotency
        var requestId = GetRequestId(traceId);
        if (requestId == null)
        {
            return BadRequest(new ApiErrorResponse(
                "REQUEST_ID_MISSING",
                "X-Request-Id header is required for idempotency",
                traceId));
        }

        _logger.LogInformation(
            "ConfirmCodOrder | TenantId: {TenantId} | OrderId: {OrderId} | RequestId: {RequestId} | TraceId: {TraceId}",
            tenantId, orderId, requestId, traceId);

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId,
                orderId,
                traceId,
                cancellationToken: cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }
            var response = await _paymentService.ConfirmCodOrderAsync(
                tenantId,
                orderId,
                requestId,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (PaymentOrderNotFoundException ex)
        {
            _logger.LogWarning("Order not found | OrderId: {OrderId} | TraceId: {TraceId}", ex.OrderId, traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CodNotApplicableException ex)
        {
            _logger.LogWarning(
                "COD not applicable | OrderId: {OrderId} | PaymentMethod: {PaymentMethod} | TraceId: {TraceId}",
                ex.OrderId, ex.PaymentMethod, traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentUnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                "Unauthorized access | OrderId: {OrderId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                ex.OrderId, ex.CustomerId, traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, "Order not found", traceId));
        }
        catch (PaymentException ex)
        {
            _logger.LogError(ex, "Payment error | OrderId: {OrderId} | TraceId: {TraceId}", orderId, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(ex.ErrorCode, "Payment processing failed. Please try again.", traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error | OrderId: {OrderId} | TraceId: {TraceId}", orderId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets the canonical payment status for storefront confirmation flows.
    /// Only PAID can be treated as success by the UI.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("status/{orderId:guid}")]
    [ProducesResponseType(typeof(CanonicalPaymentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCanonicalPaymentStatus(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "GetCanonicalPaymentStatus | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
            tenantId, orderId, traceId);

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId,
                orderId,
                traceId,
                cancellationToken: cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }
            var response = await _paymentService.GetCanonicalPaymentStatusAsync(
                tenantId,
                orderId,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (PaymentOrderNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error | OrderId: {OrderId} | TraceId: {TraceId}", orderId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Gets the payment status of an order.
    /// </summary>
    /// <param name="orderId">Order ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Order payment status</returns>
    [AllowAnonymous]
    [HttpGet("orders/{orderId:guid}/status")]
    [ProducesResponseType(typeof(OrderPaymentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderPaymentStatus(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // HARDENING: Use ITenantContext - middleware handles resolution
        var tenantId = _tenantContext.RequiredTenantId;

        _logger.LogInformation(
            "GetOrderPaymentStatus | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
            tenantId, orderId, traceId);

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId,
                orderId,
                traceId,
                cancellationToken: cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }
            var response = await _paymentService.GetOrderPaymentStatusAsync(
                tenantId,
                orderId,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (PaymentOrderNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error | OrderId: {OrderId} | TraceId: {TraceId}", orderId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Helper Methods

    /// <summary>
    /// Gets the X-Request-Id header value for idempotency.
    /// Returns null if header is missing or empty.
    /// </summary>
    private string? GetRequestId(string traceId)
    {
        if (!Request.Headers.TryGetValue(RequestIdHeader, out var requestIdHeaderValue) ||
            string.IsNullOrWhiteSpace(requestIdHeaderValue))
        {
            _logger.LogWarning("Missing X-Request-Id header | TraceId: {TraceId}", traceId);
            return null;
        }

        return requestIdHeaderValue.ToString()!.Trim();
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
