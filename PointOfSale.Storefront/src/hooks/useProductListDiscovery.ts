/**
 * useProductListDiscovery Hook
 * 
 * PHASE: Product Discovery Filters UI
 * 
 * Combines URL-driven filters with Elasticsearch-powered discovery.
 * Single hook for ProductListPage to use.
 * 
 * ARCHITECTURE:
 * - URL is the single source of truth for filter state
 * - Elasticsearch handles all filtering, sorting, faceting
 * - Frontend only renders and passes query params
 */

import { useCallback, useMemo } from 'react';
import { useDiscoveryFilters } from './useDiscoveryFilters';
import { useProductDiscovery } from './useProductDiscovery';
import { useCategories } from './useCategories';
import type {
    ProductListItemDto,
    ProductDiscoveryItemDto,
    ProductDiscoveryFacets,
    ProductSortMode,
} from '@/types';

/**
 * Maps ProductDiscoveryItemDto to ProductListItemDto for backward compatibility.
 * This ensures existing ProductCard components continue to work.
 * 
 * CRITICAL: Preserves backend-composed pricing and availability.
 */
function mapDiscoveryItemToListItem(item: ProductDiscoveryItemDto): ProductListItemDto {
    const availability = item.availability;

    return {
        id: item.id,
        name: item.name,
        nameAr: item.nameAr,
        // CRITICAL: Variant-aware pricing
        // For variant products, price will be null from ES, but we need a number for the type
        // The displayPriceText should be used instead when hasVariants is true
        price: item.price ?? 0,
        effectivePrice: item.price ?? 0,
        pricing: item.pricing,
        categoryName: item.categoryName,
        slug: item.slug,
        storefrontLayout: item.storefrontLayout,
        productTemplate: item.storefrontLayout,
        isFeatured: item.isFeatured,
        thumbnailImage: item.imageUrl,
        availability,
        isAvailable: availability?.isAvailable ?? false,
        inStock: item.inStock,
        stockQuantity: availability?.maximumQuantity ?? 0,
        // PHASE: Variant-Aware Pricing - Pass through for ProductCard
        hasVariants: item.hasVariants,
        displayPriceText: item.displayPriceText,
        // PHASE: Promotions Engine
        hasActivePromotion: item.hasActivePromotion,
        promotionBadge: item.promotionBadge,
        promotionDiscountType: item.promotionDiscountType,
        promotionDiscountValue: item.promotionDiscountValue,
    };
}

export interface UseProductListDiscoveryResult {
    // Products (mapped to ProductListItemDto for component compatibility)
    products: ProductListItemDto[];
    totalCount: number;
    isLoading: boolean;
    error: string | null;

    // Pagination
    page: number;
    pageSize: number;
    totalPages: number;
    hasNextPage: boolean;
    hasPreviousPage: boolean;

    // Facets
    facets: ProductDiscoveryFacets | null;

    // Search
    searchQuery?: string;
    searchTimeMs: number;

    // Filters
    filters: {
        categories: string[];
        brands: string[];
        priceFrom?: number;
        priceTo?: number;
        hasPromotion?: boolean;
        inStock?: boolean;
    };

    // Sort
    sort: ProductSortMode;

    // Filter counts
    hasActiveFilters: boolean;
    activeFilterCount: number;

    // Actions
    setSearch: (q: string | undefined) => void;
    toggleCategory: (id: string) => void;
    toggleBrand: (brand: string) => void;
    setPriceRange: (from?: number, to?: number) => void;
    setHasPromotion: (value: boolean | undefined) => void;
    setInStock: (value: boolean | undefined) => void;
    setSort: (sort: ProductSortMode) => void;
    setPage: (page: number) => void;
    clearAllFilters: () => void;
    refetch: () => Promise<void>;

    // Categories cache for name resolution
    getCategoryName: (id: string) => string | undefined;
    categoriesLoading: boolean;
}

/**
 * Build a category name lookup map from nested category tree
 */
function buildCategoryNameMap(categories: any[]): Map<string, string> {
    const map = new Map<string, string>();

    const traverse = (cats: any[]) => {
        for (const cat of cats) {
            map.set(cat.id, cat.name);
            if (cat.children?.length) {
                traverse(cat.children);
            }
        }
    };

    traverse(categories);
    return map;
}

/**
 * Options for the product list discovery hook
 */
export interface UseProductListDiscoveryOptions {
    /** Category slug from route params (e.g., /category/:slug) */
    routeCategory?: string;
}

/**
 * Hook for product listing with discovery filters
 * @param options - Optional configuration including category from route
 */
export function useProductListDiscovery(options: UseProductListDiscoveryOptions = {}): UseProductListDiscoveryResult {
    // URL-driven filters (with optional route category override)
    const filters = useDiscoveryFilters({ routeCategory: options.routeCategory });

    // Categories for name resolution
    const { categories, isLoading: categoriesLoading } = useCategories();

    // Build category map when categories change with useMemo
    // This ensures the map is updated synchronously when categories load
    const categoryMap = useMemo(
        () => buildCategoryNameMap(categories),
        [categories]
    );

    // getCategoryName now properly depends on categoryMap
    const getCategoryName = useCallback(
        (id: string) => categoryMap.get(id),
        [categoryMap]
    );

    // Elasticsearch-powered discovery
    const discovery = useProductDiscovery({
        q: filters.q,
        category: filters.categories.length > 0 ? filters.categories : undefined,
        brand: filters.brands.length > 0 ? filters.brands : undefined,
        priceFrom: filters.priceFrom,
        priceTo: filters.priceTo,
        hasPromotion: filters.hasPromotion,
        inStock: filters.inStock,
        sort: filters.sort,
        page: filters.page,
        pageSize: filters.pageSize,
    });

    // Map discovery items to ProductListItemDto for component compatibility
    const mappedProducts = useMemo(
        () => discovery.items.map(mapDiscoveryItemToListItem),
        [discovery.items]
    );

    return {
        // Products (mapped for component compatibility)
        products: mappedProducts,
        totalCount: discovery.totalCount,
        isLoading: discovery.isLoading,
        error: discovery.error,

        // Pagination
        page: discovery.page,
        pageSize: discovery.pageSize,
        totalPages: discovery.totalPages,
        hasNextPage: discovery.hasNextPage,
        hasPreviousPage: discovery.hasPreviousPage,

        // Facets
        facets: discovery.facets,

        // Search
        searchQuery: discovery.query ?? undefined,
        searchTimeMs: discovery.searchTimeMs,

        // Filters
        filters: {
            categories: filters.categories,
            brands: filters.brands,
            priceFrom: filters.priceFrom,
            priceTo: filters.priceTo,
            hasPromotion: filters.hasPromotion,
            inStock: filters.inStock,
        },

        // Sort
        sort: filters.sort,

        // Filter counts
        hasActiveFilters: filters.hasActiveFilters,
        activeFilterCount: filters.activeFilterCount,

        // Actions
        setSearch: filters.setSearch,
        toggleCategory: filters.toggleCategory,
        toggleBrand: filters.toggleBrand,
        setPriceRange: filters.setPriceRange,
        setHasPromotion: filters.setHasPromotion,
        setInStock: filters.setInStock,
        setSort: filters.setSort,
        setPage: filters.setPage,
        clearAllFilters: filters.clearAllFilters,
        refetch: discovery.refetch,

        // Categories cache
        getCategoryName,
        categoriesLoading,
    };
}
