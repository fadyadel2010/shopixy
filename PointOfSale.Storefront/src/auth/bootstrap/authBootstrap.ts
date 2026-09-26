/**
 * Auth Bootstrap
 * Phase F1.1: Auth UI Hardening
 * 
 * Initializes authentication on app startup:
 * 1. Sets up HTTP client 401 interceptor
 * 2. Checks current auth state via /auth/me
 * 3. Attempts silent refresh if needed
 * 4. Resolves final auth state without flicker
 * 
 * SECURITY: Never exposes tokens, all state flows through authStore.
 */

import { useAuthStore } from '../store/authStore';
import { setAuthInterceptor } from '@/services/api/httpClient';
import { isThemePreviewMode } from '@/storefront/preview/themePreviewMode';

let isInitialized = false;
let initPromise: Promise<void> | null = null;

/**
 * Initialize auth system
 * Should be called once during app bootstrap, after tenant resolution.
 * 
 * Uses a mutex to prevent concurrent initialization attempts
 * (which can happen with React Strict Mode in development).
 */
export async function initializeAuth(): Promise<void> {
    // If already fully initialized, skip
    if (isInitialized) {
        console.log('[Auth] Already initialized, skipping');
        return;
    }

    // If initialization is in progress, wait for it
    if (initPromise) {
        console.log('[Auth] Initialization already in progress, waiting...');
        return initPromise;
    }

    console.log('[Auth] Initializing auth system...');

    // Create and store the initialization promise
    initPromise = (async () => {
        const authStore = useAuthStore.getState();

        if (isThemePreviewMode()) {
            authStore.setUnauthenticated();
            isInitialized = true;
            return;
        }

        // Set up HTTP client 401 interceptor
        setAuthInterceptor({
            refreshToken: async () => {
                try {
                    return await authStore.refreshAuth({ source: 'interceptor' });
                } catch {
                    return 'unauthenticated';
                }
            },
            forceLogout: () => {
                // Clear auth state and redirect to login
                authStore.logout();
                // Don't redirect here - let the components handle it
            },
        });

        // Check current auth state
        try {
            await authStore.checkAuth();
            console.log('[Auth] Initial auth check complete, status:', useAuthStore.getState().status);
        } catch (error) {
            console.warn('[Auth] Initial auth check failed:', error);
            // Auth check failure is expected for guests - not an error
        }

        // Load social providers in background
        authStore.loadProviders().catch(() => {
            // Non-critical failure
        });

        isInitialized = true;
        console.log('[Auth] Auth system initialized');
    })();

    try {
        await initPromise;
    } finally {
        // Clear the promise after completion (success or failure)
        // but keep isInitialized true if it succeeded
        initPromise = null;
    }
}

/**
 * Check if auth system is initialized
 */
export function isAuthInitialized(): boolean {
    return isInitialized;
}

/**
 * Reset auth initialization (for testing)
 */
export function resetAuthInit(): void {
    isInitialized = false;
    initPromise = null;
}
