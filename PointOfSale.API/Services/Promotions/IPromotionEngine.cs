namespace PointOfSale.API.Services.Promotions;

/// <summary>
/// Interface for the unified Promotion Engine.
/// 
/// ARCHITECTURE (SHOPIFY-GRADE PROMOTIONS ENGINE):
/// - Coupons are code-based promotions (existing system)
/// - Offers are auto-applied promotions (new functionality)
/// - ONE engine calculates ALL discounts
/// - Checkout is the FINAL AUTHORITY (server-side enforcement)
/// 
/// CRITICAL RULES:
/// 1. PromotionEngine is CALLED AT CHECKOUT ONLY
/// 2. Cart shows preview discounts but checkout recalculates
/// 3. No frontend trust - all validation is server-side
/// 4. TenantId is MANDATORY on all operations
/// 5. Coupon + Offer stacking is NOT supported (highest priority wins)
/// </summary>
public interface IPromotionEngine
{
    /// <summary>
    /// Calculate the best applicable promotion for a cart.
    /// 
    /// LOGIC:
    /// 1. Load active offers for tenant (date + active filter)
    /// 2. If coupon is applied, load and validate it
    /// 3. Filter offers by scope (product/category/cart)
    /// 4. Resolve conflicts using Priority
    /// 5. Return the winning promotion (offer OR coupon, not both)
    /// 
    /// NO STACKING: If both offer and coupon apply, highest priority/value wins.
    /// </summary>
    /// <param name="cart">Cart snapshot for calculation</param>
    /// <param name="now">Current time for date validation</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The winning promotion result (or empty if none apply)</returns>
    Task<PromotionResult> CalculateAsync(
        CartSnapshotForPromotion cart,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all active offers that apply to specific products.
    /// Used for storefront product display (badges, discounted prices).
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="productIds">Product IDs to check</param>
    /// <param name="now">Current time for date validation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of ProductId → active offer details</returns>
    Task<Dictionary<Guid, ActiveOfferInfo>> GetActiveOffersForProductsAsync(
        Guid tenantId,
        IEnumerable<Guid> productIds,
        DateTime now,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all active offers that apply to specific categories.
    /// Used for storefront category display (banners).
    /// </summary>
    Task<Dictionary<Guid, ActiveOfferInfo>> GetActiveOffersForCategoriesAsync(
        Guid tenantId,
        IEnumerable<Guid> categoryIds,
        DateTime now,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a coupon can be combined with current offers.
    /// For future use when stacking is introduced.
    /// </summary>
    Task<bool> CanCombineCouponWithOffersAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the best active cart-level promotion for a tenant.
    /// Used for auto-applying cart promotions in the cart/checkout flow.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="subtotal">Current cart subtotal (for MinOrderAmount validation)</param>
    /// <param name="now">Current time for date validation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Best cart promotion or null if none apply</returns>
    Task<CartPromotionResult?> GetActiveCartPromotionAsync(
        Guid tenantId,
        decimal subtotal,
        DateTime now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about an active offer for display purposes
/// </summary>
public class ActiveOfferInfo
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DiscountType { get; set; } = "percentage";
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public DateTime EndDate { get; set; }
    public int Priority { get; set; }
    
    /// <summary>
    /// Badge text for display (e.g., "20% OFF", "Save $10")
    /// </summary>
    public string Badge => DiscountType.Equals("percentage", StringComparison.OrdinalIgnoreCase)
        ? $"{DiscountValue}% OFF"
        : $"Save ${DiscountValue}";
}

/// <summary>
/// Result of cart-level promotion lookup
/// </summary>
public class CartPromotionResult
{
    public Guid PromotionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "percentage";
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public DateTime EndDate { get; set; }
    public int Priority { get; set; }
    
    /// <summary>
    /// Calculated discount amount for the given subtotal
    /// </summary>
    public decimal CalculatedDiscount { get; set; }
    
    /// <summary>
    /// Badge text for display
    /// </summary>
    public string Badge => DiscountType.Equals("percentage", StringComparison.OrdinalIgnoreCase)
        ? $"{DiscountValue}% OFF"
        : $"Save ${DiscountValue}";
}
