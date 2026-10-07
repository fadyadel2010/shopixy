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
import { useStoreConfigStore } from '@/store/storeConfigStore';
import { fetchNavigation } from '@/services/api/storefrontApi';
import {
    getRuntimeCacheIdentity,
    isRuntimeCacheFresh,
    type RuntimeCacheLoadOptions,
} from '@/storefront/runtime/cache/runtimeCachePolicy';
import type { Navigation, NavigationLink } from '@/types';

interface NavigationState {
    navigation: Navigation | null;
    loading: boolean;
    error: string | null;
    cacheIdentity: string | null;
    loadedAt: number | null;
    loadNavigation: (options?: RuntimeCacheLoadOptions) => Promise<void>;
    refreshNavigation: () => Promise<void>;
    invalidateNavigation: () => void;
    getHeaderLinks: () => NavigationLink[];
    getFooterLinks: () => NavigationLink[];
}

// Empty navigation - NO fallback content per architecture rules
const emptyNavigation: Navigation = {
    header: { links: [] },
    footer: { links: [] },
    updatedAtUtc: new Date().toISOString()
};

interface NavigationRequest {
    generation: number;
    identity: string;
    promise: Promise<void>;
}

let navigationGeneration = 0;
let navigationRequest: NavigationRequest | null = null;

export const useNavigationStore = create<NavigationState>((set, get) => ({
    navigation: null,
    loading: false,
    error: null,
    cacheIdentity: null,
    loadedAt: null,

    loadNavigation: async (options = {}) => {
        const preview = useStoreConfigStore.getState();
        if (preview.configSource === 'preview' && preview.config.previewChrome) {
            set({ navigation: preview.config.previewChrome.navigation, loading: false, error: null });
            return;
        }
        const identity = getRuntimeCacheIdentity('navigation');
        const state = get();

        if (!options.force
            && state.navigation
            && !state.error
            && isRuntimeCacheFresh(state.loadedAt, state.cacheIdentity, identity)) {
            return;
        }

        if (navigationRequest
            && navigationRequest.identity === identity
            && navigationRequest.generation === navigationGeneration) {
            return navigationRequest.promise;
        }

        if (options.force || (state.cacheIdentity !== null && state.cacheIdentity !== identity)) {
            navigationGeneration += 1;
        }

        const requestGeneration = navigationGeneration;
        set({ loading: true, error: null, ...(state.cacheIdentity && state.cacheIdentity !== identity ? { navigation: null } : {}) });

        let requestRecord!: NavigationRequest;
        const promise = (async () => {
            try {
                const navigation = await fetchNavigation();

                if (navigationRequest === requestRecord && navigationGeneration === requestGeneration) {
                    set({
                        navigation,
                        loading: false,
                        error: null,
                        cacheIdentity: identity,
                        loadedAt: Date.now(),
                    });
                }
            } catch (error) {
                console.error('Failed to load navigation:', error);

                if (navigationRequest === requestRecord && navigationGeneration === requestGeneration) {
                    set({
                        navigation: get().navigation ?? emptyNavigation,
                        error: 'Failed to load navigation',
                        loading: false,
                        cacheIdentity: null,
                        loadedAt: null,
                    });
                }
            } finally {
                if (navigationRequest === requestRecord) {
                    navigationRequest = null;
                }
            }
        })();

        requestRecord = {
            generation: requestGeneration,
            identity,
            promise,
        };
        navigationRequest = requestRecord;

        return promise;
    },

    refreshNavigation: () => get().loadNavigation({ force: true }),

    invalidateNavigation: () => {
        navigationGeneration += 1;
        set({ cacheIdentity: null, loadedAt: null, error: null });
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

export function invalidateNavigationCache(): void {
    useNavigationStore.getState().invalidateNavigation();
}

export function refreshNavigationCache(): Promise<void> {
    return useNavigationStore.getState().refreshNavigation();
}

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
