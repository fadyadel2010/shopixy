/**
 * API Endpoints Configuration
 * 
 * All storefront API endpoints - READ-ONLY for products, READ-WRITE for cart
 */

export const ENDPOINTS = {
    // Store Configuration
    STORE_CONFIG: '/api/storefront/store-config',
    SMART_OFFERS: '/api/storefront/smart-offers',
    HOME_FEATURED_PRODUCTS: '/api/storefront/home/featured-products',
    HOME_PRODUCTS_BY_IDS: '/api/storefront/home/products/by-ids',
    HOME_CATEGORIES: '/api/storefront/home/categories',

    // Products
    // LEGACY: Used for backward compatibility, will be deprecated
    PRODUCTS: '/api/storefront/products',
    // ELASTICSEARCH-POWERED: Use this for all product listing, filtering, search
    PRODUCTS_DISCOVER: '/api/storefront/products/discover',
    PRODUCT_BY_SLUG: (slug: string) => `/api/storefront/products/${encodeURIComponent(slug)}`,
    PRODUCT_VARIANT_RESOLUTION: (productId: string) =>
        `/api/storefront/products/by-id/${encodeURIComponent(productId)}/variant-resolution`,

    // Categories
    CATEGORIES: '/api/storefront/categories',
    CATEGORY_BY_SLUG: (slug: string) => `/api/storefront/categories/${encodeURIComponent(slug)}`,

    // Cart
    CART: '/api/storefront/cart',
    CART_ITEMS: '/api/storefront/cart/items',
    CART_ITEM: (productId: string) => `/api/storefront/cart/items/${encodeURIComponent(productId)}`,
    // PHASE 11.7: Variant-Centric Cart Operations (VariantId is the ONLY identifier)
    CART_ITEM_BY_VARIANT: (variantId: string) => `/api/storefront/cart/items/variant/${encodeURIComponent(variantId)}`,

    // Cart Merge (Phase 10.4 - Cart Ownership Migration)
    CART_MERGE: '/api/storefront/cart/merge',

    // Coupons (Phase C2)
    CART_APPLY_COUPON: '/api/storefront/cart/apply-coupon',
    CART_REMOVE_COUPON: '/api/storefront/cart/remove-coupon',

    // CommerceAI (Phase 7.9)
    CART_APPLY_DRAFT: '/api/storefront/cart/apply-draft',

    // Orders (Phase 7.8)
    ORDER: (orderId: string) => `/api/storefront/orders/${encodeURIComponent(orderId)}`,
    ORDER_STATUS: (orderId: string) => `/api/storefront/orders/${encodeURIComponent(orderId)}/status`,

    // CMS Pages (Phase 9.11)
    CMS_PAGES: '/api/storefront/pages',
    CMS_PAGE: (slug: string) => `/api/storefront/pages/${encodeURIComponent(slug)}`,

    // Navigation (Phase 9.11)
    NAVIGATION: '/api/storefront/navigation',
    FOOTER: '/api/storefront/footer',

    // Account (Phase F2) - Customer order history
    ACCOUNT_ORDERS: '/api/storefront/account/orders',
    ACCOUNT_ORDER: (orderId: string) => `/api/storefront/account/orders/${encodeURIComponent(orderId)}`,

    // AF-27: Affiliate Funnel — Optimized Checkout Flow
    FUNNEL_START: '/api/storefront/funnel/start',

    // AF-27E: Affiliate Landing Page
    LANDING_PAGE: (referralCode: string, slug: string) =>
        `/api/storefront/landing/${encodeURIComponent(referralCode)}/${encodeURIComponent(slug)}`,
} as const;

