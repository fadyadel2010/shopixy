/**
 * Product Discovery Types
 * 
 * PHASE: Shopify-Grade Product Discovery & Filtering
 * 
 * Matches backend ProductDiscoveryDtos.cs
 * 
 * ARCHITECTURE:
 * - Elasticsearch is the ONLY engine for search, filtering, sorting, ranking
 * - Frontend NEVER performs filtering or sorting
 * - All facets are derived from Elasticsearch aggregations
 */

import type { ProductAvailabilityPresentationDto, ProductPricingPresentationDto } from './product';

/**
 * Sort mode options for product discovery.
 */
export type ProductSortMode =
    | 'relevance'      // Default: _score for search, createdAt for browse
    | 'price_asc'      // Price ascending
    | 'price_desc'     // Price descending
    | 'newest'         // CreatedAt descending
    | 'highest_discount'; // PromotionDiscountValue descending

/**
 * Query parameters for product discovery.
 * All filtering happens in Elasticsearch.
 */
export interface ProductDiscoveryQuery {
    /** Search query (optional, empty = browse mode) */
    q?: string;
    /** Category filter (ID or slug, multi-select) */
    category?: string[];
    /** Brand filter (multi-select) */
    brand?: string[];
    /** Minimum price */
    priceFrom?: number;
    /** Maximum price */
    priceTo?: number;
    /** Filter by promotion status */
    hasPromotion?: boolean;
    /** Filter by stock availability */
    inStock?: boolean;
    /** Filter by variant products */
    hasVariants?: boolean;
    /** Sort mode */
    sort?: ProductSortMode;
    /** Page number (1-indexed) */
    page?: number;
    /** Items per page (default: 12, max: 100) */
    pageSize?: number;
    /** Language: 'en' or 'ar' */
    lang?: string;
}

/**
 * A single product item from discovery results.
 * 
 * CRITICAL: Variant-aware pricing
 * - If hasVariants=true, Price=null and DisplayPriceText is set
 * - Frontend must check DisplayPriceText first
 */
export interface ProductDiscoveryItemDto {
    /** Product ID */
    id: string;
    /** English name */
    name: string;
    /** Arabic name */
    nameAr?: string;
    /** URL-friendly slug */
    slug?: string;
    /** Product-specific storefront card/layout preference */
    storefrontLayout?: string;
    /** Whether the product is featured */
    isFeatured?: boolean;
    /** Product brand */
    brand?: string;
    /** Category display name */
    categoryName?: string;
    /** Category ID */
    categoryId?: string;
    /** Primary image URL */
    imageUrl?: string;
    /** Whether product has active variants */
    hasVariants: boolean;
    /** Stock availability */
    inStock: boolean;
    /** Has active promotion */
    hasActivePromotion: boolean;
    /** 
     * Promotion badge text (e.g., "20% OFF", "خصم 20%")
     * Already localized by backend
     */
    promotionBadge?: string;
    /** Discount type: 'percentage' or 'fixed' */
    promotionDiscountType?: string;
    /** Discount value */
    promotionDiscountValue?: number;
    /** Elasticsearch relevance score */
    score: number;
    /**
     * Price (null for variant products)
     * 
     * CRITICAL: Check displayPriceText first!
     * If hasVariants=true, this will be null.
     */
    price?: number;
    /**
     * Backend-composed pricing presentation.
     * Storefront may format these values, but must not calculate discounts.
     */
    pricing: ProductPricingPresentationDto;
    /**
     * Backend-composed availability and purchasability presentation.
     * Storefront may render these values, but must not calculate purchase authority.
     */
    availability?: ProductAvailabilityPresentationDto;
    /**
     * Display text for price (e.g., "Multiple options" or "متعدد الاختيارات")
     * 
     * CRITICAL: Always render this if present, ignore price field.
     */
    displayPriceText?: string;
}

/**
 * Brand facet bucket.
 */
export interface FacetBucket {
    /** Brand name */
    key: string;
    /** Product count */
    count: number;
}

/**
 * Category facet bucket.
 */
export interface CategoryFacetBucket {
    /** Category ID */
    id: string;
    /** Category name (may need enrichment) */
    name: string;
    /** Category slug */
    slug?: string;
    /** Product count */
    count: number;
}

/**
 * Price range facet bucket.
 */
export interface PriceRangeBucket {
    /** Display label (e.g., "50 - 100") */
    label: string;
    /** Minimum price (null for "Under X") */
    from?: number;
    /** Maximum price (null for "Over X") */
    to?: number;
    /** Product count */
    count: number;
}

/**
 * Promotion facet data.
 */
export interface PromotionFacet {
    /** Count of products with active promotions */
    promotedCount: number;
    /** Count of products without promotions */
    nonPromotedCount: number;
}

/**
 * Stock facet data.
 */
export interface StockFacet {
    /** Count of in-stock products */
    inStockCount: number;
    /** Count of out-of-stock products */
    outOfStockCount: number;
}

/**
 * All facets for filtered navigation.
 * All values are derived from Elasticsearch aggregations.
 */
export interface ProductDiscoveryFacets {
    /** Available brands with counts */
    brands: FacetBucket[];
    /** Available categories with counts */
    categories: CategoryFacetBucket[];
    /** Price range buckets */
    priceRanges: PriceRangeBucket[];
    /** Promotion statistics */
    promotions: PromotionFacet;
    /** Stock availability statistics */
    stock: StockFacet;
}

/**
 * Complete product discovery result.
 * 
 * ARCHITECTURE:
 * - All data comes from Elasticsearch
 * - No MongoDB calls during discovery
 * - Facets update dynamically based on current filters
 */
export interface ProductDiscoveryResult {
    /** List of products matching the query */
    items: ProductDiscoveryItemDto[];
    /** Total count of matching products */
    totalCount: number;
    /** Current page number */
    page: number;
    /** Items per page */
    pageSize: number;
    /** Original search query */
    query?: string;
    /** Search execution time in milliseconds */
    searchTimeMs: number;
    /** Faceted navigation data */
    facets: ProductDiscoveryFacets;
    /** Whether there are more pages */
    hasNextPage: boolean;
    /** Total number of pages */
    totalPages: number;
}

/**
 * Helper to build query string from ProductDiscoveryQuery.
 * Useful for shareable URLs.
 */
export function buildDiscoveryQueryString(query: ProductDiscoveryQuery): string {
    const params = new URLSearchParams();

    if (query.q) params.set('q', query.q);
    if (query.category) query.category.forEach(c => params.append('category', c));
    if (query.brand) query.brand.forEach(b => params.append('brand', b));
    if (query.priceFrom !== undefined) params.set('priceFrom', query.priceFrom.toString());
    if (query.priceTo !== undefined) params.set('priceTo', query.priceTo.toString());
    if (query.hasPromotion !== undefined) params.set('hasPromotion', query.hasPromotion.toString());
    if (query.inStock !== undefined) params.set('inStock', query.inStock.toString());
    if (query.hasVariants !== undefined) params.set('hasVariants', query.hasVariants.toString());
    if (query.sort && query.sort !== 'relevance') params.set('sort', query.sort);
    if (query.page && query.page > 1) params.set('page', query.page.toString());
    if (query.pageSize && query.pageSize !== 12) params.set('pageSize', query.pageSize.toString());
    if (query.lang && query.lang !== 'en') params.set('lang', query.lang);

    return params.toString();
}

/**
 * Helper to parse query string into ProductDiscoveryQuery.
 */
export function parseDiscoveryQueryString(queryString: string): ProductDiscoveryQuery {
    const params = new URLSearchParams(queryString);

    return {
        q: params.get('q') || undefined,
        category: params.getAll('category').filter(Boolean) || undefined,
        brand: params.getAll('brand').filter(Boolean) || undefined,
        priceFrom: params.has('priceFrom') ? parseFloat(params.get('priceFrom')!) : undefined,
        priceTo: params.has('priceTo') ? parseFloat(params.get('priceTo')!) : undefined,
        hasPromotion: params.has('hasPromotion') ? params.get('hasPromotion') === 'true' : undefined,
        inStock: params.has('inStock') ? params.get('inStock') === 'true' : undefined,
        hasVariants: params.has('hasVariants') ? params.get('hasVariants') === 'true' : undefined,
        sort: (params.get('sort') as ProductSortMode) || undefined,
        page: params.has('page') ? parseInt(params.get('page')!, 10) : undefined,
        pageSize: params.has('pageSize') ? parseInt(params.get('pageSize')!, 10) : undefined,
        lang: params.get('lang') || undefined,
    };
}
