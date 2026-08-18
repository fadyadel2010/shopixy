using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Stripe Payments Controller.
/// 
/// Route: /api/storefront/payments/stripe
/// 
/// PHASE 10.4.D4: Stripe Integration (Global, Intent-Based, Production-Grade)
/// 
/// ARCHITECTURAL RULES:
/// - Order MUST exist before calling create-intent
/// - Returns clientSecret + publishableKey for Stripe.js
/// - NO pricing logic in this controller
/// - NO order creation in this controller
/// - Tenant isolation is mandatory
/// </summary>
[ApiController]
[Route("api/storefront/payments/stripe")]
[AllowAnonymous]
public class StorefrontStripePaymentsController : ControllerBase
{
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IPaymentRequestAuthorizationService _paymentRequestAuthorizationService;
    private readonly ILogger<StorefrontStripePaymentsController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontStripePaymentsController(
        IStripePaymentService stripePaymentService,
        IPaymentRequestAuthorizationService paymentRequestAuthorizationService,
        ILogger<StorefrontStripePaymentsController> logger)
    {
        _stripePaymentService = stripePaymentService;
        _paymentRequestAuthorizationService = paymentRequestAuthorizationService;
        _logger = logger;
    }

    /// <summary>
    /// Create a Stripe PaymentIntent for an order.
    /// 
    /// Prerequisites:
    /// - Order must exist with PaymentMethodCode = "STRIPE"
    /// - Order must be in Pending status
    /// - Tenant must have Stripe configured and enabled
    /// 
    /// Returns:
    /// - clientSecret: For Stripe.js Payment Element
    /// - publishableKey: For Stripe.js initialization
    /// 
    /// Usage in frontend:
    /// 1. Call this endpoint after order is created
    /// 2. Initialize Stripe.js with publishableKey
    /// 3. Use clientSecret to confirm payment
    /// 4. Webhook will update order status
    /// </summary>
    [HttpPost("create-intent")]
    [ProducesResponseType(typeof(StripePaymentIntentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CreatePaymentIntent(
        [FromBody] CreateStripePaymentIntentRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate tenant header
        var tenantId = GetTenantId();
        if (tenantId == null)
        {
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        // Validate request
        if (request.OrderId == Guid.Empty)
        {
            return BadRequest(new ApiErrorResponse("INVALID_ORDER_ID", "Order ID is required", traceId));
        }

        try
        {
            var effectiveCheckoutToken = string.IsNullOrWhiteSpace(request.CheckoutToken)
                ? GuestOrderAccessCookie.ReadCheckoutToken(Request)
                : request.CheckoutToken;

            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId.Value,
                request.OrderId,
                traceId,
                effectiveCheckoutToken,
                cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }

            var authenticatedCustomerId = authorization.AuthenticatedCustomerId;
            if (request.CustomerId.HasValue &&
                authenticatedCustomerId.HasValue &&
                request.CustomerId.Value != authenticatedCustomerId.Value)
            {
                _logger.LogWarning(
                    "Ignoring mismatched customer ID in Stripe payment intent request | OrderId: {OrderId} | BodyCustomerId: {BodyCustomerId} | AuthenticatedCustomerId: {AuthenticatedCustomerId} | TraceId: {TraceId}",
                    request.OrderId,
                    request.CustomerId.Value,
                    authenticatedCustomerId.Value,
                    traceId);
            }

            _logger.LogInformation(
                "CreatePaymentIntent request | OrderId: {OrderId} | TenantId: {TenantId} | TraceId: {TraceId}",
                request.OrderId, tenantId, traceId);

            var response = await _stripePaymentService.CreatePaymentIntentAsync(
                tenantId.Value,
                request.OrderId,
                effectiveCheckoutToken ?? string.Empty,
                authenticatedCustomerId,
                false,
                traceId,
                cancellationToken,
                Request.Headers["Idempotency-Key"].FirstOrDefault());

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Stripe payment intent rejected due to tenant mismatch | OrderId: {OrderId} | TenantId: {TenantId} | TraceId: {TraceId}",
                request.OrderId,
                tenantId,
                traceId);

            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                "Unauthorized access to payment intent | OrderId: {OrderId} | TenantId: {TenantId} | Error: {Error} | TraceId: {TraceId}",
                request.OrderId, tenantId, ex.Message, traceId);

            return Unauthorized(new ApiErrorResponse("UNAUTHORIZED_ACCESS", ex.Message, traceId));
        }
        catch (StripeNotConfiguredException ex)
        {
            _logger.LogWarning(
                "Stripe not configured | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(ex.ErrorCode, "Stripe is unavailable for this store.", traceId));
        }
        catch (PaymentOrderNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentUnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                "Unauthorized Stripe payment intent access | OrderId: {OrderId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                ex.OrderId,
                ex.CustomerId,
                traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, "Order not found", traceId));
        }
        catch (PaymentOrderInvalidStateException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (StripeApiException ex)
        {
            _logger.LogError(
                "Stripe API error | OrderId: {OrderId} | Error: {Error} | TraceId: {TraceId}",
                request.OrderId, ex.Message, traceId);

            return StatusCode(502, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error creating payment intent | OrderId: {OrderId} | TraceId: {TraceId}",
                request.OrderId, traceId);

            return StatusCode(500, new ApiErrorResponse("INTERNAL_ERROR", "Failed to create payment intent", traceId));
        }
    }

    /// <summary>
    /// Get payment status for an order.
    /// 
    /// Use this to poll payment status from the frontend after
    /// the user completes the Stripe payment flow.
    /// 
    /// Headers:
    /// - X-Checkout-Token (optional): Checkout session token for ownership validation.
    ///   When absent, the method falls back to reading the checkout token from the guest order access cookie
    ///   via GuestOrderAccessCookie.ReadCheckoutToken(Request).
    ///   Frontend clients may omit this header when a guest token is stored in cookies.
    /// </summary>
    [HttpGet("status/{orderId:guid}")]
    [ProducesResponseType(typeof(StripePaymentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentStatus(
        [FromRoute] Guid orderId,
        [FromHeader(Name = "X-Checkout-Token")] string? checkoutToken,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = GetTenantId();
        var effectiveCheckoutToken = string.IsNullOrWhiteSpace(checkoutToken)
            ? GuestOrderAccessCookie.ReadCheckoutToken(Request)
            : checkoutToken;

        if (tenantId == null)
        {
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        if (string.IsNullOrWhiteSpace(effectiveCheckoutToken))
        {
            return BadRequest(new ApiErrorResponse("INVALID_CHECKOUT_TOKEN", "A valid guest checkout token is required", traceId));
        }

        try
        {
            var authorization = await _paymentRequestAuthorizationService.AuthorizeOrderAccessAsync(
                HttpContext,
                tenantId.Value,
                orderId,
                traceId,
                effectiveCheckoutToken,
                cancellationToken);
            if (!authorization.Authorized)
            {
                return authorization.ToActionResult();
            }

            var response = await _stripePaymentService.GetPaymentIntentStatusAsync(
                tenantId.Value,
                orderId,
                effectiveCheckoutToken,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (PaymentOrderNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                "Unauthorized payment status access | OrderId: {OrderId} | TraceId: {TraceId}",
                orderId, traceId);
            return Unauthorized(new ApiErrorResponse("UNAUTHORIZED", ex.Message, traceId));
        }
    }

    /// <summary>
    /// Check if Stripe is configured and available for this tenant.
    /// 
    /// Used by frontend to determine if Stripe payment option should be shown.
    /// </summary>
    [HttpGet("configuration")]
    [ProducesResponseType(typeof(StripeConfigurationStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfigurationStatus(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = GetTenantId();

        if (tenantId == null)
        {
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        var response = await _stripePaymentService.GetConfigurationStatusAsync(
            tenantId.Value,
            cancellationToken);

        return Ok(response);
    }

    #region Helper Methods

    private Guid? GetTenantId()
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.GetTenantId(HttpContext);
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}

#region Request DTOs

/// <summary>
/// Request to create a Stripe PaymentIntent.
/// </summary>
public class CreateStripePaymentIntentRequest
{
    /// <summary>
    /// Order ID (must exist).
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Optional customer ID (for tracking).
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>
    /// Checkout session token for ownership validation.
    /// Required to prevent unauthorized payment intent creation.
    /// </summary>
    public string CheckoutToken { get; set; } = string.Empty;
}

#endregion

// Using ApiErrorResponse from PointOfSale.API.DTOs.Storefront
