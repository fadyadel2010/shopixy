/**
 * Analytics Event Builder
 * 
 * Builds properly structured analytics events.
 * 
 * Responsibilities:
 * - Generate EventId (UUID v4)
 * - Attach actor (session) automatically
 * - Attach context automatically
 * - Normalize payload
 * - Validate required fields per event type
 * 
 * RULES:
 * - EventId is ALWAYS generated client-side (for idempotency)
 * - Timestamp is client-side (server validates)
 * - SDK never allows malformed events
 */

import type { AnalyticsEvent, AnalyticsEventType, AnalyticsPayload, AnalyticsContext } from './eventTypes';
import { getAnalyticsSessionId } from './AnalyticsSession';
import { resolveAnalyticsContext, resolveAnalyticsContextWithUrl } from './AnalyticsContextResolver';

/**
 * Generate UUID v4
 */
function generateUUID(): string {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) {
        return crypto.randomUUID();
    }

    // Fallback for older browsers
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
        const r = (Math.random() * 16) | 0;
        const v = c === 'x' ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

/**
 * Get current UTC timestamp in ISO format
 */
function getCurrentTimestamp(): string {
    return new Date().toISOString();
}

/**
 * Builder options for creating events
 */
export interface BuildEventOptions {
    /** Override the auto-resolved context */
    context?: Partial<AnalyticsContext>;
    /** Custom page URL (for SPA navigation) */
    pageUrl?: string;
    /** Pre-generated event ID (for retries) */
    eventId?: string;
}

/**
 * Analytics Event Builder class
 */
class AnalyticsEventBuilder {
    private static instance: AnalyticsEventBuilder;

    private constructor() { }

    static getInstance(): AnalyticsEventBuilder {
        if (!AnalyticsEventBuilder.instance) {
            AnalyticsEventBuilder.instance = new AnalyticsEventBuilder();
        }
        return AnalyticsEventBuilder.instance;
    }

    /**
     * Build an analytics event
     * 
     * @param type - Event type
     * @param payload - Event-specific payload
     * @param options - Builder options
     * @returns Complete analytics event ready to send
     */
    buildEvent(
        type: AnalyticsEventType,
        payload?: Partial<AnalyticsPayload>,
        options?: BuildEventOptions
    ): AnalyticsEvent {
        // Resolve context
        let context: AnalyticsContext;
        if (options?.pageUrl) {
            context = resolveAnalyticsContextWithUrl(options.pageUrl);
        } else {
            context = resolveAnalyticsContext();
        }

        // Merge any context overrides
        if (options?.context) {
            context = { ...context, ...options.context };
        }

        // Build the event
        const event: AnalyticsEvent = {
            eventId: options?.eventId || generateUUID(),
            type,
            occurredAtUtc: getCurrentTimestamp(),
            actor: {
                sessionId: getAnalyticsSessionId(),
            },
            context,
            payload: payload ? this.normalizePayload(payload) : undefined,
        };

        // Validate required fields for event type
        this.validateEvent(event);

        return event;
    }

    /**
     * Normalize payload (remove undefined values, sanitize strings)
     */
    private normalizePayload(payload: Partial<AnalyticsPayload>): AnalyticsPayload {
        const normalized: AnalyticsPayload = {};

        if (payload.productId) normalized.productId = payload.productId;
        if (payload.variantId) normalized.variantId = payload.variantId;
        if (payload.orderId) normalized.orderId = payload.orderId;
        if (payload.categoryId) normalized.categoryId = payload.categoryId;
        if (payload.value !== undefined) normalized.value = payload.value;
        if (payload.quantity !== undefined) normalized.quantity = payload.quantity;
        if (payload.searchQuery) normalized.searchQuery = payload.searchQuery.substring(0, 500);
        if (payload.productName) normalized.productName = payload.productName.substring(0, 500);
        if (payload.productSku) normalized.productSku = payload.productSku.substring(0, 100);
        if (payload.filters && Object.keys(payload.filters).length > 0) {
            normalized.filters = payload.filters;
        }
        if (payload.extra && Object.keys(payload.extra).length > 0) {
            normalized.extra = payload.extra;
        }

        return normalized;
    }

    /**
     * Validate event has required fields for its type
     */
    private validateEvent(event: AnalyticsEvent): void {
        // All events require session ID
        if (!event.actor.sessionId) {
            throw new Error('[AnalyticsEventBuilder] SessionId is required');
        }

        if (!event.context.tenantId) {
            throw new Error('[AnalyticsEventBuilder] TenantId is required');
        }

        // All events require page URL
        if (!event.context.pageUrl) {
            throw new Error('[AnalyticsEventBuilder] PageUrl is required');
        }

        // Type-specific validation
        switch (event.type) {
            case 'ProductImpression':
            case 'ProductView':
                if (!event.payload?.productId) {
                    if (import.meta.env.DEV) {
                        console.warn(`[AnalyticsEventBuilder] ProductId recommended for ${event.type}`);
                    }
                }
                break;

            case 'SearchPerformed':
                if (!event.payload?.searchQuery) {
                    if (import.meta.env.DEV) {
                        console.warn('[AnalyticsEventBuilder] SearchQuery recommended for SearchPerformed');
                    }
                }
                break;

            case 'AddToCart':
            case 'RemoveFromCart':
                if (!event.payload?.productId) {
                    if (import.meta.env.DEV) {
                        console.warn(`[AnalyticsEventBuilder] ProductId recommended for ${event.type}`);
                    }
                }
                break;

            case 'PurchaseCompleted':
                if (!event.payload?.orderId) {
                    if (import.meta.env.DEV) {
                        console.warn('[AnalyticsEventBuilder] OrderId recommended for PurchaseCompleted');
                    }
                }
                break;
        }
    }
}

// Export singleton
export const analyticsEventBuilder = AnalyticsEventBuilder.getInstance();

// Export convenience function
export function buildAnalyticsEvent(
    type: AnalyticsEventType,
    payload?: Partial<AnalyticsPayload>,
    options?: BuildEventOptions
): AnalyticsEvent {
    return analyticsEventBuilder.buildEvent(type, payload, options);
}
