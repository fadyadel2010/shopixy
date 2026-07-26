/**
 * Search Store (Zustand)
 * 
 * Global state management for unified search.
 * 
 * Responsibilities:
 * - Manage search query state
 * - Cache autocomplete results (short-lived, in-memory)
 * - Track loading/error states
 * - Provide search actions
 * 
 * CRITICAL: 
 * - Elasticsearch is the ONLY search source
 * - NO MongoDB or PostgreSQL access
 * - NO cross-tenant data leakage
 * 
 * Phase: Smart Promotion-Aware Header Search
 * - Uses new /suggest endpoint for header search
 * - Works from 1 character
 * - Typo tolerance
 * - Variant-aware price display
 */

import { create } from 'zustand';
import { normalizeError } from '@/services/api';
import {
    search,
    suggest,
    type UnifiedSearchResponse,
    type HeaderSearchItem,
    type HeaderSearchResponse,
    MIN_SEARCH_LENGTH,
    MIN_SUGGEST_LENGTH,
    AUTOCOMPLETE_CACHE_TTL
} from '@/services/api/searchApi';

// ============================================================================
// Types
// ============================================================================

export type SearchStatus =
    | 'idle'      // No query or query too short
    | 'loading'   // Search in progress
    | 'success'   // Results found
    | 'empty'     // No results (valid response)
    | 'error';    // Request failed

interface CacheEntry {
    data: HeaderSearchResponse;
    timestamp: number;
}

interface SearchState {
    // Query state
    query: string;

    // Autocomplete state (uses new HeaderSearchItem for smart header search)
    autocompleteResults: HeaderSearchItem[];
    autocompleteStatus: SearchStatus;
    isAutocompleteOpen: boolean;

    // Full search results state
    searchResults: UnifiedSearchResponse | null;
    searchStatus: SearchStatus;

    // Error state
    error: string | null;

    // Cache (short-lived, in-memory only)
    autocompleteCache: Map<string, CacheEntry>;

    // Current request controller (for cancellation)
    abortController: AbortController | null;
}

interface SearchActions {
    // Query management
    setQuery: (query: string) => void;
    clearQuery: () => void;

    // Autocomplete
    fetchAutocomplete: (query: string) => Promise<void>;
    setAutocompleteOpen: (open: boolean) => void;
    closeAutocomplete: () => void;

    // Full search
    fetchSearch: (query: string, page?: number, type?: string) => Promise<void>;

    // Utility
    reset: () => void;
    cancelPendingRequests: () => void;
}

type SearchStore = SearchState & SearchActions;

// ============================================================================
// Initial State
// ============================================================================

const initialState: SearchState = {
    query: '',
    autocompleteResults: [],
    autocompleteStatus: 'idle',
    isAutocompleteOpen: false,
    searchResults: null,
    searchStatus: 'idle',
    error: null,
    autocompleteCache: new Map(),
    abortController: null,
};

// ============================================================================
// Store Implementation
// ============================================================================

export const useSearchStore = create<SearchStore>((set, get) => ({
    ...initialState,

    setQuery: (query: string) => {
        set({ query });

        // If query is empty, reset autocomplete
        // NOTE: Header search works from 1 character!
        if (query.trim().length < MIN_SUGGEST_LENGTH) {
            set({
                autocompleteResults: [],
                autocompleteStatus: 'idle',
                isAutocompleteOpen: false,
                error: null
            });
        }
    },

    clearQuery: () => {
        get().cancelPendingRequests();
        set({
            query: '',
            autocompleteResults: [],
            autocompleteStatus: 'idle',
            isAutocompleteOpen: false,
            error: null
        });
    },

    fetchAutocomplete: async (query: string) => {
        const trimmedQuery = query.trim();

        // Header search works from 1 character!
        if (trimmedQuery.length < MIN_SUGGEST_LENGTH) {
            set({
                autocompleteResults: [],
                autocompleteStatus: 'idle',
                isAutocompleteOpen: false
            });
            return;
        }

        // Check cache first
        const cache = get().autocompleteCache;
        const cached = cache.get(trimmedQuery);
        const now = Date.now();

        if (cached && (now - cached.timestamp) < AUTOCOMPLETE_CACHE_TTL) {
            set({
                autocompleteResults: cached.data.items,
                autocompleteStatus: cached.data.items.length > 0 ? 'success' : 'empty',
                isAutocompleteOpen: true,
                error: null
            });
            return;
        }

        // Cancel pending request
        get().cancelPendingRequests();

        // Create new abort controller
        const controller = new AbortController();
        set({
            abortController: controller,
            autocompleteStatus: 'loading'
        });

        try {
            // Get current language from document direction
            const isRtl = typeof document !== 'undefined' && document.documentElement.dir === 'rtl';
            const lang = isRtl ? 'ar' : 'en';

            // Use new smart suggest endpoint for header search
            const response = await suggest(trimmedQuery, 5, lang, controller.signal);

            // Check if request was cancelled
            if (controller.signal.aborted) return;

            // Update cache (short-lived)
            const newCache = new Map(cache);
            newCache.set(trimmedQuery, { data: response, timestamp: now });

            // Clean old cache entries
            for (const [key, entry] of newCache.entries()) {
                if (now - entry.timestamp > AUTOCOMPLETE_CACHE_TTL * 2) {
                    newCache.delete(key);
                }
            }

            set({
                autocompleteResults: response.items,
                autocompleteStatus: response.items.length > 0 ? 'success' : 'empty',
                isAutocompleteOpen: true,
                error: null,
                autocompleteCache: newCache,
                abortController: null
            });
        } catch (err: unknown) {
            // Ignore aborted requests
            if (err instanceof Error && err.name === 'CanceledError') return;
            if (err instanceof DOMException && err.name === 'AbortError') return;

            console.error('[SearchStore] Autocomplete failed:', err);
            const normalized = normalizeError(err as any);
            const errorMessage = normalized.code === 'SEARCH_UNAVAILABLE'
                ? 'Search is temporarily unavailable'
                : (normalized.message || 'Search unavailable');

            set({
                autocompleteStatus: 'error',
                error: errorMessage,
                isAutocompleteOpen: false,
                abortController: null
            });
        }
    },

    setAutocompleteOpen: (open: boolean) => {
        set({ isAutocompleteOpen: open });
    },

    closeAutocomplete: () => {
        set({ isAutocompleteOpen: false });
    },

    fetchSearch: async (query: string, page = 1, type?: string) => {
        const trimmedQuery = query.trim();

        // Validate minimum length
        if (trimmedQuery.length < MIN_SEARCH_LENGTH) {
            set({
                searchResults: null,
                searchStatus: 'idle',
                error: null
            });
            return;
        }

        // Cancel pending request
        get().cancelPendingRequests();

        // Create new abort controller
        const controller = new AbortController();
        set({
            abortController: controller,
            searchStatus: 'loading',
            query: query
        });

        try {
            const response = await search({
                q: trimmedQuery,
                page,
                pageSize: 20,
                type
            }, controller.signal);

            // Check if request was cancelled
            if (controller.signal.aborted) return;

            set({
                searchResults: response,
                searchStatus: response.items.length > 0 ? 'success' : 'empty',
                error: null,
                abortController: null
            });
        } catch (err: unknown) {
            // Ignore aborted requests
            if (err instanceof Error && err.name === 'CanceledError') return;
            if (err instanceof DOMException && err.name === 'AbortError') return;

            console.error('[SearchStore] Search failed:', err);
            const normalized = normalizeError(err as any);
            const errorMessage = normalized.code === 'SEARCH_UNAVAILABLE'
                ? 'Search is temporarily unavailable'
                : (normalized.message || 'Search unavailable. Please try again.');

            set({
                searchStatus: 'error',
                error: errorMessage,
                abortController: null
            });
        }
    },

    cancelPendingRequests: () => {
        const controller = get().abortController;
        if (controller) {
            controller.abort();
            set({ abortController: null });
        }
    },

    reset: () => {
        get().cancelPendingRequests();
        set({
            ...initialState,
            autocompleteCache: new Map() // Clear cache on reset
        });
    }
}));

// ============================================================================
// Selectors
// ============================================================================

export const selectQuery = (state: SearchStore) => state.query;
export const selectAutocompleteResults = (state: SearchStore) => state.autocompleteResults;
export const selectAutocompleteStatus = (state: SearchStore) => state.autocompleteStatus;
export const selectIsAutocompleteOpen = (state: SearchStore) => state.isAutocompleteOpen;
export const selectSearchResults = (state: SearchStore) => state.searchResults;
export const selectSearchStatus = (state: SearchStore) => state.searchStatus;
export const selectError = (state: SearchStore) => state.error;
