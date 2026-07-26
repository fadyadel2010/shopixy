using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Cart Recovery Controller
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// Base Path: /api/storefront/cart/recover
/// 
/// Endpoints:
/// - GET /api/storefront/cart/recover/{token} (validate and restore cart)
/// 
/// PUBLIC ENDPOINT (no auth required):
/// - Customer clicks recovery link from WhatsApp
/// - Token contains signed payload with cart info
/// - Validates token and restores cart
/// </summary>
[ApiController]
[Route("api/storefront/cart/recover")]
[AllowAnonymous]
public class StorefrontCartRecoveryController : ControllerBase
{
    private readonly IAbandonedCartService _cartService;
    private readonly ICartService _liveCartService;
    private readonly AbandonedCartRecoveryTelemetry _telemetry;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StorefrontCartRecoveryController> _logger;

    public StorefrontCartRecoveryController(
        IAbandonedCartService cartService,
        ICartService liveCartService,
        AbandonedCartRecoveryTelemetry telemetry,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StorefrontCartRecoveryController> logger)
    {
        _cartService = cartService;
        _liveCartService = liveCartService;
        _telemetry = telemetry;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Validates recovery token and returns cart snapshot for restoration.
    /// 
    /// GET /api/storefront/cart/recover/{token}
    /// 
    /// PUBLIC ENDPOINT - No authentication required
    /// Customer clicks link from WhatsApp message
    /// </summary>
    [HttpGet("{token}")]
    [ProducesResponseType(typeof(CartRecoveryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CartRecoveryErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CartRecoveryErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecoverCart(string token, CancellationToken ct = default)
    {
        var requestSource = ClientIpAddressResolver.GetTrustedClientIp(HttpContext, "unknown");
        _logger.LogInformation(
            "[Cart Recovery] Recovery attempt | TraceId: {TraceId} | RequestSource: {RequestSource}",
            HttpContext.TraceIdentifier,
            requestSource);

        try
        {
            var validation = _cartService.ValidateRecoveryToken(token);
            if (!validation.IsValid)
            {
                RecordValidationFailure(validation, requestSource);
                _reliabilityTelemetry.RecordRecoveryCartRestoreFailed(
                    validation.TenantId,
                    validation.SignatureFailed ? "token_signature_failed" : validation.FailureReason);
                return BadRequest(new CartRecoveryErrorResponse
                {
                    Error = "Invalid or expired recovery link",
                    Message = "This recovery link is no longer valid. Please visit our store to continue shopping."
                });
            }

            var tenantId = validation.TenantId!.Value;
            var cartId = validation.CartId!.Value;

            var cart = await _cartService.GetByCartIdAsync(tenantId, cartId, ct);
            if (cart == null || IsTerminal(cart.RecoveryStatus))
            {
                _telemetry.RecordRecoveryTokenInvalid(tenantId, cart == null ? "cart_not_found" : "cart_not_recoverable");
                _reliabilityTelemetry.RecordRecoveryCartRestoreFailed(
                    tenantId,
                    cart == null ? "cart_not_found" : "cart_not_recoverable");
                _logger.LogWarning(
                    "[Cart Recovery] Cart not recoverable | TenantId: {TenantId} | CartId: {CartId} | RequestSource: {RequestSource} | Reason: {Reason}",
                    tenantId,
                    cartId,
                    requestSource,
                    cart == null ? "cart_not_found" : "cart_not_recoverable");
                return NotFound(new CartRecoveryErrorResponse
                {
                    Error = "Cart not found",
                    Message = "This cart is no longer available. Please visit our store to continue shopping."
                });
            }

            if (!OwnershipMatches(cart, validation))
            {
                _telemetry.RecordRecoveryTokenInvalid(tenantId, "owner_mismatch");
                _reliabilityTelemetry.RecordRecoveryCartRestoreFailed(tenantId, "owner_mismatch");
                _logger.LogWarning(
                    "[Cart Recovery] Ownership validation failed | TenantId: {TenantId} | CartId: {CartId} | RequestSource: {RequestSource} | Reason: {Reason}",
                    tenantId,
                    cartId,
                    requestSource,
                    "owner_mismatch");

                return BadRequest(new CartRecoveryErrorResponse
                {
                    Error = "Invalid or expired recovery link",
                    Message = "This recovery link is no longer valid. Please visit our store to continue shopping."
                });
            }

            var restoredCart = await ResolveRecoveredCartAsync(cart, requestSource, ct);
            if (restoredCart.Snapshot == null)
            {
                _reliabilityTelemetry.RecordRecoveryCartRestoreFailed(
                    tenantId,
                    restoredCart.FailureReason ?? "snapshot_invalid");
                return BadRequest(new CartRecoveryErrorResponse
                {
                    Error = "Cart recovery data is invalid",
                    Message = "We could not safely restore this cart. Please return to the store and review the latest cart."
                });
            }

            _reliabilityTelemetry.RecordRecoveryCartRestored(
                tenantId,
                restoredCart.Source ?? "live_cart");

            _logger.LogInformation(
                "[Cart Recovery] Cart recovered | TenantId: {TenantId} | CartId: {CartId} | Status: {Status} | RequestSource: {RequestSource} | Source: {Source}",
                tenantId,
                cartId,
                cart.RecoveryStatus,
                requestSource,
                restoredCart.Source ?? "live_cart");

            var response = new CartRecoveryResponse
            {
                TenantId = tenantId,
                CartId = cartId,
                SessionId = cart.SessionId,
                CustomerId = cart.CustomerId,
                CartSnapshot = restoredCart.Snapshot,
                Message = "Your cart has been restored."
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cart Recovery] Error processing recovery | RequestSource: {RequestSource}", requestSource);
            _reliabilityTelemetry.RecordRecoveryCartRestoreFailed(null, "unexpected_controller_error");
            return StatusCode(StatusCodes.Status500InternalServerError, new CartRecoveryErrorResponse
            {
                Error = "Recovery failed",
                Message = "An error occurred while recovering your cart. Please try again or contact support."
            });
        }
    }

    private void RecordValidationFailure(
        AbandonedCartRecoveryTokenValidationResult validation,
        string requestSource)
    {
        if (validation.SignatureFailed)
        {
            _telemetry.RecordRecoveryTokenSignatureFailed(validation.TenantId, validation.FailureReason);
        }
        else
        {
            _telemetry.RecordRecoveryTokenInvalid(validation.TenantId, validation.FailureReason);
        }

        _logger.LogWarning(
            "[Cart Recovery] Token validation failed | TenantId: {TenantId} | CartId: {CartId} | RequestSource: {RequestSource} | Reason: {Reason} | SignatureFailed: {SignatureFailed}",
            validation.TenantId,
            validation.CartId,
            requestSource,
            validation.FailureReason,
            validation.SignatureFailed);
    }

    private static bool OwnershipMatches(
        AbandonedCart cart,
        AbandonedCartRecoveryTokenValidationResult validation)
    {
        var tokenSessionId = NormalizeOptional(validation.SessionId);
        var cartSessionId = NormalizeOptional(cart.SessionId);
        var sessionMatches = tokenSessionId == null && cartSessionId == null ||
                             SecurityUtilities.FixedTimeEquals(tokenSessionId, cartSessionId);

        return sessionMatches && validation.CustomerId == cart.CustomerId;
    }

    private static bool IsTerminal(AbandonedCartRecoveryStatus status) =>
        status is AbandonedCartRecoveryStatus.Recovered or
            AbandonedCartRecoveryStatus.Expired or
            AbandonedCartRecoveryStatus.Failed;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<RecoveredCartResolution> ResolveRecoveredCartAsync(
        AbandonedCart cart,
        string requestSource,
        CancellationToken ct)
    {
        if (TryBuildCartOwner(cart, out var owner))
        {
            try
            {
                var liveCart = await _liveCartService.GetCartAsync(
                    cart.TenantId,
                    owner,
                    HttpContext.TraceIdentifier,
                    ct);

                if (liveCart != null && liveCart.Items.Count > 0 && liveCart.CartId == cart.CartId)
                {
                    var liveSnapshot = RecoveredCartSnapshotFactory.FromCartDto(liveCart);
                    if (TryValidateSnapshot(liveSnapshot, cart, out var _))
                    {
                        return RecoveredCartResolution.Success(liveSnapshot, "live_cart");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[Cart Recovery] Live cart lookup failed; stored snapshot is not cart authority | TenantId: {TenantId} | CartId: {CartId} | RequestSource: {RequestSource}",
                    cart.TenantId,
                    cart.CartId,
                    requestSource);
            }
        }

        _logger.LogWarning(
            "[Cart Recovery] No backend-admitted live cart snapshot available; refusing stored snapshot restoration | TenantId: {TenantId} | CartId: {CartId} | RequestSource: {RequestSource}",
            cart.TenantId,
            cart.CartId,
            requestSource);
        return RecoveredCartResolution.Failure("live_cart_unavailable");
    }

    private static bool TryValidateSnapshot(
        RecoveredCartSnapshotDto snapshot,
        AbandonedCart cart,
        out string failureReason)
    {
        failureReason = string.Empty;

        if (snapshot.TenantId == Guid.Empty || snapshot.TenantId != cart.TenantId)
        {
            failureReason = "tenant_mismatch";
            return false;
        }

        if (snapshot.CartId == Guid.Empty || snapshot.CartId != cart.CartId)
        {
            failureReason = "cart_mismatch";
            return false;
        }

        if (!MatchesSnapshotOwner(snapshot, cart))
        {
            failureReason = "snapshot_owner_mismatch";
            return false;
        }

        if (snapshot.ExpiresAtUtc <= DateTime.UtcNow)
        {
            failureReason = "snapshot_expired";
            return false;
        }

        if (snapshot.LastUpdatedUtc < cart.LastActivityAtUtc.AddMinutes(-5))
        {
            failureReason = "snapshot_stale";
            return false;
        }

        if (snapshot.Items.Count == 0)
        {
            failureReason = "snapshot_empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Currency))
        {
            failureReason = "currency_missing";
            return false;
        }

        var computedItemCount = snapshot.Items.Sum(item => item.Quantity);
        if (computedItemCount != snapshot.ItemCount)
        {
            failureReason = "item_count_mismatch";
            return false;
        }

        foreach (var item in snapshot.Items)
        {
            if (item.ProductId == Guid.Empty || item.Quantity <= 0 || item.UnitPrice < 0m)
            {
                failureReason = "invalid_item";
                return false;
            }

            if (!AmountsMatch(item.LineTotal, item.UnitPrice * item.Quantity))
            {
                failureReason = "line_total_mismatch";
                return false;
            }
        }

        var computedSubtotal = snapshot.Items.Sum(item => item.LineTotal);
        if (!AmountsMatch(snapshot.Subtotal, computedSubtotal))
        {
            failureReason = "subtotal_mismatch";
            return false;
        }

        if (snapshot.DiscountTotal < 0m ||
            snapshot.TaxTotal < 0m ||
            snapshot.PromotionDiscountTotal < 0m ||
            snapshot.CartPromotionDiscountTotal < 0m ||
            snapshot.GrandTotal < 0m)
        {
            failureReason = "negative_totals";
            return false;
        }

        var expectedGrandTotal = snapshot.Subtotal
            - snapshot.DiscountTotal
            - snapshot.PromotionDiscountTotal
            - snapshot.CartPromotionDiscountTotal
            + snapshot.TaxTotal;
        if (expectedGrandTotal < 0m)
        {
            expectedGrandTotal = 0m;
        }

        if (!AmountsMatch(snapshot.GrandTotal, expectedGrandTotal))
        {
            failureReason = "grand_total_mismatch";
            return false;
        }

        var expectedTotalDiscount = snapshot.DiscountTotal
            + snapshot.PromotionDiscountTotal
            + snapshot.CartPromotionDiscountTotal;
        if (!AmountsMatch(snapshot.TotalDiscount, expectedTotalDiscount))
        {
            failureReason = "total_discount_mismatch";
            return false;
        }

        if (snapshot.AppliedCoupon != null &&
            !string.Equals(snapshot.AppliedCoupon.Code, snapshot.AppliedCouponCode, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = "coupon_state_mismatch";
            return false;
        }

        return true;
    }

    private static bool MatchesSnapshotOwner(RecoveredCartSnapshotDto snapshot, AbandonedCart cart)
    {
        var snapshotOwnerType = NormalizeOptional(snapshot.OwnerType);
        var snapshotOwnerId = NormalizeOptional(snapshot.OwnerId);

        if (cart.CustomerId.HasValue)
        {
            return string.Equals(snapshotOwnerType, "customer", StringComparison.OrdinalIgnoreCase) &&
                   snapshotOwnerId == cart.CustomerId.Value.ToString("N");
        }

        return string.Equals(snapshotOwnerType, "session", StringComparison.OrdinalIgnoreCase) &&
               SecurityUtilities.FixedTimeEquals(snapshotOwnerId, NormalizeOptional(cart.SessionId));
    }

    private static bool TryBuildCartOwner(AbandonedCart cart, out CartOwner owner)
    {
        if (cart.CustomerId.HasValue)
        {
            owner = new CartOwner
            {
                OwnerType = "customer",
                OwnerId = cart.CustomerId.Value.ToString("N")
            };
            return true;
        }

        var normalizedSessionId = NormalizeOptional(cart.SessionId);
        if (normalizedSessionId != null)
        {
            owner = new CartOwner
            {
                OwnerType = "session",
                OwnerId = normalizedSessionId
            };
            return true;
        }

        owner = new CartOwner();
        return false;
    }

    private static bool AmountsMatch(decimal left, decimal right) =>
        Math.Abs(left - right) < 0.01m;

    private sealed record RecoveredCartResolution(
        RecoveredCartSnapshotDto? Snapshot,
        string? Source,
        string? FailureReason)
    {
        public static RecoveredCartResolution Success(RecoveredCartSnapshotDto snapshot, string source) =>
            new(snapshot, source, null);

        public static RecoveredCartResolution Failure(string failureReason) =>
            new(null, null, failureReason);
    }
}

public class CartRecoveryResponse
{
    public Guid TenantId { get; set; }
    public Guid CartId { get; set; }
    public string? SessionId { get; set; }
    public Guid? CustomerId { get; set; }
    public RecoveredCartSnapshotDto? CartSnapshot { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CartRecoveryErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
