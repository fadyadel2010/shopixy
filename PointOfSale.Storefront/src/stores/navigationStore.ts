/**
 * Navigation Store
 * Phase 9.11: Footer & Navigation Engine
 * 
 * Zustand store for reactive navigation state (matches existing storefront patterns)
 * 
 * ARCHITECTURAL RULE: No hardcoded fallback content.
 * Navigation must come from CMS/MongoDB only.
 */

import { create } from 'zustand';
import { fetchNavigation } from '@/services/api/storefrontApi';
import type { Navigation, NavigationLink } from '@/types';

interface NavigationState {
    navigation: Navigation | null;
    loading: boolean;
    error: string | null;
    loadNavigation: () => Promise<void>;
    getHeaderLinks: () => NavigationLink[];
    getFooterLinks: () => NavigationLink[];
}

// Empty navigation - NO fallback content per architecture rules
const emptyNavigation: Navigation = {
    header: { links: [] },
    footer: { links: [] },
    updatedAtUtc: new Date().toISOString()
};

export const useNavigationStore = create<NavigationState>((set, get) => ({
    navigation: null,
    loading: false,
    error: null,

    loadNavigation: async () => {
        // Don't reload if already loaded
        if (get().navigation && !get().error) {
            return;
        }

        set({ loading: true, error: null });

        try {
            const navigation = await fetchNavigation();
            set({ navigation, loading: false });
        } catch (error) {
            console.error('Failed to load navigation:', error);
            // Per architecture: NO fallback content - show empty instead
            set({
                navigation: emptyNavigation,
                error: 'Failed to load navigation',
                loading: false
            });
        }
    },

    getHeaderLinks: () => {
        return get().navigation?.header.links ?? [];
    },

    getFooterLinks: () => {
        return get().navigation?.footer.links ?? [];
    }
}));

// Selectors for optimized re-renders
export const selectNavigation = (state: NavigationState) => state.navigation;
export const selectNavigationLoading = (state: NavigationState) => state.loading;
export const selectNavigationError = (state: NavigationState) => state.error;
export const selectHeaderLinks = (state: NavigationState) =>
    state.navigation?.header.links ?? [];
export const selectFooterLinks = (state: NavigationState) =>
    state.navigation?.footer.links ?? [];

/**
 * Resolve navigation link URL
 */
export function resolveNavLinkUrl(link: NavigationLink): string {
    switch (link.type) {
        case 'internal':
            return link.path || '/';
        case 'category':
            // FIXED: Use products page with category filter instead of non-existent /category/ route
            return `/products?category=${link.slug}`;
        case 'cms':
            return `/${link.slug}`;
        case 'external':
            return link.path || '#';
        default:
            return '#';
    }
}
