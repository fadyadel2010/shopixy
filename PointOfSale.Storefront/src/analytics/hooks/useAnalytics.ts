/**
 * useAnalytics Hook
 * 
 * The PUBLIC API for analytics in React components.
 * 
 * This hook provides:
 * - Typed event methods (pageView, productView, addToCart, etc.)
 * - Automatic context resolution
 * - Automatic event queueing
 * - SPA navigation handling
 * 
 * RULES:
 * - Hooks are thin wrappers - no business logic
 * - Hook NEVER talks to GA/Meta
 * - All events go through the SDK
 * 
 * USAGE:
 * ```tsx
 * const analytics = useAnalytics();
 * analytics.pageView();
 * analytics.productView(productId, productName);
 * analytics.addToCart(productId, variantId, price, quantity);
 * ```
 */

import { useCallback } from 'react';
import { AnalyticsEventType, type AnalyticsPayload } from '../eventTypes';
import { buildAnalyticsEvent } from '../AnalyticsEventBuilder';
import { enqueueAnalyticsEvent, flushAnalyticsQueue } from '../AnalyticsQueue';
import { markSessionActivity } from '../AnalyticsSession';

/**
 * Debounce helper for filter/search events
 */
function debounce<Args extends unknown[]>(fn: (...args: Args) => void, delay: number): (...args: Args) => void {
    let timeoutId: ReturnType<typeof setTimeout>;
    return (...args: Args) => {
        clearTimeout(timeoutId);
        timeoutId = setTimeout(() => fn(...args), delay);
    };
}

/**
 * Analytics hook interface
 */
export interface UseAnalyticsReturn {
    /** Track page view (called automatically on route change) */
    pageView: (customUrl?: string) => void;

    /** Track product impression in a list */
    productImpression: (productId: string, productName?: string, position?: number) => void;

    /** Track product detail view */
    productView: (productId: string, productName?: string, variantId?: string) => void;

    /** Track search performed */
    search: (query: string, resultCount?: number) => void;

    /** Track filter applied (debounced) */
    filterApplied: (filters: Record<string, string>) => void;

    /** Track add to cart */
    addToCart: (productId: string, variantId: string | undefined, price: number, quantity: number, productName?: string) => void;

    /** Track remove from cart */
    removeFromCart: (productId: string, variantId: string | undefined, quantity: number) => void;

    /** Track checkout started */
    beginCheckout: (cartTotal: number) => void;

    /** Track payment failed */
    paymentFailed: (orderId?: string, errorMessage?: string) => void;

    /** Manually flush the event queue */
    flush: () => Promise<void>;
}

/**
 * useAnalytics hook
 * 
 * Main hook for analytics in React components.
 * Automatically tracks page views on route changes.
 */
export function useAnalytics(): UseAnalyticsReturn {
    // Track page view
    const pageView = useCallback((customUrl?: string) => {
        const url = customUrl || window.location.href;

        markSessionActivity();

        const event = buildAnalyticsEvent(
            AnalyticsEventType.PageView,
            undefined,
            { pageUrl: url }
        );

        enqueueAnalyticsEvent(event);
    }, []);

    // Track product impression
    const productImpression = useCallback((
        productId: string,
        productName?: string,
        position?: number
    ) => {
        markSessionActivity();

        const payload: Partial<AnalyticsPayload> = {
            productId,
            productName,
        };

        if (position !== undefined) {
            payload.extra = { position };
        }

        const event = buildAnalyticsEvent(AnalyticsEventType.ProductImpression, payload);
        enqueueAnalyticsEvent(event);
    }, []);

    // Track product view
    const productView = useCallback((
        productId: string,
        productName?: string,
        variantId?: string
    ) => {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.ProductView, {
            productId,
            productName,
            variantId,
        });

        enqueueAnalyticsEvent(event);
    }, []);

    // Track search (debounced)
    const search = useCallback(
        debounce((query: string, resultCount?: number) => {
            markSessionActivity();

            const event = buildAnalyticsEvent(AnalyticsEventType.SearchPerformed, {
                searchQuery: query,
                extra: resultCount !== undefined ? { resultCount } : undefined,
            });

            enqueueAnalyticsEvent(event);
        }, 500),
        []
    );

    // Track filter applied (debounced)
    const filterApplied = useCallback(
        debounce((filters: Record<string, string>) => {
            markSessionActivity();

            const event = buildAnalyticsEvent(AnalyticsEventType.FilterApplied, {
                filters,
            });

            enqueueAnalyticsEvent(event);
        }, 500),
        []
    );

    // Track add to cart
    const addToCart = useCallback((
        productId: string,
        variantId: string | undefined,
        price: number,
        quantity: number,
        productName?: string
    ) => {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.AddToCart, {
            productId,
            variantId,
            value: price * quantity,
            quantity,
            productName,
        });

        enqueueAnalyticsEvent(event);
    }, []);

    // Track remove from cart
    const removeFromCart = useCallback((
        productId: string,
        variantId: string | undefined,
        quantity: number
    ) => {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.RemoveFromCart, {
            productId,
            variantId,
            quantity,
        });

        enqueueAnalyticsEvent(event);
    }, []);

    // Track checkout started
    const beginCheckout = useCallback((cartTotal: number) => {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.BeginCheckout, {
            value: cartTotal,
        });

        enqueueAnalyticsEvent(event);
    }, []);

    // Track payment failed
    const paymentFailed = useCallback((orderId?: string, errorMessage?: string) => {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.PaymentFailed, {
            orderId,
            extra: errorMessage ? { errorMessage } : undefined,
        });

        enqueueAnalyticsEvent(event);

        // Immediately flush on payment failure
        flushAnalyticsQueue().catch(() => {
            // Silent failure
        });
    }, []);

    // Manual flush
    const flush = useCallback(async () => {
        await flushAnalyticsQueue();
    }, []);

    return {
        pageView,
        productImpression,
        productView,
        search,
        filterApplied,
        addToCart,
        removeFromCart,
        beginCheckout,
        paymentFailed,
        flush,
    };
}

export default useAnalytics;
