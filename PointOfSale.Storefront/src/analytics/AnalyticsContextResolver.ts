/**
 * Analytics Context Resolver
 * 
 * Auto-populates context fields for analytics events.
 * Components NEVER need to pass context manually.
 * 
 * Captures:
 * - pageUrl: Current window location
 * - referrer: Document referrer
 * - language: From i18n or navigator
 * - screenWidth/Height: Viewport dimensions
 * 
 * NOT captured (backend does this):
 * - ipAddress
 * - userAgent
 * - currency (from tenant config)
 * - deviceType (derived from userAgent)
 */

import type { AnalyticsContext } from './eventTypes';
import { getTenantId, isTenantResolved } from '@/bootstrap';

/**
 * Get current language from document or navigator
 */
function getCurrentLanguage(): string {
    // Check HTML lang attribute (set by i18n)
    const htmlLang = document.documentElement.lang;
    if (htmlLang) {
        return htmlLang.split('-')[0]; // 'en-US' -> 'en'
    }

    // Fall back to navigator language
    const navLang = navigator.language || (navigator as any).userLanguage;
    if (navLang) {
        return navLang.split('-')[0];
    }

    return 'en';
}

/**
 * Get current viewport dimensions
 */
function getViewportDimensions(): { width: number; height: number } {
    return {
        width: window.innerWidth || document.documentElement.clientWidth,
        height: window.innerHeight || document.documentElement.clientHeight,
    };
}

function sanitizeAnalyticsUrl(value: string | undefined): string | undefined {
    if (!value) {
        return undefined;
    }

    try {
        const url = new URL(value, window.location.origin);
        url.search = '';
        url.hash = '';
        return url.toString();
    } catch {
        return value.split('?')[0]?.split('#')[0] || undefined;
    }
}

/**
 * Analytics Context Resolver class
 */
class AnalyticsContextResolver {
    private static instance: AnalyticsContextResolver;

    private constructor() { }

    static getInstance(): AnalyticsContextResolver {
        if (!AnalyticsContextResolver.instance) {
            AnalyticsContextResolver.instance = new AnalyticsContextResolver();
        }
        return AnalyticsContextResolver.instance;
    }

    /**
     * Resolve current context for an analytics event
     * 
     * This is called automatically by AnalyticsEventBuilder.
     * Components should NOT call this directly.
     */
    resolve(): AnalyticsContext {
        const viewport = getViewportDimensions();

        return {
            tenantId: isTenantResolved() ? getTenantId() : undefined,
            pageUrl: sanitizeAnalyticsUrl(window.location.href) || window.location.origin,
            referrer: sanitizeAnalyticsUrl(document.referrer),
            language: getCurrentLanguage(),
            screenWidth: viewport.width,
            screenHeight: viewport.height,
        };
    }

    /**
     * Resolve context with custom page URL
     * 
     * Useful for SPA navigation where the URL might not have updated yet.
     */
    resolveWithUrl(pageUrl: string): AnalyticsContext {
        const base = this.resolve();
        return {
            ...base,
            pageUrl: sanitizeAnalyticsUrl(pageUrl) || base.pageUrl,
        };
    }
}

// Export singleton
export const analyticsContextResolver = AnalyticsContextResolver.getInstance();

// Export function for easier usage
export function resolveAnalyticsContext(): AnalyticsContext {
    return analyticsContextResolver.resolve();
}

export function resolveAnalyticsContextWithUrl(pageUrl: string): AnalyticsContext {
    return analyticsContextResolver.resolveWithUrl(pageUrl);
}
