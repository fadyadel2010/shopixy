/**
 * useCategories Hook
 * 
 * Fetches and caches category data.
 * Categories are loaded once and cached in memory.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { fetchCategories } from '@/services/api/storefrontApi';
import {
    getRuntimeCacheIdentity,
    isRuntimeCacheFresh,
    type RuntimeCacheLoadOptions,
} from '@/storefront/runtime/cache/runtimeCachePolicy';
import type { CategoryTreeDto } from '@/types';

/**
 * Category fetch state
 */
export interface UseCategoriesState {
    categories: CategoryTreeDto[];
    isLoading: boolean;
    error: string | null;
    refetch: () => Promise<void>;
}

/**
 * Module-level cache for categories
 * Categories don't change often, so we cache them
 */
interface CategoryCacheEntry {
    categories: CategoryTreeDto[] | null;
    generation: number;
    inFlight: Promise<CategoryTreeDto[]> | null;
    loadedAt: number | null;
}

const categoryCache = new Map<string, CategoryCacheEntry>();

function getCategoryCacheEntry(cacheKey: string): CategoryCacheEntry {
    const existing = categoryCache.get(cacheKey);
    if (existing) {
        return existing;
    }

    const entry: CategoryCacheEntry = {
        categories: null,
        generation: 0,
        inFlight: null,
        loadedAt: null,
    };
    categoryCache.set(cacheKey, entry);
    return entry;
}

function getCachedCategories(cacheKey: string): CategoryTreeDto[] | null {
    return categoryCache.get(cacheKey)?.categories ?? null;
}

async function loadCategoriesForKey(
    cacheKey: string,
    options: RuntimeCacheLoadOptions = {}
): Promise<CategoryTreeDto[]> {
    const entry = getCategoryCacheEntry(cacheKey);

    if (entry.inFlight) {
        return entry.inFlight;
    }

    if (!options.force
        && entry.categories
        && isRuntimeCacheFresh(entry.loadedAt, cacheKey, cacheKey)) {
        return entry.categories;
    }

    if (options.force) {
        entry.generation += 1;
        entry.loadedAt = null;
    }

    const requestGeneration = entry.generation;
    let request!: Promise<CategoryTreeDto[]>;
    request = (async () => {
        try {
            const result = await fetchCategories();

            if (categoryCache.get(cacheKey) === entry && entry.generation === requestGeneration) {
                entry.categories = result;
                entry.loadedAt = Date.now();
            }

            return result;
        } finally {
            if (categoryCache.get(cacheKey) === entry && entry.inFlight === request) {
                entry.inFlight = null;
            }
        }
    })();
    entry.inFlight = request;

    return request;
}

export function refreshCategoryCache(
    options: RuntimeCacheLoadOptions = {}
): Promise<CategoryTreeDto[]> {
    return loadCategoriesForKey(
        getRuntimeCacheIdentity('categories'),
        { force: options.force ?? true }
    );
}

export function invalidateCategoryCache(): void {
    categoryCache.clear();
}

/**
 * Hook for fetching categories with caching
 */
export function useCategories(): UseCategoriesState {
    const tenantCacheKey = getRuntimeCacheIdentity('categories');
    const initialCategories = getCachedCategories(tenantCacheKey);

    const [categories, setCategories] = useState<CategoryTreeDto[]>(initialCategories ?? []);
    const [isLoading, setIsLoading] = useState(
        !isRuntimeCacheFresh(
            categoryCache.get(tenantCacheKey)?.loadedAt ?? null,
            tenantCacheKey,
            tenantCacheKey
        )
    );
    const [error, setError] = useState<string | null>(null);
    const mountedRef = useRef(true);
    const requestSequenceRef = useRef(0);

    const loadCategories = useCallback(async (forceRefresh = false) => {
        const requestSequence = ++requestSequenceRef.current;
        setIsLoading(true);
        setError(null);

        try {
            const result = await loadCategoriesForKey(tenantCacheKey, { force: forceRefresh });

            if (mountedRef.current && requestSequenceRef.current === requestSequence) {
                setCategories(result);
                setError(null);
            }
        } catch (err) {
            console.error('[useCategories] Failed to fetch categories:', err);
            if (mountedRef.current && requestSequenceRef.current === requestSequence) {
                setError('Failed to load categories');
            }
        } finally {
            if (mountedRef.current && requestSequenceRef.current === requestSequence) {
                setIsLoading(false);
            }
        }
    }, [tenantCacheKey]);

    useEffect(() => {
        mountedRef.current = true;
        loadCategories();

        return () => {
            mountedRef.current = false;
        };
    }, [loadCategories]);

    const refetch = useCallback(async () => {
        await loadCategories(true);
    }, [loadCategories]);

    return {
        categories,
        isLoading,
        error,
        refetch,
    };
}

/**
 * Backward-compatible alias for explicit Runtime invalidation.
 */
export function clearCategoryCache(): void {
    invalidateCategoryCache();
}
