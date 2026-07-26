/**
 * Search API Service
 * 
 * Wraps /api/storefront/search endpoint for unified search.
 * Elasticsearch-first, tenant-safe, read-only.
 * 
 * CRITICAL RULES:
 * - Reads from Elasticsearch ONLY via /api/storefront/search
 * - NO MongoDB direct access
 * - NO client-side filtering as substitute for search
 * - Request-scoped headers are automatically attached by httpClient
 */

import { httpClient } from './httpClient';

// ============================================================================
// Types
// ============================================================================

/**
 * Single search result item from Elasticsearch
 */
export interface UnifiedSearchItem {
    entityId: string;
    entityType: 'product' | 'category' | 'cms';
    name: string;
    nameAr?: string;
    slug?: string;
    description?: string;
    price?: number;
    categoryName?: string;
    score: number;
}

/**
 * Unified search response
 */
export interface UnifiedSearchResponse {
    query: string;
    items: UnifiedSearchItem[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    searchTimeMs: number;
    hasNextPage: boolean;
    hasPreviousPage: boolean;
}

/**
 * Search request parameters
 */
export interface SearchParams {
    q: string;
    page?: number;
    pageSize?: number;
    type?: string; // 'product' | 'category' | 'cms' (comma-separated)
    lang?: string; // 'en' | 'ar' - language for search
}

// ============================================================================
// HEADER SEARCH (AUTOCOMPLETE) TYPES
// Phase: Smart Promotion-Aware Header Search
// ============================================================================

/**
 * Header search suggestion item.
 * 
 * API CONTRACT:
 * - name: Already localized by API based on language
 * - hasVariants: If true, use displayPriceText instead of price
 * - displayPriceText: Pre-formatted text like "Multiple options"
 * - price: Raw price (null if hasVariants=true)
 * - promotionBadge: Localized badge text (e.g., "20% OFF")
 */
export interface HeaderSearchItem {
    productId: string;
    name: string;
    slug?: string;
    hasVariants: boolean;
    displayPriceText?: string;
    price?: number;
    hasActivePromotion: boolean;
    promotionBadge?: string;
    imageUrl?: string;
    categoryName?: string;
}

/**
 * Header search response
 */
export interface HeaderSearchResponse {
    query: string;
    items: HeaderSearchItem[];
    searchTimeMs: number;
}

// ============================================================================
// API Functions
// ============================================================================

/**
 * Perform unified search across products, categories, and CMS pages.
 * 
 * @param params - Search parameters
 * @param signal - Optional AbortSignal for request cancellation
 * @returns Search results from Elasticsearch
 */
export async function search(
    params: SearchParams,
    signal?: AbortSignal
): Promise<UnifiedSearchResponse> {
    const queryParams: Record<string, string | number> = {
        q: params.q,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 20,
    };

    if (params.type) {
        queryParams.type = params.type;
    }

    if (params.lang) {
        queryParams.lang = params.lang;
    }

    const response = await httpClient.get<UnifiedSearchResponse>(
        '/api/storefront/search',
        { params: queryParams, signal }
    );

    return response.data;
}

/**
 * Perform SMART header search (autocomplete) using dedicated suggest endpoint.
 * 
 * CRITICAL DIFFERENCES FROM `search()`:
 * - Works from 1 character (edge n-gram)
 * - Typo tolerance (fuzziness)
 * - Arabic input support
 * - Promotion-aware (light boost)
 * - Variant-aware price display
 * - Products only
 * 
 * @param query - Search query (min 1 character)
 * @param limit - Max results (default 5, max 8)
 * @param lang - Language code: 'en' | 'ar'
 * @param signal - Optional AbortSignal for request cancellation
 * @returns Header search suggestions
 */
export async function suggest(
    query: string,
    limit: number = 5,
    lang: string = 'en',
    signal?: AbortSignal
): Promise<HeaderSearchResponse> {
    const response = await httpClient.get<HeaderSearchResponse>(
        '/api/storefront/search/suggest',
        {
            params: { q: query, limit, lang },
            signal
        }
    );

    return response.data;
}

/**
 * Perform autocomplete search (smaller page size for dropdown)
 * 
 * @deprecated Use `suggest()` for smart header search with 1-character support
 * @param query - Search query
 * @param signal - Optional AbortSignal for request cancellation
 * @returns Search results optimized for autocomplete
 */
export async function autocomplete(
    query: string,
    signal?: AbortSignal
): Promise<UnifiedSearchResponse> {
    return search({
        q: query,
        page: 1,
        pageSize: 8, // Show top 8 results max
    }, signal);
}

// ============================================================================
// Constants
// ============================================================================

/** Minimum characters required for unified search */
export const MIN_SEARCH_LENGTH = 2;

/** Minimum characters required for header suggest (1 character!) */
export const MIN_SUGGEST_LENGTH = 1;

/** Debounce delay for autocomplete in milliseconds */
export const AUTOCOMPLETE_DEBOUNCE_MS = 200; // Faster for header search

/** Short-lived cache TTL for autocomplete results (milliseconds) */
export const AUTOCOMPLETE_CACHE_TTL = 30000; // 30 seconds

