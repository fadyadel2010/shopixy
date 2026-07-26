/**
 * Auth Store (Zustand)
 * Phase F1: Storefront Auth UI
 *
 * Centralized authentication state management.
 *
 * SECURITY RULES:
 * - Access and refresh tokens are stored in HttpOnly cookies
 * - Tokens are never accessible in JavaScript
 * - Status = 'unknown' until /auth/me confirms state
 * - Never assume auth state without API confirmation
 */

import { AxiosError } from 'axios';
import { create } from 'zustand';
import type {
    AuthStatus,
    OnlineCustomer,
    AuthError,
    AuthResponse,
    LoginRequest,
    RegisterRequest,
} from '../types';
import * as authApi from '../api/authApi';
import { isForceLogoutError } from '../types';
import { clearSession, setCustomerId } from '@/bootstrap';
import {
    emitAuthDegradedRecovery,
    emitAuthStateDegraded,
    emitCheckoutStateLeakPrevented,
} from '@/features/auth/authTelemetry';

type AuthRefreshResult = 'authenticated' | 'degraded' | 'unauthenticated';

/**
 * Auth store state interface
 */
interface AuthState {
    // Core state
    status: AuthStatus;
    customer: OnlineCustomer | null;
    isLoading: boolean;
    error: AuthError | null;

    // Social providers (includes Client IDs for OAuth initialization)
    availableProviders: {
        google: boolean;
        facebook: boolean;
        googleClientId?: string;
        facebookAppId?: string;
    } | null;

    // Actions
    login: (request: LoginRequest) => Promise<void>;
    register: (request: RegisterRequest) => Promise<void>;
    logout: () => Promise<void>;
    checkAuth: (options?: { silent?: boolean; reason?: string }) => Promise<void>;
    refreshAuth: (options?: { source?: string }) => Promise<AuthRefreshResult>;
    googleLogin: (idToken: string) => Promise<void>;
    facebookLogin: (accessToken: string) => Promise<void>;
    loadProviders: () => Promise<void>;
    clearError: () => void;

    // Internal actions
    setAuthenticated: (response: AuthResponse) => void;
    setUnauthenticated: () => void;
    setError: (error: AuthError) => void;
}

/**
 * Refresh mutex to prevent concurrent refresh attempts.
 * This is critical because:
 * 1. React Strict Mode calls effects twice in development
 * 2. Multiple components might try to refresh simultaneously
 * 3. Concurrent refreshes with the same token cause TOKEN_REUSE_DETECTED
 */
let refreshPromise: Promise<AuthRefreshResult> | null = null;
let authRecoveryTimer: ReturnType<typeof setTimeout> | null = null;
let authRecoveryAttempt = 0;

const AUTH_RECOVERY_BASE_DELAY_MS = 5000;
const AUTH_RECOVERY_MAX_DELAY_MS = 30000;

function clearAuthenticatedContext(): void {
    setCustomerId(null);
}

function removeCheckoutStorageArtifacts(): void {
    if (typeof window === 'undefined') {
        return;
    }

    try {
        window.localStorage.removeItem('checkout-state');
    } catch {
        // Storage may be unavailable in hardened browser contexts.
    }

    try {
        window.sessionStorage.removeItem('checkout-sensitive-session');
        window.sessionStorage.removeItem('active-checkout-order');
        window.sessionStorage.removeItem('paymob_order_id');
        window.sessionStorage.removeItem('paymob_return');
    } catch {
        // Storage may be unavailable in hardened browser contexts.
    }
}

async function clearCheckoutStateForAuthTransition(trigger: string): Promise<void> {
    removeCheckoutStorageArtifacts();
    emitCheckoutStateLeakPrevented(trigger);

    try {
        const { useCheckoutStore } = await import('@/store/checkoutStore');
        useCheckoutStore.getState().reset();
    } catch (error) {
        // Do not re-throw: checkout state cleanup is a side effect that should not
        // invalidate auth operations. Auth refresh callers should not fail due to
        // non-auth failures in checkout store cleanup.
        console.warn('[Auth] Failed to reset checkout state during auth transition:', {
            trigger,
            error: error instanceof Error ? error.message : String(error),
        });
    }
}

function isUnauthorizedError(error: unknown): boolean {
    return error instanceof AxiosError && error.response?.status === 401;
}

function clearScheduledAuthRetry(): void {
    if (authRecoveryTimer) {
        clearTimeout(authRecoveryTimer);
        authRecoveryTimer = null;
    }
}

function resetAuthRecoveryState(): void {
    clearScheduledAuthRetry();
    authRecoveryAttempt = 0;
}

function scheduleSilentAuthRetry(retry: () => Promise<void>): void {
    if (authRecoveryTimer) {
        return;
    }

    const delayMs = Math.min(
        AUTH_RECOVERY_BASE_DELAY_MS * Math.pow(2, authRecoveryAttempt),
        AUTH_RECOVERY_MAX_DELAY_MS
    );

    authRecoveryTimer = setTimeout(() => {
        authRecoveryTimer = null;
        void retry().catch(() => {
            // A later degraded transition will schedule the next retry.
        });
    }, delayMs);

    authRecoveryAttempt += 1;
}

function setDegradedState(
    set: (partial: Partial<AuthState>) => void,
    get: () => AuthState,
    authError: AuthError,
    source: string
): void {
    const existingCustomer = get().customer;
    clearAuthenticatedContext();
    set({
        status: 'degraded',
        customer: existingCustomer,
        isLoading: false,
        error: authError,
    });
    emitAuthStateDegraded(source, authError.code, !!existingCustomer);
    scheduleSilentAuthRetry(() => get().checkAuth({ silent: true, reason: 'scheduled_retry' }));
}

/**
 * Create auth store
 */
export const useAuthStore = create<AuthState>((set, get) => ({
    // Initial state - unknown until verified
    status: 'unknown',
    customer: null,
    isLoading: false,
    error: null,
    availableProviders: null,

    /**
     * Login with email and password
     */
    login: async (request: LoginRequest) => {
        set({ isLoading: true, error: null });

        try {
            const response = await authApi.login(request);
            get().setAuthenticated(response);
        } catch (error) {
            const authError = authApi.normalizeAuthError(error);
            clearAuthenticatedContext();
            set({
                isLoading: false,
                error: authError,
                status: 'unauthenticated',
                customer: null,
            });
            throw authError;
        }
    },

    /**
     * Register new account
     */
    register: async (request: RegisterRequest) => {
        set({ isLoading: true, error: null });

        try {
            const response = await authApi.register(request);
            get().setAuthenticated(response);
        } catch (error) {
            const authError = authApi.normalizeAuthError(error);
            clearAuthenticatedContext();
            set({
                isLoading: false,
                error: authError,
                status: 'unauthenticated',
                customer: null,
            });
            throw authError;
        }
    },

    /**
     * Logout - clear all state including cart
     *
     * PHASE C1 CART HARDENING:
     * - Clears cart state immediately on logout
     * - Clears checkout state immediately on logout
     * - Prevents cart data from persisting across different users
     */
    logout: async () => {
        resetAuthRecoveryState();
        clearAuthenticatedContext();

        // Clear auth state immediately (don't wait for API)
        set({
            status: 'unauthenticated',
            customer: null,
            error: null,
            isLoading: false,
        });

        // Clear checkout storage artifacts early, before any lazy imports that could throw
        removeCheckoutStorageArtifacts();

        // Clear cart state immediately (import avoided for circular dependency)
        // We use getState pattern to access external stores safely
        try {
            const { useCartStore } = await import('@/store/cartStore');
            const { useCheckoutStore } = await import('@/store/checkoutStore');

            clearSession();

            // Reset cart to empty state
            useCartStore.setState({
                cart: null,
                isLoading: false,
                error: null,
                updatingItemId: null,
                isInitialized: false,
            });

            // Reset checkout state
            useCheckoutStore.getState().reset();

            console.log('[Auth] Cart and checkout state cleared on logout');
        } catch (error) {
            console.warn('[Auth] Failed to clear cart state on logout:', error);
        }

        // Call API to revoke cookies/session
        try {
            await authApi.logout();
        } catch (error) {
            // Logout API errors are non-blocking
            console.warn('[Auth] Logout API call failed:', error);
        }
    },

    /**
     * Check authentication status
     * Called on app boot to verify if user is authenticated
     */
    checkAuth: async (options = {}) => {
        const { refreshAuth } = get();
        const { silent = false } = options;

        if (!silent) {
            set({ isLoading: true });
        }

        try {
            const customer = await authApi.getMe();
            const previousCustomer = get().customer;
            const wasDegraded = get().status === 'degraded';

            if (previousCustomer && previousCustomer.id !== customer.id) {
                await clearCheckoutStateForAuthTransition('user_switch_check_auth');
            }

            resetAuthRecoveryState();
            set({
                status: 'authenticated',
                customer,
                isLoading: false,
                error: null,
            });

            // PHASE 10.4: Restore customer ID in request context on page refresh
            // This ensures subsequent API calls include X-Customer-Id header
            setCustomerId(customer.id);
            if (wasDegraded) {
                emitAuthDegradedRecovery(options.reason || 'check_auth', 'recovered');
            }
        } catch (error) {
            const authError = authApi.normalizeAuthError(error);

            if (
                isUnauthorizedError(error) ||
                authError.code === 'TOKEN_EXPIRED' ||
                authError.code === 'INVALID_TOKEN' ||
                authError.code === 'MISSING_TOKEN'
            ) {
                const refreshed = await refreshAuth({ source: 'check_auth' });

                if (refreshed === 'unauthenticated') {
                    resetAuthRecoveryState();
                    clearAuthenticatedContext();
                    set({
                        status: 'unauthenticated',
                        customer: null,
                        isLoading: false,
                    });
                }

                return;
            }

            if (isForceLogoutError(authError.code)) {
                resetAuthRecoveryState();
                clearAuthenticatedContext();
                set({
                    status: 'unauthenticated',
                    customer: null,
                    error: authError,
                    isLoading: false,
                });
                return;
            }

            if (authError.code === 'NETWORK_ERROR' || authError.code === 'UNKNOWN_ERROR') {
                setDegradedState(set, get, authError, 'check_auth');
                return;
            }

            // Only clear auth state for actual auth failures (403, invalid credentials, etc.)
            resetAuthRecoveryState();
            clearAuthenticatedContext();
            set({
                status: 'unauthenticated',
                customer: null,
                isLoading: false,
                error: authError,
            });
        }
    },

    /**
     * Refresh secure session using the refresh cookie.
     * Uses a mutex to prevent concurrent refresh attempts which would
     * cause TOKEN_REUSE_DETECTED security incidents.
     */
    refreshAuth: async (options = {}) => {
        if (refreshPromise) {
            console.log('[Auth] Refresh already in progress, waiting...');
            return refreshPromise;
        }

        refreshPromise = (async () => {
            try {
                console.log('[Auth] Starting secure session refresh...');
                const wasDegraded = get().status === 'degraded';
                const response = await authApi.refreshToken();
                const previousCustomer = get().customer;

                if (previousCustomer && previousCustomer.id !== response.customer.id) {
                    await clearCheckoutStateForAuthTransition('user_switch_refresh');
                }

                resetAuthRecoveryState();
                set({
                    status: 'authenticated',
                    customer: response.customer,
                    error: null,
                    isLoading: false,
                });

                // PHASE 10.4: Update customer ID in request context on refresh
                // This ensures subsequent API calls include X-Customer-Id header
                setCustomerId(response.customer.id);
                if (wasDegraded) {
                    emitAuthDegradedRecovery(options.source || 'refresh_auth', 'recovered');
                }

                console.log('[Auth] Secure session refresh successful');
                return 'authenticated';
            } catch (error) {
                const authError = authApi.normalizeAuthError(error);
                console.warn('[Auth] Secure session refresh failed:', authError.code);

                if (authError.code === 'NETWORK_ERROR' || authError.code === 'UNKNOWN_ERROR') {
                    setDegradedState(set, get, authError, options.source || 'refresh_auth');
                    return 'degraded';
                }

                resetAuthRecoveryState();
                clearAuthenticatedContext();
                set({
                    status: 'unauthenticated',
                    customer: null,
                    error: isForceLogoutError(authError.code) ? authError : null,
                    isLoading: false,
                });

                if (authError.code === 'TOKEN_REUSE_DETECTED') {
                    set({ error: authError });
                }

                return 'unauthenticated';
            } finally {
                refreshPromise = null;
            }
        })();

        return refreshPromise;
    },

    /**
     * Google login
     */
    googleLogin: async (idToken: string) => {
        set({ isLoading: true, error: null });

        try {
            const response = await authApi.googleLogin(idToken);
            get().setAuthenticated(response);
        } catch (error) {
            const authError = authApi.normalizeAuthError(error);
            clearAuthenticatedContext();
            set({
                isLoading: false,
                error: authError,
                status: 'unauthenticated',
                customer: null,
            });
            throw authError;
        }
    },

    /**
     * Facebook login
     */
    facebookLogin: async (accessToken: string) => {
        set({ isLoading: true, error: null });

        try {
            const response = await authApi.facebookLogin(accessToken);
            get().setAuthenticated(response);
        } catch (error) {
            const authError = authApi.normalizeAuthError(error);
            clearAuthenticatedContext();
            set({
                isLoading: false,
                error: authError,
                status: 'unauthenticated',
                customer: null,
            });
            throw authError;
        }
    },

    /**
     * Load available social providers
     */
    loadProviders: async () => {
        try {
            const providers = await authApi.getProviders();
            set({ availableProviders: providers });
        } catch (error) {
            console.warn('[Auth] Failed to load providers:', error);
            // Non-blocking error - providers will show as unavailable
            set({ availableProviders: { google: false, facebook: false, googleClientId: undefined, facebookAppId: undefined } });
        }
    },

    /**
     * Clear error state
     */
    clearError: () => {
        set({ error: null });
    },

    /**
     * Set authenticated state from auth response
     *
     * PHASE 10.4: Cart Ownership Migration
     * - Sets customer ID in request context for all API calls
     * - Triggers cart merge to transfer guest cart to customer
     */
    setAuthenticated: (response: AuthResponse) => {
        resetAuthRecoveryState();
        const previousCustomer = get().customer;
        const authenticatedCustomerId = response.customer.id;
        const trigger = previousCustomer && previousCustomer.id !== response.customer.id
            ? 'user_switch_set_authenticated'
            : 'login_or_register';

        // Clear checkout state asynchronously before proceeding with auth state updates
        void clearCheckoutStateForAuthTransition(trigger).catch((error) => {
            console.error('[Auth] Failed to clear checkout state, continuing with auth transition:', error);
        });

        set({
            status: 'authenticated',
            customer: response.customer,
            error: null,
            isLoading: false,
        });

        // PHASE 10.4: Update request context with customer ID
        // CRITICAL: This must happen BEFORE cart merge to ensure the merge API
        // receives the X-Customer-Id header along with X-Session-Id
        setCustomerId(response.customer.id);

        console.log('[Auth] Customer context set:', response.customer.id.substring(0, 8) + '...');

        // PHASE 10.4: Trigger cart merge (non-blocking)
        // Merges guest session cart into customer cart if exists
        (async () => {
            let useCartStore: typeof import('@/store/cartStore').useCartStore | undefined;
            try {
                const { mergeCart } = await import('@/services/api/storefrontApi');
                const cartStoreModule = await import('@/store/cartStore');
                useCartStore = cartStoreModule.useCartStore;

                console.log('[Auth] Starting cart merge after login...');
                useCartStore.setState({ cartMergeInProgress: true });
                const mergedCart = await mergeCart();

                if (get().status !== 'authenticated' || get().customer?.id !== authenticatedCustomerId) {
                    console.log('[Auth] Auth context changed during cart merge; skipping cart state update.');
                    return;
                }

                // Update cart store with merged cart
                useCartStore.setState({
                    cart: mergedCart,
                    isLoading: false,
                    isInitialized: true,
                    error: null,
                });

                console.log('[Auth] Cart merged successfully:', {
                    itemCount: mergedCart.items?.length ?? 0,
                    ownerType: mergedCart.ownerType,
                    hasCoupon: !!mergedCart.appliedCouponCode,
                });
            } catch (error) {
                // Cart merge is non-blocking - log and continue
                // This can fail if there's no guest cart (which is fine)
                console.warn('[Auth] Cart merge skipped or failed (non-blocking):', error);

                // Refresh cart to get current state
                try {
                    if (!useCartStore) {
                        const cartStoreModule = await import('@/store/cartStore');
                        useCartStore = cartStoreModule.useCartStore;
                    }

                    if (get().status === 'authenticated' && get().customer?.id === authenticatedCustomerId) {
                        await useCartStore.getState().fetchCart();
                    }
                } catch (cartError) {
                    console.warn('[Auth] Cart fetch after merge failure also failed:', cartError);
                    
                    // Surface the failure to the UI so cart store consumers can react
                    if (useCartStore) {
                        useCartStore.setState({
                            error: 'Failed to load your cart after login. Please refresh the page.',
                            isLoading: false,
                            isInitialized: true,
                        });
                    }
                }
            } finally {
                useCartStore?.setState({ cartMergeInProgress: false });
            }
        })();
    },

    /**
     * Set unauthenticated state
     */
    setUnauthenticated: () => {
        resetAuthRecoveryState();
        clearAuthenticatedContext();
        set({
            status: 'unauthenticated',
            customer: null,
            error: null,
            isLoading: false,
        });
    },

    /**
     * Set error state
     */
    setError: (error: AuthError) => {
        set({ error, isLoading: false });
    },
}));

// Selectors for optimized re-renders
export const selectAuthStatus = (state: AuthState) => state.status;
export const selectCustomer = (state: AuthState) => state.customer;
export const selectIsAuthenticated = (state: AuthState) => state.status === 'authenticated';
export const selectIsAuthDegraded = (state: AuthState) => state.status === 'degraded';
export const selectIsLoading = (state: AuthState) => state.isLoading;
export const selectAuthError = (state: AuthState) => state.error;
export const selectAvailableProviders = (state: AuthState) => state.availableProviders;
export const selectEmailVerified = (state: AuthState) => state.customer?.emailVerified ?? false;
