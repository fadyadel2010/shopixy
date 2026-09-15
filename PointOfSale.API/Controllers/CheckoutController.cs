using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Checkout Controller - Cart to Order conversion.
/// 
/// Route: POST /api/storefront/checkout
/// 
/// PHASE 3.5: CHECKOUT API
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// - X-Session-Id: string (required for guest checkout)
/// - X-Customer-Id: GUID (optional, for registered customers)
/// - Idempotency-Key: optional compatibility header only
/// - X-Request-Id: optional compatibility header; must match Idempotency-Key when both are provided
/// 
/// Architecture:
/// - Cart is READ-ONLY during checkout
/// - PostgreSQL is Source of Truth for Orders
/// - Redis used for: Cart snapshot and owned distributed locks
/// - PostgreSQL persists checkout idempotency claims/results
/// - MongoDB is read-only (validation only)
/// - Payment is NOT executed (intent only)
/// 
/// Response Codes:
/// - 200 OK: Order created successfully (or idempotent response)
/// - 400 Bad Request: Invalid request or cart
/// - 403 Forbidden: Cart ownership mismatch
/// - 404 Not Found: Cart not found
/// - 409 Conflict: Lock contention, price changed, or out of stock
/// - 503 Service Unavailable: Redis/service unavailable
/// </summary>
[ApiController]
[Route("api/storefront/checkout")]
[AllowAnonymous]
public class CheckoutController : ControllerBase
{
    private static readonly HashSet<string> AllowedInternalScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "internal",
        "service",
        "internal_service",
    };

    private readonly ICheckoutService _checkoutService;
    private readonly ICheckoutAttachmentService _checkoutAttachmentService;
    private readonly ILogger<CheckoutController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";
    private const string SessionIdHeader = "X-Session-Id";
    private const string CustomerIdHeader = "X-Customer-Id";
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;

    public CheckoutController(
        ICheckoutService checkoutService,
        ICheckoutAttachmentService checkoutAttachmentService,
        ILogger<CheckoutController> logger)
    {
        _checkoutService = checkoutService;
        _checkoutAttachmentService = checkoutAttachmentService;
        _logger = logger;
    }

    /// <summary>
    /// Processes checkout and creates an order from the cart.
    /// 
    /// This endpoint is idempotent - the same checkout key replays the stored response.
    /// 
    /// Steps:
    /// 1. Acquire distributed lock on cart
    /// 2. Check idempotency (return cached if found)
    /// 3. Snapshot cart from Redis
    /// 4. Recalculate prices from PostgreSQL
    /// 5. Validate and reserve inventory
    /// 6. Create order in PostgreSQL
    /// 7. Generate payment intent (NO payment execution)
    /// 8. Store idempotency result
    /// 
    /// Cart remains UNCHANGED during checkout.
    /// 
    /// Required Headers:
    /// - Idempotency-Key: optional compatibility header only
    /// - X-Tenant-Id: GUID - Tenant identifier
    /// - X-Session-Id: string - Session identifier (required for guest checkout)
    /// - X-Customer-Id: GUID - Customer identifier (optional, for authenticated customers)
    /// </summary>
    /// <param name="request">Checkout request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Order created response with payment reference</returns>
    /// <response code="200">Order created successfully</response>
    /// <response code="400">Invalid request, cart empty, or validation failed. Error codes: INVALID_REQUEST, IDEMPOTENCY_KEY_MISMATCH, CART_EMPTY, etc.</response>
    /// <response code="401">Tenant mismatch - authenticated customer does not belong to resolved tenant</response>
    /// <response code="403">Cart does not belong to tenant/owner</response>
    /// <response code="404">Cart not found or expired</response>
    /// <response code="409">Checkout in progress, price changed, or out of stock</response>
    /// <response code="429">Too many requests</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CheckoutResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        // Validate request body
        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required", traceId));
        }

        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "Checkout invalid request | Error: {ErrorCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                errorCode, validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        using var checkoutLogScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = "StorefrontCheckout",
            ["TenantId"] = validation.TenantId,
            ["CartId"] = request.CartId,
            ["CheckoutSessionId"] = request.CheckoutSessionId,
            ["TraceId"] = traceId,
        });

        _logger.LogInformation(
            "Checkout request | TenantId: {TenantId} | CartId: {CartId} | CheckoutId: {CheckoutId} | OwnerType: {OwnerType} | TraceId: {TraceId}",
            validation.TenantId, request.CartId, validation.RequestId, validation.OwnerType, traceId);

        try
        {
            var response = await _checkoutService.ProcessCheckoutAsync(
                validation.TenantId!.Value,
                validation.OwnerId!,
                validation.OwnerType!,
                validation.SessionId,
                validation.CustomerId,
                validation.RequestId!,
                request,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "Checkout success | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | TraceId: {TraceId}",
                validation.TenantId, request.CartId, response.OrderId, response.OrderNumber, traceId);

            ApplyGuestOrderAccessCookies(response);
            return Ok(response);
        }
        catch (CheckoutLockException ex)
        {
            _logger.LogWarning(
                "Checkout lock conflict | CartId: {CartId} | TraceId: {TraceId}",
                ex.CartId, traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutLockInfrastructureException ex)
        {
            _logger.LogError(
                ex,
                "Checkout lock infrastructure unavailable | CartId: {CartId} | TraceId: {TraceId}",
                ex.CartId,
                traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutIdempotencyConflictException ex)
        {
            _logger.LogWarning(
                "Checkout idempotency payload conflict | CheckoutId: {CheckoutId} | TraceId: {TraceId}",
                ex.CheckoutId,
                traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutIdempotencyInProgressException ex)
        {
            _logger.LogWarning(
                "Checkout idempotency in progress | CheckoutId: {CheckoutId} | TraceId: {TraceId}",
                ex.CheckoutId,
                traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCartNotFoundException ex)
        {
            _logger.LogWarning(
                "Checkout cart not found | CartId: {CartId} | TraceId: {TraceId}",
                ex.CartId, traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCartOwnershipException ex)
        {
            _logger.LogWarning(
                "Checkout cart ownership mismatch | CartId: {CartId} | TraceId: {TraceId}",
                ex.CartId, traceId);
            return StatusCode(StatusCodes.Status403Forbidden, 
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCustomerInactiveException ex)
        {
            _logger.LogWarning(
                "Checkout blocked inactive customer | CustomerId: {CustomerId} | TraceId: {TraceId}",
                ex.CustomerId,
                traceId);
            return Unauthorized(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutPhoneBlockedException ex)
        {
            _logger.LogWarning(
                "Checkout blocked phone | Phone: {Phone} | TraceId: {TraceId}",
                MaskPhone(ex.PhoneNumber),
                traceId);
            return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCartInvalidException ex)
        {
            _logger.LogWarning(
                "Checkout cart invalid | CartId: {CartId} | TraceId: {TraceId}",
                ex.CartId, traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutDynamicFieldValidationException ex)
        {
            _logger.LogWarning(
                "Checkout dynamic field validation failed | ErrorCode: {ErrorCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                ex.ErrorCode, validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutPriceChangedException ex)
        {
            _logger.LogWarning(
                "Checkout price changed | Original: {Original} | New: {New} | Diff: {Diff}% | TraceId: {TraceId}",
                ex.OriginalTotal, ex.NewTotal, ex.DifferencePercent, traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutTotalMismatchException ex)
        {
            _logger.LogWarning(
                "Checkout total mismatch | Fields: {Fields} | TraceId: {TraceId}",
                string.Join(",", ex.MismatchedFields),
                traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId)
            {
                Data = new { MismatchedFields = ex.MismatchedFields }
            });
        }
        catch (CheckoutOutOfStockException ex)
        {
            _logger.LogWarning(
                "Checkout out of stock | ProductId: {ProductId} | SKU: {SKU} | Requested: {Requested} | Available: {Available} | TraceId: {TraceId}",
                ex.ProductId, ex.SKU, ex.RequestedQuantity, ex.AvailableQuantity, traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutConcurrencyConflictException ex)
        {
            _logger.LogWarning(
                ex,
                "Checkout commit lost a serializable race | CartId: {CartId} | Attempts: {Attempts} | TraceId: {TraceId}",
                ex.CartId,
                ex.AttemptCount,
                traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutPromotionCalculationException ex)
        {
            _logger.LogError(
                ex,
                "Checkout promotion calculation failed | TraceId: {TraceId}",
                traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutShippingMethodException ex)
        {
            _logger.LogWarning(
                "Checkout invalid shipping method | ShippingMethodId: {ShippingMethodId} | TraceId: {TraceId}",
                ex.ShippingMethodId, traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutPaymentMethodException ex)
        {
            _logger.LogWarning(
                "Checkout invalid payment method | PaymentMethod: {PaymentMethod} | TraceId: {TraceId}",
                ex.PaymentMethodCode,
                traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutManualWalletNotConfiguredException ex)
        {
            _logger.LogWarning(
                "Checkout rejected because manual wallet is not configured | TenantId: {TenantId} | TraceId: {TraceId}",
                ex.TenantId,
                traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutManualWalletProofException ex)
        {
            _logger.LogWarning(
                "Checkout rejected because manual wallet proof is invalid | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                ex.ErrorCode,
                traceId);
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentOrderNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, "Order not found", traceId));
        }
        catch (PaymentUnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                "Checkout payment ownership rejected | OrderId: {OrderId} | TraceId: {TraceId}",
                ex.OrderId,
                traceId);
            return NotFound(new ApiErrorResponse(ex.ErrorCode, "Order not found", traceId));
        }
        catch (PaymentOrderInvalidStateException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentMismatchException ex)
        {
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentProviderNotSupportedException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentManualWalletNotConfiguredException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentManualWalletProofException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentFawryFinancialAuthorityException ex)
        {
            // T-09 safety net: any checkout-internal Fawry provider boundary
            // surfaces the stable permanent authority rejection, never a
            // transient-outage 503 or a generic 500. Must precede the
            // PaymentException catch below.
            _logger.LogWarning(
                "Checkout rejected without Fawry financial authority | CartId: {CartId} | TraceId: {TraceId}",
                request.CartId,
                traceId);
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (PaymentException ex)
        {
            _logger.LogError(
                ex,
                "Checkout payment runtime unavailable | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                ex.ErrorCode,
                traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(ex.ErrorCode, "The selected payment provider is temporarily unavailable.", traceId));
        }
        catch (WarehouseResolutionException ex)
        {
            _logger.LogWarning(
                "Checkout warehouse resolution rejected | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                ex.ErrorCode,
                traceId);
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutStorefrontUnavailableException ex)
        {
            _logger.LogWarning(
                "Checkout rejected because storefront is not accepting orders | StorefrontStatus: {StorefrontStatus} | TraceId: {TraceId}",
                string.IsNullOrWhiteSpace(ex.StorefrontStatus) ? "(missing)" : ex.StorefrontStatus,
                traceId);
            return Conflict(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutOrderCreationException ex)
        {
            _logger.LogError(ex,
                "Checkout order creation failed | TraceId: {TraceId}",
                traceId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(ex.ErrorCode, "Order creation failed. Please try again.", traceId));
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Checkout tenant mismatch rejected | TenantId: {TenantId} | CartId: {CartId} | TraceId: {TraceId}",
                validation.TenantId,
                request.CartId,
                traceId);
            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }
        catch (CheckoutException ex)
        {
            var statusCode = ResolveCheckoutExceptionStatusCode(ex.ErrorCode);
            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex,
                    "Checkout failed | Step: {Step} | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                    ex.Step, ex.ErrorCode, traceId);
            }
            else
            {
                _logger.LogWarning(
                    "Checkout rejected | Step: {Step} | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                    ex.Step, ex.ErrorCode, traceId);
            }

            var responseMessage = statusCode == StatusCodes.Status500InternalServerError
                ? "Checkout could not be completed. Please try again."
                : ex.Message;
            return StatusCode(statusCode, new ApiErrorResponse(ex.ErrorCode, responseMessage, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Checkout unexpected error | TenantId: {TenantId} | CartId: {CartId} | TraceId: {TraceId}",
                validation.TenantId, request.CartId, traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(
                    "UNEXPECTED_CHECKOUT_FAILURE",
                    "Checkout could not be completed. Please try again.",
                    traceId));
        }
    }

    /// <summary>
    /// Returns an authoritative pricing preview for the current checkout selections.
    /// The backend recomputes subtotal, shipping, tax, discounts, and total from the live cart.
    /// </summary>
    [HttpPost("pricing")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CheckoutPricingPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PreviewPricing(
        [FromBody] CheckoutPricingPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        var validation = await ValidateRequestContextAsync(traceId, requireIdempotencyKey: false);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required", traceId));
        }

        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        try
        {
            var response = await _checkoutService.PreviewPricingAsync(
                validation.TenantId!.Value,
                validation.OwnerId!,
                validation.OwnerType!,
                validation.SessionId,
                validation.CustomerId,
                request,
                traceId,
                cancellationToken);

            return Ok(response);
        }
        catch (CheckoutCartNotFoundException ex)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCartOwnershipException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCustomerInactiveException ex)
        {
            return Unauthorized(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutCartInvalidException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutShippingMethodException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutPaymentMethodException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutFawryFinancialAuthorityException ex)
        {
            // T-09: Fawry preview is fail-closed with the stable authority code.
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (CheckoutPromotionCalculationException ex)
        {
            _logger.LogError(
                ex,
                "Checkout pricing preview promotion calculation failed | TenantId: {TenantId} | CartId: {CartId} | TraceId: {TraceId}",
                validation.TenantId,
                request.CartId,
                traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Checkout pricing preview failed | TenantId: {TenantId} | CartId: {CartId} | TraceId: {TraceId}",
                validation.TenantId,
                request.CartId,
                traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPost("attachments/upload-lease")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CheckoutAttachmentUploadLeaseResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> CreateAttachmentUploadLease(
        [FromBody] CheckoutAttachmentUploadLeaseRequestDto? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required", traceId));
        }

        try
        {
            var response = await _checkoutAttachmentService.CreateUploadLeaseAsync(
                validation.TenantId!.Value,
                validation.OwnerId!,
                validation.OwnerType!,
                validation.SessionId,
                validation.CustomerId,
                request,
                cancellationToken);
            return Ok(response);
        }
        catch (CheckoutDynamicFieldValidationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Checkout attachment upload lease failed | TenantId: {TenantId} | CartId: {CartId} | TraceId: {TraceId}", validation.TenantId, request.CartId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPost("attachments/confirm")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(CheckoutAttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> ConfirmAttachmentUpload(
        [FromBody] CheckoutAttachmentConfirmRequestDto? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required", traceId));
        }

        try
        {
            var response = await _checkoutAttachmentService.ConfirmUploadAsync(
                validation.TenantId!.Value,
                validation.OwnerId!,
                validation.OwnerType!,
                validation.SessionId,
                validation.CustomerId,
                request,
                cancellationToken);
            return Ok(response);
        }
        catch (CheckoutDynamicFieldValidationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Checkout attachment confirmation failed | TenantId: {TenantId} | CartId: {CartId} | AttachmentId: {AttachmentId} | TraceId: {TraceId}", validation.TenantId, request.CartId, request.AttachmentId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPut("attachments/development-upload")]
    [LeaseTokenAuthorizedUpload]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadDevelopmentAttachmentObject(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new ApiErrorResponse(
                "CHECKOUT_ATTACHMENT_UPLOAD_TOKEN_REQUIRED",
                "Upload lease token is required.",
                traceId));
        }

        var storage = HttpContext.RequestServices.GetRequiredService<PointOfSale.API.Services.IR2StorageService>();
        if (storage is not PointOfSale.API.Services.IDevelopmentPresignedUploadStorage developmentStorage)
        {
            return NotFound();
        }

        try
        {
            await developmentStorage.StorePresignedUploadAsync(
                token,
                Request.ContentType,
                Request.ContentLength,
                Request.Body,
                cancellationToken);
            return Ok();
        }
        catch (PointOfSale.API.Services.DevelopmentPresignedUploadException ex)
        {
            var statusCode = ex.ErrorCode switch
            {
                "CHECKOUT_ATTACHMENT_UPLOAD_LEASE_EXPIRED" => StatusCodes.Status410Gone,
                "CHECKOUT_ATTACHMENT_UPLOAD_REPLAYED" => StatusCodes.Status409Conflict,
                "CHECKOUT_ATTACHMENT_UPLOAD_TOKEN_INVALID" or
                "CHECKOUT_ATTACHMENT_UPLOAD_LEASE_NOT_FOUND" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Development checkout attachment upload failed | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpGet("attachments/development-download")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DownloadDevelopmentAttachmentObject(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new ApiErrorResponse(
                "DEVELOPMENT_DOWNLOAD_TOKEN_REQUIRED",
                "Download lease token is required.",
                traceId));
        }

        var storage = HttpContext.RequestServices.GetRequiredService<PointOfSale.API.Services.IR2StorageService>();
        if (storage is not PointOfSale.API.Services.IDevelopmentPresignedDownloadStorage developmentStorage)
        {
            return NotFound();
        }

        try
        {
            var download = await developmentStorage.OpenPresignedDownloadAsync(token, cancellationToken);
            return File(download.Content, download.ContentType, enableRangeProcessing: true);
        }
        catch (PointOfSale.API.Services.DevelopmentPresignedDownloadException ex)
        {
            var statusCode = ex.ErrorCode switch
            {
                "DEVELOPMENT_DOWNLOAD_LEASE_EXPIRED" => StatusCodes.Status410Gone,
                "DEVELOPMENT_DOWNLOAD_TOKEN_INVALID" or
                "DEVELOPMENT_DOWNLOAD_LEASE_NOT_FOUND" => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status400BadRequest
            };

            return StatusCode(statusCode, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ApiErrorResponse(
                "DEVELOPMENT_DOWNLOAD_OBJECT_NOT_FOUND",
                "Stored object was not found.",
                traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Development attachment download failed | TraceId: {TraceId}", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpDelete("attachments/{attachmentId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting("checkout")]
    public async Task<IActionResult> RemoveAttachment(
        Guid attachmentId,
        [FromQuery] Guid cartId,
        [FromQuery] Guid fieldId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        try
        {
            await _checkoutAttachmentService.RemovePendingAttachmentAsync(
                validation.TenantId!.Value,
                validation.OwnerId!,
                validation.OwnerType!,
                validation.SessionId,
                validation.CustomerId,
                cartId,
                fieldId,
                attachmentId,
                cancellationToken);
            return NoContent();
        }
        catch (CheckoutDynamicFieldValidationException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Checkout attachment removal failed | TenantId: {TenantId} | CartId: {CartId} | AttachmentId: {AttachmentId} | TraceId: {TraceId}",
                validation.TenantId,
                cartId,
                attachmentId,
                traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Header Validation

    private async Task<(Guid? TenantId, string? OwnerId, string? OwnerType, string? SessionId, Guid? CustomerId, string? RequestId, IActionResult? Error)> ValidateHeadersAsync(string traceId)
    {
        return await ValidateRequestContextAsync(traceId, requireIdempotencyKey: false);
    }

    private async Task<(Guid? TenantId, string? OwnerId, string? OwnerType, string? SessionId, Guid? CustomerId, string? RequestId, IActionResult? Error)> ValidateRequestContextAsync(
        string traceId,
        bool requireIdempotencyKey)
    {
        // Step 1: Validate X-Tenant-Id (required)
        if (!Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeaderValue) ||
            string.IsNullOrWhiteSpace(tenantHeaderValue))
        {
            _logger.LogWarning(
                "Missing X-Tenant-Id header | Endpoint: Checkout | TraceId: {TraceId}",
                traceId);
            return (null, null, null, null, null, null, BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId)));
        }

        // Try to parse as GUID first
        Guid tenantId;
        if (!Guid.TryParse(tenantHeaderValue, out tenantId))
        {
            // Not a GUID - try using tenant context from middleware (slug resolution)
            var tenantContext = HttpContext.RequestServices.GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
            if (tenantContext?.TenantId != null)
            {
                tenantId = tenantContext.TenantId.Value;
            }
            else
            {
                _logger.LogWarning(
                    "Invalid X-Tenant-Id format | Value: {Value} | TraceId: {TraceId}",
                    tenantHeaderValue, traceId);
                return (null, null, null, null, null, null, BadRequest(ApiErrorResponse.TenantHeaderInvalid(traceId)));
            }
        }

        // Step 2: Resolve the canonical checkout id
        string? requestId = null;
        if (requireIdempotencyKey || Request.Headers.ContainsKey(IdempotencyKeyHeader) || Request.Headers.ContainsKey(RequestIdHeader))
        {
            var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
            var compatibilityRequestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();

            if (requireIdempotencyKey &&
                string.IsNullOrWhiteSpace(idempotencyKey) &&
                string.IsNullOrWhiteSpace(compatibilityRequestId))
            {
                _logger.LogWarning(
                    "Missing Idempotency-Key header | Endpoint: Checkout | TenantId: {TenantId} | TraceId: {TraceId}",
                    tenantId,
                    traceId);
                return (null, null, null, null, null, null, BadRequest(new ApiErrorResponse(
                    "IDEMPOTENCY_KEY_MISSING",
                    $"{IdempotencyKeyHeader} header is required for checkout idempotency.",
                    traceId)));
            }

            if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
                !string.IsNullOrWhiteSpace(compatibilityRequestId))
            {
                // CRITICAL FIX: Normalize GUID comparison to handle format variations (N vs D, casing)
                // If both values parse as GUIDs, compare their canonical form (D format: 8-4-4-4-12)
                // Otherwise fall back to ordinal string comparison for non-GUID idempotency keys
                // NOTE: Normalization is ONLY for comparison/logging - original value is preserved for storage
                bool keysMatch;
                string? normalizedKey = null;
                
                if (Guid.TryParse(idempotencyKey, out var idempotencyGuid) &&
                    Guid.TryParse(compatibilityRequestId, out var compatibilityGuid))
                {
                    // Both are valid GUIDs - compare canonical form
                    keysMatch = idempotencyGuid == compatibilityGuid;
                    normalizedKey = idempotencyGuid.ToString("D"); // Use canonical D format for logging only
                }
                else
                {
                    // At least one is not a GUID - use ordinal string comparison
                    keysMatch = string.Equals(idempotencyKey, compatibilityRequestId, StringComparison.Ordinal);
                }
                
                if (!keysMatch)
                {
                    _logger.LogWarning(
                        "Mismatched checkout id headers | Idempotency-Key: {IdempotencyKey} | X-Request-Id: {RequestId} | Normalized: {NormalizedKey} | TenantId: {TenantId} | TraceId: {TraceId}",
                        idempotencyKey,
                        compatibilityRequestId,
                        normalizedKey,
                        tenantId,
                        traceId);
                    return (null, null, null, null, null, null, BadRequest(new ApiErrorResponse(
                        "IDEMPOTENCY_KEY_MISMATCH",
                        $"{IdempotencyKeyHeader} and {RequestIdHeader} must match when both are provided.",
                        traceId)));
                }
                
                // Use original idempotencyKey value for storage - do NOT replace with normalized form
                requestId = idempotencyKey;
            }
            else
            {
                // Only one header provided - use original value as-is for storage
                requestId = !string.IsNullOrWhiteSpace(idempotencyKey)
                    ? idempotencyKey
                    : compatibilityRequestId;
            }
        }

        requestId ??= Guid.NewGuid().ToString("D");

        var sessionId = Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) &&
                        !string.IsNullOrWhiteSpace(sessionHeaderValue)
            ? sessionHeaderValue.ToString()!.Trim()
            : null;

        // Step 3: Determine cart owner
        string ownerId;
        string ownerType;
        Guid? customerId = null;
        var callerIdentity = await ResolveCallerIdentityAsync();

        if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
            !string.IsNullOrWhiteSpace(customerHeaderValue) &&
            Guid.TryParse(customerHeaderValue, out var parsedCustomerId))
        {
            var authorizedHeaderCustomer = ResolveAuthorizedCustomerHeader(
                parsedCustomerId,
                tenantId,
                traceId,
                callerIdentity);

            if (authorizedHeaderCustomer.Error != null)
            {
                return (null, null, null, null, null, null, authorizedHeaderCustomer.Error);
            }

            if (authorizedHeaderCustomer.CustomerId.HasValue)
            {
                ownerType = "customer";
                ownerId = authorizedHeaderCustomer.CustomerId.Value.ToString("N");
                customerId = authorizedHeaderCustomer.CustomerId.Value;
            }
            else
            {
                // Guest checkout - X-Session-Id required
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _logger.LogWarning(
                        "Missing X-Session-Id header (required for guest checkout) | TenantId: {TenantId} | TraceId: {TraceId}",
                        tenantId, traceId);
                    return (null, null, null, null, null, null, BadRequest(new ApiErrorResponse(
                        "SESSION_HEADER_MISSING",
                        "X-Session-Id header is required for guest checkout",
                        traceId)));
                }

                ownerType = "session";
                ownerId = sessionId;
            }
        }
        else
        {
            // No X-Customer-Id header - check if caller is JWT-authenticated
            if (callerIdentity.AuthenticatedCustomerId.HasValue)
            {
                ownerType = "customer";
                ownerId = callerIdentity.AuthenticatedCustomerId.Value.ToString("N");
                customerId = callerIdentity.AuthenticatedCustomerId.Value;
            }
            else
            {
                // Guest checkout - X-Session-Id required
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _logger.LogWarning(
                        "Missing X-Session-Id header (required for guest checkout) | TenantId: {TenantId} | TraceId: {TraceId}",
                        tenantId, traceId);
                    return (null, null, null, null, null, null, BadRequest(new ApiErrorResponse(
                        "SESSION_HEADER_MISSING",
                        "X-Session-Id header is required for guest checkout",
                        traceId)));
                }

                ownerType = "session";
                ownerId = sessionId;
            }
        }

        return (tenantId, ownerId, ownerType, sessionId, customerId, requestId, null);
    }

    private void ApplyGuestOrderAccessCookies(CheckoutResponseDto response)
    {
        var options = HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Options.IOptions<Configuration.CheckoutSessionSecurityOptions>>()
            ?.Value;
        var environment = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
        var allowInsecureDevelopmentCookie = environment?.IsDevelopment() == true;

        if (!string.IsNullOrWhiteSpace(response.CheckoutToken))
        {
            GuestOrderAccessCookie.SetCheckoutToken(
                HttpContext,
                response.CheckoutToken,
                TimeSpan.FromMinutes(options?.PaymentTokenTtlMinutes ?? 20),
                allowInsecureDevelopmentCookie);
            response.CheckoutToken = null;
        }

        if (!string.IsNullOrWhiteSpace(response.TrackingToken))
        {
            GuestOrderAccessCookie.SetTrackingToken(
                HttpContext,
                response.TrackingToken,
                TimeSpan.FromMinutes(options?.GuestTrackingTokenTtlMinutes ?? 20),
                allowInsecureDevelopmentCookie);
            response.TrackingToken = null;
        }
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion

    #region Phone OTP Verification (WhatsApp)

    /// <summary>
    /// Check if phone verification via WhatsApp OTP is required for checkout.
    /// 
    /// This is CONDITIONAL on:
    /// - Tenant having an active WhatsApp account with OTP enabled
    /// - Phone not already verified in the session
    /// 
    /// If not required, checkpoint returns IsRequired=false and checkout can proceed.
    /// </summary>
    /// <param name="phone">The phone number to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Status indicating if OTP is required</returns>
    [HttpGet("phone-verification-status")]
    [ProducesResponseType(typeof(Services.Storefront.CheckoutPhoneVerificationStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckPhoneVerificationStatus(
        [FromQuery] string phone,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        
        // Get tenant ID
        if (!Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            // Try tenant context from middleware
            var tenantContext = HttpContext.RequestServices.GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
            if (tenantContext?.TenantId == null)
            {
                return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
            }
            tenantId = tenantContext.TenantId.Value;
        }
        
        // Resolve caller identity and authorize customer header
        var callerIdentity = await ResolveCallerIdentityAsync();
        Guid? customerId = callerIdentity.AuthenticatedCustomerId;
        
        if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
            !string.IsNullOrWhiteSpace(customerHeaderValue) &&
            Guid.TryParse(customerHeaderValue, out var headerCustomerId))
        {
            var authorizedHeaderCustomer = ResolveAuthorizedCustomerHeader(
                headerCustomerId,
                tenantId,
                traceId,
                callerIdentity);

            if (authorizedHeaderCustomer.Error != null)
            {
                return authorizedHeaderCustomer.Error;
            }

            if (authorizedHeaderCustomer.CustomerId.HasValue)
            {
                customerId = authorizedHeaderCustomer.CustomerId.Value;
            }
        }
        
        var verificationScopeId = Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) &&
                                  !string.IsNullOrWhiteSpace(sessionHeaderValue)
            ? sessionHeaderValue.ToString()!.Trim()
            : string.Empty;

        var phoneVerificationService = HttpContext.RequestServices.GetRequiredService<Services.Storefront.IStorefrontPhoneVerificationService>();
        var status = await phoneVerificationService.CheckVerificationRequiredAsync(
            tenantId,
            verificationScopeId,
            phone ?? string.Empty,
            customerId,
            cancellationToken);
        
        _logger.LogInformation(
            "[Checkout] Phone verification status checked | TenantId: {TenantId} | IsRequired: {IsRequired} | IsVerified: {IsVerified} | TraceId: {TraceId}",
            tenantId, status.IsRequired, status.IsVerified, traceId);
        
        return Ok(status);
    }

    /// <summary>
    /// Request a phone verification OTP via WhatsApp for checkout.
    /// 
    /// The OTP is sent to the customer's WhatsApp and must be verified
    /// before the order can be placed.
    /// </summary>
    /// <param name="request">Phone number to verify</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success status and expiry time</returns>
    [HttpPost("request-phone-otp")]
    [EnableRateLimiting("CheckoutOtpRequestRateLimit")]
    [ProducesResponseType(typeof(Services.Storefront.PhoneVerificationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestPhoneOtp(
        [FromBody] Services.Storefront.CheckoutPhoneVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        
        // Get tenant ID
        if (!Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            var tenantContext = HttpContext.RequestServices.GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
            if (tenantContext?.TenantId == null)
            {
                return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
            }
            tenantId = tenantContext.TenantId.Value;
        }
        
        // Get session ID
        if (!Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) ||
            string.IsNullOrWhiteSpace(sessionHeaderValue))
        {
            return BadRequest(new ApiErrorResponse(
                "SESSION_REQUIRED",
                "X-Session-Id header is required for phone verification",
                traceId));
        }
        var sessionId = sessionHeaderValue.ToString()!.Trim();
        
        if (string.IsNullOrWhiteSpace(request?.Phone))
        {
            return BadRequest(new ApiErrorResponse(
                "PHONE_REQUIRED",
                "Phone number is required",
                traceId));
        }
        
        var phoneVerificationService = HttpContext.RequestServices.GetRequiredService<Services.Storefront.IStorefrontPhoneVerificationService>();
        var clientIp = ClientIpAddressResolver.GetTrustedClientIp(HttpContext, fallback: string.Empty);
        var result = await phoneVerificationService.RequestVerificationAsync(
            tenantId,
            sessionId,
            request.Phone,
            clientIp,
            cancellationToken);
        
        if (!result.Success)
        {
            return StatusCode(ResolvePhoneOtpFailureStatusCode(result.Error), new ApiErrorResponse(
                result.Error ?? "OTP_REQUEST_FAILED",
                result.Message ?? "Failed to send OTP",
                traceId));
        }
        
        _logger.LogInformation(
            "[Checkout] Phone OTP requested | TenantId: {TenantId} | SessionId: {SessionId} | TraceId: {TraceId}",
            tenantId, sessionId, traceId);
        
        return Ok(result);
    }

    /// <summary>
    /// Verify phone with OTP code for checkout.
    /// 
    /// Call this after customer receives OTP on WhatsApp.
    /// Once verified, checkout can proceed.
    /// </summary>
    /// <param name="request">Phone and OTP code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Verification result</returns>
    [HttpPost("verify-phone-otp")]
    [EnableRateLimiting("CheckoutOtpVerifyRateLimit")]
    [ProducesResponseType(typeof(Services.Storefront.PhoneVerificationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyPhoneOtp(
        [FromBody] Services.Storefront.VerifyCheckoutOtpRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        
        // Get tenant ID
        if (!Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeaderValue) ||
            !Guid.TryParse(tenantHeaderValue, out var tenantId))
        {
            var tenantContext = HttpContext.RequestServices.GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
            if (tenantContext?.TenantId == null)
            {
                return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
            }
            tenantId = tenantContext.TenantId.Value;
        }
        
        // Get session ID
        if (!Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) ||
            string.IsNullOrWhiteSpace(sessionHeaderValue))
        {
            return BadRequest(new ApiErrorResponse(
                "SESSION_REQUIRED",
                "X-Session-Id header is required for phone verification",
                traceId));
        }
        var sessionId = sessionHeaderValue.ToString()!.Trim();
        
        if (string.IsNullOrWhiteSpace(request?.Phone) || string.IsNullOrWhiteSpace(request?.OtpCode))
        {
            return BadRequest(new ApiErrorResponse(
                "INVALID_REQUEST",
                "Phone and OTP code are required",
                traceId));
        }
        
        var phoneVerificationService = HttpContext.RequestServices.GetRequiredService<Services.Storefront.IStorefrontPhoneVerificationService>();
        var clientIp = ClientIpAddressResolver.GetTrustedClientIp(HttpContext, fallback: string.Empty);
        
        var callerIdentity = await ResolveCallerIdentityAsync();
        Guid? customerId = callerIdentity.AuthenticatedCustomerId;
        
        if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
            !string.IsNullOrWhiteSpace(customerHeaderValue) &&
            Guid.TryParse(customerHeaderValue, out var headerCustomerId))
        {
            var authorizedHeaderCustomer = ResolveAuthorizedCustomerHeader(
                headerCustomerId,
                tenantId,
                traceId,
                callerIdentity);

            if (authorizedHeaderCustomer.Error != null)
            {
                return authorizedHeaderCustomer.Error;
            }

            if (authorizedHeaderCustomer.CustomerId.HasValue)
            {
                customerId = authorizedHeaderCustomer.CustomerId.Value;
            }
        }
        
        var result = await phoneVerificationService.VerifyOtpAsync(
            tenantId,
            sessionId,
            request.Phone,
            request.OtpCode,
            clientIp,
            customerId,  // Pass customer ID to store verification for both session and customer
            cancellationToken);
        
        if (!result.Success)
        {
            return StatusCode(ResolvePhoneOtpFailureStatusCode(result.Error), new ApiErrorResponse(
                result.Error ?? "VERIFY_FAILED",
                result.Message ?? "Phone verification failed",
                traceId));
        }
        
        _logger.LogInformation(
            "[Checkout] Phone OTP verified | TenantId: {TenantId} | SessionId: {SessionId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            tenantId, sessionId, customerId, traceId);
        
        return Ok(result);
    }

    private static int ResolvePhoneOtpFailureStatusCode(string? errorCode) => errorCode switch
    {
        "RATE_LIMIT_EXCEEDED" or "OTP_ATTEMPTS_EXCEEDED" => StatusCodes.Status429TooManyRequests,
        "OTP_REQUEST_IN_PROGRESS" or "OTP_VERIFICATION_IN_PROGRESS" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    private static int ResolveCheckoutExceptionStatusCode(string errorCode) => errorCode switch
    {
        "PHONE_VERIFICATION_REQUIRED" or
        "INVALID_GEOGRAPHIC_CANDIDATE" or
        "CURRENCY_MISMATCH" or
        "CURRENCY_NOT_SET" or
        "CURRENCY_INVALID" or
        "AFFILIATE_IDENTITY_MISSING" or
        "AFFILIATE_IDENTITY_INVALID" or
        "AFFILIATE_PROMOTION_LINK_INVALID" or
        "AFFILIATE_PROMOTION_LINK_MISMATCH" => StatusCodes.Status400BadRequest,

        "AFFILIATE_AUTHORITY_STALE" or
        "AFFILIATE_PROMOTION_LINK_STALE" => StatusCodes.Status409Conflict,

        "FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE" => StatusCodes.Status422UnprocessableEntity,

        "GEOGRAPHIC_ADMISSION_UNAVAILABLE" or
        "GEOGRAPHIC_ADDRESS_ADMISSION_FAILED" or
        "STOREFRONT_AVAILABILITY_UNCERTAIN" or
        "PAYMENT_RUNTIME_UNAVAILABLE" or
        "AFFILIATE_POLICY_AUTHORITY_CONFLICT" or
        "AFFILIATE_REFERRAL_CODE_NOT_UNIQUE" => StatusCodes.Status503ServiceUnavailable,

        _ => StatusCodes.Status500InternalServerError,
    };

    private (Guid? CustomerId, ActionResult? Error) ResolveAuthorizedCustomerHeader(
        Guid headerCustomerId,
        Guid tenantId,
        string traceId,
        CheckoutCallerIdentity callerIdentity)
    {
        if (callerIdentity.AuthenticatedCustomerId.HasValue)
        {
            if (callerIdentity.AuthenticatedCustomerId.Value == headerCustomerId)
            {
                return (headerCustomerId, null);
            }

            _logger.LogWarning(
                "X-Customer-Id header mismatch with authenticated customer | Header: {HeaderId} | AuthenticatedCustomerId: {AuthenticatedCustomerId} | TenantId: {TenantId} | TraceId: {TraceId}",
                headerCustomerId,
                callerIdentity.AuthenticatedCustomerId.Value,
                tenantId,
                traceId);

            return (null, StatusCode(
                StatusCodes.Status403Forbidden,
                new ApiErrorResponse(
                    "CUSTOMER_ID_MISMATCH",
                    "X-Customer-Id does not match authenticated identity",
                    traceId)));
        }

        if (callerIdentity.IsTrustedInternalService)
        {
            return (headerCustomerId, null);
        }

        _logger.LogWarning(
            "Ignoring X-Customer-Id header for unauthenticated or untrusted checkout caller | Header: {HeaderId} | TenantId: {TenantId} | TraceId: {TraceId}",
            headerCustomerId,
            tenantId,
            traceId);

        return (null, null);
    }

    private async Task<CheckoutCallerIdentity> ResolveCallerIdentityAsync()
    {
        var authenticatedCustomerId = await ResolveAuthenticatedCustomerIdAsync();
        var isTrustedInternalService = await IsTrustedInternalServiceAsync();

        return new CheckoutCallerIdentity(authenticatedCustomerId, isTrustedInternalService);
    }

    private async Task<Guid?> ResolveAuthenticatedCustomerIdAsync()
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(customerAuthenticationResult.Principal, out var customerId))
        {
            return customerId;
        }

        var storefrontAuthenticationResult = await HttpContext.AuthenticateAsync(StorefrontAuthenticationScheme);
        return TryGetAuthenticatedCustomerId(storefrontAuthenticationResult.Principal, out customerId)
            ? customerId
            : null;
    }

    private async Task<bool> IsTrustedInternalServiceAsync()
    {
        var bearerAuthenticationResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        return IsTrustedInternalServicePrincipal(bearerAuthenticationResult.Principal);
    }

    private static bool TryGetAuthenticatedCustomerId(ClaimsPrincipal? principal, out Guid customerId)
    {
        customerId = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var customerIdClaim = principal.FindFirst("CustomerId")?.Value
            ?? principal.FindFirst("customer_id")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(customerIdClaim, out customerId);
    }

    private static bool IsTrustedInternalServicePrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var roleClaim = principal.FindFirst(ClaimTypes.Role)?.Value
            ?? principal.FindFirst("role")?.Value;
        var authTypeClaim = principal.FindFirst("auth_type")?.Value;
        
        bool hasServiceRole = string.Equals(roleClaim, "Service", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(roleClaim, "InternalService", StringComparison.OrdinalIgnoreCase);
        
        bool hasServiceAuthType = string.Equals(authTypeClaim, "service", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authTypeClaim, "internal_service", StringComparison.OrdinalIgnoreCase);

        // Role or auth_type alone is not sufficient - must also verify client_id and scopes
        if (!hasServiceRole && !hasServiceAuthType)
        {
            return false;
        }

        var clientIdClaim = principal.FindFirst("client_id")?.Value
            ?? principal.FindFirst("azp")?.Value;
        if (string.IsNullOrWhiteSpace(clientIdClaim))
        {
            return false;
        }

        return principal.FindAll("scope")
            .Concat(principal.FindAll("scp"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(scope => AllowedInternalScopes.Contains(scope));
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "(missing)";
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4)
        {
            return "****";
        }

        return $"****{digits[^4..]}";
    }

    private sealed record CheckoutCallerIdentity(Guid? AuthenticatedCustomerId, bool IsTrustedInternalService);

    #endregion
}
