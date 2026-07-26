/**
 * useWishlist Hook
 * Phase 10.3.6 - Wishlist (Production-grade)
 * 
 * Provides easy access to wishlist functionality with auth integration.
 * Handles automatic sync on auth state changes.
 */

import { useEffect, useCallback } from 'react';
import { useWishlistStore, selectWishlistCount } from '@/stores/wishlistStore';
import { useAuthStore, selectIsAuthenticated, selectAuthStatus } from '@/auth/store/authStore';

/**
 * Error key for degraded auth state - consumers should translate this
 */
const DEGRADED_SESSION_ERROR_KEY = 'wishlist.reconnecting';

export interface UseWishlistReturn {
    // State
    count: number;
    items: { productId: string; addedAtUtc: string }[];
    isLoading: boolean;
    error: string | null;

    // Actions
    addItem: (productId: string) => Promise<void>;
    removeItem: (productId: string) => Promise<void>;
    toggleItem: (productId: string) => Promise<void>;
    clearWishlist: () => Promise<void>;
    isInWishlist: (productId: string) => boolean;
    refresh: () => Promise<void>;
}

export function useWishlist(): UseWishlistReturn {
    const {
        items,
        count,
        isLoading,
        error,
        fetchWishlist,
        fetchCount,
        addItem: storeAddItem,
        removeItem: storeRemoveItem,
        clearWishlist: storeClearWishlist,
        isInWishlist: storeIsInWishlist,
        reset,
    } = useWishlistStore();

    const isAuthenticated = useAuthStore(selectIsAuthenticated);
    const authStatus = useAuthStore(selectAuthStatus);

    // Sync wishlist on auth state changes
    useEffect(() => {
        if (authStatus === 'authenticated') {
            // Fetch count on login (lightweight)
            fetchCount();
        } else if (authStatus === 'unauthenticated') {
            // Clear wishlist on logout
            reset();
        }
    }, [authStatus, fetchCount, reset]);

    // Add item wrapper with auth check
    const addItem = useCallback(async (productId: string) => {
        if (authStatus === 'degraded') {
            throw new Error(DEGRADED_SESSION_ERROR_KEY);
        }
        if (!isAuthenticated) {
            throw new Error('wishlist.loginRequired');
        }
        await storeAddItem(productId);
    }, [authStatus, isAuthenticated, storeAddItem]);

    // Remove item wrapper with auth check
    const removeItem = useCallback(async (productId: string) => {
        if (authStatus === 'degraded') {
            throw new Error(DEGRADED_SESSION_ERROR_KEY);
        }
        if (!isAuthenticated) {
            throw new Error('wishlist.loginRequired');
        }
        await storeRemoveItem(productId);
    }, [authStatus, isAuthenticated, storeRemoveItem]);

    // Toggle item (add if not in wishlist, remove if in wishlist)
    const toggleItem = useCallback(async (productId: string) => {
        if (authStatus === 'degraded') {
            throw new Error(DEGRADED_SESSION_ERROR_KEY);
        }
        if (!isAuthenticated) {
            throw new Error('wishlist.loginRequired');
        }

        if (storeIsInWishlist(productId)) {
            await storeRemoveItem(productId);
        } else {
            await storeAddItem(productId);
        }
    }, [authStatus, isAuthenticated, storeIsInWishlist, storeAddItem, storeRemoveItem]);

    // Clear wishlist wrapper with auth check
    const clearWishlist = useCallback(async () => {
        if (authStatus === 'degraded') {
            throw new Error(DEGRADED_SESSION_ERROR_KEY);
        }
        if (!isAuthenticated) {
            throw new Error('wishlist.loginRequired');
        }
        await storeClearWishlist();
    }, [authStatus, isAuthenticated, storeClearWishlist]);

    // Check if in wishlist (works even when not authenticated - returns false)
    const isInWishlist = useCallback((productId: string) => {
        if (!isAuthenticated) {
            return false;
        }
        return storeIsInWishlist(productId);
    }, [isAuthenticated, storeIsInWishlist]);

    // Refresh wishlist
    const refresh = useCallback(async () => {
        if (isAuthenticated) {
            await fetchWishlist();
        }
    }, [isAuthenticated, fetchWishlist]);

    return {
        count,
        items,
        isLoading,
        error,
        addItem,
        removeItem,
        toggleItem,
        clearWishlist,
        isInWishlist,
        refresh,
    };
}

/**
 * Lightweight hook for just the wishlist count (header badge).
 */
export function useWishlistCount(): number {
    const count = useWishlistStore(selectWishlistCount);
    const isAuthenticated = useAuthStore(selectIsAuthenticated);

    return isAuthenticated ? count : 0;
}

/**
 * Hook for checking if a specific product is in wishlist.
 * Optimized for product cards.
 */
export function useIsInWishlist(productId: string): boolean {
    const { isInWishlist } = useWishlistStore();
    const isAuthenticated = useAuthStore(selectIsAuthenticated);

    return isAuthenticated && isInWishlist(productId);
}
