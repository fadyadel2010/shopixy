/**
 * Product Types
 * Matches backend StorefrontDtos.cs
 * 
 * PHASE 11.3 — STOREFRONT VARIANT SELECTION
 */

/**
 * Product unit DTO for storefront pricing.
 * Contains the selling price for a specific unit of measure.
 */
export interface ProductUnitDto {
    /** Unit ID reference */
    unitId: string;
    /** Unit name (e.g., "Piece", "Carton", "Box") */
    name: string;
    /** Unit symbol (e.g., "pc", "ctn", "bx") */
    symbol?: string;
    /** Selling price for this unit - THIS IS THE CORRECT PRICE */
    salePrice: number;
    /** Conversion factor relative to base unit */
    conversionFactor: number;
}

/**
 * Variant attribute for storefront variant selection.
 * 
 * PHASE 11.3 — STOREFRONT VARIANT SELECTION
 */
export interface VariantAttributeDto {
    /** Attribute name (e.g., "Color", "Size") */
    name: string;
    /** Attribute value (e.g., "Red", "XL") */
    value: string;
}

/**
 * Backend-composed product pricing presentation.
 * Storefront may format these values, but must not calculate discounts.
 */
export interface ProductPricingPresentationDto {
    currentPrice: number;
    compareAtPrice?: number | null;
    discountedPrice?: number | null;
    discountBadge?: string | null;
}

/**
 * Backend-composed product availability and purchasability presentation.
 * Storefront may render these values, but must not calculate purchase authority.
 */
export interface ProductAvailabilityPresentationDto {
    isAvailable: boolean;
    canPurchase: boolean;
    canAddToCart: boolean;
    canBuyNow: boolean;
    stockLabel: string;
    maximumQuantity?: number | null;
    unavailableReason?: string | null;
}

/**
 * Product variant DTO for storefront variant selection.
 * 
 * PHASE 11.3 — STOREFRONT VARIANT SELECTION
 * 
 * Cart and Checkout use VariantId (not ProductId) for pricing.
 */
export interface ProductVariantDto {
    /** Variant unique identifier - used for cart */
    id: string;
    /** Variant SKU */
    sku?: string;
    /** POS sale price */
    price: number;
    /** Online/Storefront price. If null or 0, use price */
    onlinePrice?: number;
    /** Computed effective price: onlinePrice if set, otherwise price */
    effectivePrice: number;
    /** Backend-composed pricing presentation for this variant */
    pricing: ProductPricingPresentationDto;
    /** Backend-composed availability and purchasability presentation for this variant */
    availability?: ProductAvailabilityPresentationDto;
    /** Product media image reference for variant switching */
    mediaId?: string;
    /** Variant-specific image URL. Falls back to product image if null */
    imageUrl?: string;
    /** Total stock quantity for this variant */
    totalStock: number;
    /** Whether this variant is in stock */
    inStock: boolean;
    /** Whether this is the default variant (auto-selected) */
    isDefault: boolean;
    /** Whether this variant is active */
    isActive: boolean;
    /** Variant attributes (Color, Size, etc.) */
    attributes: VariantAttributeDto[];
}

export interface StorefrontVariantResolutionRequest {
    selectedOptions: Record<string, string>;
}

export interface StorefrontVariantResolutionResponse {
    productId: string;
    resolvedVariantId: string;
    variant: ProductVariantDto;
}

export interface ProductMediaDto {
    /** Product media unique identifier */
    id: string;
    /** Media type: image or video */
    mediaType: string;
    /** Public media URL */
    url: string;
    /** Backend-composed media playback URL for embedded providers */
    presentationUrl?: string | null;
    /** Backend-composed thumbnail URL for video/media thumbnails */
    thumbnailUrl?: string | null;
    /** Optional provider: youtube, vimeo, mp4 */
    provider?: string;
    /** Position in the product gallery */
    position: number;
}

export interface ProductReadDto {
    id: string;
    name: string;
    nameAr?: string;
    description?: string;
    sku?: string;
    barcode?: string;
    /** LEGACY: Use effectivePrice or defaultUnit.salePrice instead */
    price: number;
    /** Default selling unit - SOURCE OF TRUTH for pricing */
    defaultUnit?: ProductUnitDto;
    /** Computed effective price: defaultUnit.salePrice if available, otherwise price */
    effectivePrice: number;
    /** Backend-composed pricing presentation for the default/current product price */
    pricing: ProductPricingPresentationDto;
    /** Backend-composed availability and purchasability presentation for this product */
    availability?: ProductAvailabilityPresentationDto;
    categoryName?: string;
    categoryId?: string;
    slug?: string;
    productTemplate?: string;
    storefrontLayout?: string;
    isFeatured?: boolean;
    media: ProductMediaDto[];
    images: string[];
    /** False if no valid price or not active/visible */
    isAvailable: boolean;
    /** Whether the product is in stock */
    inStock: boolean;
    /** Available stock quantity */
    stockQuantity: number;
    createdAtUtc: string;

    // PHASE 11.3 — VARIANT SELECTION FIELDS
    /** Product variants for storefront selection */
    variants: ProductVariantDto[];
    /** Whether this product has multiple variants (show selector if true) */
    hasVariants: boolean;
    /** Default variant ID to auto-select */
    defaultVariantId?: string;

    // PHASE: Promotions Engine - Badge Display Fields
    /** Whether this product has an active promotion (offer) */
    hasActivePromotion?: boolean;
    /** Badge text to display (e.g., "20% OFF") */
    promotionBadge?: string;
    /** Discount type: "percentage" or "fixed" */
    promotionDiscountType?: string;
    /** Discount value for calculating discounted price */
    promotionDiscountValue?: number;
}

export interface ProductListItemDto {
    id: string;
    name: string;
    nameAr?: string;
    /** LEGACY: Use effectivePrice or defaultUnit.salePrice instead */
    price: number;
    /** Default selling unit - SOURCE OF TRUTH for pricing */
    defaultUnit?: ProductUnitDto;
    /** Computed effective price: defaultUnit.salePrice if available, otherwise price */
    effectivePrice: number;
    /** Backend-composed pricing presentation for product cards */
    pricing: ProductPricingPresentationDto;
    /** Backend-composed availability and purchasability presentation for product cards */
    availability?: ProductAvailabilityPresentationDto;
    categoryName?: string;
    slug?: string;
    thumbnailImage?: string;
    productTemplate?: string;
    storefrontLayout?: string;
    isFeatured?: boolean;
    /** False if no valid price or not active/visible */
    isAvailable: boolean;
    /** Whether the product is in stock */
    inStock: boolean;
    /** Available stock quantity */
    stockQuantity: number;

    // PHASE: Variant-Aware Pricing
    /**
     * Whether this product has multiple variants.
     * 
     * CRITICAL: If true, displayPriceText should be rendered instead of price.
     */
    hasVariants?: boolean;
    /**
     * Display text for variant products (e.g., "Multiple options" or "متعدد الاختيارات").
     * 
     * CRITICAL: Always render this if present, ignore price field.
     */
    displayPriceText?: string;

    // PHASE: Promotions Engine - Badge Display Fields
    /** Whether this product has an active promotion (offer) */
    hasActivePromotion?: boolean;
    /** Badge text to display (e.g., "20% OFF") */
    promotionBadge?: string;
    /** Discount type: "percentage" or "fixed" */
    promotionDiscountType?: string;
    /** Discount value for calculating discounted price */
    promotionDiscountValue?: number;
}

/**
 * Paginated result wrapper
 */
export interface PagedResult<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    hasNextPage: boolean;
    hasPreviousPage: boolean;
}

