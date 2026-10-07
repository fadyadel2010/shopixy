using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs.Cart;

/// <summary>
/// Cart DTO for storefront cart operations.
/// Redis-first, TTL-based ephemeral cart.
/// 
/// Design:
/// - Frontend-ready (camelCase serialization)
/// - Tenant-scoped for multi-tenant isolation
/// - Owner can be session (guest) or customer (authenticated)
/// 
/// Phase C2: Coupon Integration
/// - AppliedCouponCode stored in Redis (just the code)
/// - AppliedCoupon populated on read (full discount details)
/// - Coupon revalidated on every cart read/mutation
/// </summary>
public class CartDto
{
    /// <summary>
    /// Unique cart identifier
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Tenant that owns this cart
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Owner type: "session" (guest) or "customer" (authenticated)
    /// </summary>
    public string OwnerType { get; set; } = "session";

    /// <summary>
    /// Owner identifier (session ID or customer ID)
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Items in the cart
    /// </summary>
    public List<CartItemDto> Items { get; set; } = new();

    /// <summary>
    /// Subtotal before discounts and taxes
    /// </summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Total discount amount (from coupon)
    /// </summary>
    public decimal DiscountTotal { get; set; }

    /// <summary>
    /// Total tax amount
    /// </summary>
    public decimal TaxTotal { get; set; }

    /// <summary>
    /// Grand total (Subtotal - DiscountTotal + TaxTotal)
    /// </summary>
    public decimal GrandTotal { get; set; }

    /// <summary>
    /// Currency code (e.g., "USD", "SAR")
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// When the cart was last updated
    /// </summary>
    public DateTime LastUpdatedUtc { get; set; }

    /// <summary>
    /// When the cart will expire (TTL)
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Applied coupon code (stored in Redis).
    /// Only the code is persisted - discount is recalculated every time.
    /// </summary>
    public string? AppliedCouponCode { get; set; }

    /// <summary>
    /// Applied coupon details (populated on read, not stored in Redis).
    /// Contains calculated discount for the current cart state.
    /// Null if no coupon is applied.
    /// </summary>
    public AppliedCouponInfo? AppliedCoupon { get; set; }

    /// <summary>
    /// Applied cart-level promotion (populated on read, not stored in Redis).
    /// This is an auto-applied promotion that applies to the entire cart.
    /// Null if no cart promotion is applicable.
    /// </summary>
    public AppliedCartPromotionInfo? AppliedCartPromotion { get; set; }

    /// <summary>
    /// Server-generated active checkout idempotency key for this cart/session.
    /// Checkout owns this value; any material cart mutation clears it so the
    /// next checkout attempt derives a new key from the latest cart state.
    /// </summary>
    public string? ActiveCheckoutIdempotencyKey { get; set; }

    /// <summary>
    /// Canonical snapshot hash that produced ActiveCheckoutIdempotencyKey.
    /// Stored only to make replay/debug behavior auditable across retries.
    /// </summary>
    public string? ActiveCheckoutSnapshotHash { get; set; }

    /// <summary>
    /// Cart-level promotion discount amount.
    /// Calculated from AppliedCartPromotion.
    /// </summary>
    public decimal CartPromotionDiscountTotal { get; set; }

    /// <summary>
    /// Number of items in cart
    /// </summary>
    public int ItemCount => Items.Sum(i => i.Quantity);

    /// <summary>
    /// Total weight of all items in cart (in kg).
    /// Used for weight-based shipping calculations.
    /// </summary>
    public decimal TotalWeight { get; set; }

    /// <summary>
    /// PHASE: Promotions Engine - Total promotion savings across all items.
    /// Calculated from item-level LineDiscount values.
    /// </summary>
    public decimal PromotionDiscountTotal => Items.Sum(i => i.LineDiscount);

    /// <summary>
    /// Total discount (item promotion + cart promotion + coupon)
    /// </summary>
    public decimal TotalDiscount => PromotionDiscountTotal + CartPromotionDiscountTotal + DiscountTotal;

    // ═══════════════════════════════════════════════════════════════════════
    // AF-27A: ANTI-TAMPERING AFFILIATE ATTRIBUTION (stored in Redis)
    //
    // When the funnel endpoint (POST /api/storefront/funnel/start) resolves
    // an affiliate, it stamps these fields directly on the cart in Redis.
    //
    // CheckoutService reads these as PRIORITY 0 (trusted, server-stamped)
    // before falling back to HttpContext or request DTO ?ref= param.
    //
    // This eliminates ref-hijacking via URL manipulation.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-27A: Server-stamped affiliate account ID.
    /// Set by funnel endpoint, read by checkout. Null if not funnel-originated.
    /// </summary>
    public Guid? AffiliateAccountId { get; set; }

    /// <summary>
    /// AF-27A: Server-stamped affiliate referral code.
    /// Snapshot from affiliate resolution at funnel time.
    /// </summary>
    public string? AffiliateReferralCode { get; set; }

    /// <summary>
    /// AF-27A: Source of affiliate attribution.
    /// "Funnel" = AF-27 funnel endpoint (trusted, server-stamped)
    /// null = not funnel-originated (use normal AF-18 flow)
    /// </summary>
    public string? AffiliateSource { get; set; }

    /// <summary>
    /// Recalculates cart totals based on items.
    /// Call ApplyCouponDiscount() after this to apply coupon discount.
    /// Call ApplyCartPromotion() after this to apply cart-level promotion.
    /// </summary>
    public void RecalculateTotals()
    {
        // Subtotal is based on original prices (before promotion)
        Subtotal = Items.Sum(i => i.UnitPrice * i.Quantity);
        // Calculate total weight for shipping
        TotalWeight = Items.Sum(i => i.Weight * i.Quantity);
        // Discount is applied separately via ApplyCouponDiscount()
        // This preserves the base subtotal for coupon recalculation
        if (AppliedCoupon == null)
        {
            DiscountTotal = 0;
        }
        if (AppliedCartPromotion == null)
        {
            CartPromotionDiscountTotal = 0;
        }
        TaxTotal = 0;
        // Grand total includes item promotions, cart promotions, and coupon discounts
        // PromotionDiscountTotal is calculated from item-level discounts
        GrandTotal = Subtotal - PromotionDiscountTotal - CartPromotionDiscountTotal - DiscountTotal + TaxTotal;
        LastUpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Applies coupon discount to the cart totals.
    /// Must be called after RecalculateTotals().
    /// </summary>
    /// <param name="couponInfo">Coupon info with calculated discount, or null to clear</param>
    public void ApplyCouponDiscount(AppliedCouponInfo? couponInfo)
    {
        AppliedCoupon = couponInfo;
        
        if (couponInfo != null)
        {
            AppliedCouponCode = couponInfo.Code;
            DiscountTotal = couponInfo.Amount;
        }
        else
        {
            AppliedCouponCode = null;
            DiscountTotal = 0;
        }
        
        GrandTotal = Subtotal - PromotionDiscountTotal - CartPromotionDiscountTotal - DiscountTotal + TaxTotal;
        LastUpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Applies a cart-level promotion to the cart totals.
    /// Must be called after RecalculateTotals().
    /// </summary>
    /// <param name="promoInfo">Cart promotion info with calculated discount, or null to clear</param>
    public void ApplyCartPromotion(AppliedCartPromotionInfo? promoInfo)
    {
        AppliedCartPromotion = promoInfo;
        
        if (promoInfo != null)
        {
            CartPromotionDiscountTotal = promoInfo.DiscountAmount;
        }
        else
        {
            CartPromotionDiscountTotal = 0;
        }
        
        GrandTotal = Subtotal - PromotionDiscountTotal - CartPromotionDiscountTotal - DiscountTotal + TaxTotal;
        LastUpdatedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the applied coupon from the cart
    /// </summary>
    public void ClearCoupon()
    {
        AppliedCouponCode = null;
        AppliedCoupon = null;
        DiscountTotal = 0;
        GrandTotal = Subtotal - PromotionDiscountTotal - CartPromotionDiscountTotal - DiscountTotal + TaxTotal;
        LastUpdatedUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Applied coupon information for cart response.
/// This is NOT stored in Redis - calculated on every read.
/// </summary>
public class AppliedCouponInfo
{
    /// <summary>
    /// Coupon ID (for checkout validation and consumption).
    /// PHASE: Shopify-grade Coupon Engine
    /// </summary>
    public Guid CouponId { get; set; }
    
    /// <summary>
    /// Applied coupon code
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Discount value (percentage or fixed amount)
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// Calculated discount amount for current cart
    /// </summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Applied cart-level promotion information for cart response.
/// This is NOT stored in Redis - calculated on every read.
/// Auto-applied promotions with Scope = Cart.
/// </summary>
public class AppliedCartPromotionInfo
{
    /// <summary>
    /// Promotion ID (for tracking and consumption)
    /// </summary>
    public Guid PromotionId { get; set; }
    
    /// <summary>
    /// Promotion name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Promotion description (optional)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>
    /// Discount value (percentage or fixed amount)
    /// </summary>
    public decimal DiscountValue { get; set; }

    /// <summary>
    /// Calculated discount amount for current cart
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Display badge (e.g., "10% OFF entire cart!")
    /// </summary>
    public string Badge { get; set; } = string.Empty;
}

/// <summary>
/// Variant attribute for cart display.
/// Shows selected options like "Color: Red, Size: XL".
/// 
/// PHASE 11.3 — STOREFRONT VARIANT SELECTION
/// </summary>
public class CartVariantAttribute
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Cart item DTO representing a sellable product variant in the cart.
/// Contains snapshot pricing at time of add.
/// 
/// PHASE 11.3 — STOREFRONT VARIANT SELECTION:
/// - VariantId is the primary key for pricing and stock
/// - ProductId is kept for product grouping and display
/// - VariantAttributes shows selected options in cart UI
/// </summary>
public class CartItemDto
{
    /// <summary>
    /// Product ID reference (for product grouping and display).
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Variant ID reference (for pricing and stock).
    /// PHASE 11.3: This is the primary key for cart items.
    /// Persisted for both simple products (their canonical default variant) and
    /// products that require variant selection. Nullable only for migration of
    /// Redis carts written by older application versions.
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Product/Variant SKU (snapshot).
    /// </summary>
    public string SKU { get; set; } = string.Empty;

    /// <summary>
    /// Product name (snapshot for display).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Product name in Arabic (snapshot).
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// Quantity in cart (1-99).
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Unit price snapshot at time of add.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Line total (UnitPrice * Quantity).
    /// </summary>
    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>
    /// Current canonical image URL, refreshed from SQL-composed media on cart read.
    /// Simple products use primary media; variable products use the selected
    /// variant's MediaId when present, otherwise product primary media.
    /// </summary>
    public string? ThumbnailImage { get; set; }

    /// <summary>
    /// When the item was added to cart.
    /// </summary>
    public DateTime AddedAtUtc { get; set; }

    /// <summary>
    /// Weight per unit in kg (for shipping calculations).
    /// Snapshot from product/variant at time of add.
    /// </summary>
    public decimal Weight { get; set; }

    /// <summary>
    /// Selected variant attributes for display.
    /// Example: [{ Name: "Color", Value: "Red" }, { Name: "Size", Value: "XL" }]
    /// Used to show "Red / XL" in cart UI.
    /// PHASE 11.3 — Shopify-grade cart display.
    /// </summary>
    public List<CartVariantAttribute> VariantAttributes { get; set; } = new();

    /// <summary>
    /// PHASE 11.7.1: Available stock for this variant.
    /// Used by frontend to disable + button when quantity >= availableStock.
    /// Null if stock not tracked or not loaded.
    /// </summary>
    public int? AvailableStock { get; set; }

    /// <summary>
    /// Backend-admitted affiliate eligibility for this line at the last cart admission pass.
    /// This is calculated per line from current backend product/policy/category/price/stock authority.
    /// </summary>
    public bool IsAffiliateEligibleLine { get; set; }

    /// <summary>
    /// Backend-admitted affiliate account for this line, when the line independently qualifies.
    /// Null for ordinary or currently non-eligible cart lines in mixed carts.
    /// </summary>
    public Guid? AffiliateAccountId { get; set; }

    /// <summary>
    /// Backend-admitted referral code for this line, when the line independently qualifies.
    /// Null for ordinary or currently non-eligible cart lines in mixed carts.
    /// </summary>
    public string? AffiliateReferralCode { get; set; }

    /// <summary>
    /// Backend-admitted promotion-link evidence for this line, when available and current.
    /// Null for ordinary or currently non-eligible cart lines in mixed carts.
    /// </summary>
    public Guid? PromotionLinkId { get; set; }

    // =======================================================================
    // PROMOTIONS ENGINE: Display fields for cart item promotions
    // =======================================================================

    /// <summary>
    /// Whether this product has an active promotion.
    /// </summary>
    public bool HasActivePromotion { get; set; }

    /// <summary>
    /// Promotion badge to display (e.g., "10% OFF").
    /// </summary>
    public string? PromotionBadge { get; set; }

    /// <summary>
    /// Promotion discount type: "percentage" or "fixed".
    /// </summary>
    public string? PromotionDiscountType { get; set; }

    /// <summary>
    /// Promotion discount value (percentage or fixed amount).
    /// </summary>
    public decimal? PromotionDiscountValue { get; set; }

    /// <summary>
    /// Discounted unit price after promotion (if applicable).
    /// Null if no promotion is active.
    /// </summary>
    public decimal? DiscountedUnitPrice { get; set; }

    /// <summary>
    /// Line discount amount (savings on this item).
    /// Calculated as: (UnitPrice - DiscountedUnitPrice) * Quantity
    /// </summary>
    public decimal LineDiscount => HasActivePromotion && DiscountedUnitPrice.HasValue 
        ? (UnitPrice - DiscountedUnitPrice.Value) * Quantity 
        : 0;

    /// <summary>
    /// Discounted line total (after promotion).
    /// </summary>
    public decimal DiscountedLineTotal => HasActivePromotion && DiscountedUnitPrice.HasValue
        ? DiscountedUnitPrice.Value * Quantity
        : LineTotal;
}

/// <summary>
/// Request DTO for adding item to cart.
/// 
/// PHASE 11.3 — VARIANT SELECTION:
/// - ProductId identifies the product whose sellable SKU must be resolved
/// - VariantId is required for products with selectable variants
/// - VariantId is optional for simple-product requests and, when supplied, must
///   match the server-resolved canonical variant
/// </summary>
public class AddCartItemRequest
{
    /// <summary>
    /// Product ID whose sellable SKU is being added.
    /// </summary>
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Selected variant ID. Required for products with selectable variants.
    /// Optional for simple products, where the server resolves and persists the
    /// canonical default variant and rejects a mismatched supplied value.
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// Product SKU (optional, for validation).
    /// </summary>
    [StringLength(128)]
    public string? SKU { get; set; }

    /// <summary>
    /// Quantity to add (1-99).
    /// </summary>
    [Range(1, 99)]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Validates the request.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (VariantId == null && ProductId == Guid.Empty)
        {
            return ("INVALID_REQUEST", "Either VariantId or ProductId is required");
        }

        // Quantity range validation is handled by [Range(1, 99)] attribute
        return (null, null);
    }
}

/// <summary>
/// Request DTO for updating cart item quantity
/// </summary>
public class UpdateCartItemRequest
{
    /// <summary>
    /// New quantity (0 to remove, 1-99 to update)
    /// </summary>
    [Range(0, 99)]
    public int Quantity { get; set; }

    /// <summary>
    /// Validates the request
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (Quantity < 0)
        {
            return ("INVALID_QUANTITY", "Quantity cannot be negative");
        }

        if (Quantity > 99)
        {
            return ("INVALID_QUANTITY", "Maximum quantity is 99");
        }

        return (null, null);
    }
}

/// <summary>
/// Cart owner identification from headers
/// </summary>
public class CartOwner
{
    /// <summary>
    /// Owner type: "session" or "customer"
    /// </summary>
    public string OwnerType { get; set; } = "session";

    /// <summary>
    /// Owner identifier
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a customer cart
    /// </summary>
    public bool IsCustomer => OwnerType == "customer";
}
