/**
 * Application Bootstrap
 * 
 * Centralized, deterministic bootstrap flow for the storefront.
 * 
 * Flow:
 * bootstrap()
 *   ↓
 * resolveTenant()
 *   ↓
 * initializeSession()
 *   ↓
 * loadStoreConfig()
 *   ↓
 * applyTheme()
 *   ↓
 * Ready to render
 * 
 * Rules:
 * - If tenant resolution fails → TenantNotFound
 * - If store-config fails (404/400) → StoreUnavailable
 * - If network fails → NetworkError with retry
 */

import { resolveTenant, setResolvedTenant } from './tenantResolver';
import { initializeSession } from './sessionManager';
import { initAffiliateRefTracker } from './affiliateRefTracker';
import {
    BootstrapErrorCode,
    createBootstrapError,
    mapApiErrorToBootstrapError,
    isNetworkError,
    type BootstrapError
} from './bootstrapErrors';
import { fetchStoreConfig } from '@/services/api';
import { useStoreConfigStore } from '@/store';
import { resolveActiveTheme } from '@/storefront/themes/ThemeResolver';
import { applyTheme, logLayoutVariants } from '@/theme';
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig';
import type { AxiosError } from 'axios';
import type { ApiErrorResponse } from '@/types';

/**
 * Bootstrap result type
 */
export type BootstrapResult = {
    success: true;
} | {
    success: false;
    error: BootstrapError;
};

/**
 * Bootstrap state (singleton)
 */
interface BootstrapState {
    isBootstrapped: boolean;
    isBootstrapping: boolean;
    error: BootstrapError | null;
}

const bootstrapState: BootstrapState = {
    isBootstrapped: false,
    isBootstrapping: false,
    error: null,
};

/**
 * Get current bootstrap state
 */
export function getBootstrapState(): Readonly<BootstrapState> {
    return { ...bootstrapState };
}

/**
 * Main bootstrap function
 * 
 * Orchestrates the entire bootstrap flow.
 * Idempotent - safe to call multiple times.
 */
export async function bootstrap(): Promise<BootstrapResult> {
    // Already bootstrapped
    if (bootstrapState.isBootstrapped) {
        if (import.meta.env.DEV) {
            console.log('[Bootstrap] Already bootstrapped, skipping');
        }
        return { success: true };
    }

    // Bootstrap in progress
    if (bootstrapState.isBootstrapping) {
        if (import.meta.env.DEV) {
            console.warn('[Bootstrap] Bootstrap already in progress');
        }
        // Wait for current bootstrap to complete
        await new Promise<void>((resolve) => {
            const checkInterval = setInterval(() => {
                if (!bootstrapState.isBootstrapping) {
                    clearInterval(checkInterval);
                    resolve();
                }
            }, 50);
        });

        return bootstrapState.error
            ? { success: false, error: bootstrapState.error }
            : { success: true };
    }

    bootstrapState.isBootstrapping = true;
    bootstrapState.error = null;

    if (import.meta.env.DEV) {
        console.log('[Bootstrap] Starting bootstrap flow...');
    }

    try {
        // Step 1: Resolve Tenant
        const tenantResult = resolveTenant();

        if (!tenantResult.success) {
            if (import.meta.env.DEV) {
                console.error('[Bootstrap] Tenant resolution failed');
            }
            bootstrapState.error = tenantResult.error;
            return { success: false, error: tenantResult.error };
        }

        setResolvedTenant(tenantResult.tenantId);

        if (import.meta.env.DEV) {
            console.log('[Bootstrap] Tenant resolved:', tenantResult.tenantId, `(source: ${tenantResult.source})`);
        }

        // Step 1.5: AF-12 — Capture affiliate ?ref= from browser URL
        // Must run BEFORE httpClient starts making API calls.
        initAffiliateRefTracker();

        // Step 2: Initialize Session
        const sessionId = initializeSession();

        if (import.meta.env.DEV) {
            console.log('[Bootstrap] Session initialized:', sessionId.substring(0, 8) + '...');
        }

        // Step 3: Load Store Config
        const store = useStoreConfigStore.getState();
        store.setLoading(true);

        try {
            const config = await fetchStoreConfig();

            if (import.meta.env.DEV) {
                const activeTheme = resolveActiveTheme(config.storefrontTheme);
                console.log('[Bootstrap] StoreConfig loaded:', {
                    storeName: config.storeName,
                    themeId: config.themeId,
                    storefrontTheme: config.storefrontTheme,
                    activeTheme: activeTheme.id,
                    headerVariant: config.theme.layout.headerVariant,
                    productCardVariant: config.theme.layout.productCardVariant,
                    commerceAiEnabled: config.commerceAiEnabled, // PHASE 6: Debug logging
                });
            }

            // Step 4: Apply Theme
            applyTheme(config.theme);

            if (import.meta.env.DEV) {
                console.log('[Bootstrap] Theme applied');
            }

            // Log layout variants (dev only)
            logLayoutVariants(config.theme.layout);

            // Store config in Zustand
            store.setConfig(config);

        } catch (error) {
            // Handle store config fetch error
            const axiosError = error as AxiosError<ApiErrorResponse>;

            if (import.meta.env.DEV) {
                console.error('[Bootstrap] Failed to load store config:', error);
            }

            const bootstrapError = mapApiErrorToBootstrapError(
                axiosError.response?.status,
                axiosError.response?.data?.code,
                error
            );

            // For retryable errors, apply default theme but still report error
            if (bootstrapError.retryable) {
                applyTheme(DEFAULT_STORE_CONFIG.theme);
                store.setError(bootstrapError.message);
            }

            bootstrapState.error = bootstrapError;
            return { success: false, error: bootstrapError };
        }

        // Bootstrap complete
        bootstrapState.isBootstrapped = true;

        if (import.meta.env.DEV) {
            console.log('[Bootstrap] Bootstrap complete ✓');
        }

        return { success: true };

    } catch (error) {
        // Unexpected error
        if (import.meta.env.DEV) {
            console.error('[Bootstrap] Unexpected error:', error);
        }

        const bootstrapError = isNetworkError(error)
            ? createBootstrapError(
                BootstrapErrorCode.NETWORK_ERROR,
                'Unable to connect to the server.',
                { cause: error, retryable: true }
            )
            : createBootstrapError(
                BootstrapErrorCode.UNKNOWN_ERROR,
                'An unexpected error occurred during startup.',
                { cause: error, retryable: true }
            );

        bootstrapState.error = bootstrapError;
        return { success: false, error: bootstrapError };

    } finally {
        bootstrapState.isBootstrapping = false;
    }
}

/**
 * Reset bootstrap state (for testing/retry)
 */
export function resetBootstrap(): void {
    bootstrapState.isBootstrapped = false;
    bootstrapState.isBootstrapping = false;
    bootstrapState.error = null;

    if (import.meta.env.DEV) {
        console.log('[Bootstrap] Bootstrap state reset');
    }
}

/**
 * Check if app is bootstrapped
 */
export function isBootstrapped(): boolean {
    return bootstrapState.isBootstrapped;
}

// Re-export for convenience
export { BootstrapErrorCode } from './bootstrapErrors';
export type { BootstrapError } from './bootstrapErrors';
