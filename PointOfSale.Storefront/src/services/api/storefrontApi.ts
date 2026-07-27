import { httpClient } from './httpClient';
import { ENDPOINTS } from './endpoints';
import type {
    StoreConfig,
    ProductReadDto,
    ProductListItemDto,
    StorefrontVariantResolutionRequest,
    StorefrontVariantResolutionResponse,
    PagedResult,
    CategoryTreeDto,
    CartDto,
    AddCartItemRequest,
    UpdateCartItemRequest,
    ApplyCouponRequest,
    ProductDiscoveryQuery,
    ProductDiscoveryResult,
    SmartOffer,
    RecoveredCheckoutResponse,
    RecoveredCartResponse,
} from '@/types';

/**
 * Storefront API Service
 * 
 * READ-ONLY APIs for products/categories
 * READ-WRITE APIs for cart
 * All requests include request-scoped session/auth headers via httpClient
 */

// ============================================================
// Store Config
// ============================================================

/**
 * Fetch store configuration
 */
export const fetchStoreConfig = async (): Promise<StoreConfig> => {
    const previewToken = typeof window === 'undefined'
        ? null
        : new URLSearchParams(window.location.search).get('themePreviewToken');
    const response = await httpClient.get<StoreConfig>(
        previewToken ? ENDPOINTS.THEME_PREVIEW_CONFIG : ENDPOINTS.STORE_CONFIG,
        previewToken ? { params: { themePreviewToken: previewToken } } : undefined
    );
    return response.data;
};

/**
 * Fetch active smart offers for the current storefront tenant.
 */
export const fetchSmartOffers = async (): Promise<SmartOffer[]> => {
    const response = await httpClient.get<SmartOffer[]>(ENDPOINTS.SMART_OFFERS);
    return response.data;
};

export interface SmartOfferRedeemResponse {
    redeemToken: string;
    expiresAtUtc: string;
}

export const redeemSmartOffer = async (smartOfferId: string): Promise<SmartOfferRedeemResponse> => {
    const response = await httpClient.post<SmartOfferRedeemResponse>(
        `${ENDPOINTS.SMART_OFFERS}/${encodeURIComponent(smartOfferId)}/redeem`,
        {}
    );
    return response.data;
};

/**
 * Fetch featured homepage products for the active storefront tenant.
 */
export const fetchHomepageFeaturedProducts = async (): Promise<ProductListItemDto[]> => {
    const response = await httpClient.get<ProductListItemDto[]>(ENDPOINTS.HOME_FEATURED_PRODUCTS);
    return response.data;
};

/**
 * Fetch curated homepage products in the exact ProductId order requested.
 */
export const fetchHomepageProductsByIds = async (
    productIds: string[]
): Promise<ProductListItemDto[]> => {
    const params = new URLSearchParams();
    productIds.forEach((productId) => params.append('productIds', productId));

    const response = await httpClient.get<ProductListItemDto[]>(
        `${ENDPOINTS.HOME_PRODUCTS_BY_IDS}?${params.toString()}`
    );

    return response.data;
};

// ============================================================
// Products
// ============================================================

/**
 * Fetch paginated product list
 */
export const fetchProducts = async (
    page: number = 1,
    pageSize: number = 20,
    categoryId?: string
): Promise<PagedResult<ProductListItemDto>> => {
    const params: Record<string, string | number> = { page, pageSize };
    if (categoryId) {
        params.categoryId = categoryId;
    }

    const response = await httpClient.get<PagedResult<ProductListItemDto>>(
        ENDPOINTS.PRODUCTS,
        { params }
    );
    return response.data;
};

/**
 * Fetch products via Elasticsearch-powered discovery.
 * 
 * PHASE: Shopify-Grade Product Discovery
 * 
 * This is the RECOMMENDED way to fetch product listings.
 * Uses Elasticsearch for search, filtering, sorting, and faceted navigation.
 * 
 * @param query - Discovery query parameters
 * @returns ProductDiscoveryResult with items, pagination, and facets
 */
export const fetchProductsDiscover = async (
    query: ProductDiscoveryQuery = {}
): Promise<ProductDiscoveryResult> => {
    // Build URLSearchParams manually to properly serialize arrays for ASP.NET
    // ASP.NET expects: ?category=a&category=b (NOT ?category[0]=a&category[1]=b)
    const params = new URLSearchParams();

    if (query.q) params.append('q', query.q);

    // Serialize arrays as repeated keys for ASP.NET
    if (query.category?.length) {
        query.category.forEach(cat => params.append('category', cat));
    }
    if (query.brand?.length) {
        query.brand.forEach(b => params.append('brand', b));
    }

    if (query.priceFrom !== undefined) params.append('priceFrom', query.priceFrom.toString());
    if (query.priceTo !== undefined) params.append('priceTo', query.priceTo.toString());
    if (query.hasPromotion !== undefined) params.append('hasPromotion', query.hasPromotion.toString());
    if (query.inStock !== undefined) params.append('inStock', query.inStock.toString());
    if (query.hasVariants !== undefined) params.append('hasVariants', query.hasVariants.toString());
    if (query.sort) params.append('sort', query.sort);
    if (query.page) params.append('page', query.page.toString());
    if (query.pageSize) params.append('pageSize', query.pageSize.toString());
    if (query.lang) params.append('lang', query.lang);

    const response = await httpClient.get<ProductDiscoveryResult>(
        `${ENDPOINTS.PRODUCTS_DISCOVER}?${params.toString()}`
    );
    return response.data;
};

/**
 * Fetch single product by slug
 */
export const fetchProductBySlug = async (slug: string): Promise<ProductReadDto> => {
    const response = await httpClient.get<ProductReadDto>(
        ENDPOINTS.PRODUCT_BY_SLUG(slug)
    );
    return response.data;
};

export const resolveProductVariant = async (
    productId: string,
    request: StorefrontVariantResolutionRequest,
    signal?: AbortSignal
): Promise<StorefrontVariantResolutionResponse> => {
    const response = await httpClient.post<StorefrontVariantResolutionResponse>(
        ENDPOINTS.PRODUCT_VARIANT_RESOLUTION(productId),
        request,
        { signal }
    );
    return response.data;
};

// ============================================================
// Categories
// ============================================================

/**
 * Fetch category tree
 */
export const fetchCategories = async (): Promise<CategoryTreeDto[]> => {
    const response = await httpClient.get<CategoryTreeDto[]>(ENDPOINTS.CATEGORIES);
    return response.data;
};

// ============================================================
// Cart
// ============================================================

/**
 * Fetch current cart
 */
export const fetchCart = async (): Promise<CartDto> => {
    const response = await httpClient.get<CartDto>(ENDPOINTS.CART);
    return response.data;
};

/**
 * Recover a saved checkout session from a recovery token.
 */
export const recoverCheckout = async (token: string): Promise<RecoveredCheckoutResponse> => {
    const response = await httpClient.get<RecoveredCheckoutResponse>(
        `/api/storefront/checkout-session/recover/${encodeURIComponent(token)}`
    );
    return response.data;
};

/**
 * Recover an abandoned cart from a signed recovery token.
 */
export const recoverCart = async (token: string): Promise<RecoveredCartResponse> => {
    const response = await httpClient.get<RecoveredCartResponse>(
        `/api/storefront/cart/recover/${encodeURIComponent(token)}`
    );
    return response.data;
};

/**
 * Add item to cart
 */
export const addCartItem = async (request: AddCartItemRequest): Promise<CartDto> => {
    const response = await httpClient.post<CartDto>(ENDPOINTS.CART_ITEMS, request);
    return response.data;
};

/**
 * Update cart item quantity
 */
export const updateCartItem = async (
    productId: string,
    request: UpdateCartItemRequest
): Promise<CartDto> => {
    const response = await httpClient.put<CartDto>(
        ENDPOINTS.CART_ITEM(productId),
        request
    );
    return response.data;
};

/**
 * Remove item from cart
 */
export const removeCartItem = async (productId: string): Promise<CartDto> => {
    const response = await httpClient.delete<CartDto>(ENDPOINTS.CART_ITEM(productId));
    return response.data;
};

// ============================================================
// PHASE 11.7 — Variant-Centric Cart Operations
// ============================================================

/**
 * Update cart item quantity by VariantId.
 * PHASE 11.7: VariantId is the ONLY identifier for cart items.
 * @param variantId Variant ID to update
 * @param request Update request with quantity
 * @returns Updated cart
 * @throws CART_ITEM_NOT_FOUND if variant not in cart
 * @throws VARIANT_INSUFFICIENT_STOCK if quantity exceeds available stock
 */
export const updateCartItemByVariant = async (
    variantId: string,
    request: UpdateCartItemRequest
): Promise<CartDto> => {
    const response = await httpClient.put<CartDto>(
        ENDPOINTS.CART_ITEM_BY_VARIANT(variantId),
        request
    );
    return response.data;
};

/**
 * Remove cart item by VariantId.
 * PHASE 11.7: VariantId is the ONLY identifier for cart items.
 * @param variantId Variant ID to remove
 * @returns Updated cart
 * @throws CART_ITEM_NOT_FOUND if variant not in cart
 */
export const removeCartItemByVariant = async (variantId: string): Promise<CartDto> => {
    const response = await httpClient.delete<CartDto>(ENDPOINTS.CART_ITEM_BY_VARIANT(variantId));
    return response.data;
};

/**
 * Clear entire cart
 */
export const clearCart = async (): Promise<void> => {
    await httpClient.delete(ENDPOINTS.CART);
};

/**
 * Merge session cart into customer cart on login
 * 
 * PHASE 10.4: Cart Ownership Migration
 * 
 * Call this after login to transfer guest cart items to customer account.
 * The session cart is deleted after successful merge.
 * 
 * IMPORTANT: This must be called AFTER setCustomerId() has been called in requestContext
 * so that httpClient includes X-Customer-Id header automatically.
 * 
 * @returns Merged cart with customer ownership
 */
export const mergeCart = async (): Promise<CartDto> => {
    // httpClient interceptor automatically includes:
    // - X-Session-Id (from sessionManager)
    // - X-Customer-Id (from requestContext, set in authStore.setAuthenticated)
    const response = await httpClient.post<CartDto>(ENDPOINTS.CART_MERGE, null);
    return response.data;
};

// ============================================================
// Coupons (Phase C2)
// ============================================================

/**
 * Apply coupon to cart
 * @param code Optional coupon code to apply. Can be omitted when using smartOfferRedeemToken for token-only smart-offer flows.
 * @param smartOfferRedeemToken Optional smart offer redemption token. Used as an alternative redemption mechanism when code is omitted (e.g., applyCoupon(null, smartOfferRedeemToken)).
 * @returns Updated cart with applied coupon and calculated discount
 * @throws Error with coupon-specific error code
 * 
 * Maps to ApplyCouponRequest payload:
 * - code (string | undefined): The coupon code, or undefined if not provided
 * - smartOfferRedeemToken (string | null): The smart offer token for alternative redemption
 */
export const applyCoupon = async (code?: string | null, smartOfferRedeemToken?: string | null): Promise<CartDto> => {
    const request: ApplyCouponRequest = {
        code: code?.trim() || undefined,
        smartOfferRedeemToken: smartOfferRedeemToken?.trim() || null,
    };
    const response = await httpClient.post<CartDto>(ENDPOINTS.CART_APPLY_COUPON, request);
    return response.data;
};

/**
 * Remove coupon from cart
 * @returns Updated cart without coupon
 */
export const removeCoupon = async (): Promise<CartDto> => {
    const response = await httpClient.post<CartDto>(ENDPOINTS.CART_REMOVE_COUPON, {});
    return response.data;
};

// ============================================================
// CommerceAI (Phase 7.9)
// ============================================================

import type { OrderDraft } from '@/types/chat';

/**
 * Apply order draft from AI assistant to cart
 */
export const applyOrderDraft = async (draft: OrderDraft): Promise<CartDto> => {
    // Map internal OrderDraft to API request DTO
    const request = {
        items: draft.items.map(item => ({
            productId: item.productId,
            variantId: item.variantId,
            quantity: item.quantity
        })),
        shippingMethodId: draft.shippingMethodId
    };

    const response = await httpClient.post<CartDto>(ENDPOINTS.CART_APPLY_DRAFT, request);
    return response.data;
};

// ============================================================
// CMS Pages (Phase 9.11)
// ============================================================

import type { CmsPage, CmsPageListItem, Navigation, StorefrontFooter } from '@/types';

/**
 * Fetch a CMS page by slug
 */
export const fetchCmsPage = async (
    slug: string,
    preview: boolean = false,
    previewToken?: string | null
): Promise<CmsPage> => {
    const response = await httpClient.get<CmsPage>(
        ENDPOINTS.CMS_PAGE(slug),
        preview
            ? {
                params: previewToken
                    ? { preview: true, previewToken }
                    : { preview: true }
            }
            : undefined
    );
    return response.data;
};

/**
 * Fetch all published CMS pages
 */
export const fetchCmsPages = async (): Promise<CmsPageListItem[]> => {
    const response = await httpClient.get<CmsPageListItem[]>(ENDPOINTS.CMS_PAGES);
    return response.data;
};

// ============================================================
// Navigation (Phase 9.11)
// ============================================================

/**
 * Fetch navigation configuration (header + footer)
 */
export const fetchNavigation = async (): Promise<Navigation> => {
    const response = await httpClient.get<Navigation>(ENDPOINTS.NAVIGATION);
    return response.data;
};

export const fetchStorefrontFooter = async (): Promise<StorefrontFooter> => {
    const response = await httpClient.get<StorefrontFooter>(ENDPOINTS.FOOTER);
    return response.data;
};

// ============================================================
// Customer Orders (Phase F2.Orders)
// ============================================================

import type { CustomerOrdersResponse, CustomerOrderDetail } from '@/types/order';

/**
 * Fetch customer's order history (requires authentication)
 */
export const fetchCustomerOrders = async (
    page: number = 1,
    pageSize: number = 10
): Promise<CustomerOrdersResponse> => {
    const response = await httpClient.get<CustomerOrdersResponse>(
        ENDPOINTS.ACCOUNT_ORDERS,
        { params: { page, pageSize } }
    );
    return response.data;
};

/**
 * Fetch a single customer order by ID (requires authentication)
 */
export const fetchCustomerOrder = async (orderId: string): Promise<CustomerOrderDetail> => {
    const response = await httpClient.get<CustomerOrderDetail>(
        ENDPOINTS.ACCOUNT_ORDER(orderId)
    );
    return response.data;
};

// ============================================================
// AF-27: Affiliate Funnel — Optimized Checkout Flow
// ============================================================

/**
 * AF-27: Funnel Start Request
 */
export interface FunnelStartRequest {
    productId: string;
    quantity?: number;
    referralCode?: string; // AF-27E: Renamed from affiliateCode
    variantId?: string;
    /** AF-27A: Forces qty=1, prevents accumulation on repeated CTA clicks */
    singleProductMode?: boolean;
    templateType?: string;
    tenantSlug?: string;
    productSlug?: string;
}

/**
 * AF-27: Funnel Start Response
 */
export interface FunnelStartResponse {
    checkoutUrl: string;
    cartId: string;
    affiliateAttached: boolean;
    itemCount: number;
    productName?: string;
}

/**
 * AF-27: Start affiliate funnel checkout flow.
 * 
 * Server-side cart injection with direct checkout redirect.
 * - Validates product against tenant
 * - Resolves affiliate globally (silent fallback if invalid)
 * - Creates/reuses cart
 * - Returns checkout URL for redirect
 * 
 * @param request - Product ID, quantity, and optional affiliate code
 * @param signal - Optional AbortSignal to cancel the request
 * @returns FunnelStartResponse with checkout URL
 */
export const startAffiliateFunnel = async (request: FunnelStartRequest, signal?: AbortSignal): Promise<FunnelStartResponse> => {
    const response = await httpClient.post<FunnelStartResponse>(
        ENDPOINTS.FUNNEL_START,
        request,
        { signal }
    );
    return response.data;
};

// ============================================================
// AF-27A: Affiliate Landing Page Engine
// ============================================================

/**
 * AF-27A: Landing Page Data
 */
export interface LandingPageData {
    affiliate: LandingPageAffiliate;
    product: LandingPageProduct;
    templateType: string;
    tenant: LandingPageTenant;
    /** AF-27B §7: Backend-driven CTA configuration */
    ctaConfig: LandingPageCTAConfig;
}

/** AF-27B §7: Backend-driven CTA button configuration */
export interface LandingPageCTAConfig {
    buttonText: string;
    singleProductMode: boolean;
    includeAffiliateAttribution: boolean;
    defaultQuantity: number;
}

export interface LandingPageAffiliate {
    affiliateAccountId: string;
    referralCode: string;
    displayName: string;
    isValid: boolean;
}

export interface LandingPageProduct {
    productId: string;
    name: string;
    nameAr?: string;
    description?: string;
    descriptionAr?: string;
    slug: string;
    price: number;
    compareAtPrice?: number;
    currency: string;
    mainImage?: string;
    images: string[];
    inStock: boolean | null;
    hasVariants: boolean;
    variants: LandingPageVariant[];
    hasActivePromotion: boolean;
    promotionBadge?: string;
    promotionDiscountValue?: number;
    promotionDiscountType?: string;
    brand?: string;
    categoryName?: string;
}

export interface LandingPageVariant {
    variantId: string;
    sku?: string;
    price: number;
    compareAtPrice?: number;
    inStock: boolean;
    imageUrl?: string;
    attributes: LandingPageVariantAttribute[];
}

export interface LandingPageVariantAttribute {
    name: string;
    value: string;
}

export interface LandingPageTenant {
    tenantId: string;
    storeName: string;
    logoUrl?: string;
    currency: string;
}

/**
 * AF-27A: Fetch landing page data for an affiliate + product.
 * READ-ONLY endpoint — no cart/checkout side effects.
 */
/**
 * AF-27E: Fetch landing page data
 * 
 * @param referralCode - Affiliate referral code
 * @param slug - Product slug
 * @param template - Optional template override for preview (e.g., "Storytelling:v1")
 * @returns Landing page data
 */
export const fetchLandingPage = async (
    referralCode: string,
    slug: string,
    template?: string // AF-27E: Preview support
): Promise<LandingPageData> => {
    const url = template
        ? `${ENDPOINTS.LANDING_PAGE(referralCode, slug)}?template=${encodeURIComponent(template)}`
        : ENDPOINTS.LANDING_PAGE(referralCode, slug);
    const response = await httpClient.get<LandingPageData>(url);
    return response.data;
};
