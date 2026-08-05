using PointOfSale.API.DTOs.Cart;

namespace PointOfSale.API.Services.Cart;

/// <summary>
/// Service interface for Cart operations.
/// 
/// ARCHITECTURAL BOUNDARY (Phase 3.1 - Cart API):
/// - Redis is the ONLY storage for cart state
/// - Cart is tenant-scoped and ephemeral (TTL-based)
/// - MongoDB is read-only (product validation only)
/// - Every cart operation emits an event via MassTransit
/// - No stock reservation, no checkout, no payments
/// 
/// Cart Identification:
/// - If X-Customer-Id header exists → use customer cart
/// - Else → use session cart (X-Session-Id required)
/// 
/// TTL: 24 hours (reset on every write)
/// </summary>
public interface ICartService
{
    /// <summary>
    /// Gets the current cart for the owner.
    /// If cart doesn't exist, returns an empty cart structure (not null).
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner (session or customer)</param>
    /// <param name="correlationId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cart DTO (never null)</returns>
    Task<CartDto> GetCartAsync(
        Guid tenantId,
        CartOwner owner,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an item to the cart.
    /// If item already exists, increments quantity.
    /// If cart doesn't exist, creates it.
    /// AF-PA5.2: Admits line-level affiliate attribution from backend context and current product eligibility.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="request">Add item request</param>
    /// <param name="affiliateAccountId">Backend-resolved affiliate account evidence, if any</param>
    /// <param name="affiliateReferralCode">Backend-resolved referral evidence, if any</param>
    /// <param name="promotionLinkId">Backend-resolved promotion-link evidence, if any</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    Task<CartDto> AddItemAsync(
        Guid tenantId,
        CartOwner owner,
        AddCartItemRequest request,
        Guid? affiliateAccountId,
        string? affiliateReferralCode,
        Guid? promotionLinkId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy compatibility overload. A mode flag alone is not attribution evidence,
    /// so this overload performs ordinary cart admission without line attribution.
    /// </summary>
    Task<CartDto> AddItemAsync(
        Guid tenantId,
        CartOwner owner,
        AddCartItemRequest request,
        bool affiliateModeOnly,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the quantity of an item in the cart.
    /// If quantity is 0, removes the item.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="productId">Product ID to update</param>
    /// <param name="request">Update request with new quantity</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    Task<CartDto> UpdateItemAsync(
        Guid tenantId,
        CartOwner owner,
        Guid productId,
        UpdateCartItemRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the quantity of an item in the cart by VariantId.
    /// PHASE 11.7: VariantId is the ONLY identifier for cart items.
    /// If quantity is 0, removes the item.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="variantId">Variant ID to update</param>
    /// <param name="request">Update request with new quantity</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <exception cref="CartItemNotFoundException">Thrown when variant not found in cart</exception>
    /// <exception cref="VariantInsufficientStockException">Thrown when requested quantity exceeds available stock</exception>
    Task<CartDto> UpdateItemByVariantAsync(
        Guid tenantId,
        CartOwner owner,
        Guid variantId,
        UpdateCartItemRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item from the cart.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="productId">Product ID to remove</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    Task<CartDto> RemoveItemAsync(
        Guid tenantId,
        CartOwner owner,
        Guid productId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item from the cart by VariantId.
    /// PHASE 11.7: VariantId is the ONLY identifier for cart items.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="variantId">Variant ID to remove</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated cart DTO</returns>
    /// <exception cref="CartItemNotFoundException">Thrown when variant not found in cart</exception>
    Task<CartDto> RemoveItemByVariantAsync(
        Guid tenantId,
        CartOwner owner,
        Guid variantId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all items from the cart.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Empty cart DTO</returns>
    Task<CartDto> ClearCartAsync(
        Guid tenantId,
        CartOwner owner,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a guest (session) cart into a customer cart.
    /// Phase 3.3: Cart Merge (Guest → Customer)
    /// 
    /// Trigger: Called when X-Customer-Id is present AND a session cart exists.
    /// 
    /// Merge Rules:
    /// - Currency mismatch → Abort, delete guest cart, keep customer cart
    /// - Product not in customer cart → Add item
    /// - Product exists → Sum quantities (cap at 99)
    /// - Total quantity > 300 → Cap and log
    /// - Guest cart is ALWAYS deleted after merge
    /// 
    /// Idempotency: Safe to retry (no duplication if guest cart already deleted)
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="sessionOwner">Guest cart owner (session)</param>
    /// <param name="customerOwner">Customer cart owner</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged customer cart DTO</returns>
    Task<CartDto> MergeCartAsync(
        Guid tenantId,
        CartOwner sessionOwner,
        CartOwner customerOwner,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the cart with an applied coupon code.
    /// Phase C2: Coupon Integration
    /// 
    /// Only stores the coupon code in Redis.
    /// Discount is recalculated on every cart read.
    /// Pass null to remove the coupon.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="owner">Cart owner</param>
    /// <param name="couponCode">Coupon code to apply (null to remove)</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateCartWithCouponAsync(
        Guid tenantId,
        CartOwner owner,
        string? couponCode,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the full cart state to Redis including discount info.
    /// PHASE: Shopify-grade Coupon Engine
    /// 
    /// This method is used after coupon validation to persist the complete cart
    /// with DiscountTotal and AppliedCoupon so checkout can read the discount
    /// directly from Redis without revalidating.
    /// </summary>
    /// <param name="cart">Full cart DTO with discount applied</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveCartAsync(
        CartDto cart,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the checkout reservation fields on the authoritative Redis cart.
    /// Checkout is a client of this operation and may not read or rewrite the cart
    /// document directly.
    /// </summary>
    Task SetActiveCheckoutIdempotencyAsync(
        Guid tenantId,
        CartOwner owner,
        Guid cartId,
        string checkoutIdempotencyKey,
        string snapshotHash,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an order draft to the cart (adds items).
    /// </summary>
    Task<CartDto> ApplyDraftAsync(
        Guid tenantId,
        CartOwner owner,
        ApplyOrderDraftRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

