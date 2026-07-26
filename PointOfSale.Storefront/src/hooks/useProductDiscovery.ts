/**
 * useProductDiscovery Hook
 * 
 * PHASE: Shopify-Grade Product Discovery
 * 
 * Fetches products via Elasticsearch-powered discovery endpoint.
 * Supports full filtering, sorting, faceted navigation, and search.
 * 
 * ARCHITECTURE:
 * - Elasticsearch is the ONLY data source
 * - Frontend performs NO filtering or sorting
 * - Facets update dynamically based on current filters
 */

import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { fetchProductsDiscover } from '@/services/api';
import type {
    ProductDiscoveryQuery,
    ProductDiscoveryResult,
    ProductDiscoveryItemDto,
    ProductDiscoveryFacets
} from '@/types';
import { useLanguageStore, selectLanguage } from '@/i18n/languageStore';

/**
 * Discovery query parameters (extends ProductDiscoveryQuery with defaults)
 */
export interface DiscoveryQueryParams extends Partial<ProductDiscoveryQuery> {
    // All params are optional with sensible defaults
}

/**
 * Product discovery state
 */
export interface UseProductDiscoveryState {
    /** Product items */
    items: ProductDiscoveryItemDto[];
    /** Total count of matching products */
    totalCount: number;
    /** Current page */
    page: number;
    /** Items per page */
    pageSize: number;
    /** Total pages */
    totalPages: number;
    /** Whether next page exists */
    hasNextPage: boolean;
    /** Whether previous page exists */
    hasPreviousPage: boolean;
    /** Faceted navigation data */
    facets: ProductDiscoveryFacets | null;
    /** Search query that was executed */
    query: string | null;
    /** Search execution time (ms) */
    searchTimeMs: number;
    /** Loading state */
    isLoading: boolean;
    /** Error message */
    error: string | null;
    /** Refetch function */
    refetch: () => Promise<void>;
    /** Update query params */
    setQueryParams: (params: Partial<DiscoveryQueryParams>) => void;
    /** Current query params */
    queryParams: DiscoveryQueryParams;
}

/**
 * Default query params
 */
const DEFAULT_PARAMS: DiscoveryQueryParams = {
    page: 1,
    pageSize: 12,
    sort: 'relevance',
};

/**
 * Hook for Elasticsearch-powered product discovery
 * 
 * @param initialParams - Query parameters (reactive - triggers fetch on change)
 * @returns Discovery state with products, facets, pagination, and actions
 */
export function useProductDiscovery(
    initialParams: Partial<DiscoveryQueryParams> = {}
): UseProductDiscoveryState {
    const language = useLanguageStore(selectLanguage);

    // Create stable query params using primitives as dependencies
    // This ensures the memo recomputes when any filter value actually changes
    const categoryKey = initialParams.category?.join(',') ?? '';
    const brandKey = initialParams.brand?.join(',') ?? '';

    const queryParams = useMemo<DiscoveryQueryParams>(() => {
        const params = {
            ...DEFAULT_PARAMS,
            ...initialParams,
            lang: language,
        };
        console.log('[useProductDiscovery] queryParams computed:', params);
        return params;
    }, [
        // Use primitive values to detect changes
        initialParams.q,
        categoryKey,
        brandKey,
        initialParams.priceFrom,
        initialParams.priceTo,
        initialParams.hasPromotion,
        initialParams.inStock,
        initialParams.sort,
        initialParams.page,
        initialParams.pageSize,
        language,
    ]);

    // Result state
    const [items, setItems] = useState<ProductDiscoveryItemDto[]>([]);
    const [totalCount, setTotalCount] = useState(0);
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(12);
    const [totalPages, setTotalPages] = useState(0);
    const [hasNextPage, setHasNextPage] = useState(false);
    const [hasPreviousPage, setHasPreviousPage] = useState(false);
    const [facets, setFacets] = useState<ProductDiscoveryFacets | null>(null);
    const [query, setQuery] = useState<string | null>(null);
    const [searchTimeMs, setSearchTimeMs] = useState(0);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    const mountedRef = useRef(true);
    const requestIdRef = useRef(0);

    const loadProducts = useCallback(async () => {
        const currentRequestId = ++requestIdRef.current;

        setIsLoading(true);
        setError(null);

        try {
            console.log('[useProductDiscovery] Fetching with params:', queryParams);
            const result: ProductDiscoveryResult = await fetchProductsDiscover(queryParams);

            // Only update state if this is still the latest request
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                setItems(result.items);
                setTotalCount(result.totalCount);
                setPage(result.page);
                setPageSize(result.pageSize);
                setTotalPages(result.totalPages);
                setHasNextPage(result.hasNextPage);
                setHasPreviousPage(result.page > 1);
                setFacets(result.facets);
                setQuery(result.query ?? null);
                setSearchTimeMs(result.searchTimeMs);
                setError(null);
                console.log('[useProductDiscovery] Loaded', result.items.length, 'products');
            }
        } catch (err) {
            console.error('[useProductDiscovery] Failed to fetch products:', err);
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                setError('Failed to load products');
                setItems([]);
                setTotalCount(0);
                setFacets(null);
            }
        } finally {
            if (mountedRef.current && currentRequestId === requestIdRef.current) {
                setIsLoading(false);
            }
        }
    }, [queryParams]);

    // Fetch products when queryParams change
    useEffect(() => {
        mountedRef.current = true;
        loadProducts();

        return () => {
            mountedRef.current = false;
        };
    }, [loadProducts]);

    const refetch = useCallback(async () => {
        await loadProducts();
    }, [loadProducts]);

    // For manual param updates (not used in URL-driven mode, but kept for API compatibility)
    const setQueryParams = useCallback((_params: Partial<DiscoveryQueryParams>) => {
        // In URL-driven mode, this is a no-op - params come from URL
        console.warn('[useProductDiscovery] setQueryParams called but ignored in URL-driven mode. Update URL instead.');
    }, []);

    return {
        items,
        totalCount,
        page,
        pageSize,
        totalPages,
        hasNextPage,
        hasPreviousPage,
        facets,
        query,
        searchTimeMs,
        isLoading,
        error,
        refetch,
        setQueryParams,
        queryParams,
    };
}

/**
 * Helper hook to build filter chips from current query params
 */
export function useActiveFilters(queryParams: DiscoveryQueryParams) {
    return useMemo(() => {
        const filters: { type: string; value: string; remove: () => Partial<DiscoveryQueryParams> }[] = [];

        if (queryParams.q) {
            filters.push({
                type: 'search',
                value: queryParams.q,
                remove: () => ({ q: undefined }),
            });
        }

        queryParams.category?.forEach(cat => {
            filters.push({
                type: 'category',
                value: cat,
                remove: () => ({
                    category: queryParams.category?.filter(c => c !== cat),
                }),
            });
        });

        queryParams.brand?.forEach(brand => {
            filters.push({
                type: 'brand',
                value: brand,
                remove: () => ({
                    brand: queryParams.brand?.filter(b => b !== brand),
                }),
            });
        });

        if (queryParams.priceFrom !== undefined || queryParams.priceTo !== undefined) {
            const from = queryParams.priceFrom ?? 0;
            const to = queryParams.priceTo ?? '∞';
            filters.push({
                type: 'price',
                value: `${from} - ${to}`,
                remove: () => ({ priceFrom: undefined, priceTo: undefined }),
            });
        }

        if (queryParams.hasPromotion) {
            filters.push({
                type: 'promotion',
                value: 'On Sale',
                remove: () => ({ hasPromotion: undefined }),
            });
        }

        if (queryParams.inStock) {
            filters.push({
                type: 'stock',
                value: 'In Stock',
                remove: () => ({ inStock: undefined }),
            });
        }

        return filters;
    }, [queryParams]);
}
