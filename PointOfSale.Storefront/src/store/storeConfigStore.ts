/**
 * Store Config Store (Zustand)
 * 
 * Holds StoreConfig state loaded at app bootstrap.
 * NO business logic - just state container.
 */

import { create } from 'zustand';
import type { StoreConfig } from '../types';
import { DEFAULT_STORE_CONFIG } from '../types/storeConfig';

export interface StoreConfigState {
    // Data
    config: StoreConfig;

    // Loading states
    isLoading: boolean;
    isLoaded: boolean;

    // Error state
    error: string | null;

    // Runtime-fetched configuration and controlled preview candidates have
    // different refresh authority.
    configSource: 'runtime' | 'preview' | null;

    // Actions
    /** Commit a controlled editor/preview candidate. */
    setConfig: (config: StoreConfig) => void;
    /** Commit configuration loaded by the canonical Runtime refresh owner. */
    setRuntimeConfig: (config: StoreConfig) => void;
    setLoading: (loading: boolean) => void;
    setError: (error: string | null) => void;
    reset: () => void;
}

export const useStoreConfigStore = create<StoreConfigState>((set) => ({
    // Initial state using defaults
    config: DEFAULT_STORE_CONFIG,
    isLoading: true,
    isLoaded: false,
    error: null,
    configSource: null,

    // Actions
    setConfig: (config) => {
        if (import.meta.env.DEV) {
            console.log('[StoreConfigStore] setConfig:', {
                storefrontTheme: config.storefrontTheme,
                themeId: config.themeId,
                storeName: config.storeName,
            });
        }

        set({
            config,
            isLoading: false,
            isLoaded: true,
            error: null,
            configSource: 'preview'
        });
    },

    setRuntimeConfig: (config) => set({
        config,
        isLoading: false,
        isLoaded: true,
        error: null,
        configSource: 'runtime'
    }),

    setLoading: (isLoading) => set({ isLoading }),

    setError: (error) => set({
        error,
        isLoading: false
    }),

    reset: () => set({
        config: DEFAULT_STORE_CONFIG,
        isLoading: true,
        isLoaded: false,
        error: null,
        configSource: null
    }),
}));

/**
 * Selectors for common use cases
 */
export const selectConfig = (state: StoreConfigState) => state.config;
export const selectStoreName = (state: StoreConfigState) => state.config.storeName;
export const selectTheme = (state: StoreConfigState) => state.config.theme;
export const selectLayout = (state: StoreConfigState) => state.config.theme.layout;
export const selectUi = (state: StoreConfigState) => state.config.theme.ui;
export const selectBehavior = (state: StoreConfigState) => state.config.behavior;
export const selectIsConfigLoaded = (state: StoreConfigState) => state.isLoaded;
export const selectStorefrontStatus = (state: StoreConfigState) => state.config.storefrontStatus;
export const selectMaintenanceMessage = (state: StoreConfigState) => state.config.maintenanceMessage;
export const selectCurrency = (state: StoreConfigState) => state.config.currency;
// PHASE 6: Commerce AI Feature Flag Selector
// PHASE 6: Commerce AI Feature Flag Selector
export const selectCommerceAiEnabled = (state: StoreConfigState) => state.config.commerceAiEnabled ?? false;
// PHASE 7: Commerce AI Experience Config
export const selectCommerceAiWidgetStyle = (state: StoreConfigState) => state.config.commerceAiWidgetStyle ?? 'minimal';
export const selectCommerceAiWidgetPosition = (state: StoreConfigState) => state.config.commerceAiWidgetPosition ?? 'bottom-right';
export const selectCommerceAiAutoGreetingEnabled = (state: StoreConfigState) => state.config.commerceAiAutoGreetingEnabled ?? false;
export const selectCommerceAiAutoGreetingMessage = (state: StoreConfigState) => state.config.commerceAiAutoGreetingMessage ?? '';
export const selectCommerceAiPersonality = (state: StoreConfigState) => state.config.commerceAiPersonality ?? 'friendly';
