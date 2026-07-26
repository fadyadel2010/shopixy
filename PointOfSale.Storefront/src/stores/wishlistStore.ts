/**
 * Wishlist Store (Zustand)
 * Phase 10.3.6 - Wishlist (Production-grade)
 * 
 * Centralized wishlist state management.
 * 
 * ARCHITECTURAL RULES:
 * - Auth-aware: Only works for authenticated customers
 * - Optimistic updates for better UX
 * - Automatic sync on login/logout
 * - Count maintained for header badge
 */

import { create } from 'zustand';
import { useAuthStore } from '@/auth/store/authStore';
import * as wishlistApi from '@/services/api/wishlistApi';

export interface WishlistItem {
    productId: string;
    addedAtUtc: string;
}

interface WishlistState {
    // Core state
    items: WishlistItem[];
    count: number;
    isLoading: boolean;
    error: string | null;

    // Product ID set for fast lookup
    productIds: Set<string>;

    // Actions
    fetchWishlist: () => Promise<void>;
    fetchCount: () => Promise<void>;
    addItem: (productId: string) => Promise<void>;
    removeItem: (productId: string) => Promise<void>;
    clearWishlist: () => Promise<void>;
    isInWishlist: (productId: string) => boolean;
    reset: () => void;
}

export const useWishlistStore = create<WishlistState>((set, get) => ({
    // Initial state
    items: [],
    count: 0,
    isLoading: false,
    error: null,
    productIds: new Set(),

    /**
     * Fetch full wishlist from API.
     * Only works for authenticated customers.
     */
    fetchWishlist: async () => {
        const { status } = useAuthStore.getState();

        if (status !== 'authenticated') {
            set({ items: [], count: 0, productIds: new Set() });
            return;
        }

        set({ isLoading: true, error: null });

        try {
            const response = await wishlistApi.getWishlist();
            const productIds = new Set(response.items.map(i => i.productId));

            set({
                items: response.items,
                count: response.totalCount,
                productIds,
                isLoading: false,
            });
        } catch (error) {
            console.error('[Wishlist] Failed to fetch wishlist:', error);
            set({
                error: error instanceof Error ? error.message : 'Failed to fetch wishlist',
                isLoading: false,
            });
        }
    },

    /**
     * Fetch only the count (for header badge).
     */
    fetchCount: async () => {
        const { status } = useAuthStore.getState();

        if (status !== 'authenticated') {
            set({ count: 0 });
            return;
        }

        try {
            const count = await wishlistApi.getWishlistCount();
            set({ count });
        } catch (error) {
            console.warn('[Wishlist] Failed to fetch count:', error);
            // Non-critical - don't update error state
        }
    },

    /**
     * Add a product to wishlist.
     * Uses optimistic update for better UX.
     */
    addItem: async (productId: string) => {
        const { status } = useAuthStore.getState();

        if (status !== 'authenticated') {
            throw new Error('Not authenticated');
        }

        const { items, productIds, count } = get();

        // Already in wishlist - idempotent
        if (productIds.has(productId)) {
            return;
        }

        // Optimistic update
        const newItem: WishlistItem = {
            productId,
            addedAtUtc: new Date().toISOString(),
        };
        const newProductIds = new Set(productIds);
        newProductIds.add(productId);

        set({
            items: [...items, newItem],
            count: count + 1,
            productIds: newProductIds,
            error: null,
        });

        try {
            const response = await wishlistApi.addToWishlist(productId);

            // Update with server count (source of truth)
            set({ count: response.wishlistCount });
        } catch (error) {
            console.error('[Wishlist] Failed to add item:', error);

            // Rollback optimistic update
            const rollbackProductIds = new Set(productIds);
            set({
                items,
                count,
                productIds: rollbackProductIds,
                error: error instanceof Error ? error.message : 'Failed to add to wishlist',
            });

            throw error;
        }
    },

    /**
     * Remove a product from wishlist.
     * Uses optimistic update for better UX.
     */
    removeItem: async (productId: string) => {
        const { status } = useAuthStore.getState();

        if (status !== 'authenticated') {
            throw new Error('Not authenticated');
        }

        const { items, productIds, count } = get();

        // Not in wishlist - idempotent
        if (!productIds.has(productId)) {
            return;
        }

        // Optimistic update
        const newProductIds = new Set(productIds);
        newProductIds.delete(productId);
        const newItems = items.filter(i => i.productId !== productId);

        set({
            items: newItems,
            count: Math.max(0, count - 1),
            productIds: newProductIds,
            error: null,
        });

        try {
            const response = await wishlistApi.removeFromWishlist(productId);

            // Update with server count (source of truth)
            set({ count: response.wishlistCount });
        } catch (error) {
            console.error('[Wishlist] Failed to remove item:', error);

            // Rollback optimistic update
            set({
                items,
                count,
                productIds,
                error: error instanceof Error ? error.message : 'Failed to remove from wishlist',
            });

            throw error;
        }
    },

    /**
     * Clear entire wishlist.
     */
    clearWishlist: async () => {
        const { status } = useAuthStore.getState();

        if (status !== 'authenticated') {
            throw new Error('Not authenticated');
        }

        const { items, productIds, count } = get();

        // Optimistic update
        set({
            items: [],
            count: 0,
            productIds: new Set(),
            error: null,
        });

        try {
            await wishlistApi.clearWishlist();
        } catch (error) {
            console.error('[Wishlist] Failed to clear wishlist:', error);

            // Rollback optimistic update
            set({
                items,
                count,
                productIds,
                error: error instanceof Error ? error.message : 'Failed to clear wishlist',
            });

            throw error;
        }
    },

    /**
     * Fast lookup for product in wishlist.
     */
    isInWishlist: (productId: string) => {
        return get().productIds.has(productId);
    },

    /**
     * Reset wishlist state (on logout).
     */
    reset: () => {
        set({
            items: [],
            count: 0,
            productIds: new Set(),
            isLoading: false,
            error: null,
        });
    },
}));

// Selectors for optimized re-renders
export const selectWishlistCount = (state: WishlistState) => state.count;
export const selectWishlistItems = (state: WishlistState) => state.items;
export const selectWishlistLoading = (state: WishlistState) => state.isLoading;
export const selectWishlistError = (state: WishlistState) => state.error;
