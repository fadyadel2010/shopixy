using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Exception thrown when cart snapshot deserialization fails.
/// </summary>
public class CartSnapshotDeserializationException : Exception
{
    public string TokenFingerprint { get; }
    public Guid TenantId { get; }
    public Guid? CartId { get; }

    public CartSnapshotDeserializationException(
        string message,
        string tokenFingerprint,
        Guid tenantId,
        Guid? cartId,
        Exception? innerException = null)
        : base(message, innerException)
    {
        TokenFingerprint = tokenFingerprint;
        TenantId = tenantId;
        CartId = cartId;
    }
}

/// <summary>
/// Storefront-facing controller for checkout session management.
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// Endpoints:
/// - POST   /api/storefront/checkout-session     → Create or resume session
/// - PATCH  /api/storefront/checkout-session/{id} → Update session (autosave)
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// - X-Session-Id: string (required for guest checkout)
/// 
/// SECURITY:
/// - Customer identity is extracted from authenticated claims (customer_id or NameIdentifier)
/// - Anonymous users are supported for guest checkout (customerId remains null)
/// - X-Customer-Id header is IGNORED to prevent identity spoofing
/// 
/// DESIGN RULES:
/// - No authentication required (guest + logged-in customers)
/// - Debounced calls from frontend (fire-and-forget UX)
/// - Never blocks checkout flow — all errors are swallowed for UX
/// </summary>
[ApiController]
[Route("api/storefront/checkout-session")]
[AllowAnonymous]
public class CheckoutSessionController : ControllerBase
{
    private readonly ICheckoutSessionService _sessionService;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly ICartService _cartService;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<CheckoutSessionController> _logger;
    private sealed record RevalidatedRecoveredCartResult(
        PointOfSale.API.DTOs.Cart.CartDto? Cart,
        bool Attempted,
        bool Failed,
        bool CartChanged);

    private const string TenantIdHeader = "X-Tenant-Id";
    private const string SessionIdHeader = "X-Session-Id";
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;
    private const string AsyncReconciliationRecovery = "async_reconciliation";

    public CheckoutSessionController(
        ICheckoutSessionService sessionService,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        ICartService cartService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<CheckoutSessionController> logger)
    {
        _sessionService = sessionService;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _cartService = cartService;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Create or resume a checkout session.
    /// Called when customer enters the /checkout page.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrResume(
        [FromBody] CreateCheckoutSessionRequest request,
        CancellationToken ct)
    {
        var traceId = GetTraceId();

        if (!TryGetTenantId(out var tenantId))
        {
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        var sessionId = Request.Headers[SessionIdHeader].FirstOrDefault();
        var authenticatedCustomer = await ResolveAuthenticatedCustomerAsync();

        if (authenticatedCustomer.CustomerId.HasValue)
        {
            var tenantMismatch = ValidateAuthenticatedCustomerTenant(
                authenticatedCustomer.Principal,
                tenantId,
                traceId);

            if (tenantMismatch != null)
            {
                return tenantMismatch;
            }
        }
        else if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new ApiErrorResponse(
                "SESSION_ID_REQUIRED",
                "X-Session-Id header is required for guest checkout",
                traceId));
        }

        var customerId = authenticatedCustomer.CustomerId;
        if (request.CartId == Guid.Empty)
        {
            RecordCheckoutSessionAuthorizationFailure("create_or_resume", "missing_cart_id");
            return NotFound(CreateGenericCartAccessDeniedResponse(traceId));
        }

        var authoritativeCart = await LoadAuthoritativeCartAsync(
            tenantId,
            sessionId,
            customerId,
            traceId,
            ct);

        if (authoritativeCart == null || authoritativeCart.TenantId != tenantId || authoritativeCart.CartId != request.CartId)
        {
            var ownerType = customerId.HasValue ? "customer" : "session";
            var ownerId = customerId?.ToString("N") ?? sessionId ?? "(missing)";

            _logger.LogWarning(
                "[CheckoutSession] Cart ownership mismatch during create/resume | TenantId: {TenantId} | CartId: {CartId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | TraceId: {TraceId}",
                tenantId,
                request.CartId,
                ownerType,
                ownerId,
                traceId);

            RecordCheckoutSessionAuthorizationFailure("create_or_resume", "cart_ownership_mismatch");
            return NotFound(CreateGenericCartAccessDeniedResponse(traceId));
        }

        try
        {
            var outcome = await _sessionService.CreateOrResumeSessionWithOutcomeAsync(
                tenantId,
                authoritativeCart.CartId,
                sessionId,
                customerId,
                BuildAuthoritativeItemsSnapshotJson(authoritativeCart),
                authoritativeCart.GrandTotal,
                authoritativeCart.Currency,
                request.DeviceType,
                request.Source,
                ct);

            var session = outcome.Session ?? throw new InvalidOperationException("Checkout session could not be created or resumed.");

            if (outcome.IsPartialSuccess)
            {
                _reliabilityTelemetry.RecordCheckoutFalseFailure("create_or_resume", "dispatch_failure_after_persist");
                _reliabilityTelemetry.RecordCheckoutPartialSuccess("create_or_resume", AsyncReconciliationRecovery);
            }

            return Ok(new CheckoutSessionResponse
            {
                SessionId = session.Id,
                Status = outcome.IsPartialSuccess ? "partial_success" : session.Status.ToString(),
                Recovery = outcome.IsPartialSuccess ? AsyncReconciliationRecovery : null,
                RecoveryToken = _checkoutSessionTokenService.CreateRecoveryToken(session),
                ManualWalletUploadToken = _checkoutSessionTokenService.CreateManualWalletUploadToken(session),
                CreatedAtUtc = session.CreatedAtUtc,
                LastActivityAtUtc = session.LastActivityAtUtc,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[CheckoutSession] Failed to create/resume session for cart {CartId} tenant {TenantId} | TraceId: {TraceId}",
                request.CartId,
                tenantId,
                traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Update checkout session with partial data (progressive autosave).
    /// Called debounced as customer fills checkout form.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCheckoutSessionRequest request,
        CancellationToken ct)
    {
        var traceId = GetTraceId();

        if (!TryGetTenantId(out var tenantId))
        {
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        // Ownership verification: load session and verify caller matches session owner
        var existingSession = await _sessionService.GetByIdAsync(id, tenantId, ct);
        if (existingSession == null)
        {
            return NotFound(new ApiErrorResponse(
                "CHECKOUT_SESSION_NOT_FOUND",
                "Session not found",
                traceId));
        }

        // Resolve caller identity (X-Session-Id header or authenticated customer)
        var callerSessionId = Request.Headers[SessionIdHeader].FirstOrDefault();
        var authenticatedCustomer = await ResolveAuthenticatedCustomerAsync();

        // Verify ownership: either session ID matches header, or customer ID matches authenticated customer
        var isOwnerBySessionId = !string.IsNullOrEmpty(callerSessionId) &&
                                 !string.IsNullOrEmpty(existingSession.SessionId) &&
                                 existingSession.SessionId.Equals(callerSessionId, StringComparison.OrdinalIgnoreCase);
        var isOwnerByCustomerId = authenticatedCustomer.CustomerId.HasValue &&
                                  existingSession.OnlineCustomerId == authenticatedCustomer.CustomerId;

        if (!isOwnerBySessionId && !isOwnerByCustomerId)
        {
            _logger.LogWarning(
                "[CheckoutSession] Unauthorized update attempt | SessionId: {SessionId} | " +
                "HeaderSessionId: {HeaderSessionId} | AuthCustomerId: {AuthCustomerId} | SessionOwnerId: {SessionOwnerId} | TraceId: {TraceId}",
                id,
                callerSessionId,
                authenticatedCustomer.CustomerId,
                existingSession.OnlineCustomerId,
                traceId);
            RecordCheckoutSessionAuthorizationFailure("update", "session_ownership_mismatch");
            return NotFound(CreateGenericSessionAccessDeniedResponse(traceId));
        }

        try
        {
            var authoritativeCart = await LoadAuthoritativeCartAsync(
                tenantId,
                callerSessionId,
                authenticatedCustomer.CustomerId,
                traceId,
                ct);

            if (authoritativeCart == null ||
                authoritativeCart.TenantId != tenantId ||
                authoritativeCart.CartId != existingSession.CartId)
            {
                RecordCheckoutSessionAuthorizationFailure("update", "cart_ownership_mismatch");
                return NotFound(CreateGenericCartAccessDeniedResponse(traceId));
            }

            var outcome = await _sessionService.UpdateSessionWithOutcomeAsync(
                new PointOfSale.Core.DTOs.UpdateCheckoutSessionRequest
                {
                    SessionId = id,
                    TenantId = tenantId,
                    CustomerName = request.CustomerName,
                    Phone = request.Phone,
                    Email = request.Email,
                    City = request.City,
                    Area = request.Area,
                    Address = request.Address,
                    Country = request.Country,
                    Notes = request.Notes,
                    ShippingMethodId = request.ShippingMethodId,
                    PaymentMethodCode = request.PaymentMethodCode,
                    WalletType = request.WalletType,
                    WalletPhoneNumber = request.WalletPhoneNumber,
                    ItemsSnapshotJson = BuildAuthoritativeItemsSnapshotJson(authoritativeCart),
                    CartTotal = authoritativeCart.GrandTotal,
                    Currency = authoritativeCart.Currency,
                },
                ct);

            if (outcome.Session == null)
            {
                return NotFound(new ApiErrorResponse(
                    "CHECKOUT_SESSION_NOT_FOUND",
                    "Session not found or not active",
                    traceId));
            }

            if (outcome.IsPartialSuccess)
            {
                _reliabilityTelemetry.RecordCheckoutFalseFailure("update", "dispatch_failure_after_persist");
                _reliabilityTelemetry.RecordCheckoutPartialSuccess("update", AsyncReconciliationRecovery);
            }

            return Ok(outcome.IsPartialSuccess
                ? CheckoutSessionMutationResponse.CreatePartialSuccess(AsyncReconciliationRecovery)
                : CheckoutSessionMutationResponse.CreateSuccess());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[CheckoutSession] Failed to update session {SessionId} tenant {TenantId} | TraceId: {TraceId}",
                id,
                tenantId,
                traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var header = Request.Headers[TenantIdHeader].FirstOrDefault();

        // Try GUID first
        if (Guid.TryParse(header, out tenantId) && tenantId != Guid.Empty)
            return true;

        // Fallback: middleware already resolved slug → TenantContext
        var tenantContext = HttpContext.RequestServices
            .GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
        if (tenantContext?.TenantId != null)
        {
            tenantId = tenantContext.TenantId.Value;
            return true;
        }

        return false;
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private async Task<CheckoutSessionCallerIdentity> ResolveAuthenticatedCustomerAsync()
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(customerAuthenticationResult.Principal, out var customerId))
        {
            return new CheckoutSessionCallerIdentity(customerAuthenticationResult.Principal, customerId);
        }

        var storefrontAuthenticationResult = await HttpContext.AuthenticateAsync(StorefrontAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(storefrontAuthenticationResult.Principal, out customerId))
        {
            return new CheckoutSessionCallerIdentity(storefrontAuthenticationResult.Principal, customerId);
        }

        return new CheckoutSessionCallerIdentity(null, null);
    }

    private IActionResult? ValidateAuthenticatedCustomerTenant(
        ClaimsPrincipal? principal,
        Guid tenantId,
        string traceId)
    {
        var tenantIdClaim = principal?.FindFirst("TenantId")?.Value;
        if (string.IsNullOrWhiteSpace(tenantIdClaim) ||
            !Guid.TryParse(tenantIdClaim, out var claimTenantId))
        {
            return null;
        }

        if (claimTenantId == tenantId)
        {
            return null;
        }

        _logger.LogWarning(
            "[CheckoutSession] Authenticated customer tenant mismatch | HeaderTenantId: {HeaderTenantId} | ClaimTenantId: {ClaimTenantId} | TraceId: {TraceId}",
            tenantId,
            claimTenantId,
            traceId);

        return Unauthorized(new ApiErrorResponse(
            TenantErrorCodes.TenantMismatch,
            "Authenticated user does not belong to the current tenant.",
            traceId));
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

    private sealed record CheckoutSessionCallerIdentity(ClaimsPrincipal? Principal, Guid? CustomerId);

    /// <summary>
    /// Recover a checkout session via secure token.
    /// Returns the session data so the storefront can restore the checkout form.
    /// 
    /// PRODUCTION HARDENING:
    /// - Session must exist
    /// - Session must NOT be ConvertedToOrder (already completed)
    /// - Recovery token must be purpose-bound, tenant-bound, and not expired
    /// - Session must belong to the current tenant
    /// </summary>
    [HttpGet("recover/{token}")]
    [ProducesResponseType(typeof(CheckoutRecoveryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecoverByToken(string token, CancellationToken ct)
    {
        var traceId = GetTraceId();

        if (string.IsNullOrEmpty(token))
            return BadRequest(new ApiErrorResponse("RECOVERY_TOKEN_MISSING", "Missing recovery token", traceId));

        if (!TryGetTenantId(out var tenantId))
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));

        var tokenFingerprint = GetTokenFingerprint(token);

        try
        {
            var validation = _checkoutSessionTokenService.ValidateRecoveryToken(token, tenantId);
            if (validation.IsExpired)
            {
                _logger.LogInformation(
                    "[CheckoutSession] Recovery link expired | TokenFingerprint: {TokenFingerprint} | TenantId: {TenantId}",
                    tokenFingerprint,
                    tenantId);
                return BadRequest(new ApiErrorResponse("SESSION_EXPIRED", "This recovery link has expired.", traceId));
            }

            if (!validation.IsValid || !Guid.TryParseExact(validation.Subject, "N", out var sessionId))
            {
                _logger.LogWarning(
                    "[CheckoutSession] Recovery token validation failed | TokenFingerprint: {TokenFingerprint} | TenantId: {TenantId}",
                    tokenFingerprint,
                    tenantId);
                return NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Recovery session not found or expired", traceId));
            }

            var requestedSession = await _sessionService.GetByIdAsync(sessionId, tenantId, ct);

            // Validation 1: Session must exist
            if (requestedSession == null)
            {
                _logger.LogWarning(
                    "[CheckoutSession] Recovery token not found | TokenFingerprint: {TokenFingerprint} | TenantId: {TenantId}",
                    tokenFingerprint, tenantId);
                return NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Recovery session not found or expired", traceId));
            }

            // Validation 2: Session must belong to current tenant and match the signed nonce
            if (requestedSession.TenantId != tenantId ||
                string.IsNullOrWhiteSpace(validation.Nonce) ||
                !SecurityUtilities.FixedTimeEquals(requestedSession.RecoveryToken, validation.Nonce))
            {
                _logger.LogWarning(
                    "[CheckoutSession] Recovery token/session mismatch | TokenFingerprint: {TokenFingerprint} | " +
                    "SessionTenant: {SessionTenant} | RequestTenant: {RequestTenant}",
                    tokenFingerprint,
                    requestedSession.TenantId,
                    tenantId);
                return NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Recovery session not found or expired", traceId));
            }

            // Validation 3: Session must NOT be already converted to an order
            if (requestedSession.Status == Core.Enums.CheckoutSessionStatus.ConvertedToOrder)
            {
                _logger.LogInformation(
                    "[CheckoutSession] Recovery attempted for converted session | TokenFingerprint: {TokenFingerprint} | OrderId: {OrderId}",
                    tokenFingerprint, requestedSession.ConvertedOrderId);
                return BadRequest(new ApiErrorResponse(
                    "SESSION_CONVERTED",
                    "This checkout was already completed.",
                    traceId)
                {
                    Data = new { orderNumber = requestedSession.ConvertedOrderNumber }
                });
            }

            var session = await _sessionService.ResolveRecoverySessionAsync(sessionId, tenantId, ct);
            if (session == null)
            {
                _logger.LogWarning(
                    "[CheckoutSession] Recovery token was invalidated during session reconciliation | TokenFingerprint: {TokenFingerprint} | TenantId: {TenantId}",
                    tokenFingerprint,
                    tenantId);
                return NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Recovery session not found or expired", traceId));
            }

            var recoveredLiveCart = await TryRevalidateRecoveredCartAsync(
                session,
                tenantId,
                tokenFingerprint,
                ct);

            if (recoveredLiveCart.Cart == null)
            {
                _logger.LogWarning(
                    "[CheckoutSession] Recovery refused because live cart revalidation did not produce backend authority | " +
                    "TokenFingerprint: {TokenFingerprint} | TenantId: {TenantId} | SessionId: {SessionId} | CartId: {CartId} | Attempted: {Attempted} | Failed: {Failed}",
                    tokenFingerprint,
                    tenantId,
                    session.Id,
                    session.CartId,
                    recoveredLiveCart.Attempted,
                    recoveredLiveCart.Failed);

                return Conflict(new ApiErrorResponse(
                    "RECOVERY_CART_REVALIDATION_FAILED",
                    "Unable to safely recover checkout because the live cart could not be revalidated.",
                    traceId)
                {
                    Data = new { cartId = session.CartId }
                });
            }

            var responseCart = MapRecoveredCartItems(recoveredLiveCart.Cart);

            var requiresManualWalletReentry = string.Equals(
                session.PaymentMethodCode,
                "MANUALWALLET",
                StringComparison.OrdinalIgnoreCase);

            var requiresReview = requiresManualWalletReentry || 
                (recoveredLiveCart.Attempted && (recoveredLiveCart.Failed || recoveredLiveCart.CartChanged));
            
            var recoveryMessage = BuildRecoveryMessage(
                recoveredLiveCart,
                requiresManualWalletReentry);

            return Ok(new CheckoutRecoveryResponse
            {
                SessionId = session.Id,
                GuestSessionId = session.SessionId,
                CartId = session.CartId,
                Status = session.Status.ToString(),
                Cart = responseCart,
                Address = new RecoveredCheckoutAddressDto
                {
                    FullName = session.CustomerName ?? string.Empty,
                    Phone = session.Phone ?? string.Empty,
                    Email = session.Email,
                    City = session.City ?? string.Empty,
                    Area = session.Area ?? string.Empty,
                    AddressLine = session.Address ?? string.Empty,
                    Country = session.Country,
                },
                ShippingMethod = session.ShippingMethodId.HasValue
                    ? new RecoveredCheckoutShippingMethodDto
                    {
                        Id = session.ShippingMethodId.Value
                    }
                    : null,
                PaymentMethod = !string.IsNullOrWhiteSpace(session.PaymentMethodCode)
                    ? new RecoveredCheckoutPaymentMethodDto
                    {
                        Code = session.PaymentMethodCode.Trim()
                    }
                    : null,
                Notes = session.Notes,
                CartTotal = recoveredLiveCart.Cart.GrandTotal,
                Currency = recoveredLiveCart.Cart.Currency,
                ShippingMethodId = session.ShippingMethodId,
                PaymentMethodCode = session.PaymentMethodCode,
                CouponCode = recoveredLiveCart.Cart?.AppliedCouponCode ?? recoveredLiveCart.Cart?.AppliedCoupon?.Code,
                CouponDiscount = recoveredLiveCart.Cart?.AppliedCoupon != null ? recoveredLiveCart.Cart.DiscountTotal : null,
                CouponType = recoveredLiveCart.Cart?.AppliedCoupon?.Type,
                CouponValue = recoveredLiveCart.Cart?.AppliedCoupon?.Value,
                PromotionId = recoveredLiveCart.Cart?.AppliedCartPromotion?.PromotionId,
                CartPromotionDiscount = recoveredLiveCart.Cart?.AppliedCartPromotion != null
                    ? recoveredLiveCart.Cart.CartPromotionDiscountTotal
                    : null,
                PromotionName = recoveredLiveCart.Cart?.AppliedCartPromotion?.Name,
                PromotionDescription = recoveredLiveCart.Cart?.AppliedCartPromotion?.Description,
                PromotionDiscountType = recoveredLiveCart.Cart?.AppliedCartPromotion?.DiscountType,
                PromotionDiscountValue = recoveredLiveCart.Cart?.AppliedCartPromotion?.DiscountValue,
                PromotionBadge = recoveredLiveCart.Cart?.AppliedCartPromotion?.Badge,
                CartWasRevalidated = recoveredLiveCart.Attempted && recoveredLiveCart.Cart != null && !recoveredLiveCart.Failed,
                CartChangedDuringRecovery = recoveredLiveCart.CartChanged,
                RequiresReview = requiresReview,
                RecoveryMessage = recoveryMessage,
                RevalidatedAtUtc = recoveredLiveCart.Attempted && recoveredLiveCart.Cart != null && !recoveredLiveCart.Failed
                    ? DateTime.UtcNow
                    : null,
                CheckoutToken = null, // Payment tokens are order-specific and generated after order creation
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CheckoutSession] Recovery failed | TokenFingerprint: {TokenFingerprint}", tokenFingerprint);
            return NotFound(new ApiErrorResponse("SESSION_NOT_FOUND", "Recovery session not found", traceId));
        }
    }

    private static string GetTokenFingerprint(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash)[..12];
    }

    private List<AbandonedCheckoutItemDto> DeserializeCartSnapshot(
        string? itemsSnapshotJson,
        string tokenFingerprint,
        Guid tenantId,
        Guid? cartId)
    {
        if (string.IsNullOrWhiteSpace(itemsSnapshotJson))
        {
            throw new CartSnapshotDeserializationException(
                "Cart snapshot is null or empty",
                tokenFingerprint,
                tenantId,
                cartId);
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<AbandonedCheckoutItemDto>>(
                itemsSnapshotJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (items == null)
            {
                throw new CartSnapshotDeserializationException(
                    "Cart snapshot deserialized to null",
                    tokenFingerprint,
                    tenantId,
                    cartId);
            }

            return items;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "[CheckoutSession] Failed to deserialize recovery cart snapshot | TokenFingerprint: {TokenFingerprint} | TenantId: {TenantId}",
                tokenFingerprint,
                tenantId);

            throw new CartSnapshotDeserializationException(
                "Cart snapshot JSON is invalid or corrupted",
                tokenFingerprint,
                tenantId,
                cartId,
                ex);
        }
    }

    private async Task<RevalidatedRecoveredCartResult> TryRevalidateRecoveredCartAsync(
        CheckoutSession session,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var owner = ResolveRecoveredCartOwner(session);
        if (owner == null)
        {
            return new RevalidatedRecoveredCartResult(null, Attempted: false, Failed: false, CartChanged: false);
        }

        try
        {
            var liveCart = await _cartService.GetCartAsync(
                tenantId,
                owner,
                correlationId,
                cancellationToken);
            if (liveCart == null || liveCart.CartId != session.CartId || liveCart.TenantId != tenantId ||
                !string.Equals(liveCart.OwnerType, owner.OwnerType, StringComparison.OrdinalIgnoreCase) ||
                !SecurityUtilities.FixedTimeEquals(liveCart.OwnerId, owner.OwnerId))
            {
                return new RevalidatedRecoveredCartResult(null, Attempted: true, Failed: true, CartChanged: false);
            }
            return new RevalidatedRecoveredCartResult(
                liveCart,
                Attempted: true,
                Failed: false,
                CartChanged: HasRecoveredCartChanged(session.ItemsSnapshotJson, liveCart));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[CheckoutSession] Failed to revalidate recovered cart promotions/coupon | SessionId: {SessionId} | CartId: {CartId}",
                session.Id,
                session.CartId);
            return new RevalidatedRecoveredCartResult(null, Attempted: true, Failed: true, CartChanged: false);
        }
    }

    private static List<AbandonedCheckoutItemDto> MapRecoveredCartItems(PointOfSale.API.DTOs.Cart.CartDto cart) =>
        cart.Items.Select(item => new AbandonedCheckoutItemDto
        {
            VariantId = item.VariantId,
            ProductName = item.Name,
            VariantName = item.VariantAttributes != null && item.VariantAttributes.Count > 0
                ? string.Join(" / ", item.VariantAttributes.Select(attribute => attribute.Value))
                : null,
            Quantity = item.Quantity,
            Price = item.UnitPrice,
            ImageUrl = item.ThumbnailImage
        }).ToList();

    private static bool HasRecoveredCartChanged(
        string? snapshotJson,
        PointOfSale.API.DTOs.Cart.CartDto? liveCart)
    {
        if (liveCart == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return liveCart.Items.Count > 0;
        }

        try
        {
            var snapshotItems = JsonSerializer.Deserialize<List<AbandonedCheckoutItemDto>>(
                snapshotJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<AbandonedCheckoutItemDto>();

            var normalizedSnapshot = snapshotItems
                .Select(item => $"{item.VariantId}:{item.ProductName}:{item.VariantName}:{item.Quantity}:{item.Price}")
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();

            var normalizedLive = MapRecoveredCartItems(liveCart)
                .Select(item => $"{item.VariantId}:{item.ProductName}:{item.VariantName}:{item.Quantity}:{item.Price}")
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();

            return !normalizedSnapshot.SequenceEqual(normalizedLive, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string BuildRecoveryMessage(
        RevalidatedRecoveredCartResult revalidatedCart,
        bool requiresManualWalletReentry)
    {
        if (!revalidatedCart.Attempted)
        {
            return requiresManualWalletReentry
                ? "We restored your checkout, but you must re-enter your wallet transfer details before placing the order."
                : "We restored your checkout. Review the latest cart, stock, and totals before placing the order.";
        }

        if (revalidatedCart.Failed)
        {
            return requiresManualWalletReentry
                ? "We restored your checkout, but we could not revalidate the live cart. Review the cart carefully and re-enter your wallet transfer details before placing the order."
                : "We restored your checkout, but live cart revalidation could not be completed. Review the cart carefully before placing the order.";
        }

        if (revalidatedCart.CartChanged)
        {
            return requiresManualWalletReentry
                ? "Your cart was updated to the latest available items and pricing. Review the changes and re-enter your wallet transfer details before placing the order."
                : "Your cart was refreshed to the latest available items and pricing. Review the changes before placing the order.";
        }

        return requiresManualWalletReentry
            ? "Your cart was revalidated successfully. Re-enter your wallet transfer details before placing the order."
            : "Your cart and pricing were revalidated successfully. Review the latest totals before placing the order.";
    }

    private static PointOfSale.API.DTOs.Cart.CartOwner? ResolveRecoveredCartOwner(CheckoutSession session)
    {
        if (session.OnlineCustomerId.HasValue)
        {
            return new PointOfSale.API.DTOs.Cart.CartOwner
            {
                OwnerType = "customer",
                OwnerId = session.OnlineCustomerId.Value.ToString("N")
            };
        }

        if (!string.IsNullOrWhiteSpace(session.SessionId))
        {
            return new PointOfSale.API.DTOs.Cart.CartOwner
            {
                OwnerType = "session",
                OwnerId = session.SessionId
            };
        }

        return null;
    }

    private async Task<PointOfSale.API.DTOs.Cart.CartDto?> LoadAuthoritativeCartAsync(
        Guid tenantId,
        string? sessionId,
        Guid? customerId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var owner = ResolveCartOwner(sessionId, customerId);
        if (owner == null)
        {
            RecordCheckoutSessionAuthorizationFailure("create_or_resume", "missing_owner_context");
            _logger.LogWarning(
                "[CheckoutSession] Missing owner context while loading authoritative cart | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
            return null;
        }

        return await _cartService.GetCartAsync(
            tenantId,
            owner,
            traceId,
            cancellationToken);
    }

    private static PointOfSale.API.DTOs.Cart.CartOwner? ResolveCartOwner(
        string? sessionId,
        Guid? customerId)
    {
        if (customerId.HasValue)
        {
            return new PointOfSale.API.DTOs.Cart.CartOwner
            {
                OwnerType = "customer",
                OwnerId = customerId.Value.ToString("N")
            };
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return new PointOfSale.API.DTOs.Cart.CartOwner
        {
            OwnerType = "session",
            OwnerId = sessionId.Trim()
        };
    }

    private static string BuildAuthoritativeItemsSnapshotJson(PointOfSale.API.DTOs.Cart.CartDto cart)
    {
        return JsonSerializer.Serialize(
            MapRecoveredCartItems(cart),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static ApiErrorResponse CreateGenericCartAccessDeniedResponse(string traceId) =>
        new(
            "CHECKOUT_CART_NOT_FOUND",
            "Cart not found.",
            traceId);

    private static ApiErrorResponse CreateGenericSessionAccessDeniedResponse(string traceId) =>
        new(
            "CHECKOUT_SESSION_NOT_FOUND",
            "Session not found.",
            traceId);

    private void RecordCheckoutSessionAuthorizationFailure(string endpoint, string reason)
    {
        _reliabilityTelemetry.RecordCheckoutSessionAuthorizationFailure(endpoint, reason);
    }
}
