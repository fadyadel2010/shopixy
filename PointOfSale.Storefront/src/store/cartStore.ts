/**
 * Cart Store (Zustand)
 * 
 * Manages cart state with full API integration.
 * Supports optimistic updates for better UX.
 * 
 * Phase 7.6: Full implementation with API integration.
 * Phase C2: Added coupon support.
 * Phase 10.4.Y: CQRS Currency Hardening - NO hardcoded currency defaults.
 * 
 * =============================================================================
 * CQRS CURRENCY GUARANTEE
 * =============================================================================
 * Cart currency MUST come from API response (which gets it from Redis/MongoDB).
 * NO hardcoded fallbacks to 'EGP', 'USD', or any other currency.
 * If currency is empty, the UI MUST throw CurrencyNotLoadedError.
 * This prevents charging customers in the wrong currency.
 * =============================================================================
 */

import { create } from 'zustand';
import {
    fetchCart,
    addCartItem,
    updateCartItem,
    removeCartItem,
    // PHASE 11.7: Variant-Centric Cart Operations
    updateCartItemByVariant,
    removeCartItemByVariant,
    clearCart as clearCartApi,
    applyCoupon as applyCouponApi,
    removeCoupon as removeCouponApi,
    applyOrderDraft,
} from '@/services/api';
import type { CartDto, AppliedCouponInfo } from '@/types';
import type { OrderDraft } from '@/types/chat';
import { resolveCartFetchFailure } from './cartStateResolver';
import { emitCartStateMismatchDetected } from '@/features/cart/cartTelemetry';

type CartHydrationSource = 'api' | 'recovery' | null;

const CART_MISMATCH_MESSAGE = 'We could not confirm the latest cart totals. Please retry.';
const CART_CONCURRENCY_CONFLICT_CODE = 'CART_CONCURRENCY_CONFLICT';
const CART_CONCURRENCY_CONFLICT_MESSAGE = 'Your cart changed in another session. We refreshed it so you can try again.';
const CART_MERGE_WAIT_TIMEOUT_MS = 5000;
const cartConflictRetryKeys = new Set<string>();

/**
 * Recursively canonicalize an object by sorting keys at all levels.
 * Preserves array order while ensuring deterministic object key ordering.
 */
function canonicalizePayload(value: any): any {
    if (value === null || value === undefined) {
        return value;
    }
    
    if (Array.isArray(value)) {
        // Preserve array order, but canonicalize each element
        return value.map(item => canonicalizePayload(item));
    }
    
    if (typeof value === 'object') {
        // Sort object keys and recursively canonicalize values
        const sortedKeys = Object.keys(value).sort();
        const canonical: any = {};
        for (const key of sortedKeys) {
            canonical[key] = canonicalizePayload(value[key]);
        }
        return canonical;
    }
    
    // Primitive values (string, number, boolean) are returned as-is
    return value;
}

/**
 * Generate a deterministic fingerprint for a mutation payload.
 * Used to ensure retry keys are unique to the exact mutation being performed.
 */
function generatePayloadFingerprint(payload: any): string {
    if (!payload) return 'empty';
    
    // For objects, create a canonical JSON representation
    if (typeof payload === 'object') {
        // Canonicalize the payload to ensure deterministic ordering at all levels
        const canonical = canonicalizePayload(payload);
        const sortedPayload = JSON.stringify(canonical);
        // Simple hash function for the fingerprint
        let hash = 0;
        for (let i = 0; i < sortedPayload.length; i++) {
            const char = sortedPayload.charCodeAt(i);
            hash = ((hash << 5) - hash) + char;
            hash = hash & hash; // Convert to 32-bit integer
        }
        return Math.abs(hash).toString(36);
    }
    
    return String(payload);
}

/**
 * Generate a scoped retry key that includes cart ID and payload fingerprint.
 * This prevents false duplicates when different carts or different payloads
 * are being mutated concurrently.
 */
function generateRetryKey(cartId: string, mutationType: string, payload?: any): string {
    const fingerprint = payload ? generatePayloadFingerprint(payload) : 'none';
    return `${cartId}:${mutationType}:${fingerprint}`;
}

/**
 * Cart store state
 */
export interface CartState {
    /** Current cart data */
    cart: CartDto | null;
    /** Loading state */
    isLoading: boolean;
    /** Error message */
    error: string | null;
    /** Item being updated (for optimistic UI) */
    updatingItemId: string | null;
    /** Whether cart has been fetched at least once */
    isInitialized: boolean;
    /** Coupon-specific error code (for display) */
    couponError: string | null;
    /** Whether coupon operation is in progress */
    isApplyingCoupon: boolean;
    /** Tracks whether the current cart was hydrated from backend or recovery */
    hydrationSource: CartHydrationSource;
    /** Skip one automatic refetch after recovery hydration */
    skipNextFetch: boolean;
    /** One-time success message shown after recovery hydration */
    recoveryMessage: string | null;
    /** Whether an auth-triggered cart merge is actively running */
    cartMergeInProgress: boolean;
}

/**
 * Cart store actions
 * 
 * PHASE 11.4 — VARIANT-CENTRIC CART
 * All cart operations now use variantId as the primary identifier.
 * - addItem requires variantId (falls back to default variant on backend if null)
 * - updateQuantity uses variantId for item lookup
 * - removeItem uses variantId for item lookup
 */
export interface CartActions {
    /** Fetch cart from API */
    fetchCart: () => Promise<void>;
    /** 
     * Add item to cart by product and variant
     * PHASE 11.4: variantId is REQUIRED for variant products
     */
    addItem: (productId: string, quantity: number, variantId?: string) => Promise<boolean>;
    /** Update item quantity by variantId (or productId for legacy) */
    updateQuantity: (productId: string, quantity: number, variantId?: string) => Promise<boolean>;
    /** Remove item from cart by variantId (or productId for legacy) */
    removeItem: (productId: string, variantId?: string) => Promise<boolean>;
    /** Clear entire cart */
    clearCart: () => Promise<boolean>;
    /** Reset error state */
    clearError: () => void;
    /** Apply coupon code */
    applyCoupon: (code?: string | null, smartOfferRedeemToken?: string | null) => Promise<boolean>;
    /** Remove applied coupon */
    removeCoupon: () => Promise<boolean>;
    /** Clear coupon error */
    clearCouponError: () => void;
    /** Apply order draft */
    applyDraft: (draft: OrderDraft) => Promise<boolean>;
    /** Restore a recovered cart snapshot and keep it authoritative initially */
    restoreRecoveredCart: (cart: CartDto, message?: string | null) => void;
    /** Dismiss the restored-cart success message */
    clearRecoveryMessage: () => void;
}

/**
 * Empty cart state
 * 
 * PHASE 10.4.Y: Currency is empty string - MUST be filled by API response.
 * NO hardcoded currency defaults. If currency is empty when displayed,
 * the UI MUST throw CurrencyNotLoadedError (via useCurrency hook).
 */
const EMPTY_CART: CartDto = {
    cartId: '',
    tenantId: '',
    ownerType: 'session',
    ownerId: '',
    items: [],
    itemCount: 0,
    subtotal: 0,
    discountTotal: 0,
    taxTotal: 0,
    grandTotal: 0,
    currency: '', // PHASE 10.4.Y: MUST be empty - API response is the only valid currency source
    lastUpdatedUtc: new Date().toISOString(),
    expiresAtUtc: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
    appliedCouponCode: null,
    appliedCoupon: null,
};

function almostEqual(left: number, right: number): boolean {
    return Math.abs(left - right) < 0.01;
}

function detectCartStateMismatch(cart: CartDto): string | null {
    const expectedItemCount = cart.items.reduce((sum, item) => sum + Math.max(item.quantity, 0), 0);
    if (expectedItemCount !== cart.itemCount) {
        return 'item_count_mismatch';
    }

    const expectedSubtotal = cart.items.reduce(
        (sum, item) => sum + (item.lineTotal ?? (item.unitPrice * item.quantity)),
        0
    );

    if (!almostEqual(expectedSubtotal, cart.subtotal)) {
        return 'subtotal_mismatch';
    }

    return null;
}

function rejectMismatchedCart(reason: string): void {
    emitCartStateMismatchDetected('cart_store', reason);
}

function getCartMutationErrorCode(error: unknown): string | null {
    if (!error || typeof error !== 'object' || !('response' in error)) {
        return null;
    }

    const candidate = error as { response?: { data?: { code?: string } } };
    return candidate.response?.data?.code ?? null;
}

/**
 * Cart store
 */
export const useCartStore = create<CartState & CartActions>((set, get) => {
    const syncCartFromServer = async (): Promise<boolean> => {
        const cart = await fetchCart();
        const mismatchReason = detectCartStateMismatch(cart);
        if (mismatchReason) {
            rejectMismatchedCart(mismatchReason);
            set({
                cart: get().cart ?? EMPTY_CART,
                isLoading: false,
                updatingItemId: null,
                isApplyingCoupon: false,
                isInitialized: true,
                error: CART_MISMATCH_MESSAGE,
            });
            return false;
        }

        set({
            cart,
            isLoading: false,
            updatingItemId: null,
            isApplyingCoupon: false,
            isInitialized: true,
            couponError: null,
            error: null,
            hydrationSource: 'api',
            skipNextFetch: false,
            recoveryMessage: null,
        });

        return true;
    };

    const retryCartActionAfterConflict = async (
        retryKey: string,
        retryAction: () => Promise<boolean>
    ): Promise<boolean> => {
        if (cartConflictRetryKeys.has(retryKey)) {
            set({
                isLoading: false,
                updatingItemId: null,
                isApplyingCoupon: false,
                error: CART_CONCURRENCY_CONFLICT_MESSAGE,
            });
            return false;
        }

        cartConflictRetryKeys.add(retryKey);
        try {
            const synced = await syncCartFromServer();
            if (!synced) {
                return false;
            }

            return await retryAction();
        } catch (error: any) {
            set({
                isLoading: false,
                updatingItemId: null,
                isApplyingCoupon: false,
                error: error?.response?.data?.message || CART_CONCURRENCY_CONFLICT_MESSAGE,
            });
            return false;
        } finally {
            cartConflictRetryKeys.delete(retryKey);
        }
    };

    return ({
    // Initial state
    cart: null,
    isLoading: false,
    error: null,
    updatingItemId: null,
    isInitialized: false,
    couponError: null,
    isApplyingCoupon: false,
    hydrationSource: null,
    skipNextFetch: false,
    recoveryMessage: null,
    cartMergeInProgress: false,

    /**
     * Fetch cart from API
     */
    fetchCart: async () => {
        // Prevent duplicate fetches
        if (get().isLoading) return;

        if (get().skipNextFetch && get().cart) {
            set({
                error: null,
                isInitialized: true,
                isLoading: false,
                skipNextFetch: false,
            });
            return;
        }

        set({ isLoading: true, error: null });

        try {
            const cart = await fetchCart();
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({
                    cart: get().cart ?? EMPTY_CART,
                    isLoading: false,
                    isInitialized: true,
                    error: CART_MISMATCH_MESSAGE,
                });
                return;
            }

            set({
                cart,
                isLoading: false,
                isInitialized: true,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
        } catch (error) {
            console.error('[CartStore] Failed to fetch cart:', error);
            const previousCart = get().cart;
            const previousHydrationSource = get().hydrationSource;
            const resolution = resolveCartFetchFailure(
                previousCart,
                EMPTY_CART,
                error
            );
            set({
                cart: resolution.nextCart,
                isLoading: false,
                error: resolution.message,
                isInitialized: true,
                hydrationSource: resolution.preserveLastValidCart ? previousHydrationSource : null,
                skipNextFetch: false,
            });
        }
    },

    /**
     * Add item to cart
     * PHASE 11.4: Uses variantId for variant products
     * PHASE 11.6: Enhanced error handling for variant errors
     */
        addItem: async (productId: string, quantity: number, variantId?: string) => {
        if (get().isLoading || get().updatingItemId || get().isApplyingCoupon) {
            return false;
        }

        set({ isLoading: true, error: null });

        try {
            // PHASE 11.4: Pass variantId to API
            const cart = await addCartItem({ productId, quantity, variantId });
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({
                    isLoading: false,
                    error: CART_MISMATCH_MESSAGE,
                });
                return false;
            }

            set({
                cart,
                isLoading: false,
                isInitialized: true,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
            return true;
        } catch (error: any) {
            console.error('[CartStore] Failed to add item:', error);
            const errorCode = getCartMutationErrorCode(error);

            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'add', { productId, variantId, quantity }),
                    () => get().addItem(productId, quantity, variantId)
                );
            }

            // PHASE 11.6: Extract variant-specific error codes for better UX
            let errorMessage = 'Failed to add item to cart';

            switch (errorCode) {
                case 'VARIANT_REQUIRED':
                    errorMessage = 'Please select product options before adding to cart';
                    break;
                case 'VARIANT_NOT_FOUND':
                    errorMessage = 'Selected option is no longer available';
                    break;
                case 'VARIANT_INACTIVE':
                    errorMessage = 'This variant is currently unavailable';
                    break;
                case 'VARIANT_OUT_OF_STOCK':
                    errorMessage = 'This option is out of stock';
                    break;
                case 'PRODUCT_NOT_FOUND':
                    errorMessage = 'Product is no longer available';
                    break;
                case 'PRODUCT_OUT_OF_STOCK':
                    errorMessage = 'Product is out of stock';
                    break;
                default:
                    errorMessage = error?.response?.data?.message || 'Failed to add item to cart';
            }

            set({
                isLoading: false,
                error: errorMessage,
            });
            return false;
        }
    },

    /**
     * Update item quantity
     * PHASE 11.7: Uses variantId as the ONLY identifier (variant-centric)
     */
    updateQuantity: async (productId: string, quantity: number, variantId?: string) => {
        const currentCart = get().cart;
        if (get().isLoading || get().isApplyingCoupon || get().updatingItemId) {
            return false;
        }

        // PHASE 11.7: Find item by variantId if provided, otherwise by productId
        const item = variantId
            ? currentCart?.items.find((i) => i.variantId === variantId)
            : currentCart?.items.find((i) => i.productId === productId && !i.variantId);

        if (!item) {
            console.warn('[CartStore] Item not found for update:', { productId, variantId });
            return false;
        }

        // Use variantId as the updating identifier if available
        const itemIdentifier = variantId || productId;
        set({
            updatingItemId: itemIdentifier,
            error: null,
        });

        try {
            // PHASE 11.7: Use variant-based API when variantId is provided
            const cart = variantId
                ? await updateCartItemByVariant(variantId, { quantity })
                : await updateCartItem(productId, { quantity });
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({
                    updatingItemId: null,
                    error: CART_MISMATCH_MESSAGE,
                });
                return false;
            }

            set({
                cart,
                updatingItemId: null,
                isInitialized: true,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
            return true;
        } catch (error: any) {
            console.error('[CartStore] Failed to update quantity:', error);

            // PHASE 11.7: Handle variant-specific error codes
            const errorCode = getCartMutationErrorCode(error) || '';
            const errorData = error?.response?.data?.data;
            let errorMessage = 'Failed to update quantity';

            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'update', { productId, variantId, quantity }),
                    () => get().updateQuantity(productId, quantity, variantId)
                );
            }

            switch (errorCode) {
                case 'CART_ITEM_NOT_FOUND':
                    // PHASE 11.7.1: Item no longer exists - auto-refresh cart
                    errorMessage = 'Item not found in cart. Refreshing...';
                    // Delay refresh slightly to allow error to display
                    setTimeout(() => get().fetchCart(), 500);
                    break;
                case 'VARIANT_INSUFFICIENT_STOCK':
                    // PHASE 11.7.1: Stock changed - auto-refresh cart to sync
                    const availableStock = errorData?.availableStock ?? 0;
                    errorMessage = `Only ${availableStock} items available in stock`;
                    // Auto-refresh to get updated availableStock values
                    setTimeout(() => get().fetchCart(), 500);
                    break;
                default:
                    errorMessage = error?.response?.data?.message || 'Failed to update quantity';
            }

            set({
                updatingItemId: null,
                error: errorMessage,
            });
            return false;
        }
    },

    /**
     * Remove item from cart
     * PHASE 11.7: Uses variantId as the ONLY identifier (variant-centric)
     */
    removeItem: async (productId: string, variantId?: string) => {
        if (get().isLoading || get().isApplyingCoupon || get().updatingItemId) {
            return false;
        }

        const itemIdentifier = variantId || productId;
        set({
            updatingItemId: itemIdentifier,
            error: null,
        });

        try {
            // PHASE 11.7: Use variant-based API when variantId is provided
            const cart = variantId
                ? await removeCartItemByVariant(variantId)
                : await removeCartItem(productId);
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({
                    updatingItemId: null,
                    error: CART_MISMATCH_MESSAGE,
                });
                return false;
            }

            set({
                cart,
                updatingItemId: null,
                isInitialized: true,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
            return true;
        } catch (error: any) {
            console.error('[CartStore] Failed to remove item:', error);

            // PHASE 11.7: Handle variant-specific error codes
            const errorCode = getCartMutationErrorCode(error) || '';
            let errorMessage = 'Failed to remove item';

            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'remove', { productId, variantId }),
                    () => get().removeItem(productId, variantId)
                );
            }

            if (errorCode === 'CART_ITEM_NOT_FOUND') {
                // Item no longer exists - refresh cart
                errorMessage = 'Item not found in cart. Refreshing cart...';
                get().fetchCart();
            } else {
                errorMessage = error?.response?.data?.message || 'Failed to remove item';
            }

            set({
                updatingItemId: null,
                error: errorMessage,
            });
            return false;
        }
    },

    /**
     * Clear entire cart
     */
    clearCart: async () => {
        if (get().isLoading || get().updatingItemId || get().isApplyingCoupon) {
            return false;
        }

        const currentCart = get().cart;

        // Optimistic update
        set({
            cart: EMPTY_CART,
            isLoading: true,
            isInitialized: true,
            error: null,
            hydrationSource: 'api',
            skipNextFetch: false,
            recoveryMessage: null,
        });

        try {
            await clearCartApi();
            set({ isLoading: false });
            return true;
        } catch (error) {
            console.error('[CartStore] Failed to clear cart:', error);
            const errorCode = getCartMutationErrorCode(error);
            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'clear', null),
                    () => get().clearCart()
                );
            }

            // Rollback optimistic update
            set({
                cart: currentCart,
                isLoading: false,
                error: 'Failed to clear cart',
            });
            return false;
        }
    },

    /**
     * Clear error state
     */
    clearError: () => {
        set({ error: null });
    },

    /**
     * Apply coupon code
     */
    applyCoupon: async (code?: string | null, smartOfferRedeemToken?: string | null) => {
        if (get().isApplyingCoupon || get().isLoading || get().updatingItemId) {
            return false;
        }

        set({ isApplyingCoupon: true, couponError: null, error: null });

        try {
            const cart = await applyCouponApi(code, smartOfferRedeemToken);
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({
                    isApplyingCoupon: false,
                    error: CART_MISMATCH_MESSAGE,
                });
                return false;
            }

            set({
                cart,
                isApplyingCoupon: false,
                isInitialized: true,
                couponError: null,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
            return true;
        } catch (error: any) {
            console.error('[CartStore] Failed to apply coupon:', error);

            // Extract error code from API response
            const errorCode = getCartMutationErrorCode(error) || 'COUPON_INVALID';

            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'coupon:apply', {
                        code: code?.trim().toUpperCase() || null,
                        smartOfferRedeemToken: smartOfferRedeemToken?.trim() || null,
                    }),
                    () => get().applyCoupon(code, smartOfferRedeemToken)
                );
            }

            const errorMessage = error?.response?.data?.message || 'Failed to apply coupon';

            set({
                isApplyingCoupon: false,
                couponError: errorCode,
                error: errorMessage,
            });
            return false;
        }
    },

    /**
     * Remove applied coupon
     */
    removeCoupon: async () => {
        if (get().isApplyingCoupon || get().isLoading || get().updatingItemId) {
            return false;
        }

        set({
            isApplyingCoupon: true,
            couponError: null,
            error: null,
        });

        try {
            const cart = await removeCouponApi();
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({
                    isApplyingCoupon: false,
                    error: CART_MISMATCH_MESSAGE,
                });
                return false;
            }

            set({
                cart,
                isApplyingCoupon: false,
                isInitialized: true,
                couponError: null,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
            return true;
        } catch (error) {
            console.error('[CartStore] Failed to remove coupon:', error);
            const errorCode = getCartMutationErrorCode(error);
            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'coupon:remove', null),
                    () => get().removeCoupon()
                );
            }

            set({
                isApplyingCoupon: false,
                error: 'Failed to remove coupon',
            });
            return false;
        }
    },

    /**
     * Clear coupon error
     */
    clearCouponError: () => {
        set({ couponError: null });
    },

    /**
     * Apply order draft
     */
    applyDraft: async (draft: OrderDraft) => {
        if (get().isLoading || get().updatingItemId || get().isApplyingCoupon) {
            return false;
        }

        set({ isLoading: true, error: null });

        try {
            const cart = await applyOrderDraft(draft);
            const mismatchReason = detectCartStateMismatch(cart);
            if (mismatchReason) {
                rejectMismatchedCart(mismatchReason);
                set({ isLoading: false, error: CART_MISMATCH_MESSAGE });
                return false;
            }

            set({
                cart,
                isLoading: false,
                isInitialized: true,
                hydrationSource: 'api',
                skipNextFetch: false,
                recoveryMessage: null,
            });
            return true;
        } catch (error: any) {
            console.error('[CartStore] Failed to apply draft:', error);
            const errorCode = getCartMutationErrorCode(error);
            if (errorCode === CART_CONCURRENCY_CONFLICT_CODE) {
                const cartId = get().cart?.cartId || 'unknown';
                // Create a deterministic fingerprint of the draft by sorting items
                const sortedDraft = {
                    intent: draft.intent,
                    items: [...draft.items].sort((a, b) => {
                        // Sort by productId first, then variantId, then quantity
                        if (a.productId !== b.productId) return a.productId.localeCompare(b.productId);
                        const aVariant = a.variantId || '';
                        const bVariant = b.variantId || '';
                        if (aVariant !== bVariant) return aVariant.localeCompare(bVariant);
                        return a.quantity - b.quantity;
                    }),
                    shippingMethodId: draft.shippingMethodId,
                };
                return await retryCartActionAfterConflict(
                    generateRetryKey(cartId, 'draft', sortedDraft),
                    () => get().applyDraft(draft)
                );
            }

            const message = error?.response?.data?.message || 'Failed to apply order draft';
            set({ isLoading: false, error: message });
            return false;
        }
    },

    restoreRecoveredCart: (cart, message) => {
        set({
            cart,
            error: null,
            isInitialized: false,
            isLoading: false,
            hydrationSource: 'recovery',
            skipNextFetch: false,
            recoveryMessage: message ?? 'Your cart has been restored.',
        });
    },

    clearRecoveryMessage: () => {
        set({ recoveryMessage: null });
    },
    });
});

export function waitForCartMerge(timeoutMs = CART_MERGE_WAIT_TIMEOUT_MS): Promise<void> {
    if (!useCartStore.getState().cartMergeInProgress) {
        return Promise.resolve();
    }

    return new Promise((resolve, reject) => {
        let settled = false;
        let unsubscribe: (() => void) | undefined;
        const timeoutId = setTimeout(() => {
            cleanup(() => reject(new Error('Cart merge did not finish in time.')));
        }, timeoutMs);

        const cleanup = (callback?: () => void) => {
            if (settled) {
                return;
            }

            settled = true;
            clearTimeout(timeoutId);
            unsubscribe?.();
            callback?.();
        };

        unsubscribe = useCartStore.subscribe((state) => {
            if (!state.cartMergeInProgress) {
                cleanup(resolve);
            }
        });

        if (!useCartStore.getState().cartMergeInProgress) {
            cleanup(resolve);
        }
    });
}

// ============================================================
// Selectors
// ============================================================

export const selectCart = (state: CartState) => state.cart;
export const selectCartItems = (state: CartState) => state.cart?.items ?? [];
export const selectCartItemCount = (state: CartState) => state.cart?.itemCount ?? 0;
export const selectCartSubtotal = (state: CartState) => state.cart?.subtotal ?? 0;
export const selectCartDiscountTotal = (state: CartState) => state.cart?.discountTotal ?? 0;
export const selectCartTaxTotal = (state: CartState) => state.cart?.taxTotal ?? 0;
export const selectCartGrandTotal = (state: CartState) => state.cart?.grandTotal ?? 0;
// PHASE 10.4.Y: NO fallback - currency MUST come from API or throw error
// Returns empty string if not loaded - consumers should use useCurrency() hook
// which throws CurrencyNotLoadedError if currency is empty
export const selectCartCurrency = (state: CartState): string => state.cart?.currency || '';

// Helper to assert currency is valid (for use in components that need it)
export const selectCartCurrencyOrThrow = (state: CartState): string => {
    const currency = state.cart?.currency;
    if (!currency || currency.trim() === '') {
        throw new Error('Cart currency is not loaded. This indicates the cart API did not return currency.');
    }
    return currency;
};
export const selectIsCartLoading = (state: CartState) => state.isLoading;
export const selectCartError = (state: CartState) => state.error;
export const selectCartRecoveryMessage = (state: CartState) => state.recoveryMessage;
export const selectCartMergeInProgress = (state: CartState) => state.cartMergeInProgress;
export const selectIsCartEmpty = (state: CartState) => (state.cart?.items.length ?? 0) === 0;
export const selectUpdatingItemId = (state: CartState) => state.updatingItemId;
export const selectIsCartInitialized = (state: CartState) => state.isInitialized;

// Coupon selectors
export const selectAppliedCoupon = (state: CartState): AppliedCouponInfo | null => state.cart?.appliedCoupon ?? null;
export const selectAppliedCouponCode = (state: CartState): string | null => state.cart?.appliedCouponCode ?? null;
export const selectCouponError = (state: CartState) => state.couponError;
export const selectIsApplyingCoupon = (state: CartState) => state.isApplyingCoupon;
export const selectHasCoupon = (state: CartState) => !!state.cart?.appliedCouponCode;

// PHASE: Promotions Engine - Promotion discount selectors
export const selectCartPromotionDiscountTotal = (state: CartState) => state.cart?.promotionDiscountTotal ?? 0;
export const selectCartCartPromotionDiscountTotal = (state: CartState) => state.cart?.cartPromotionDiscountTotal ?? 0;
export const selectAppliedCartPromotion = (state: CartState) => state.cart?.appliedCartPromotion ?? null;
export const selectCartTotalDiscount = (state: CartState) => state.cart?.totalDiscount ?? 0;
export const selectHasPromotionDiscount = (state: CartState) => (state.cart?.promotionDiscountTotal ?? 0) > 0;
export const selectHasCartPromotion = (state: CartState) => !!state.cart?.appliedCartPromotion;

