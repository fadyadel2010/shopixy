using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Events;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Helpers;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Cart Controller - Redis-based cart operations.
/// 
/// Route: /api/storefront/cart
/// 
/// Endpoints:
/// - GET / → Get current cart
/// - POST /items → Add item to cart
/// - PUT /items/{productId} → Update item quantity
/// - DELETE /items/{productId} → Remove item
/// - DELETE / → Clear cart
/// 
/// Authentication:
/// - Guest cart requests are allowed with X-Session-Id
/// - Authenticated customers use CustomerId from authenticated claims
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required for all endpoints)
/// - X-Session-Id: required for guest cart operations
/// 
/// Cart Ownership Rules:
/// - Authenticated owner = customerId + tenantId
/// - Guest owner = sessionId + tenantId
/// 
/// Architecture:
/// - Redis is the ONLY storage for cart state
/// - MongoDB is read-only (product validation only)
/// - Every cart operation emits an event via MassTransit
/// - Cart TTL: 24 hours (reset on every write)
/// </summary>
[ApiController]
[Route("api/storefront/cart")]
public class StorefrontCartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly IStorefrontCouponService _couponService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ISmartOfferRedeemTokenService _smartOfferRedeemTokenService;
    private readonly PointOfSale.Infrastructure.Data.ApplicationDbContext _dbContext;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<StorefrontCartController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";
    private const string SessionIdHeader = "X-Session-Id";
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;

    public StorefrontCartController(
        ICartService cartService,
        IStorefrontCouponService couponService,
        IEventPublisher eventPublisher,
        ISmartOfferRedeemTokenService smartOfferRedeemTokenService,
        PointOfSale.Infrastructure.Data.ApplicationDbContext dbContext,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<StorefrontCartController> logger)
    {
        _cartService = cartService;
        _couponService = couponService;
        _eventPublisher = eventPublisher;
        _smartOfferRedeemTokenService = smartOfferRedeemTokenService;
        _dbContext = dbContext;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current cart.
    /// If cart doesn't exist, returns an empty cart structure.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cart DTO</returns>
    /// <response code="200">Cart returned successfully</response>
    /// <response code="400">Missing or invalid headers</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetCart(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "GetCart request | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | TraceId: {TraceId}",
            validation.TenantId, validation.Owner!.OwnerType, validation.Owner.OwnerId, traceId);

        try
        {
            var cart = await _cartService.GetCartAsync(
                validation.TenantId!.Value,
                validation.Owner,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "GetCart success | TenantId: {TenantId} | CartId: {CartId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
                validation.TenantId, cart.CartId, cart.ItemCount, traceId);

            return Ok(cart);
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "GetCart failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GetCart failed | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Adds an item to the cart.
    /// If item already exists, increments quantity.
    /// If cart doesn't exist, creates it.
    /// </summary>
    /// <param name="request">Add item request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <response code="200">Item added successfully</response>
    /// <response code="400">Invalid request or headers</response>
    /// <response code="404">Product not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPost("items")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AddItem(
        [FromBody] AddCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        // AF-12: Detect Affiliate Mode
        var affiliateMode = AffiliateModeResolver.IsAffiliateMode(HttpContext);
        var affiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(HttpContext);
        var affiliateReferralCode = AffiliateModeResolver.GetReferralCode(HttpContext);
        var promotionLinkId = AffiliateModeResolver.GetPromotionLinkId(HttpContext);

        // AF-12: Record Affiliate Mode request metric
        HttpContext.RecordAffiliateModeRequest(validation.TenantId!.Value);

        // AF-12: Add response headers for frontend
        HttpContext.AddAffiliateModeHeaders();

        // Validate request
        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "AddItem invalid request | Error: {ErrorCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                errorCode, validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        _logger.LogInformation(
            "AddItem request | TenantId: {TenantId} | ProductId: {ProductId} | Quantity: {Quantity} | AffiliateMode: {AffiliateMode} | TraceId: {TraceId}",
            validation.TenantId, request.ProductId, request.Quantity, affiliateMode, traceId);

        try
        {
            var cart = await _cartService.AddItemAsync(
                validation.TenantId!.Value,
                validation.Owner!,
                request,
                affiliateAccountId,
                affiliateReferralCode,
                promotionLinkId,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "AddItem success | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
                validation.TenantId, cart.CartId, request.ProductId, cart.ItemCount, traceId);

            return Ok(cart);
        }
        catch (ProductNotFoundException ex)
        {
            _logger.LogWarning(
                "AddItem failed - product not found | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                validation.TenantId, ex.ProductId, traceId);
            return NotFound(new ApiErrorResponse("PRODUCT_NOT_FOUND", ex.Message, traceId));
        }
        catch (CartLimitExceededException ex)
        {
            _logger.LogWarning(
                "AddItem failed - cart limit exceeded | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse("CART_LIMIT_EXCEEDED", ex.Message, traceId));
        }
        catch (CurrencyMismatchException ex)
        {
            _logger.LogWarning(
                "AddItem failed - currency mismatch | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse("CURRENCY_MISMATCH", ex.Message, traceId));
        }
        catch (ProductNotForSaleException ex)
        {
            _logger.LogWarning(
                "AddItem failed - product not for sale (no valid price) | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                validation.TenantId, ex.ProductId, traceId);
            return BadRequest(new ApiErrorResponse("PRODUCT_NOT_FOR_SALE", "This product is not available for purchase (no valid price configured)", traceId));
        }
        catch (ProductOutOfStockException ex)
        {
            _logger.LogWarning(
                "AddItem failed - product out of stock | TenantId: {TenantId} | ProductId: {ProductId} | ProductName: {ProductName} | TraceId: {TraceId}",
                validation.TenantId, ex.ProductId, ex.ProductName, traceId);
            return Conflict(new ApiErrorResponse("PRODUCT_OUT_OF_STOCK", ex.Message, traceId));
        }
        // =============================================================================
        // AF-12 — AFFILIATE MODE EXCEPTION HANDLER
        // =============================================================================
        catch (ProductNotEligibleException ex)
        {
            _logger.LogWarning(
                "AddItem failed - product not eligible in affiliate mode | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                validation.TenantId, ex.ProductId, traceId);
            return BadRequest(new ApiErrorResponse("PRODUCT_NOT_ELIGIBLE", ex.Message, traceId));
        }
        // =============================================================================
        // PHASE 11.4 — VARIANT-SPECIFIC EXCEPTION HANDLERS
        // =============================================================================
        catch (VariantRequiredException ex)
        {
            _logger.LogWarning(
                "AddItem failed - variant selection required | TenantId: {TenantId} | ProductId: {ProductId} | ProductName: {ProductName} | TraceId: {TraceId}",
                validation.TenantId, ex.ProductId, ex.ProductName, traceId);
            return BadRequest(new ApiErrorResponse("VARIANT_REQUIRED", ex.Message, traceId));
        }
        catch (VariantNotFoundException ex)
        {
            _logger.LogWarning(
                "AddItem failed - variant not found | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | TraceId: {TraceId}",
                validation.TenantId, ex.ProductId, ex.VariantId, traceId);
            return NotFound(new ApiErrorResponse("VARIANT_NOT_FOUND", ex.Message, traceId));
        }
        catch (VariantInactiveException ex)
        {
            _logger.LogWarning(
                "AddItem failed - variant inactive | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                validation.TenantId, ex.VariantId, traceId);
            return BadRequest(new ApiErrorResponse("VARIANT_INACTIVE", ex.Message, traceId));
        }
        catch (VariantOutOfStockException ex)
        {
            _logger.LogWarning(
                "AddItem failed - variant out of stock | TenantId: {TenantId} | VariantId: {VariantId} | SKU: {SKU} | TraceId: {TraceId}",
                validation.TenantId, ex.VariantId, ex.SKU, traceId);
            return Conflict(new ApiErrorResponse("VARIANT_OUT_OF_STOCK", ex.Message, traceId));
        }
        catch (VariantInsufficientStockException ex)
        {
            _logger.LogWarning(
                "AddItem failed - insufficient variant stock | TenantId: {TenantId} | VariantId: {VariantId} | SKU: {SKU} | RequestedQuantity: {RequestedQuantity} | AvailableStock: {AvailableStock} | TraceId: {TraceId}",
                validation.TenantId,
                ex.VariantId,
                ex.SKU,
                ex.RequestedQuantity,
                ex.AvailableStock,
                traceId);
            return Conflict(new ApiErrorResponse("VARIANT_INSUFFICIENT_STOCK", ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "AddItem rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "AddItem failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AddItem failed | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                validation.TenantId, request.ProductId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Updates the quantity of an item in the cart.
    /// If quantity is 0, removes the item.
    /// </summary>
    /// <param name="productId">Product ID to update</param>
    /// <param name="request">Update request with new quantity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <response code="200">Item updated successfully</response>
    /// <response code="400">Invalid request or headers</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPut("items/{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateItem(
        [FromRoute] Guid productId,
        [FromBody] UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        // Validate request
        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "UpdateItem invalid request | Error: {ErrorCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                errorCode, validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        _logger.LogInformation(
            "UpdateItem request | TenantId: {TenantId} | ProductId: {ProductId} | NewQuantity: {Quantity} | TraceId: {TraceId}",
            validation.TenantId, productId, request.Quantity, traceId);

        try
        {
            var cart = await _cartService.UpdateItemAsync(
                validation.TenantId!.Value,
                validation.Owner!,
                productId,
                request,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "UpdateItem success | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
                validation.TenantId, cart.CartId, productId, cart.ItemCount, traceId);

            return Ok(cart);
        }
        catch (CartLimitExceededException ex)
        {
            _logger.LogWarning(
                "UpdateItem failed - cart limit exceeded | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse("CART_LIMIT_EXCEEDED", ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "UpdateItem rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "UpdateItem failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UpdateItem failed | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                validation.TenantId, productId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Removes an item from the cart.
    /// </summary>
    /// <param name="productId">Product ID to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <response code="200">Item removed successfully</response>
    /// <response code="400">Invalid headers</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpDelete("items/{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveItem(
        [FromRoute] Guid productId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "RemoveItem request | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
            validation.TenantId, productId, traceId);

        try
        {
            var cart = await _cartService.RemoveItemAsync(
                validation.TenantId!.Value,
                validation.Owner!,
                productId,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "RemoveItem success | TenantId: {TenantId} | CartId: {CartId} | ProductId: {ProductId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
                validation.TenantId, cart.CartId, productId, cart.ItemCount, traceId);

            return Ok(cart);
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "RemoveItem rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "RemoveItem failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RemoveItem failed | TenantId: {TenantId} | ProductId: {ProductId} | TraceId: {TraceId}",
                validation.TenantId, productId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region PHASE 11.7 — Variant-Centric Cart Operations

    /// <summary>
    /// PHASE 11.7: Updates the quantity of an item by VariantId.
    /// VariantId is the ONLY identifier for cart items.
    /// If quantity is 0, removes the item.
    /// </summary>
    /// <param name="variantId">Variant ID to update</param>
    /// <param name="request">Update request with new quantity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <response code="200">Item updated successfully</response>
    /// <response code="400">Invalid request or headers</response>
    /// <response code="404">Cart item not found</response>
    /// <response code="409">Insufficient stock</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPut("items/variant/{variantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateItemByVariant(
        [FromRoute] Guid variantId,
        [FromBody] UpdateCartItemRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        // Validate request
        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "[PHASE 11.7] UpdateItemByVariant invalid request | Error: {ErrorCode} | TenantId: {TenantId} | TraceId: {TraceId}",
                errorCode, validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        _logger.LogInformation(
            "[PHASE 11.7] UpdateItemByVariant request | TenantId: {TenantId} | VariantId: {VariantId} | NewQuantity: {Quantity} | TraceId: {TraceId}",
            validation.TenantId, variantId, request.Quantity, traceId);

        try
        {
            var cart = await _cartService.UpdateItemByVariantAsync(
                validation.TenantId!.Value,
                validation.Owner!,
                variantId,
                request,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "[PHASE 11.7] UpdateItemByVariant success | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
                validation.TenantId, cart.CartId, variantId, cart.ItemCount, traceId);

            return Ok(cart);
        }
        catch (CartItemNotFoundException ex)
        {
            _logger.LogWarning(
                "[PHASE 11.7] UpdateItemByVariant - item not found | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                validation.TenantId, ex.VariantId, traceId);
            return NotFound(new ApiErrorResponse("CART_ITEM_NOT_FOUND", ex.Message, traceId));
        }
        catch (VariantInsufficientStockException ex)
        {
            _logger.LogWarning(
                "[PHASE 11.7] UpdateItemByVariant - insufficient stock | TenantId: {TenantId} | VariantId: {VariantId} | Requested: {Requested} | Available: {Available} | TraceId: {TraceId}",
                validation.TenantId, ex.VariantId, ex.RequestedQuantity, ex.AvailableStock, traceId);
            return Conflict(new ApiErrorResponse("VARIANT_INSUFFICIENT_STOCK", ex.Message, traceId)
            {
                // Include additional data for frontend
                Data = new { availableStock = ex.AvailableStock }
            });
        }
        catch (CartLimitExceededException ex)
        {
            _logger.LogWarning(
                "[PHASE 11.7] UpdateItemByVariant - cart limit exceeded | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return BadRequest(new ApiErrorResponse("CART_LIMIT_EXCEEDED", ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "[PHASE 11.7] UpdateItemByVariant rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "[PHASE 11.7] UpdateItemByVariant - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PHASE 11.7] UpdateItemByVariant failed | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                validation.TenantId, variantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// PHASE 11.7: Removes an item from the cart by VariantId.
    /// VariantId is the ONLY identifier for cart items.
    /// </summary>
    /// <param name="variantId">Variant ID to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <response code="200">Item removed successfully</response>
    /// <response code="400">Invalid headers</response>
    /// <response code="404">Cart item not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpDelete("items/variant/{variantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveItemByVariant(
        [FromRoute] Guid variantId,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "[PHASE 11.7] RemoveItemByVariant request | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
            validation.TenantId, variantId, traceId);

        try
        {
            var cart = await _cartService.RemoveItemByVariantAsync(
                validation.TenantId!.Value,
                validation.Owner!,
                variantId,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "[PHASE 11.7] RemoveItemByVariant success | TenantId: {TenantId} | CartId: {CartId} | VariantId: {VariantId} | ItemCount: {ItemCount} | TraceId: {TraceId}",
                validation.TenantId, cart.CartId, variantId, cart.ItemCount, traceId);

            return Ok(cart);
        }
        catch (CartItemNotFoundException ex)
        {
            _logger.LogWarning(
                "[PHASE 11.7] RemoveItemByVariant - item not found | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                validation.TenantId, ex.VariantId, traceId);
            return NotFound(new ApiErrorResponse("CART_ITEM_NOT_FOUND", ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "[PHASE 11.7] RemoveItemByVariant rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "[PHASE 11.7] RemoveItemByVariant - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PHASE 11.7] RemoveItemByVariant failed | TenantId: {TenantId} | VariantId: {VariantId} | TraceId: {TraceId}",
                validation.TenantId, variantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #endregion

    /// <summary>
    /// Clears all items from the cart.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Empty cart DTO</returns>
    /// <response code="200">Cart cleared successfully</response>
    /// <response code="400">Invalid headers</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpDelete]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ClearCart(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "ClearCart request | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | TraceId: {TraceId}",
            validation.TenantId, validation.Owner!.OwnerType, validation.Owner.OwnerId, traceId);

        try
        {
            var cart = await _cartService.ClearCartAsync(
                validation.TenantId!.Value,
                validation.Owner,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "ClearCart success | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);

            return Ok(cart);
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "ClearCart rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "ClearCart failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ClearCart failed | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Coupon Operations

    /// <summary>
    /// Applies a coupon code to the cart.
    /// Validates the coupon against all rules and calculates discount.
    /// </summary>
    /// <param name="request">Apply coupon request with code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart with applied coupon</returns>
    /// <response code="200">Coupon applied successfully</response>
    /// <response code="400">Invalid coupon or validation failed</response>
    /// <response code="429">Too many requests - rate limited</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPost("apply-coupon")]
    [EnableRateLimiting("CouponRateLimit")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ApplyCoupon(
        [FromBody] ApplyCouponRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate request
        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            _logger.LogWarning(
                "ApplyCoupon validation failed | Error: {ErrorCode} | TraceId: {TraceId}",
                errorCode, traceId);
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        if (!validation.CustomerId.HasValue)
        {
            _logger.LogInformation(
                "ApplyCoupon blocked for guest cart | TenantId: {TenantId} | Code: {Code} | SessionId: {SessionId} | TraceId: {TraceId}",
                validation.TenantId,
                request.NormalizedCode,
                validation.Owner?.OwnerId,
                traceId);
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ApiErrorResponse(
                    CouponErrorCodes.AuthenticationRequired,
                    "You must sign in to use coupons",
                    traceId));
        }

        string? normalizedCode = null;
        try
        {
            var smartOfferRedemption = await ResolveSmartOfferRedemptionAsync(
                validation.TenantId!.Value,
                validation.CustomerId.Value,
                request.NormalizedSmartOfferRedeemToken,
                traceId,
                cancellationToken);
            if (smartOfferRedemption.Error != null)
            {
                return smartOfferRedemption.Error;
            }

            normalizedCode = smartOfferRedemption.CouponCode ?? request.NormalizedCode;

            if (smartOfferRedemption.CouponCode == null)
            {
                var generatedOfferCoupon = await IsSmartOfferGeneratedCouponAsync(
                    validation.TenantId.Value,
                    normalizedCode,
                    cancellationToken);
                if (generatedOfferCoupon)
                {
                    _reliabilityTelemetry.RecordSmartOfferAbuseBlocked("raw_coupon_code_rejected", HttpContext.Request.Path.Value);
                    return BadRequest(new ApiErrorResponse(
                        SmartOfferErrorCodes.SmartOfferTokenRequired,
                        "This offer must be redeemed through the storefront offer flow.",
                        traceId));
                }
            }

            _logger.LogInformation(
                "ApplyCoupon request | TenantId: {TenantId} | Code: {Code} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | TraceId: {TraceId}",
                validation.TenantId, normalizedCode, validation.Owner!.OwnerType, validation.Owner.OwnerId, traceId);

            // Get current cart
            var cart = await _cartService.GetCartAsync(
                validation.TenantId!.Value,
                validation.Owner,
                traceId,
                cancellationToken);

            // Check if cart is empty
            if (cart.Items.Count == 0)
            {
                _logger.LogWarning(
                    "ApplyCoupon failed - cart is empty | TenantId: {TenantId} | Code: {Code} | TraceId: {TraceId}",
                    validation.TenantId, normalizedCode, traceId);
                return BadRequest(new ApiErrorResponse(CouponErrorCodes.CartEmpty, "Cannot apply coupon to empty cart", traceId));
            }

            // Validate coupon
            var couponResult = await _couponService.ValidateCouponAsync(
                validation.TenantId.Value,
                normalizedCode,
                cart,
                validation.CustomerId.Value,
                traceId,
                cancellationToken);

            if (!couponResult.IsValid)
            {
                _logger.LogWarning(
                    "ApplyCoupon failed - validation error | TenantId: {TenantId} | Code: {Code} | Error: {ErrorCode} | TraceId: {TraceId}",
                    validation.TenantId, normalizedCode, couponResult.ErrorCode, traceId);
                return BadRequest(new ApiErrorResponse(couponResult.ErrorCode!, couponResult.ErrorMessage!, traceId));
            }

            // Apply coupon to cart
            var couponInfo = new AppliedCouponInfo
            {
                CouponId = couponResult.CouponId!.Value, // CRITICAL: Store CouponId for checkout
                Code = couponResult.Code!,
                Type = couponResult.DiscountType!,
                Value = couponResult.DiscountValue!.Value,
                Amount = couponResult.DiscountAmount!.Value
            };

            cart.ApplyCouponDiscount(couponInfo);
            await PersistCouponApplicationAsync(
                cart,
                couponInfo,
                validation.CustomerId,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            return Ok(cart);
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "ApplyCoupon rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ApplyCoupon failed | TenantId: {TenantId} | Code: {Code} | TraceId: {TraceId}",
                validation.TenantId, normalizedCode ?? request.NormalizedCode ?? "(unknown)", traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Applies an order draft from AI assistant.
    /// </summary>
    [HttpPost("apply-draft")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ApplyDraft(
        [FromBody] ApplyOrderDraftRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "ApplyDraft request | TenantId: {TenantId} | Items: {Count} | TraceId: {TraceId}",
            validation.TenantId, request.Items.Count, traceId);

        try
        {
            var cart = await _cartService.ApplyDraftAsync(
                validation.TenantId!.Value,
                validation.Owner!,
                request,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                validation.TenantId.Value,
                validation.CustomerId,
                traceId,
                cancellationToken);

            return Ok(cart);
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "ApplyDraft rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ApplyDraft failed | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }




    /// <summary>
    /// Removes the applied coupon from the cart.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart without coupon</returns>
    /// <response code="200">Coupon removed successfully</response>
    /// <response code="400">No coupon applied</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPost("remove-coupon")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RemoveCoupon(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate headers
        var validation = await ValidateHeadersAsync(traceId);
        if (validation.Error != null)
        {
            return validation.Error;
        }

        _logger.LogInformation(
            "RemoveCoupon request | TenantId: {TenantId} | OwnerType: {OwnerType} | OwnerId: {OwnerId} | TraceId: {TraceId}",
            validation.TenantId, validation.Owner!.OwnerType, validation.Owner.OwnerId, traceId);

        try
        {
            // Get current cart
            var cart = await _cartService.GetCartAsync(
                validation.TenantId!.Value,
                validation.Owner,
                traceId,
                cancellationToken);

            // Check if coupon is applied
            if (string.IsNullOrEmpty(cart.AppliedCouponCode))
            {
                _logger.LogWarning(
                    "RemoveCoupon failed - no coupon applied | TenantId: {TenantId} | TraceId: {TraceId}",
                    validation.TenantId, traceId);
                return BadRequest(new ApiErrorResponse(CouponErrorCodes.NotApplied, "No coupon is applied to remove", traceId));
            }

            var removedCode = cart.AppliedCouponCode;
            var removedCouponId = cart.AppliedCoupon?.CouponId;

            // Remove coupon
            cart.ClearCoupon();
            await PersistCouponRemovalAsync(
                cart,
                removedCode!,
                removedCouponId,
                validation.CustomerId,
                "User removed",
                wasAutomatic: false,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "RemoveCoupon success | TenantId: {TenantId} | RemovedCode: {Code} | TraceId: {TraceId}",
                validation.TenantId, removedCode, traceId);

            return Ok(cart);
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, validation.TenantId);
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "RemoveCoupon failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "RemoveCoupon rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RemoveCoupon failed | TenantId: {TenantId} | TraceId: {TraceId}",
                validation.TenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #endregion

    #region Cart Merge (Phase 10.4 - Cart Ownership Migration)

    /// <summary>
    /// Merges a guest (session) cart into a customer cart.
    /// Called automatically after login to transfer guest cart items.
    /// 
    /// PHASE 10.4: Cart Ownership Migration
    /// 
    /// Required Headers:
    /// - X-Tenant-Id: Tenant identifier
    /// - X-Session-Id: Source session cart identifier (required)
    /// 
    /// Authentication:
    /// - Requires StorefrontJwt authentication (customer must be logged in)
    /// - Target customer ID is derived from authenticated CustomerId claim
    /// 
    /// SECURITY:
    /// - Customer ID is NEVER trusted from headers - only from authenticated claims
    /// - This prevents attackers from merging into arbitrary customer carts
    /// 
    /// Merge Rules:
    /// - If guest cart is empty: Returns customer cart (no-op)
    /// - If customer cart is empty: Creates customer cart with guest items
    /// - If both have items: Merges (sums quantities, caps at 99)
    /// - Guest cart is ALWAYS deleted after merge
    /// - Coupon is preserved and revalidated against customer
    /// 
    /// This endpoint is idempotent - safe to retry if guest cart already deleted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged customer cart</returns>
    /// <response code="200">Cart merged successfully</response>
    /// <response code="400">Missing required headers</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpPost("merge")]
    [Authorize(AuthenticationSchemes = StorefrontAuthenticationScheme)]
    [ProducesResponseType(typeof(CartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> MergeCart(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantValidation = PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
        if (tenantValidation.Error != null)
        {
            _logger.LogWarning(
                "MergeCart failed - tenant context unavailable | TraceId: {TraceId}",
                traceId);
            return tenantValidation.Error;
        }

        var tenantId = tenantValidation.TenantId!.Value;

        // Step 2: Validate X-Session-Id (source cart - required)
        if (!Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) ||
            string.IsNullOrWhiteSpace(sessionHeaderValue))
        {
            _logger.LogWarning(
                "MergeCart failed - missing session header | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return BadRequest(new ApiErrorResponse(
                "SESSION_HEADER_MISSING",
                "X-Session-Id header is required for cart merge (source cart)",
                traceId));
        }

        // Step 3: SECURITY - Derive customer ID from the authenticated claim only
        var authenticatedCustomer = await ResolveAuthenticatedCustomerAsync();
        var customerId = authenticatedCustomer.CustomerId;
        if (!customerId.HasValue)
        {
            _logger.LogWarning(
                "MergeCart failed - missing or invalid CustomerId claim | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return Unauthorized(new ApiErrorResponse(
                "INVALID_TOKEN",
                "Authentication token is missing or invalid customer ID claim",
                traceId));
        }

        var tenantClaimError = ValidateAuthenticatedTenantClaim(
            authenticatedCustomer.Principal,
            tenantId,
            traceId);
        if (tenantClaimError != null)
        {
            return tenantClaimError;
        }

        var customerAuthorityError = await ValidateAuthenticatedCustomerAuthorityAsync(
            tenantId,
            customerId.Value,
            traceId,
            cancellationToken);
        if (customerAuthorityError != null)
        {
            return customerAuthorityError;
        }

        // Build cart owners
        var sessionOwner = new CartOwner
        {
            OwnerType = "session",
            OwnerId = sessionHeaderValue.ToString()!.Trim()
        };

        var customerOwner = new CartOwner
        {
            OwnerType = "customer",
            OwnerId = customerId.Value.ToString("N")
        };

        _logger.LogInformation(
            "MergeCart request | TenantId: {TenantId} | SessionId: {SessionId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            tenantId, sessionOwner.OwnerId.Substring(0, Math.Min(8, sessionOwner.OwnerId.Length)) + "...", 
            customerId, traceId);

        try
        {
            // Call the cart service to perform the merge
            var cart = await _cartService.MergeCartAsync(
                tenantId,
                sessionOwner,
                customerOwner,
                traceId,
                cancellationToken);

            cart = await RevalidateCouponForResponseAsync(
                cart,
                tenantId,
                customerId.Value,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "MergeCart success | TenantId: {TenantId} | CustomerId: {CustomerId} | ItemCount: {ItemCount} | HasCoupon: {HasCoupon} | TraceId: {TraceId}",
                tenantId, customerId, cart.ItemCount, !string.IsNullOrEmpty(cart.AppliedCouponCode), traceId);

            return Ok(cart);
        }
        catch (CurrencyMismatchException ex)
        {
            _logger.LogWarning(
                "MergeCart failed - currency mismatch | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return BadRequest(new ApiErrorResponse("CURRENCY_MISMATCH", ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (IsTenantMismatch(ex))
        {
            return TenantMismatchResult(traceId, tenantId);
        }
        catch (CartConcurrencyConflictException ex)
        {
            _logger.LogInformation(
                "MergeCart rejected due to cart concurrency conflict | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return Conflict(new ApiErrorResponse("CART_CONCURRENCY_CONFLICT", ex.Message, traceId));
        }
        catch (RedisUnavailableException)
        {
            _logger.LogError(
                "MergeCart failed - Redis unavailable | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MergeCart failed | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId, customerId, traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #endregion

    #region Header Validation

    private async Task<CartDto> RevalidateCouponForResponseAsync(
        CartDto cart,
        Guid tenantId,
        Guid? customerId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var persistedState = await LoadPersistedCouponStateAsync(tenantId, cart.CartId, cancellationToken);
        ApplyPersistedCouponState(cart, persistedState);

        if (string.IsNullOrWhiteSpace(cart.AppliedCouponCode))
        {
            // Clear orphaned coupon state if AppliedCouponCode is missing
            if (cart.AppliedCoupon != null || cart.DiscountTotal > 0)
            {
                _logger.LogWarning(
                    "Clearing orphaned coupon state | TenantId: {TenantId} | HasAppliedCoupon: {HasAppliedCoupon} | DiscountTotal: {DiscountTotal} | TraceId: {TraceId}",
                    tenantId,
                    cart.AppliedCoupon != null,
                    cart.DiscountTotal,
                    traceId);

                cart.ClearCoupon();
                await TrySaveCartCacheAsync(cart, traceId, cancellationToken);
            }
            return cart;
        }

        if (!customerId.HasValue || customerId.Value == Guid.Empty)
        {
            _logger.LogInformation(
                "Removing coupon from guest cart because checkout requires authenticated coupon ownership | TenantId: {TenantId} | CartId: {CartId} | Code: {Code} | TraceId: {TraceId}",
                tenantId,
                cart.CartId,
                cart.AppliedCouponCode,
                traceId);

            var removedCode = cart.AppliedCouponCode;
            var removedCouponId = cart.AppliedCoupon?.CouponId;
            cart.ClearCoupon();
            await PersistCouponRemovalAsync(
                cart,
                removedCode!,
                removedCouponId,
                null,
                "Authentication required",
                wasAutomatic: true,
                traceId,
                cancellationToken);
            return cart;
        }

        var requestedCode = cart.AppliedCouponCode;
        
        var couponResult = await _couponService.RevalidateCouponAsync(
            tenantId,
            requestedCode,
            cart,
            customerId.Value,
            traceId,
            cancellationToken);

        if (!couponResult.IsValid)
        {
            _logger.LogInformation(
                "Coupon auto-removed during cart response normalization | TenantId: {TenantId} | Code: {Code} | Reason: {Reason} | TraceId: {TraceId}",
                tenantId,
                requestedCode,
                couponResult.ErrorCode,
                traceId);

            var removedCouponId = cart.AppliedCoupon?.CouponId;
            cart.ClearCoupon();
            await PersistCouponRemovalAsync(
                cart,
                requestedCode,
                removedCouponId,
                customerId,
                couponResult.ErrorCode ?? "Coupon invalidated",
                wasAutomatic: true,
                traceId,
                cancellationToken);
            return cart;
        }

        var normalizedCode = couponResult.Code ?? requestedCode;
        var couponId = couponResult.CouponId ?? cart.AppliedCoupon?.CouponId ?? Guid.Empty;
        var discountType = couponResult.DiscountType ?? cart.AppliedCoupon?.Type ?? "percentage";
        var discountValue = couponResult.DiscountValue ?? cart.AppliedCoupon?.Value ?? 0m;
        var discountAmount = couponResult.DiscountAmount ?? 0m;

        var couponInfo = new AppliedCouponInfo
        {
            CouponId = couponId,
            Code = normalizedCode,
            Type = discountType,
            Value = discountValue,
            Amount = discountAmount
        };

        var requiresPersistence =
            persistedState == null ||
            !PersistedCouponStateMatches(persistedState, couponInfo, customerId.Value, cart.Subtotal);

        if (!requiresPersistence)
        {
            return cart;
        }

        cart.ApplyCouponDiscount(couponInfo);
        await PersistCouponApplicationAsync(cart, couponInfo, customerId, traceId, cancellationToken);
        return cart;
    }

    private async Task PersistCouponApplicationAsync(
        CartDto cart,
        AppliedCouponInfo couponInfo,
        Guid? customerId,
        string traceId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var state = await GetOrCreateCouponStateAsync(cart, cancellationToken);
            state.ApplyCoupon(
                couponInfo.CouponId,
                couponInfo.Code,
                couponInfo.Type,
                couponInfo.Value,
                couponInfo.Amount,
                cart.Subtotal,
                customerId);

            var cartCouponAppliedEvent = new CartCouponAppliedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                CorrelationId = traceId,
                IdempotencyKey = $"cart-coupon-apply:{cart.CartId:D}:{state.MutationVersion}",
                OccurredAtUtc = DateTime.UtcNow,
                CouponCode = couponInfo.Code,
                CouponId = couponInfo.CouponId,
                DiscountType = couponInfo.Type,
                DiscountValue = couponInfo.Value,
                DiscountAmount = couponInfo.Amount,
                CartSubtotal = cart.Subtotal,
                CustomerId = customerId
            };

            await _eventPublisher.PublishAsync(cartCouponAppliedEvent, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _reliabilityTelemetry.RecordCouponEventPersistFailure(cart.TenantId, GetCouponPersistFailureReason(ex));

            if (_dbContext.Database.CurrentTransaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogError(
                ex,
                "Coupon apply persistence failed before outbox commit | TenantId: {TenantId} | CartId: {CartId} | CouponCode: {CouponCode} | TraceId: {TraceId}",
                cart.TenantId,
                cart.CartId,
                couponInfo.Code,
                traceId);
            throw;
        }

        await TrySaveCartCacheAsync(cart, traceId, cancellationToken);
    }

    private async Task PersistCouponRemovalAsync(
        CartDto cart,
        string couponCode,
        Guid? couponId,
        Guid? customerId,
        string removalReason,
        bool wasAutomatic,
        string traceId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var state = await GetOrCreateCouponStateAsync(cart, cancellationToken);
            state.ClearCoupon(cart.Subtotal, customerId, removalReason);

            var cartCouponRemovedEvent = new CartCouponRemovedEvent
            {
                TenantId = cart.TenantId,
                CartId = cart.CartId,
                OwnerType = cart.OwnerType,
                OwnerId = cart.OwnerId,
                CorrelationId = traceId,
                IdempotencyKey = $"cart-coupon-remove:{cart.CartId:D}:{state.MutationVersion}",
                OccurredAtUtc = DateTime.UtcNow,
                CouponCode = couponCode,
                CouponId = couponId,
                RemovalReason = removalReason,
                WasAutomatic = wasAutomatic,
                CartSubtotal = cart.Subtotal,
                CustomerId = customerId
            };

            await _eventPublisher.PublishAsync(cartCouponRemovedEvent, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _reliabilityTelemetry.RecordCouponEventPersistFailure(cart.TenantId, GetCouponPersistFailureReason(ex));

            if (_dbContext.Database.CurrentTransaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogError(
                ex,
                "Coupon removal persistence failed before outbox commit | TenantId: {TenantId} | CartId: {CartId} | CouponCode: {CouponCode} | TraceId: {TraceId}",
                cart.TenantId,
                cart.CartId,
                couponCode,
                traceId);
            throw;
        }

        await TrySaveCartCacheAsync(cart, traceId, cancellationToken);
    }

    private async Task<StorefrontCartCouponState> GetOrCreateCouponStateAsync(
        CartDto cart,
        CancellationToken cancellationToken)
    {
        var state = await _dbContext.StorefrontCartCouponStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.TenantId == cart.TenantId && item.CartId == cart.CartId,
                cancellationToken);

        if (state != null)
        {
            return state;
        }

        state = StorefrontCartCouponState.Create(
            cart.TenantId,
            cart.CartId,
            cart.OwnerType,
            cart.OwnerId);

        _dbContext.StorefrontCartCouponStates.Add(state);
        return state;
    }

    private async Task<StorefrontCartCouponState?> LoadPersistedCouponStateAsync(
        Guid tenantId,
        Guid cartId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StorefrontCartCouponStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId && item.CartId == cartId,
                cancellationToken);
    }

    private static bool ApplyPersistedCouponState(CartDto cart, StorefrontCartCouponState? persistedState)
    {
        if (persistedState == null)
        {
            return false;
        }

        if (!persistedState.HasAppliedCoupon)
        {
            if (string.IsNullOrWhiteSpace(cart.AppliedCouponCode) &&
                cart.AppliedCoupon == null &&
                cart.DiscountTotal == 0m)
            {
                return false;
            }

            cart.ClearCoupon();
            return true;
        }

        if (!persistedState.CouponId.HasValue ||
            string.IsNullOrWhiteSpace(persistedState.CouponCode) ||
            string.IsNullOrWhiteSpace(persistedState.DiscountType) ||
            !persistedState.DiscountValue.HasValue ||
            !persistedState.DiscountAmount.HasValue)
        {
            cart.ClearCoupon();
            return true;
        }

        if (cart.AppliedCoupon?.CouponId == persistedState.CouponId.Value &&
            string.Equals(cart.AppliedCouponCode, persistedState.CouponCode, StringComparison.Ordinal) &&
            string.Equals(cart.AppliedCoupon?.Type, persistedState.DiscountType, StringComparison.Ordinal) &&
            cart.AppliedCoupon?.Value == persistedState.DiscountValue.Value &&
            cart.DiscountTotal == persistedState.DiscountAmount.Value)
        {
            return false;
        }

        cart.ApplyCouponDiscount(new AppliedCouponInfo
        {
            CouponId = persistedState.CouponId.Value,
            Code = persistedState.CouponCode,
            Type = persistedState.DiscountType,
            Value = persistedState.DiscountValue.Value,
            Amount = persistedState.DiscountAmount.Value
        });

        return true;
    }

    private static bool PersistedCouponStateMatches(
        StorefrontCartCouponState state,
        AppliedCouponInfo couponInfo,
        Guid customerId,
        decimal cartSubtotal)
    {
        return state.HasAppliedCoupon &&
               state.CouponId == couponInfo.CouponId &&
               string.Equals(state.CouponCode, couponInfo.Code, StringComparison.Ordinal) &&
               string.Equals(state.DiscountType, couponInfo.Type, StringComparison.Ordinal) &&
               state.DiscountValue == couponInfo.Value &&
               state.DiscountAmount == couponInfo.Amount &&
               state.CustomerId == customerId &&
               state.CartSubtotal == cartSubtotal;
    }

    private async Task TrySaveCartCacheAsync(
        CartDto cart,
        string traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cartService.SaveCartAsync(cart, traceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Coupon state committed but cart cache refresh failed | TenantId: {TenantId} | CartId: {CartId} | TraceId: {TraceId}",
                cart.TenantId,
                cart.CartId,
                traceId);

            _reliabilityTelemetry.RecordCartStateMismatchDetected(
                "coupon_cache_refresh",
                "redis_refresh_failed");
        }
    }

    private static string GetCouponPersistFailureReason(Exception exception)
    {
        return exception switch
        {
            DbUpdateException => "db_commit_failed",
            InvalidOperationException => "outbox_publish_failed",
            _ => "coupon_persist_failed"
        };
    }

    private async Task<(string? CouponCode, IActionResult? Error)> ResolveSmartOfferRedemptionAsync(
        Guid tenantId,
        Guid customerId,
        string? redeemToken,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(redeemToken))
        {
            return (null, null);
        }

        var validation = _smartOfferRedeemTokenService.ValidateToken(redeemToken, tenantId, customerId);
        if (!validation.IsValid)
        {
            var errorCode = validation.IsExpired
                ? SmartOfferErrorCodes.SmartOfferTokenExpired
                : SmartOfferErrorCodes.SmartOfferTokenInvalid;
            var message = validation.IsExpired
                ? "The smart offer has expired. Please open the offer again."
                : "The smart offer is invalid. Please try again from the storefront offer.";

            _reliabilityTelemetry.RecordSmartOfferAbuseBlocked(
                validation.IsExpired ? "redeem_token_expired" : "redeem_token_invalid",
                HttpContext.Request.Path.Value);

            return (null, BadRequest(new ApiErrorResponse(errorCode, message, traceId)));
        }

        var generatedOfferCoupon = await _dbContext.SmartOffers
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.Id == validation.SmartOfferId!.Value &&
                x.Status == SmartOfferStatus.Active &&
                x.GeneratedCouponId == validation.CouponId)
            .Join(
                _dbContext.Coupons
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.IsActive),
                offer => offer.GeneratedCouponId!.Value,
                coupon => coupon.Id,
                (offer, coupon) => coupon.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(generatedOfferCoupon))
        {
            _reliabilityTelemetry.RecordSmartOfferAbuseBlocked("redeem_offer_missing", HttpContext.Request.Path.Value);
            return (null, BadRequest(new ApiErrorResponse(
                SmartOfferErrorCodes.SmartOfferTokenInvalid,
                "The smart offer is no longer available.",
                traceId)));
        }

        return (generatedOfferCoupon.Trim().ToUpperInvariant(), null);
    }

    private async Task<bool> IsSmartOfferGeneratedCouponAsync(
        Guid tenantId,
        string normalizedCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return false;
        }

        return await _dbContext.SmartOffers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.GeneratedCouponId.HasValue)
            .Join(
                _dbContext.Coupons
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.NormalizedCode == normalizedCode),
                offer => offer.GeneratedCouponId!.Value,
                coupon => coupon.Id,
                (offer, coupon) => offer.Id)
            .AnyAsync(cancellationToken);
    }

    private async Task<(Guid? TenantId, CartOwner? Owner, Guid? CustomerId, IActionResult? Error)> ValidateHeadersAsync(string traceId)
    {
        var tenantValidation = PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
        if (tenantValidation.Error != null)
        {
            _logger.LogWarning(
                "Missing tenant context | Endpoint: Cart | TraceId: {TraceId}",
                traceId);
            return (null, null, null, tenantValidation.Error);
        }

        var tenantId = tenantValidation.TenantId!.Value;
        var sessionId = Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) &&
                        !string.IsNullOrWhiteSpace(sessionHeaderValue)
            ? sessionHeaderValue.ToString()!.Trim()
            : null;

        var authenticatedCustomer = await ResolveAuthenticatedCustomerAsync();
        if (authenticatedCustomer.CustomerId.HasValue)
        {
            var tenantClaimError = ValidateAuthenticatedTenantClaim(
                authenticatedCustomer.Principal,
                tenantId,
                traceId);
            if (tenantClaimError != null)
            {
                return (null, null, null, tenantClaimError);
            }

            var customerAuthorityError = await ValidateAuthenticatedCustomerAuthorityAsync(
                tenantId,
                authenticatedCustomer.CustomerId.Value,
                traceId,
                HttpContext.RequestAborted);
            if (customerAuthorityError != null)
            {
                return (null, null, null, customerAuthorityError);
            }

            var customerOwner = new CartOwner
            {
                OwnerType = "customer",
                OwnerId = authenticatedCustomer.CustomerId.Value.ToString("N")
            };

            return (tenantId, customerOwner, authenticatedCustomer.CustomerId.Value, null);
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning(
                "Missing X-Session-Id header for guest cart request | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
            return (null, null, null, BadRequest(new ApiErrorResponse(
                "SESSION_HEADER_MISSING",
                "X-Session-Id header is required for guest cart requests",
                traceId)));
        }

        var guestOwner = new CartOwner
        {
            OwnerType = "session",
            OwnerId = sessionId
        };

        return (tenantId, guestOwner, null, null);
    }

    private async Task<CartCallerIdentity> ResolveAuthenticatedCustomerAsync()
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(customerAuthenticationResult.Principal, out var customerId))
        {
            return new CartCallerIdentity(customerAuthenticationResult.Principal, customerId);
        }

        var storefrontAuthenticationResult = await HttpContext.AuthenticateAsync(StorefrontAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(storefrontAuthenticationResult.Principal, out customerId))
        {
            return new CartCallerIdentity(storefrontAuthenticationResult.Principal, customerId);
        }

        return new CartCallerIdentity(null, null);
    }

    private IActionResult? ValidateAuthenticatedTenantClaim(
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
            "X-Tenant-Id header mismatch with authenticated claim | Header: {Header} | Claim: {Claim} | TraceId: {TraceId}",
            tenantId,
            claimTenantId,
            traceId);
        return Unauthorized(new ApiErrorResponse(
            TenantErrorCodes.TenantMismatch,
            "X-Tenant-Id header does not match authenticated tenant",
            traceId));
    }

    private async Task<IActionResult?> ValidateAuthenticatedCustomerAuthorityAsync(
        Guid tenantId,
        Guid customerId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var customerAuthority = await _dbContext.OnlineCustomers
            .AsNoTracking()
            .Where(
                customer =>
                    customer.Id == customerId)
            .Select(customer => new
            {
                customer.TenantId,
                customer.IsActive
            })
            .FirstOrDefaultAsync(
                cancellationToken);

        if (customerAuthority is { TenantId: var authorityTenantId } &&
            authorityTenantId != tenantId)
        {
            _logger.LogWarning(
                "Storefront cart request blocked because authenticated online customer belongs to another tenant | TenantId: {TenantId} | CustomerId: {CustomerId} | AuthorityTenantId: {AuthorityTenantId} | TraceId: {TraceId}",
                tenantId,
                customerId,
                authorityTenantId,
                traceId);

            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }

        if (customerAuthority is { IsActive: true })
        {
            return null;
        }

        _logger.LogWarning(
            "Storefront cart request blocked because authenticated online customer is inactive or no longer belongs to tenant | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            tenantId,
            customerId,
            traceId);

        return Unauthorized(new ApiErrorResponse(
            "CUSTOMER_INACTIVE",
            "Customer account is not active.",
            traceId));
    }

    private static bool TryGetAuthenticatedCustomerId(ClaimsPrincipal? principal, out Guid customerId)
    {
        customerId = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return Guid.TryParse(
            principal.FindFirst("CustomerId")?.Value
                ?? principal.FindFirst("customer_id")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out customerId) &&
            customerId != Guid.Empty;
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private static bool IsTenantMismatch(UnauthorizedAccessException ex)
    {
        return string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal);
    }

    private IActionResult TenantMismatchResult(string traceId, Guid? tenantId)
    {
        _logger.LogWarning(
            "Cart request rejected due to tenant mismatch | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId,
            traceId);

        return Unauthorized(new ApiErrorResponse(
            TenantErrorCodes.TenantMismatch,
            "Authenticated customer does not belong to the resolved tenant.",
            traceId));
    }

    private sealed record CartCallerIdentity(ClaimsPrincipal? Principal, Guid? CustomerId);

    #endregion
}
