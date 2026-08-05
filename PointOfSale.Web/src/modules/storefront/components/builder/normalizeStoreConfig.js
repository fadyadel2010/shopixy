/**
 * normalizeStoreConfig — ARCH-1.4R Gate E closure.
 *
 * Shared preview normalizer. Pure function: fills the
 * frozen StoreConfig shape from DEFAULT_STORE_CONFIG without mutating input.
 *
 * Consumers: BuilderThemeProvider and PreviewModal.
 */

import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig'

export function normalizeStoreConfig(storeConfig) {
    return {
        ...DEFAULT_STORE_CONFIG,
        ...storeConfig,
        theme: {
            ...DEFAULT_STORE_CONFIG.theme,
            ...(storeConfig?.theme || {}),
            colors: {
                ...DEFAULT_STORE_CONFIG.theme.colors,
                ...(storeConfig?.theme?.colors || {}),
            },
            font: {
                ...DEFAULT_STORE_CONFIG.theme.font,
                ...(storeConfig?.theme?.font || {}),
            },
            ui: {
                ...DEFAULT_STORE_CONFIG.theme.ui,
                ...(storeConfig?.theme?.ui || {}),
            },
            layout: {
                ...DEFAULT_STORE_CONFIG.theme.layout,
                ...(storeConfig?.theme?.layout || {}),
            },
        },
        behavior: {
            ...DEFAULT_STORE_CONFIG.behavior,
            ...(storeConfig?.behavior || {}),
        },
        pages: storeConfig?.pages || DEFAULT_STORE_CONFIG.pages,
    }
}

export default normalizeStoreConfig
