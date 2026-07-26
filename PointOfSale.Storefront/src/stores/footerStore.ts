import { create } from 'zustand';
import { fetchStorefrontFooter } from '@/services/api/storefrontApi';
import type { StorefrontFooter } from '@/types';

interface FooterState {
    footer: StorefrontFooter | null;
    loading: boolean;
    error: string | null;
    loadFooter: () => Promise<void>;
}

const emptyFooter: StorefrontFooter = {
    footerSource: 'Derived',
    socialSource: 'None',
    sections: [],
    socialLinks: [],
    generatedAtUtc: new Date().toISOString()
};

export const useFooterStore = create<FooterState>((set, get) => ({
    footer: null,
    loading: false,
    error: null,

    loadFooter: async () => {
        if (get().footer && !get().error) {
            return;
        }

        set({ loading: true, error: null });

        try {
            const footer = await fetchStorefrontFooter();
            set({ footer, loading: false });
        } catch (error) {
            console.error('Failed to load storefront footer:', error);
            set({ footer: emptyFooter, error: 'Failed to load footer', loading: false });
        }
    }
}));

export const selectFooterSections = (state: FooterState) => state.footer?.sections ?? [];
export const selectFooterSocialLinks = (state: FooterState) => state.footer?.socialLinks ?? [];
