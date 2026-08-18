using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Paymob Payments Controller.
/// 
/// PHASE 10.4.D5: Paymob Payment Integration
/// 
/// This controller provides Storefront APIs for Paymob payments:
/// - Get payment status (polling after redirect)
/// - Create payment intent (if needed by frontend)
/// 
/// ARCHITECTURAL RULES:
/// - X-Tenant-Id header is REQUIRED
/// - Paymob configuration comes from TenantPaymentMethod (encrypted)
/// - Payment amount comes from Order (not frontend)
/// - Webhooks are source of truth for final status
/// </summary>
[ApiController]
[Route("api/storefront/payments/paymob")]
[AllowAnonymous]
public class StorefrontPaymobPaymentsController : ControllerBase
{
    private readonly IPaymobPaymentService _paymobPaymentService;
    private readonly IPaymentRequestAuthorizationService _paymentRequestAuthorizationService;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<StorefrontPaymobPaymentsController> _logger;

    public StorefrontPaymobPaymentsController(
        IPaymobPaymentService paymobPaymentService,
        IPaymentRequestAuthorizationService paymentRequestAuthorizationService,
        TenantContext tenantContext,
        ILogger<StorefrontPaymobPaymentsController> logger)
    {
        _paymobPaymentService = paymobPaymentService;
        _paymentRequestAuthorizationService = paymentRequestAuthorizationService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get Paymob payment status for an order.
    /// 
    /// Use this endpoint to poll for payment status after customer
    /// returns from Paymob payment page.
    /// 
    /// Polling Flow:
    /// 1. Customer completes payment on Paymob
    /// 2. Customer is redirected back to storefront
    /// 3. Storefront polls this endpoint until IsPaid = true
    /// 4. Webhook updates payment status in background
    /// 
    /// SECURITY: Validates customer ownership before returning status.
    /// </summary>
    /// <param name="orderId">Order ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet("status/{orderId:guid}")]
    [ProducesResponseType(typeof(PaymobPaymentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentStatus(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == null)
        {
            return BadRequest(new { error = "Tenant ID is required" });
        }

        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "GetPaymobPaymentStatus | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            orderId, tenantId, correlationId);

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId.Value,
                orderId,
                correlationId,
                GetCheckoutToken(),
                cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }

            var status = await _paymobPaymentService.GetPaymentStatusAsync(
                tenantId.Value,
                orderId,
                authorization.AuthenticatedCustomerId,
                correlationId,
                cancellationToken);

            return Ok(status);
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Tenant mismatch rejected for Paymob payment status | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                orderId,
                tenantId.Value,
                correlationId);
            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                correlationId));
        }
        catch (PaymentOrderNotFoundException)
        {
            return NotFound(new { error = "Order not found" });
        }
        catch (PaymentUnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Unauthorized payment status access attempt | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);
            return NotFound(new { error = "Order not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetPaymobPaymentStatus error | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);

            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to get payment status" });
        }
    }

    /// <summary>
    /// Get Paymob configuration status for the tenant.
    /// 
    /// Returns whether Paymob is configured and enabled.
    /// Used by storefront to determine if Paymob option should be shown.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(PaymobConfigurationStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetConfigurationStatus(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == null)
        {
            return BadRequest(new { error = "Tenant ID is required" });
        }

        try
        {
            var status = await _paymobPaymentService.GetConfigurationStatusAsync(
                tenantId.Value,
                cancellationToken);

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPaymobConfigurationStatus error | TenantId: {TenantId}", tenantId);

            return Ok(new PaymobConfigurationStatusDto
            {
                IsEnabled = false,
                IsConfigured = false,
                IsSandbox = true,
                ValidationMessage = "Failed to check configuration"
            });
        }
    }

    /// <summary>
    /// Create Paymob payment intent for an order.
    /// 
    /// This is typically called by CheckoutService during checkout,
    /// but can be called manually if needed to retry payment.
    /// 
    /// Requires: Order must exist and be in Pending status.
    /// SECURITY: Validates customer ownership before creating intent.
    /// </summary>
    /// <param name="orderId">Order ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("intent/{orderId:guid}")]
    [ProducesResponseType(typeof(PaymobPaymentIntentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePaymentIntent(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == null)
        {
            return BadRequest(new { error = "Tenant ID is required" });
        }

        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "CreatePaymobPaymentIntent | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            orderId, tenantId, correlationId);

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId.Value,
                orderId,
                correlationId,
                GetCheckoutToken(),
                cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }

            var response = await _paymobPaymentService.CreatePaymentIntentAsync(
                tenantId.Value,
                orderId,
                authorization.AuthenticatedCustomerId,
                false,
                correlationId,
                cancellationToken,
                Request.Headers["Idempotency-Key"].FirstOrDefault());

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Tenant mismatch rejected for Paymob payment intent creation | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                orderId,
                tenantId.Value,
                correlationId);
            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                correlationId));
        }
        catch (PaymentOrderNotFoundException)
        {
            return NotFound(new { error = "Order not found" });
        }
        catch (PaymentUnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Unauthorized payment intent creation attempt | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);
            return NotFound(new { error = "Order not found" });
        }
        catch (PaymobNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Paymob is unavailable for this store" });
        }
        catch (PaymentOrderInvalidStateException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PaymobApiException ex)
        {
            _logger.LogError(ex,
                "Paymob API error | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);

            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Payment service error. Please try again." });
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning(
                ex,
                "Paymob payment intent request rejected | OrderId: {OrderId} | ErrorCode: {ErrorCode} | CorrelationId: {CorrelationId}",
                orderId,
                ex.ErrorCode,
                correlationId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(ex.ErrorCode, "The selected payment provider is temporarily unavailable.", correlationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CreatePaymobPaymentIntent error | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
                orderId, correlationId);

            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to create payment intent" });
        }
    }

    private string GetCorrelationId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private string? GetCheckoutToken()
    {
        if (!Request.Headers.TryGetValue("X-Checkout-Token", out var checkoutToken) ||
            string.IsNullOrWhiteSpace(checkoutToken))
        {
            return GuestOrderAccessCookie.ReadCheckoutToken(Request);
        }

        return checkoutToken.ToString().Trim();
    }
}
