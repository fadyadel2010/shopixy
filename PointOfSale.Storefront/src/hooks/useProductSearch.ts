/**
 * useProductSearch Hook
 * 
 * State-machine based product search with:
 * - Deterministic state transitions
 * - AbortController for request cancellation
 * - Debouncing with bypass support
 * - Same-query deduplication
 * - Soft error handling
 * 
 * Phase 7.6.1: Search UX Enhancements
 */

import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useDebounce } from '@/utils';
import { httpClient, normalizeError } from '@/services/api';
import { ENDPOINTS } from '@/services/api/endpoints';
import type { ProductListItemDto, PagedResult } from '@/types';

// ============================================================================
// Constants
// ============================================================================

/** Minimum characters required for search */
export const MIN_SEARCH_LENGTH = 2;

/** Debounce delay in milliseconds */
export const DEBOUNCE_DELAY_MS = 400;

/** Default page size */
const DEFAULT_PAGE_SIZE = 20;

// ============================================================================
// Types
// ============================================================================

/**
 * Search query parameters
 */
export interface SearchQueryParams {
    query: string;
    page: number;
    pageSize: number;
    categoryId?: string;
    minPrice?: number;
    maxPrice?: number;
}

/**
 * Search status - deterministic state machine
 * 
 * State transitions:
 * - idle → typing (user starts typing)
 * - typing → searching (debounce complete, API call starts)
 * - typing → idle (user clears input before debounce)
 * - searching → success (results found)
 * - searching → empty (no results)
 * - searching → error (request failed)
 * - success/empty/error → typing (user types again)
 * - success/empty/error → idle (user clears search)
 */
export type SearchStatus =
    | 'idle'        // No query or query too short
    | 'typing'      // User typing, debounce window active
    | 'searching'   // API call in progress
    | 'success'     // Results found
    | 'empty'       // No results (valid response)
    | 'error';      // Request failed

/**
 * Search response type
 */
export interface SearchResult {
    products: ProductListItemDto[];
    pagination: {
        page: number;
        pageSize: number;
        totalCount: number;
        totalPages: number;
        hasNextPage: boolean;
        hasPreviousPage: boolean;
    } | null;
    searchTimeMs?: number;
}

/**
 * Product search hook state
 */
export interface UseProductSearchState {
    /** Current search query */
    query: string;
    /** Set search query */
    setQuery: (query: string) => void;
    /** Search results */
    results: SearchResult;
    /** Current search status */
    status: SearchStatus;
    /** Error message if status is 'error' */
    error: string | null;
    /** Clear search completely */
    clearSearch: () => void;
    /** Force immediate search (bypasses debounce) */
    forceSearch: () => void;
    /** Last successful results (for soft error recovery) */
    lastSuccessfulResults: SearchResult | null;
    /** Last successful query */
    lastSuccessfulQuery: string | null;
    /** Whether search is active (query >= MIN_SEARCH_LENGTH) */
    isSearchActive: boolean;
}

/**
 * Hook options
 */
interface UseProductSearchOptions {
    /** Current page number */
    page?: number;
    /** Items per page */
    pageSize?: number;
    /** Category filter */
    categoryId?: string;
    /** Minimum price filter */
    minPrice?: number;
    /** Maximum price filter */
    maxPrice?: number;
    /** Initial query (from URL) */
    initialQuery?: string;
}

// ============================================================================
// Hook Implementation
// ============================================================================

/**
 * Hook for product search with state-machine based logic
 */
export function useProductSearch(
    options: UseProductSearchOptions = {}
): UseProductSearchState {
    const {
        page = 1,
        pageSize = DEFAULT_PAGE_SIZE,
        categoryId,
        minPrice,
        maxPrice,
        initialQuery = '',
    } = options;

    // State
    const [query, setQueryState] = useState(initialQuery);
    const [status, setStatus] = useState<SearchStatus>(() =>
        initialQuery.length >= MIN_SEARCH_LENGTH ? 'typing' : 'idle'
    );
    const [error, setError] = useState<string | null>(null);
    const [results, setResults] = useState<SearchResult>({
        products: [],
        pagination: null,
    });

    // Soft error recovery - preserve last successful results
    const [lastSuccessfulResults, setLastSuccessfulResults] = useState<SearchResult | null>(null);
    const [lastSuccessfulQuery, setLastSuccessfulQuery] = useState<string | null>(null);

    // Refs for request management
    const abortControllerRef = useRef<AbortController | null>(null);
    const lastSearchedQueryRef = useRef<string>('');
    const mountedRef = useRef(true);
    const forceSearchRef = useRef(false);

    // Debounced query for API calls
    const debouncedQuery = useDebounce(query, DEBOUNCE_DELAY_MS);

    // Computed: is search active
    const isSearchActive = query.trim().length >= MIN_SEARCH_LENGTH;

    /**
     * Cancel any in-flight request
     */
    const cancelPendingRequest = useCallback(() => {
        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
            abortControllerRef.current = null;
        }
    }, []);

    /**
     * Perform search API call
     */
    const performSearch = useCallback(async (searchQuery: string) => {
        const trimmedQuery = searchQuery.trim();

        // Validate minimum length
        if (trimmedQuery.length < MIN_SEARCH_LENGTH) {
            setStatus('idle');
            setResults({ products: [], pagination: null });
            setError(null);
            return;
        }

        // Skip if same query was just searched (deduplication)
        if (trimmedQuery === lastSearchedQueryRef.current && !forceSearchRef.current) {
            return;
        }
        forceSearchRef.current = false;

        // Cancel any pending request
        cancelPendingRequest();

        // Create new AbortController
        const controller = new AbortController();
        abortControllerRef.current = controller;

        // Transition to searching state
        setStatus('searching');
        setError(null);

        try {
            // Build search params for Elasticsearch Discovery endpoint
            const searchParams: Record<string, string | number> = {
                q: trimmedQuery,
                page,
                pageSize,
            };

            if (categoryId) {
                // Discovery endpoint expects 'category' not 'categoryId'
                searchParams.category = categoryId;
            }
            if (minPrice !== undefined) {
                searchParams.priceFrom = minPrice;
            }
            if (maxPrice !== undefined) {
                searchParams.priceTo = maxPrice;
            }

            // Use Elasticsearch-powered discovery endpoint
            const response = await httpClient.get<PagedResult<ProductListItemDto>>(
                ENDPOINTS.PRODUCTS_DISCOVER,
                {
                    params: searchParams,
                    signal: controller.signal,
                }
            );

            // Check if request was aborted or component unmounted
            if (!mountedRef.current || controller.signal.aborted) {
                return;
            }

            const data = response.data;
            lastSearchedQueryRef.current = trimmedQuery;

            const newResults: SearchResult = {
                products: data.items,
                pagination: {
                    page: data.page,
                    pageSize: data.pageSize,
                    totalCount: data.totalCount,
                    totalPages: data.totalPages,
                    hasNextPage: data.hasNextPage,
                    hasPreviousPage: data.hasPreviousPage,
                },
            };

            setResults(newResults);

            if (data.items.length === 0) {
                setStatus('empty');
            } else {
                setStatus('success');
                // Store for soft error recovery
                setLastSuccessfulResults(newResults);
                setLastSuccessfulQuery(trimmedQuery);
            }
        } catch (err: unknown) {
            // Ignore aborted requests
            if (err instanceof Error && err.name === 'CanceledError') {
                return;
            }
            if (err instanceof DOMException && err.name === 'AbortError') {
                return;
            }

            // Log error
            console.error('[useProductSearch] Search failed:', err);

            if (mountedRef.current) {
                const normalized = normalizeError(err as any);
                setStatus('error');
                setError(
                    normalized.code === 'SEARCH_UNAVAILABLE'
                        ? 'Search is temporarily unavailable'
                        : (normalized.message || 'Search failed. Please try again.')
                );

                // Keep existing results on error (soft failure)
                // Don't clear results - this provides better UX
            }
        }
    }, [page, pageSize, categoryId, minPrice, maxPrice, cancelPendingRequest]);

    /**
     * Handle query changes
     */
    const setQuery = useCallback((newQuery: string) => {
        setQueryState(newQuery);

        const trimmed = newQuery.trim();

        if (trimmed.length >= MIN_SEARCH_LENGTH) {
            // Only set to typing if not already searching this query
            if (trimmed !== lastSearchedQueryRef.current) {
                setStatus('typing');
            }
        } else if (trimmed.length === 0) {
            cancelPendingRequest();
            setStatus('idle');
            setResults({ products: [], pagination: null });
            setError(null);
            lastSearchedQueryRef.current = '';
        } else {
            // Between 0 and MIN_SEARCH_LENGTH
            setStatus('idle');
        }
    }, [cancelPendingRequest]);

    /**
     * Clear search completely
     */
    const clearSearch = useCallback(() => {
        cancelPendingRequest();
        setQueryState('');
        setStatus('idle');
        setError(null);
        setResults({ products: [], pagination: null });
        lastSearchedQueryRef.current = '';
    }, [cancelPendingRequest]);

    /**
     * Force immediate search (bypasses debounce)
     */
    const forceSearch = useCallback(() => {
        const trimmed = query.trim();
        if (trimmed.length >= MIN_SEARCH_LENGTH) {
            forceSearchRef.current = true;
            performSearch(trimmed);
        }
    }, [query, performSearch]);

    // Trigger search when debounced query changes
    useEffect(() => {
        const trimmed = debouncedQuery.trim();
        if (trimmed.length >= MIN_SEARCH_LENGTH) {
            performSearch(trimmed);
        }
    }, [debouncedQuery, performSearch]);

    // Re-search when filters change (if we have an active query)
    useEffect(() => {
        const trimmed = query.trim();
        if (trimmed.length >= MIN_SEARCH_LENGTH && trimmed === lastSearchedQueryRef.current) {
            // Force re-search with same query when filters change
            forceSearchRef.current = true;
            performSearch(trimmed);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [page, categoryId, minPrice, maxPrice]);

    // Sync with initial query from URL
    useEffect(() => {
        if (initialQuery && initialQuery !== query) {
            setQueryState(initialQuery);
            if (initialQuery.length >= MIN_SEARCH_LENGTH) {
                performSearch(initialQuery);
            }
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [initialQuery]);

    // Cleanup on unmount
    useEffect(() => {
        mountedRef.current = true;
        return () => {
            mountedRef.current = false;
            cancelPendingRequest();
        };
    }, [cancelPendingRequest]);

    // Memoize return value to prevent unnecessary re-renders
    return useMemo(() => ({
        query,
        setQuery,
        results,
        status,
        error,
        clearSearch,
        forceSearch,
        lastSuccessfulResults,
        lastSuccessfulQuery,
        isSearchActive,
    }), [
        query,
        setQuery,
        results,
        status,
        error,
        clearSearch,
        forceSearch,
        lastSuccessfulResults,
        lastSuccessfulQuery,
        isSearchActive,
    ]);
}
