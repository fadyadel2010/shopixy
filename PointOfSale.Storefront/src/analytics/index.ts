/**
 * Analytics SDK - Main Entry Point
 * 
 * POS-Native Storefront Analytics SDK
 * 
 * ARCHITECTURE:
 * - All analytics go through this SDK
 * - No component calls analytics endpoint directly
 * - No GA/Meta code anywhere
 * - Fully decoupled from external providers
 * 
 * EXPORTS:
 * - Event Types & Interfaces
 * - Session Manager
 * - Context Resolver
 * - Event Builder
 * - Queue Manager
 * - Network Client
 * - React Hooks
 * 
 * USAGE:
 * ```tsx
 * import { useAnalytics, AnalyticsEventType } from '@/analytics';
 * 
 * function MyComponent() {
 *   const analytics = useAnalytics();
 *   
 *   const handleAddToCart = () => {
 *     analytics.addToCart(productId, variantId, price, quantity);
 *   };
 * }
 * ```
 */

// Event Types
export {
    AnalyticsEventType,
    type AnalyticsEvent,
    type AnalyticsActor,
    type AnalyticsContext,
    type AnalyticsPayload,
    type AnalyticsEventResponse,
    type AnalyticsEventBatchResponse,
} from './eventTypes';

// Session Management
export {
    analyticsSession,
    getAnalyticsSessionId,
    markSessionActivity,
    isSessionActive,
} from './AnalyticsSession';

// Context Resolution
export {
    analyticsContextResolver,
    resolveAnalyticsContext,
    resolveAnalyticsContextWithUrl,
} from './AnalyticsContextResolver';

// Event Building
export {
    analyticsEventBuilder,
    buildAnalyticsEvent,
    type BuildEventOptions,
} from './AnalyticsEventBuilder';

// Queue Management
export {
    analyticsQueue,
    enqueueAnalyticsEvent,
    flushAnalyticsQueue,
    getAnalyticsQueueLength,
} from './AnalyticsQueue';

// Network Client
export {
    sendAnalyticsEvent,
    sendAnalyticsBatch,
    checkAnalyticsHealth,
} from './AnalyticsClient';

// React Hooks
export {
    useAnalytics,
    useProductImpressions,
    type UseAnalyticsReturn,
} from './hooks';

// Import for internal use
import { analyticsSession as _session } from './AnalyticsSession';
import { analyticsQueue as _queue, flushAnalyticsQueue as _flush } from './AnalyticsQueue';

/**
 * Initialize analytics SDK
 * 
 * Call this during app bootstrap to ensure analytics is ready.
 * The SDK auto-initializes on first use, so this is optional.
 */
export function initializeAnalytics(): void {
    // Trigger singleton initialization
    void _session;
    void _queue;

    if (import.meta.env.DEV) {
        console.log('[Analytics] SDK initialized');
    }
}

/**
 * Shutdown analytics SDK
 * 
 * Flushes any pending events. Call this before app unmount if needed.
 */
export async function shutdownAnalytics(): Promise<void> {
    try {
        await _flush();
        if (import.meta.env.DEV) {
            console.log('[Analytics] SDK shutdown complete');
        }
    } catch {
        // Silent failure
    }
}

