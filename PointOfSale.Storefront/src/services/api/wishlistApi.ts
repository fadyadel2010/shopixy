/**
 * Wishlist API Service
 * Phase 10.3.6 - Wishlist (Production-grade)
 * 
 * Handles all wishlist API calls to the backend.
 * Requires StorefrontJwt authentication.
 */

import { httpClient } from '@/services/api/httpClient';

// Types
export interface WishlistItem {
    productId: string;
    addedAtUtc: string;
}

export interface WishlistResponse {
    items: WishlistItem[];
    totalCount: number;
}

export interface AddToWishlistResponse {
    success: boolean;
    message: string;
    wishlistCount: number;
}

export interface RemoveFromWishlistResponse {
    success: boolean;
    message: string;
    wishlistCount: number;
}

export interface ClearWishlistResponse {
    success: boolean;
    message: string;
}

export interface WishlistCountResponse {
    count: number;
}

export interface CheckWishlistResponse {
    productId: string;
    isInWishlist: boolean;
}

/**
 * Add a product to the wishlist.
 * Idempotent - adding same product twice is OK.
 */
export async function addToWishlist(
    productId: string
): Promise<AddToWishlistResponse> {
    const response = await httpClient.post<AddToWishlistResponse>(
        '/api/storefront/wishlist',
        { productId }
    );
    return response.data;
}

/**
 * Remove a product from the wishlist.
 * Idempotent - removing non-existing product is OK.
 */
export async function removeFromWishlist(
    productId: string
): Promise<RemoveFromWishlistResponse> {
    const response = await httpClient.delete<RemoveFromWishlistResponse>(
        `/api/storefront/wishlist/${productId}`
    );
    return response.data;
}

/**
 * Get the full wishlist.
 */
export async function getWishlist(): Promise<WishlistResponse> {
    const response = await httpClient.get<WishlistResponse>(
        '/api/storefront/wishlist'
    );
    return response.data;
}

/**
 * Clear the entire wishlist.
 */
export async function clearWishlist(): Promise<ClearWishlistResponse> {
    const response = await httpClient.delete<ClearWishlistResponse>(
        '/api/storefront/wishlist'
    );
    return response.data;
}

/**
 * Get wishlist count for badge display.
 */
export async function getWishlistCount(): Promise<number> {
    try {
        const response = await httpClient.get<WishlistCountResponse>(
            '/api/storefront/wishlist/count'
        );
        return response.data.count;
    } catch {
        // Non-critical - return 0 on error
        return 0;
    }
}

/**
 * Check if a product is in the wishlist.
 */
export async function checkInWishlist(
    productId: string
): Promise<boolean> {
    try {
        const response = await httpClient.get<CheckWishlistResponse>(
            `/api/storefront/wishlist/check/${productId}`
        );
        return response.data.isInWishlist;
    } catch {
        // Non-critical - return false on error
        return false;
    }
}
