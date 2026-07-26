using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Promotions;

/// <summary>
/// Result of PromotionEngine.Calculate()
/// Contains the winning promotion (offer or coupon) to apply.
/// </summary>
public class PromotionResult
{
    /// <summary>
    /// ID of the applied promotion or coupon
    /// </summary>
    public Guid? PromotionId { get; set; }
    
    /// <summary>
    /// Type of promotion: Offer (auto-applied) or Coupon (code-based)
    /// </summary>
    public PromotionType PromotionType { get; set; }
    
    /// <summary>
    /// Name of the promotion for display purposes
    /// </summary>
    public string PromotionName { get; set; } = string.Empty;
    
    /// <summary>
    /// Coupon code if PromotionType is Coupon (null for offers)
    /// </summary>
    public string? CouponCode { get; set; }
    
    /// <summary>
    /// Total discount amount to apply
    /// </summary>
    public decimal DiscountAmount { get; set; }
    
    /// <summary>
    /// Human-readable description for cart/checkout display
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Items the promotion was applied to (for item-level discounts)
    /// </summary>
    public List<PromotionAppliedToItem> AppliedToItems { get; set; } = new();
    
    /// <summary>
    /// Whether a promotion was successfully applied
    /// </summary>
    public bool HasPromotion => PromotionId.HasValue && DiscountAmount > 0;
    
    /// <summary>
    /// Alias for HasPromotion for clearer semantics in checkout
    /// </summary>
    public bool HasDiscount => HasPromotion;
    
    /// <summary>
    /// Type as string for simplified comparisons ("offer" or "coupon")
    /// </summary>
    public string PromotionTypeString => PromotionType == PromotionType.Offer ? "offer" : "coupon";
    
    /// <summary>
    /// Create an empty result (no promotion applied)
    /// </summary>
    public static PromotionResult None() => new();
    
    /// <summary>
    /// Create a result from a coupon
    /// </summary>
    public static PromotionResult FromCoupon(
        Guid couponId,
        string code,
        decimal discountAmount,
        string description,
        List<PromotionAppliedToItem>? appliedToItems = null)
    {
        return new PromotionResult
        {
            PromotionId = couponId,
            PromotionType = PromotionType.Coupon,
            PromotionName = code,
            CouponCode = code,
            DiscountAmount = discountAmount,
            Description = description,
            AppliedToItems = appliedToItems ?? new()
        };
    }
    
    /// <summary>
    /// Create a result from an offer (auto-applied promotion)
    /// </summary>
    public static PromotionResult FromOffer(
        Guid promotionId,
        string name,
        decimal discountAmount,
        string description,
        List<PromotionAppliedToItem>? appliedToItems = null)
    {
        return new PromotionResult
        {
            PromotionId = promotionId,
            PromotionType = PromotionType.Offer,
            PromotionName = name,
            CouponCode = null,
            DiscountAmount = discountAmount,
            Description = description,
            AppliedToItems = appliedToItems ?? new()
        };
    }
}

/// <summary>
/// Represents an item that a promotion was applied to
/// </summary>
public class PromotionAppliedToItem
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal OriginalPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalPrice { get; set; }
    
    /// <summary>
    /// Total discount for this line (DiscountAmount * Quantity may not equal this due to rounding)
    /// This field ensures the sum of line discounts matches PromotionResult.DiscountAmount exactly
    /// </summary>
    public decimal LineDiscountTotal { get; set; }
}

/// <summary>
/// Snapshot of a cart item for promotion calculation
/// </summary>
public class CartItemForPromotion
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public Guid? CategoryId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

/// <summary>
/// Snapshot of cart for promotion calculation
/// </summary>
public class CartSnapshotForPromotion
{
    public Guid TenantId { get; set; }
    public Guid? CustomerId { get; set; }
    public decimal Subtotal { get; set; }
    public string Currency { get; set; } = "USD";
    public List<CartItemForPromotion> Items { get; set; } = new();
    
    /// <summary>
    /// Applied coupon code (if any)
    /// </summary>
    public string? AppliedCouponCode { get; set; }
    
    /// <summary>
    /// Applied coupon ID (if any)
    /// </summary>
    public Guid? AppliedCouponId { get; set; }

    /// <summary>
    /// SQL-authoritative coupon discount amount already validated before promotion conflict resolution.
    /// </summary>
    public decimal? PrevalidatedCouponDiscountAmount { get; set; }

    /// <summary>
    /// SQL-authoritative coupon discount type for deterministic replay and display.
    /// </summary>
    public string? PrevalidatedCouponDiscountType { get; set; }

    /// <summary>
    /// SQL-authoritative coupon discount value for deterministic replay and display.
    /// </summary>
    public decimal? PrevalidatedCouponDiscountValue { get; set; }
}
