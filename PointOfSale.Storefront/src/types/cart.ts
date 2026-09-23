/**
 * Cart Types
 * 
 * Type definitions for cart data structures.
 * Matches backend Cart API responses.
 * 
 * Phase C2: Added coupon support
 * Phase 11.3: Added variant support
 */

/**
 * Variant attribute for cart display.
 * Shows selected options like "Color: Red, Size: XL".
 * 
 * PHASE 11.3 — STOREFRONT VARIANT SELECTION
 */
export interface CartVariantAttribute {
    name: string;
    value: string;
}

/**
 * Cart item in the cart
 * 
 * PHASE 11.3 — VARIANT SELECTION:
 * - variantId is the primary key for pricing and stock
 * - productId is kept for product grouping and display
 * - variantAttributes shows selected options in cart UI
 */
export interface CartItemDto {
    /** Product ID for grouping and display */
    productId: string;
    /** Canonical sellable SKU ID for simple and variant products */
    variantId?: string;
    productName: string;
    productNameAr?: string;
    sku: string;
    unitPrice: number;
    quantity: number;
    lineTotal: number;
    imageUrl?: string;
    maxQuantity?: number;
    /** 
     * PHASE 11.7.1: Available stock for this variant
     * Used to disable + button when quantity >= availableStock
     */
    availableStock?: number;
    /** Selected variant attributes for display (e.g., "Red / XL") */
    variantAttributes?: CartVariantAttribute[];

    // =======================================================================
    // PROMOTIONS ENGINE: Display fields for cart item promotions
    // =======================================================================

    /** Whether this product has an active promotion */
    hasActivePromotion?: boolean;
    /** Promotion badge to display (e.g., "10% OFF") */
    promotionBadge?: string;
    /** Promotion discount type: "percentage" or "fixed" */
    promotionDiscountType?: string;
    /** Promotion discount value */
    promotionDiscountValue?: number;
    /** Discounted unit price after promotion */
    discountedUnitPrice?: number;
    /** Line discount amount (savings on this item) */
    lineDiscount?: number;
    /** Discounted line total (after promotion) */
    discountedLineTotal?: number;
}

/**
 * Applied coupon information
 * Populated on every cart read (not stored in Redis)
 */
export interface AppliedCouponInfo {
    /** Coupon code */
    code: string;
    /** Discount type: "percentage" or "fixed" */
    type: 'percentage' | 'fixed';
    /** Discount value (percentage or fixed amount) */
    value: number;
    /** Calculated discount amount for current cart */
    amount: number;
}

/**
 * Applied cart-level promotion information
 * Populated on every cart read (not stored in Redis)
 * Auto-applied promotions with Scope = Cart
 */
export interface AppliedCartPromotionInfo {
    /** Promotion ID */
    promotionId: string;
    /** Promotion name for display */
    name: string;
    /** Promotion description */
    description?: string;
    /** Discount type: "percentage" or "fixed" */
    discountType: string;
    /** Discount value (percentage or fixed amount) */
    discountValue: number;
    /** Calculated discount amount for current cart */
    discountAmount: number;
    /** Badge text (e.g., "10% OFF") */
    badge: string;
}

/**
 * Full cart response
 */
export interface CartDto {
    cartId: string;
    tenantId: string;
    ownerType: 'session' | 'customer';
    ownerId: string;
    items: CartItemDto[];
    itemCount: number;
    subtotal: number;
    discountTotal: number;
    taxTotal: number;
    grandTotal: number;
    currency: string;
    lastUpdatedUtc: string;
    expiresAtUtc: string;
    /** Applied coupon code (stored in Redis) */
    appliedCouponCode?: string | null;
    /** Applied coupon details (calculated on read) */
    appliedCoupon?: AppliedCouponInfo | null;

    // =======================================================================
    // PROMOTIONS ENGINE: Cart-level promotion totals
    // =======================================================================

    /** Total promotion savings across item-level promotions */
    promotionDiscountTotal?: number;
    /** Cart-level promotion discount amount */
    cartPromotionDiscountTotal?: number;
    /** Applied cart-level promotion (auto-applied) */
    appliedCartPromotion?: AppliedCartPromotionInfo | null;
    /** Total discount (item promotion + cart promotion + coupon) */
    totalDiscount?: number;
}

/**
 * Add item request
 * 
 * PHASE 11.3 — VARIANT SELECTION:
 * - variantId is the primary identifier for variant products
 * - productId is kept for backward compatibility with legacy products
 */
export interface AddCartItemRequest {
    /** Product ID (LEGACY, for backward compatibility) */
    productId: string;
    /** Variant ID (PHASE 11.3+) - takes precedence when set */
    variantId?: string;
    quantity: number;
}

/**
 * Update quantity request
 */
export interface UpdateCartItemRequest {
    quantity: number;
}


/**
 * Apply coupon request
 */
export interface ApplyCouponRequest {
    code?: string;
    smartOfferRedeemToken?: string | null;
}

/**
 * Coupon error codes for frontend error handling
 */
export const CouponErrorCodes = {
    Invalid: 'COUPON_INVALID',
    Expired: 'COUPON_EXPIRED',
    NotYetValid: 'COUPON_NOT_YET_VALID',
    Inactive: 'COUPON_INACTIVE',
    MinimumNotMet: 'COUPON_MIN_NOT_MET',
    UsageExceeded: 'COUPON_USAGE_EXCEEDED',
    CustomerUsageExceeded: 'COUPON_CUSTOMER_USAGE_EXCEEDED',
    FirstOrderOnly: 'COUPON_FIRST_ORDER_ONLY',
    NotApplicable: 'COUPON_NOT_APPLICABLE',
    SmartOfferTokenRequired: 'COUPON_SMART_OFFER_TOKEN_REQUIRED',
    SmartOfferTokenInvalid: 'COUPON_SMART_OFFER_TOKEN_INVALID',
    SmartOfferTokenExpired: 'COUPON_SMART_OFFER_TOKEN_EXPIRED',
    RemovedAutomatically: 'COUPON_REMOVED_AUTOMATICALLY',
    NotApplied: 'COUPON_NOT_APPLIED',
    CartEmpty: 'COUPON_CART_EMPTY',
} as const;

export type CouponErrorCode = typeof CouponErrorCodes[keyof typeof CouponErrorCodes];

/**
 * Variant error codes for frontend error handling
 * PHASE 11.6 — VARIANT-CENTRIC CART ERRORS
 */
export const VariantErrorCodes = {
    /** Variant selection is required but not provided */
    Required: 'VARIANT_REQUIRED',
    /** Specified variant not found in product */
    NotFound: 'VARIANT_NOT_FOUND',
    /** Variant exists but is inactive */
    Inactive: 'VARIANT_INACTIVE',
    /** Variant is out of stock */
    OutOfStock: 'VARIANT_OUT_OF_STOCK',
    /** PHASE 11.7.1: Requested quantity exceeds available stock */
    InsufficientStock: 'VARIANT_INSUFFICIENT_STOCK',
    /** PHASE 11.7.1: Cart item not found (likely stale cart) */
    CartItemNotFound: 'CART_ITEM_NOT_FOUND',
} as const;

export type VariantErrorCode = typeof VariantErrorCodes[keyof typeof VariantErrorCodes];
