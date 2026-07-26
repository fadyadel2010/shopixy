using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Funnel;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Helpers;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-27/27A/27B: Affiliate Funnel Controller — Hardened Optimized Checkout Flow.
///
/// Route: POST /api/storefront/funnel/start
///
/// PURPOSE:
/// Reduces affiliate funnel friction by allowing landing pages to:
/// 1. Add a product to cart server-side
/// 2. Stamp affiliate attribution directly into cart metadata (Redis)
/// 3. Return a checkout URL for direct redirect
///
/// AF-27A HARDENING:
/// - Anti-Ref Tampering: AffiliateAccountId stored in cart metadata (Redis),
///   read by CheckoutService as PRIORITY 0 (trusted, server-stamped)
/// - Quantity Guard: SingleProductMode forces qty=1, prevents accumulation
/// - Rate Limiting: IP-based sliding window (20 calls/min/IP)
/// - Structured Observability: All logs prefixed [AF-27:FunnelStart]
///
/// AF-27B HARDENING:
/// - Re-validates product affiliate-eligibility (never trusts landing page)
/// - Re-validates affiliate is active (graceful degradation)
/// - Validates storefront is live
/// - Funnel metrics tracking (FunnelStarted)
/// - Fraud signal logging (non-blocking)
///
/// ARCHITECTURAL BOUNDARIES:
/// - This controller does NOT create orders
/// - This controller does NOT calculate commissions
/// - This controller does NOT write directly to PostgreSQL
/// - Cart operations are delegated to CartService (Redis-only)
/// - Affiliate resolution is read-only (no DB writes)
/// - Checkout remains the commerce authority
///
/// SCALE NOTE (AF-27B §6):
/// The rate limiter uses in-memory ConcurrentDictionary.
/// For HORIZONTAL SCALING (multi-instance deployment), this should be
/// replaced with a Redis-based sliding window (ZRANGEBYSCORE pattern).
/// Current implementation is sufficient for single-instance deployment.
///
/// FLOW:
/// Landing Page CTA → POST /api/storefront/funnel/start
///   → Server validates product, tenant, affiliate
///   → Server re-validates affiliate eligibility (AF-27B §1)
///   → Server creates/reuses cart (Redis)
///   → Server stamps AffiliateAccountId into cart.AffiliateAccountId
///   → Returns { checkoutUrl: "/checkout?cartId=XYZ" }
///   → Client redirects to checkout
///   → CheckoutService reads cart.AffiliateAccountId (PRIORITY 0)
///   → Normal checkout flow → Normal commission pipeline
/// </summary>
[ApiController]
[Route("api/storefront/funnel")]
[AllowAnonymous]
public class StorefrontFunnelController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly TenantContext _tenantContext;
    private readonly IFunnelRateLimiter _rateLimiter;
    private readonly ILogger<StorefrontFunnelController> _logger;

    // AF-27B §4: Funnel conversion metric counters (in-memory, non-blocking)
    private static long _funnelStartedTotal;
    private static long _funnelConvertedTotal; // Incremented by CheckoutService (future)

    // AF-27B §5: Fraud signal tracking (non-blocking, observability only)
    private static readonly ConcurrentDictionary<string, FraudSignalTracker> _fraudSignalStore = new();

    private const string SessionIdHeader = "X-Session-Id";
    private const string CustomerIdHeader = "X-Customer-Id";

    public StorefrontFunnelController(
        ICartService cartService,
        TenantContext tenantContext,
        IFunnelRateLimiter rateLimiter,
        ILogger<StorefrontFunnelController> logger)
    {
        _cartService = cartService;
        _tenantContext = tenantContext;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// AF-27/27A: Starts an affiliate funnel checkout flow (hardened).
    ///
    /// Server-side cart injection with anti-tampering affiliate attribution.
    ///
    /// Steps:
    /// 1. Rate limit check (20 calls/min/IP)
    /// 2. Resolve tenant from request context (NOT from body)
    /// 3. Validate request body
    /// 4. Resolve affiliate by code (global, silent fallback if invalid)
    /// 5. Create or reuse session cart
    /// 6. Apply quantity guard (SingleProductMode)
    /// 7. Add product to cart via CartService
    /// 8. Stamp affiliate attribution into cart metadata (Redis)
    /// 9. Return checkout URL
    ///
    /// Edge Cases:
    /// - Invalid affiliate code → continues without attribution
    /// - Product out of stock → 409 Conflict
    /// - Product belongs to different tenant → 403 Forbidden
    /// - SingleProductMode CTA click → forces qty=1, no accumulation
    /// - Rate limit exceeded → 429 Too Many Requests
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(FunnelStartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> StartFunnel(
        [FromBody] FunnelStartRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "[AF-27:FunnelStart] Request received | ProductId: {ProductId} | Quantity: {Quantity} | " +
            "ReferralCode: {ReferralCode} | SingleProductMode: {SingleProductMode} | TraceId: {TraceId}",
            request.ProductId, request.Quantity, request.ReferralCode ?? "(none)",
            request.SingleProductMode, traceId);

        // ═══════════════════════════════════════════════════════════════════
        // STEP 0: Rate Limiting (AF-27A §3 / AF-28: Redis-backed)
        // IP-based sliding window — 20 calls/min/IP (horizontal-scaling safe)
        // ═══════════════════════════════════════════════════════════════════
        var clientIp = GetClientIp();
        if (!await _rateLimiter.CheckRateLimitAsync(clientIp))
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Rate limit exceeded | IP: {ClientIp} | TraceId: {TraceId}",
                clientIp, traceId);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ApiErrorResponse("RATE_LIMIT_EXCEEDED",
                    "Too many funnel requests. Please wait a moment and try again.", traceId));
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 1: Validate request body
        // ═══════════════════════════════════════════════════════════════════
        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Validation failed | Error: {ErrorCode} | TraceId: {TraceId}",
                errorCode, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 2: Resolve tenant from request context (NOT from body)
        // ═══════════════════════════════════════════════════════════════════
        var tenantId = ResolveTenantId();
        if (tenantId == null)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Tenant resolution failed | TraceId: {TraceId}", traceId);
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 3: AF-27E - Read affiliate from middleware context
        // Middleware already resolved and validated the affiliate
        // No duplicate resolution logic needed
        // ═══════════════════════════════════════════════════════════════════
        Guid? resolvedAffiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(HttpContext);
        string? resolvedReferralCode = AffiliateModeResolver.GetReferralCode(HttpContext);
        Guid? resolvedPromotionLinkId = AffiliateModeResolver.GetPromotionLinkId(HttpContext);
        bool affiliateAttached = resolvedAffiliateAccountId.HasValue;

        if (affiliateAttached)
        {
            _logger.LogInformation(
                "[AF-27E:FunnelStart] Affiliate resolved from middleware | AffiliateAccountId: {AffiliateAccountId} | " +
                "ReferralCode: {ReferralCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                resolvedAffiliateAccountId, resolvedReferralCode, tenantId.Value, traceId);
        }
        else if (!string.IsNullOrWhiteSpace(request.ReferralCode))
        {
            // Middleware didn't resolve affiliate (invalid/suspended)
            // Graceful degradation: continue without attribution
            _logger.LogInformation(
                "[AF-27E:FunnelStart] Affiliate not resolved (invalid/suspended) | ReferralCode: {ReferralCode} | TraceId: {TraceId}",
                request.ReferralCode, traceId);
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 4: Resolve cart owner (session-based for funnel)
        // ═══════════════════════════════════════════════════════════════════
        var owner = ResolveCartOwner(traceId);
        if (owner == null)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] No session or customer ID | TraceId: {TraceId}", traceId);
            return BadRequest(new ApiErrorResponse(
                "SESSION_REQUIRED",
                "X-Session-Id header or an authenticated customer token is required for funnel checkout",
                traceId));
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 5: Add product to cart via CartService
        // ═══════════════════════════════════════════════════════════════════
        try
        {
            // AF-27E: Set affiliate mode on HttpContext if resolved
            if (resolvedAffiliateAccountId.HasValue)
            {
                AffiliateModeResolver.SetAffiliateMode(HttpContext, resolvedAffiliateAccountId.Value, resolvedReferralCode!);
            }

            // AF-27A §2: SingleProductMode Quantity Guard
            var effectiveQuantity = request.SingleProductMode ? 1 : request.Quantity;

            var addRequest = new AddCartItemRequest
            {
                ProductId = request.ProductId,
                VariantId = request.VariantId,
                Quantity = effectiveQuantity
            };

            // AF-27A §2: SingleProductMode — pre-clear the item if it already exists
            // This prevents quantity accumulation on repeated CTA clicks
            if (request.SingleProductMode)
            {
                try
                {
                    var existingCart = await _cartService.GetCartAsync(
                        tenantId.Value, owner, traceId, cancellationToken);

                    if (existingCart.Items.Any(i => i.ProductId == request.ProductId))
                    {
                        // Remove the existing item so AddItemAsync adds fresh with qty=1
                        if (request.VariantId.HasValue)
                        {
                            await _cartService.RemoveItemByVariantAsync(
                                tenantId.Value, owner, request.VariantId.Value, traceId, cancellationToken);
                        }
                        else
                        {
                            await _cartService.RemoveItemAsync(
                                tenantId.Value, owner, request.ProductId, traceId, cancellationToken);
                        }

                        _logger.LogInformation(
                            "[AF-27:FunnelStart] SingleProductMode: cleared existing item | ProductId: {ProductId} | TraceId: {TraceId}",
                            request.ProductId, traceId);
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: if clear fails, AddItemAsync will still work (just accumulates)
                    _logger.LogWarning(ex,
                        "[AF-27:FunnelStart] SingleProductMode: clear failed (non-fatal) | ProductId: {ProductId} | TraceId: {TraceId}",
                        request.ProductId, traceId);
                }
            }

            var cart = await _cartService.AddItemAsync(
                tenantId.Value,
                owner,
                addRequest,
                resolvedAffiliateAccountId,
                resolvedReferralCode,
                resolvedPromotionLinkId,
                traceId,
                cancellationToken);

            // ═══════════════════════════════════════════════════════════════
            // STEP 6: AF-27A §1 — ANTI-TAMPERING: Stamp affiliate into cart metadata
            // This is persisted in Redis and read by CheckoutService as PRIORITY 0
            // ═══════════════════════════════════════════════════════════════
            if (resolvedAffiliateAccountId.HasValue)
            {
                cart.AffiliateAccountId = resolvedAffiliateAccountId.Value;
                cart.AffiliateReferralCode = resolvedReferralCode;
                cart.AffiliateSource = "Funnel";

                // Re-save cart with affiliate metadata stamped
                await _cartService.SaveCartAsync(cart, traceId, cancellationToken);

                _logger.LogInformation(
                    "[AF-27:FunnelStart] Anti-tampering: affiliate stamped in cart metadata | " +
                    "CartId: {CartId} | AffiliateAccountId: {AffiliateAccountId} | " +
                    "ReferralCode: {ReferralCode} | Source: Funnel | TraceId: {TraceId}",
                    cart.CartId, resolvedAffiliateAccountId.Value, resolvedReferralCode, traceId);
            }

            // ═══════════════════════════════════════════════════════════════
            // STEP 7: Build response with checkout URL
            // ═══════════════════════════════════════════════════════════════
            var checkoutUrl = $"/checkout?cartId={cart.CartId}";

            // AF-27: If affiliate is attached, include ref code in checkout URL
            // This serves as FALLBACK for non-funnel checkout (AF-18 pattern)
            // Primary attribution now comes from cart metadata (AF-27A anti-tampering)
            if (affiliateAttached && !string.IsNullOrWhiteSpace(resolvedReferralCode))
            {
                checkoutUrl += $"&ref={resolvedReferralCode}";
            }

            var productName = cart.Items
                .FirstOrDefault(i => i.ProductId == request.ProductId)?.Name;

            var response = new FunnelStartResponse
            {
                CheckoutUrl = checkoutUrl,
                CartId = cart.CartId,
                AffiliateAttached = affiliateAttached,
                ItemCount = cart.ItemCount,
                ProductName = productName
            };

            // AF-27: Set affiliate headers for downstream middleware compatibility
            if (affiliateAttached)
            {
                HttpContext.Response.Headers["X-Affiliate-Funnel"] = "true";
                HttpContext.Response.Headers["X-Affiliate-Ref"] = resolvedReferralCode;
            }

            stopwatch.Stop();

            // ═══════════════════════════════════════════════════════════════
            // AF-27D §5: STRUCTURED OBSERVABILITY START
            // ═══════════════════════════════════════════════════════════════
            _logger.LogInformation(
                "[AF-27D:Validation] FunnelStarted | TenantSlug: {TenantSlug} | ReferralCode: {ReferralCode} | ProductSlug: {ProductSlug} | TemplateType: {TemplateType} | TraceId: {TraceId} | CartId: {CartId} | DurationMs: {DurationMs}",
                request.TenantSlug, request.ReferralCode ?? "(none)", request.ProductSlug, request.TemplateType, traceId, cart.CartId, stopwatch.ElapsedMilliseconds);

            // ═══════════════════════════════════════════════════════════════
            // AF-27B §4: FUNNEL METRICS (non-blocking)
            // ═══════════════════════════════════════════════════════════════
            Interlocked.Increment(ref _funnelStartedTotal);

            // ═══════════════════════════════════════════════════════════════
            // AF-27B §5: FRAUD SIGNAL TRACKING (non-blocking, observability only)
            // Track clicks per IP per affiliate for anomaly detection.
            // NEVER blocks orders. NEVER changes commission logic.
            // ═══════════════════════════════════════════════════════════════
            if (affiliateAttached && resolvedAffiliateAccountId.HasValue)
            {
                try
                {
                    var fraudKey = $"{clientIp}:{resolvedAffiliateAccountId.Value}";
                    var tracker = _fraudSignalStore.GetOrAdd(fraudKey, _ => new FraudSignalTracker());
                    tracker.RecordClick();

                    if (tracker.ClickCount > 10 && tracker.ClickCount % 10 == 0)
                    {
                        _logger.LogWarning(
                            "[AF-27D:Validation] FraudSignalDetected | TenantSlug: {TenantSlug} | ReferralCode: {ReferralCode} | ProductSlug: {ProductSlug} | TemplateType: {TemplateType} | TraceId: {TraceId} | IP: {ClientIp}",
                            request.TenantSlug, request.ReferralCode, request.ProductSlug, request.TemplateType, traceId, clientIp);
                    }
                }
                catch
                {
                    // Fraud tracking is NEVER allowed to break the funnel
                }
            }

            return Ok(response);
        }
        catch (ProductNotFoundException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Product not found | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId.Value, ex.ProductId, traceId);
            return NotFound(new ApiErrorResponse("PRODUCT_NOT_FOUND", ex.Message, traceId));
        }
        catch (ProductOutOfStockException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Product out of stock | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId.Value, ex.ProductId, traceId);
            return Conflict(new ApiErrorResponse("PRODUCT_OUT_OF_STOCK", ex.Message, traceId));
        }
        catch (ProductNotForSaleException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Product not for sale | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId.Value, ex.ProductId, traceId);
            return BadRequest(new ApiErrorResponse("PRODUCT_NOT_FOR_SALE",
                "This product is not available for purchase", traceId));
        }
        catch (ProductNotEligibleException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Product not affiliate-eligible | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId.Value, ex.ProductId, traceId);
            return BadRequest(new ApiErrorResponse("PRODUCT_NOT_ELIGIBLE", ex.Message, traceId));
        }
        catch (CartLimitExceededException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Cart limit exceeded | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId.Value, traceId);
            return BadRequest(new ApiErrorResponse("CART_LIMIT_EXCEEDED", ex.Message, traceId));
        }
        catch (VariantRequiredException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Variant selection required | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId.Value, ex.ProductId, traceId);
            return BadRequest(new ApiErrorResponse("VARIANT_REQUIRED", ex.Message, traceId));
        }
        catch (VariantNotFoundException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Variant not found | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                tenantId.Value, ex.VariantId, traceId);
            return NotFound(new ApiErrorResponse("VARIANT_NOT_FOUND", ex.Message, traceId));
        }
        catch (VariantOutOfStockException ex)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Variant out of stock | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                tenantId.Value, ex.VariantId, traceId);
            return Conflict(new ApiErrorResponse("VARIANT_OUT_OF_STOCK", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "[AF-27:FunnelStart] Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId.Value, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal) || 
                                                       string.Equals(ex.Message, TenantErrorCodes.CustomerIdMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Tenant mismatch rejected | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId.Value,
                traceId);
            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AF-27:FunnelStart] Unexpected error | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                tenantId.Value, request.ProductId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    private Guid? ResolveTenantId()
        => PointOfSale.API.Helpers.StorefrontTenantContextResolver.GetTenantId(HttpContext) ?? _tenantContext.TenantId;

    private CartOwner? ResolveCartOwner(string traceId)
    {
        var authenticatedCustomerId = User.GetCustomerId();
        if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerHeaderValue) &&
            !string.IsNullOrWhiteSpace(customerHeaderValue) &&
            Guid.TryParse(customerHeaderValue, out var headerCustomerId) &&
            authenticatedCustomerId.HasValue &&
            headerCustomerId != authenticatedCustomerId.Value)
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Authenticated customer mismatch detected | HeaderCustomerId: {HeaderCustomerId} | AuthenticatedCustomerId: {AuthenticatedCustomerId} | TraceId: {TraceId}",
                headerCustomerId,
                authenticatedCustomerId.Value,
                traceId);
            throw new UnauthorizedAccessException(TenantErrorCodes.CustomerIdMismatch);
        }

        if (authenticatedCustomerId.HasValue)
        {
            return new CartOwner
            {
                OwnerType = "customer",
                OwnerId = authenticatedCustomerId.Value.ToString("N")
            };
        }

        if (Request.Headers.TryGetValue(CustomerIdHeader, out var unauthenticatedCustomerHeaderValue) &&
            !string.IsNullOrWhiteSpace(unauthenticatedCustomerHeaderValue) &&
            Guid.TryParse(unauthenticatedCustomerHeaderValue, out _))
        {
            _logger.LogWarning(
                "[AF-27:FunnelStart] Ignoring unauthenticated X-Customer-Id header | TraceId: {TraceId}",
                traceId);
        }

        if (Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) &&
            !string.IsNullOrWhiteSpace(sessionHeaderValue))
        {
            return new CartOwner
            {
                OwnerType = "session",
                OwnerId = sessionHeaderValue.ToString()!.Trim()
            };
        }

        return null;
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private string GetClientIp()
    {
        return ClientIpAddressResolver.GetTrustedClientIp(HttpContext);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-27A §3: RATE LIMITING
    // Moved to IFunnelRateLimiter (Redis-backed, horizontal-scaling safe).
    // See: PointOfSale.Infrastructure.Services.RedisFunnelRateLimiter
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════
    // AF-27B §5: FRAUD SIGNAL TRACKER
    //
    // Non-blocking, observability-only tracker for click patterns.
    // Tracks clicks per unique IP×Affiliate pair.
    // NEVER blocks orders. NEVER modifies commission logic.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thread-safe fraud signal tracker for a single IP×Affiliate pair.
    /// </summary>
    private class FraudSignalTracker
    {
        private long _clickCount;

        public long ClickCount => Interlocked.Read(ref _clickCount);

        public void RecordClick()
        {
            Interlocked.Increment(ref _clickCount);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-27B §4: FUNNEL METRICS ACCESS (for internal monitoring)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns current funnel metrics for observability endpoints.
    /// Thread-safe, non-blocking reads.
    /// </summary>
    public static (long Started, long Converted) GetFunnelMetrics()
    {
        return (
            Interlocked.Read(ref _funnelStartedTotal),
            Interlocked.Read(ref _funnelConvertedTotal)
        );
    }

    /// <summary>
    /// Increments the funnel converted counter.
    /// Called by CheckoutService when an order is created with AffiliateSource == "Funnel".
    /// </summary>
    public static void IncrementFunnelConverted()
    {
        Interlocked.Increment(ref _funnelConvertedTotal);
    }
}
