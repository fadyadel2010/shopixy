/**
 * useCategories Hook
 * 
 * Fetches and caches category data.
 * Categories are loaded once and cached in memory.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { getTenantId } from '@/bootstrap';
import { fetchCategories } from '@/services/api';
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
const categoryCache = new Map<string, CategoryTreeDto[]>();
const categoryPromises = new Map<string, Promise<CategoryTreeDto[]>>();

/**
 * Hook for fetching categories with caching
 */
export function useCategories(): UseCategoriesState {
    const tenantCacheKey = (() => {
        try {
            return getTenantId();
        } catch {
            return 'unresolved';
        }
    })();

    const [categories, setCategories] = useState<CategoryTreeDto[]>(categoryCache.get(tenantCacheKey) || []);
    const [isLoading, setIsLoading] = useState(!categoryCache.has(tenantCacheKey));
    const [error, setError] = useState<string | null>(null);
    const mountedRef = useRef(true);

    const loadCategories = useCallback(async (forceRefresh = false) => {
        // Return cached data if available and not forcing refresh
        const cachedCategories = categoryCache.get(tenantCacheKey);
        if (cachedCategories && !forceRefresh) {
            setCategories(cachedCategories);
            setIsLoading(false);
            return;
        }

        // If a fetch is already in progress, wait for it
        const cachePromise = categoryPromises.get(tenantCacheKey) ?? null;
        if (cachePromise && !forceRefresh) {
            try {
                const result = await cachePromise;
                if (mountedRef.current) {
                    setCategories(result);
                    setIsLoading(false);
                }
                return;
            } catch {
                // Fall through to fetch again
            }
        }

        setIsLoading(true);
        setError(null);

        try {
            // Create promise and store it for deduplication
            const nextPromise = fetchCategories();
            categoryPromises.set(tenantCacheKey, nextPromise);
            const result = await nextPromise;

            // Cache the result
            categoryCache.set(tenantCacheKey, result);

            if (mountedRef.current) {
                setCategories(result);
                setError(null);
            }
        } catch (err) {
            console.error('[useCategories] Failed to fetch categories:', err);
            if (mountedRef.current) {
                setError('Failed to load categories');
            }
        } finally {
            categoryPromises.delete(tenantCacheKey);
            if (mountedRef.current) {
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
 * Clear the category cache (for testing)
 */
export function clearCategoryCache(): void {
    categoryCache.clear();
    categoryPromises.clear();
}
