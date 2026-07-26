/**
 * useProductImpressions Hook
 * 
 * Specialized hook for tracking product impressions in lists.
 * Uses IntersectionObserver to detect when products are visible.
 * 
 * USAGE:
 * ```tsx
 * const impressionRef = useProductImpressions(products);
 * 
 * return (
 *   <div ref={impressionRef}>
 *     {products.map(product => <ProductCard key={product.id} product={product} />)}
 *   </div>
 * );
 * ```
 */

import { useRef, useEffect, useCallback } from 'react';
import { AnalyticsEventType } from '../eventTypes';
import { buildAnalyticsEvent } from '../AnalyticsEventBuilder';
import { enqueueAnalyticsEvent } from '../AnalyticsQueue';

interface ImpressionProduct {
    id: string;
    name?: string;
}

/**
 * Track product impressions when they become visible
 * 
 * @param products - Array of products to track
 * @param options - Configuration options
 * @returns Ref to attach to the container element
 */
export function useProductImpressions(
    products: ImpressionProduct[],
    options: {
        /** Threshold for visibility (0-1) */
        threshold?: number;
        /** Only track impressions once per product */
        trackOnce?: boolean;
    } = {}
) {
    const { threshold = 0.5, trackOnce = true } = options;
    const containerRef = useRef<HTMLDivElement>(null);
    const trackedRef = useRef<Set<string>>(new Set());

    const trackImpression = useCallback((productId: string, productName?: string, position?: number) => {
        // Skip if already tracked (when trackOnce is true)
        if (trackOnce && trackedRef.current.has(productId)) {
            return;
        }

        trackedRef.current.add(productId);

        const event = buildAnalyticsEvent(AnalyticsEventType.ProductImpression, {
            productId,
            productName,
            extra: position !== undefined ? { position } : undefined,
        });

        enqueueAnalyticsEvent(event);

        if (import.meta.env.DEV) {
            console.log('[useProductImpressions] Tracked:', productId);
        }
    }, [trackOnce]);

    useEffect(() => {
        const container = containerRef.current;
        if (!container || products.length === 0) return;

        // Create observer
        const observer = new IntersectionObserver(
            (entries) => {
                entries.forEach((entry) => {
                    if (entry.isIntersecting) {
                        const element = entry.target as HTMLElement;
                        const productId = element.dataset.productId;
                        const productName = element.dataset.productName;
                        const position = element.dataset.position
                            ? parseInt(element.dataset.position, 10)
                            : undefined;

                        if (productId) {
                            trackImpression(productId, productName, position);
                        }
                    }
                });
            },
            { threshold }
        );

        // Observe all product elements
        const productElements = container.querySelectorAll('[data-product-id]');
        productElements.forEach((element) => {
            observer.observe(element);
        });

        return () => {
            observer.disconnect();
        };
    }, [products, threshold, trackImpression]);

    // Reset tracked products when product list changes significantly
    useEffect(() => {
        const productIds = new Set(products.map(p => p.id));
        // Remove tracked IDs that are no longer in the list
        trackedRef.current.forEach((id) => {
            if (!productIds.has(id)) {
                trackedRef.current.delete(id);
            }
        });
    }, [products]);

    return containerRef;
}

export default useProductImpressions;
