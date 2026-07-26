/**
 * useDiscoveryFilters Hook
 * 
 * PHASE: Product Discovery Filters UI
 * 
 * Manages filter state via URL query parameters.
 * URL = Single Source of Truth
 * 
 * ARCHITECTURE:
 * - All filters reflected in URL
 * - Refresh preserves state
 * - Shareable URLs
 * - Page resets to 1 on filter change
 */

import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { ProductSortMode } from '@/types';

/**
 * Filter state derived from URL
 */
export interface DiscoveryFilters {
    /** Search query */
    q?: string;
    /** Selected categories (IDs or slugs) */
    categories: string[];
    /** Selected brands */
    brands: string[];
    /** Minimum price */
    priceFrom?: number;
    /** Maximum price */
    priceTo?: number;
    /** Filter by promotion */
    hasPromotion?: boolean;
    /** Filter by stock */
    inStock?: boolean;
    /** Filter by variants */
    hasVariants?: boolean;
    /** Sort mode */
    sort: ProductSortMode;
    /** Current page */
    page: number;
    /** Items per page */
    pageSize: number;
}

/**
 * Filter update functions
 */
export interface DiscoveryFilterActions {
    /** Set search query */
    setSearch: (q: string | undefined) => void;
    /** Toggle a category (add/remove from selection) */
    toggleCategory: (categoryId: string) => void;
    /** Set categories directly */
    setCategories: (categories: string[]) => void;
    /** Clear all categories */
    clearCategories: () => void;
    /** Toggle a brand (add/remove from selection) */
    toggleBrand: (brand: string) => void;
    /** Set brands directly */
    setBrands: (brands: string[]) => void;
    /** Clear all brands */
    clearBrands: () => void;
    /** Set price range */
    setPriceRange: (from?: number, to?: number) => void;
    /** Clear price range */
    clearPriceRange: () => void;
    /** Set promotion filter */
    setHasPromotion: (value: boolean | undefined) => void;
    /** Set stock filter */
    setInStock: (value: boolean | undefined) => void;
    /** Set variants filter */
    setHasVariants: (value: boolean | undefined) => void;
    /** Set sort mode */
    setSort: (sort: ProductSortMode) => void;
    /** Set page */
    setPage: (page: number) => void;
    /** Clear all filters */
    clearAllFilters: () => void;
    /** Check if any filter is active */
    hasActiveFilters: boolean;
    /** Get count of active filters */
    activeFilterCount: number;
}

export type UseDiscoveryFiltersResult = DiscoveryFilters & DiscoveryFilterActions;

const DEFAULT_PAGE_SIZE = 12;

/**
 * Parse a comma-separated string into an array
 */
function parseMultiValue(value: string | null): string[] {
    if (!value) return [];
    return value.split(',').filter(Boolean);
}

/**
 * Serialize an array to a comma-separated string
 */
function serializeMultiValue(values: string[]): string | null {
    if (values.length === 0) return null;
    return values.join(',');
}

/**
 * Options for the discovery filters hook
 */
export interface UseDiscoveryFiltersOptions {
    /** Category slug from route params (e.g., /category/:slug) */
    routeCategory?: string;
}

/**
 * Hook for managing discovery filters via URL
 * @param options - Optional configuration including category from route
 */
export function useDiscoveryFilters(options: UseDiscoveryFiltersOptions = {}): UseDiscoveryFiltersResult {
    const { routeCategory } = options;
    const [searchParams, setSearchParams] = useSearchParams();

    // Parse current filters from URL
    const filters = useMemo((): DiscoveryFilters => {
        const pageParam = searchParams.get('page');
        const pageSizeParam = searchParams.get('pageSize');
        const priceFromParam = searchParams.get('priceFrom');
        const priceToParam = searchParams.get('priceTo');
        const hasPromotionParam = searchParams.get('hasPromotion');
        const inStockParam = searchParams.get('inStock');
        const hasVariantsParam = searchParams.get('hasVariants');

        // Categories from query params
        const queryCategories = parseMultiValue(searchParams.get('category'));

        // If routeCategory is provided (from /category/:slug), use it as the primary category
        // Otherwise fall back to query params
        const categories = routeCategory
            ? [routeCategory]
            : queryCategories;

        return {
            q: searchParams.get('q') || undefined,
            categories,
            brands: parseMultiValue(searchParams.get('brand')),
            priceFrom: priceFromParam ? parseFloat(priceFromParam) : undefined,
            priceTo: priceToParam ? parseFloat(priceToParam) : undefined,
            hasPromotion: hasPromotionParam ? hasPromotionParam === 'true' : undefined,
            inStock: inStockParam ? inStockParam === 'true' : undefined,
            hasVariants: hasVariantsParam ? hasVariantsParam === 'true' : undefined,
            sort: (searchParams.get('sort') as ProductSortMode) || 'relevance',
            page: pageParam ? parseInt(pageParam, 10) : 1,
            pageSize: pageSizeParam ? parseInt(pageSizeParam, 10) : DEFAULT_PAGE_SIZE,
        };
    }, [searchParams, routeCategory]);

    /**
     * Update URL params with new values
     * Resets page to 1 unless explicitly setting page
     */
    const updateParams = useCallback((
        updates: Record<string, string | null>,
        options: { resetPage?: boolean } = { resetPage: true }
    ) => {
        setSearchParams(prev => {
            const params = new URLSearchParams(prev);

            // Apply updates
            Object.entries(updates).forEach(([key, value]) => {
                if (value === null || value === undefined || value === '') {
                    params.delete(key);
                } else {
                    params.set(key, value);
                }
            });

            // Reset page to 1 if not explicitly setting page
            if (options.resetPage && !('page' in updates)) {
                params.delete('page');
            }

            return params;
        });
    }, [setSearchParams]);

    // ========================================================================
    // Filter Actions
    // ========================================================================

    const setSearch = useCallback((q: string | undefined) => {
        updateParams({ q: q || null });
    }, [updateParams]);

    const toggleCategory = useCallback((categoryId: string) => {
        const current = filters.categories;
        const newCategories = current.includes(categoryId)
            ? current.filter(c => c !== categoryId)
            : [...current, categoryId];
        updateParams({ category: serializeMultiValue(newCategories) });
    }, [filters.categories, updateParams]);

    const setCategories = useCallback((categories: string[]) => {
        updateParams({ category: serializeMultiValue(categories) });
    }, [updateParams]);

    const clearCategories = useCallback(() => {
        updateParams({ category: null });
    }, [updateParams]);

    const toggleBrand = useCallback((brand: string) => {
        const current = filters.brands;
        const newBrands = current.includes(brand)
            ? current.filter(b => b !== brand)
            : [...current, brand];
        updateParams({ brand: serializeMultiValue(newBrands) });
    }, [filters.brands, updateParams]);

    const setBrands = useCallback((brands: string[]) => {
        updateParams({ brand: serializeMultiValue(brands) });
    }, [updateParams]);

    const clearBrands = useCallback(() => {
        updateParams({ brand: null });
    }, [updateParams]);

    const setPriceRange = useCallback((from?: number, to?: number) => {
        updateParams({
            priceFrom: from !== undefined ? from.toString() : null,
            priceTo: to !== undefined ? to.toString() : null,
        });
    }, [updateParams]);

    const clearPriceRange = useCallback(() => {
        updateParams({ priceFrom: null, priceTo: null });
    }, [updateParams]);

    const setHasPromotion = useCallback((value: boolean | undefined) => {
        updateParams({ hasPromotion: value !== undefined ? value.toString() : null });
    }, [updateParams]);

    const setInStock = useCallback((value: boolean | undefined) => {
        updateParams({ inStock: value !== undefined ? value.toString() : null });
    }, [updateParams]);

    const setHasVariants = useCallback((value: boolean | undefined) => {
        updateParams({ hasVariants: value !== undefined ? value.toString() : null });
    }, [updateParams]);

    const setSort = useCallback((sort: ProductSortMode) => {
        updateParams(
            { sort: sort === 'relevance' ? null : sort },
            { resetPage: false }
        );
    }, [updateParams]);

    const setPage = useCallback((page: number) => {
        updateParams(
            { page: page > 1 ? page.toString() : null },
            { resetPage: false }
        );
    }, [updateParams]);

    const clearAllFilters = useCallback(() => {
        setSearchParams(new URLSearchParams());
    }, [setSearchParams]);

    // ========================================================================
    // Computed Values
    // ========================================================================

    const hasActiveFilters = useMemo(() => {
        return !!(
            filters.q ||
            filters.categories.length > 0 ||
            filters.brands.length > 0 ||
            filters.priceFrom !== undefined ||
            filters.priceTo !== undefined ||
            filters.hasPromotion !== undefined ||
            filters.inStock !== undefined ||
            filters.hasVariants !== undefined
        );
    }, [filters]);

    const activeFilterCount = useMemo(() => {
        let count = 0;
        if (filters.q) count++;
        count += filters.categories.length;
        count += filters.brands.length;
        if (filters.priceFrom !== undefined || filters.priceTo !== undefined) count++;
        if (filters.hasPromotion !== undefined) count++;
        if (filters.inStock !== undefined) count++;
        if (filters.hasVariants !== undefined) count++;
        return count;
    }, [filters]);

    return {
        ...filters,
        setSearch,
        toggleCategory,
        setCategories,
        clearCategories,
        toggleBrand,
        setBrands,
        clearBrands,
        setPriceRange,
        clearPriceRange,
        setHasPromotion,
        setInStock,
        setHasVariants,
        setSort,
        setPage,
        clearAllFilters,
        hasActiveFilters,
        activeFilterCount,
    };
}
