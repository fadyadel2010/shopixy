/**
 * AF-12: Affiliate-Aware Navigation Hooks
 * 
 * Provides navigation utilities that automatically preserve ?ref= parameter.
 * 
 * USAGE:
 * - Replace useNavigate() with useAffiliateNavigate()
 * - Use buildAffiliateUrl() to construct URLs with ref parameter
 * 
 * FEATURES:
 * - Automatic ref parameter preservation
 * - Query string merging (preserves existing params)
 * - Handles both path-only and full URL navigation
 * - No duplicate ref parameters
 */

import { useCallback } from 'react';
import { useNavigate, NavigateOptions, useLocation } from 'react-router-dom';
import { useAffiliate } from '@/contexts/AffiliateContext';

/**
 * Build a URL with affiliate ref parameter if active
 * 
 * @param path - The target path (e.g., "/products" or "/products?category=123")
 * @param affiliateRef - The affiliate ref code (optional, uses context if not provided)
 * @returns URL with ref parameter appended if affiliate mode is active
 */
export function buildAffiliateUrl(path: string, affiliateRef?: string | null): string {
    if (!affiliateRef) {
        return path;
    }

    try {
        // Parse the path to handle existing query parameters
        const [pathname, search] = path.split('?');
        const params = new URLSearchParams(search || '');

        // Only add ref if not already present
        if (!params.has('ref')) {
            params.set('ref', affiliateRef);
        }

        const queryString = params.toString();
        return queryString ? `${pathname}?${queryString}` : pathname;
    } catch (error) {
        console.error('[AffiliateNavigation] Error building URL:', error);
        return path;
    }
}

/**
 * Hook that returns a navigate function with automatic ref preservation
 * 
 * @returns Navigate function that preserves affiliate ref parameter
 */
export function useAffiliateNavigate() {
    const navigate = useNavigate();
    const { affiliateRef } = useAffiliate();

    return useCallback(
        (to: string | number, options?: NavigateOptions) => {
            // Handle numeric navigation (back/forward)
            if (typeof to === 'number') {
                navigate(to);
                return;
            }

            // Build URL with ref parameter
            const urlWithRef = buildAffiliateUrl(to, affiliateRef);

            // Navigate with preserved ref
            navigate(urlWithRef, options);
        },
        [navigate, affiliateRef]
    );
}

/**
 * Hook that returns a function to build affiliate-aware URLs
 * Useful for constructing href attributes
 * 
 * @returns Function that builds URLs with ref parameter
 */
export function useBuildAffiliateUrl() {
    const { affiliateRef } = useAffiliate();

    return useCallback(
        (path: string) => buildAffiliateUrl(path, affiliateRef),
        [affiliateRef]
    );
}

/**
 * Hook to get current location with affiliate ref preserved
 * Useful for return URLs redirects
 * 
 * @returns Current pathname + search with ref parameter
 */
export function useAffiliateLocation(): string {
    const { affiliateRef } = useAffiliate();
    const location = useLocation();

    const fullPath = location.search ? `${location.pathname}${location.search}` : location.pathname;
    return buildAffiliateUrl(fullPath, affiliateRef);
}
