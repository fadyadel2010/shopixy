import { create } from 'zustand';
import { fetchStorefrontFooter } from '@/services/api/storefrontApi';
import {
    getRuntimeCacheIdentity,
    isRuntimeCacheFresh,
    type RuntimeCacheLoadOptions,
} from '@/storefront/runtime/cache/runtimeCachePolicy';
import type { StorefrontFooter } from '@/types';

interface FooterState {
    footer: StorefrontFooter | null;
    loading: boolean;
    error: string | null;
    cacheIdentity: string | null;
    loadedAt: number | null;
    loadFooter: (options?: RuntimeCacheLoadOptions) => Promise<void>;
    refreshFooter: () => Promise<void>;
    invalidateFooter: () => void;
}

const emptyFooter: StorefrontFooter = {
    footerSource: 'Derived',
    socialSource: 'None',
    sections: [],
    socialLinks: [],
    generatedAtUtc: new Date().toISOString()
};

interface FooterRequest {
    generation: number;
    identity: string;
    promise: Promise<void>;
}

let footerGeneration = 0;
let footerRequest: FooterRequest | null = null;

export const useFooterStore = create<FooterState>((set, get) => ({
    footer: null,
    loading: false,
    error: null,
    cacheIdentity: null,
    loadedAt: null,

    loadFooter: async (options = {}) => {
        const identity = getRuntimeCacheIdentity('footer', { includeLanguage: true });
        const state = get();

        if (!options.force
            && state.footer
            && !state.error
            && isRuntimeCacheFresh(state.loadedAt, state.cacheIdentity, identity)) {
            return;
        }

        if (footerRequest
            && footerRequest.identity === identity
            && footerRequest.generation === footerGeneration) {
            return footerRequest.promise;
        }

        if (options.force || (state.cacheIdentity !== null && state.cacheIdentity !== identity)) {
            footerGeneration += 1;
        }

        const requestGeneration = footerGeneration;
        set({ loading: true, error: null });

        let requestRecord!: FooterRequest;
        const promise = (async () => {
            try {
                const footer = await fetchStorefrontFooter();

                if (footerRequest === requestRecord && footerGeneration === requestGeneration) {
                    set({
                        footer,
                        loading: false,
                        error: null,
                        cacheIdentity: identity,
                        loadedAt: Date.now(),
                    });
                }
            } catch (error) {
                console.error('Failed to load storefront footer:', error);

                if (footerRequest === requestRecord && footerGeneration === requestGeneration) {
                    set({
                        footer: get().footer ?? emptyFooter,
                        error: 'Failed to load footer',
                        loading: false,
                        cacheIdentity: null,
                        loadedAt: null,
                    });
                }
            } finally {
                if (footerRequest === requestRecord) {
                    footerRequest = null;
                }
            }
        })();

        requestRecord = {
            generation: requestGeneration,
            identity,
            promise,
        };
        footerRequest = requestRecord;

        return promise;
    },

    refreshFooter: () => get().loadFooter({ force: true }),

    invalidateFooter: () => {
        footerGeneration += 1;
        set({ cacheIdentity: null, loadedAt: null, error: null });
    },
}));

export const selectFooterSections = (state: FooterState) => state.footer?.sections ?? [];
export const selectFooterSocialLinks = (state: FooterState) => state.footer?.socialLinks ?? [];

export function invalidateFooterCache(): void {
    useFooterStore.getState().invalidateFooter();
}

export function refreshFooterCache(): Promise<void> {
    return useFooterStore.getState().refreshFooter();
}
