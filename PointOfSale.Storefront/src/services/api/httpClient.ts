/**
 * HTTP Client
 * 
 * Configured Axios instance with:
 * - Automatic request-scoped session/auth headers
 * - Request rejection if bootstrap context is not ready
 * - Centralized error normalization
 * - 401 handling with silent refresh (Phase F1.1)
 * 
 * NO hardcoded Tenant IDs.
 * All headers come from request context.
 */

import axios, { AxiosInstance, AxiosError, InternalAxiosRequestConfig } from 'axios';
import type { ApiErrorResponse } from '@/types';
import { getRequestHeaders, isRequestContextReady } from '@/bootstrap';
import { getAffiliateRefCode } from '@/bootstrap/affiliateRefTracker';
import { useLanguageStore } from '@/i18n';
import { appendCsrfHeader } from './csrf';

/**
 * Normalized error response
 */
export interface NormalizedError {
    status: number | undefined;
    code?: string;
    message?: string;
    isNetworkError: boolean;
    isServerError: boolean;
    isClientError: boolean;
    retryable: boolean;
    isAuthError: boolean;
}

/**
 * Normalize axios error to a consistent format
 */
export function normalizeError(error: AxiosError<ApiErrorResponse>): NormalizedError {
    const status = error.response?.status;
    const code = error.response?.data?.code;
    const message = error.response?.data?.message || error.message;

    const isNetworkError = !status && error.message?.trim().toLowerCase() === 'network error';
    const isServerError = status !== undefined && status >= 500;
    const isClientError = status !== undefined && status >= 400 && status < 500;
    const isAuthError = status === 401;

    return {
        status,
        code,
        message,
        isNetworkError,
        isServerError,
        isClientError,
        retryable: isNetworkError || isServerError,
        isAuthError,
    };
}

// Base URL - defaults to proxy path
const BASE_URL = import.meta.env.VITE_API_URL || '';
const THEME_PREVIEW_TOKEN_HEADER = 'X-Theme-Preview-Token';
type AuthRefreshResult = 'authenticated' | 'degraded' | 'unauthenticated';

// Track if we're currently refreshing to prevent loops
let refreshPromise: Promise<AuthRefreshResult> | null = null;
let forcedLogoutPromise: Promise<void> | null = null;

// Callback to get auth store actions (set lazily to avoid circular imports)
let authStoreActions: {
    refreshToken: () => Promise<AuthRefreshResult>;
    forceLogout: () => void;
} | null = null;

/**
 * Set auth store actions for 401 handling
 * Called during app initialization to avoid circular imports
 */
export function setAuthInterceptor(actions: {
    refreshToken: () => Promise<AuthRefreshResult>;
    forceLogout: () => void;
}) {
    authStoreActions = actions;
    forcedLogoutPromise = null;
}

export function isAuthEndpoint(url: string): boolean {
    return url.includes('/api/storefront/auth/');
}

function getThemePreviewToken(): string | null {
    if (typeof window === 'undefined') {
        return null;
    }

    const token = new URLSearchParams(window.location.search).get('themePreviewToken');
    const normalized = token?.trim();
    return normalized ? normalized : null;
}

function isReadOnlyStorefrontRequest(config: InternalAxiosRequestConfig): boolean {
    const method = (config.method || 'get').toLowerCase();
    const url = config.url || '';

    return (method === 'get' || method === 'head') &&
        url.startsWith('/api/storefront/') &&
        !url.startsWith('/api/storefront/cart') &&
        !url.startsWith('/api/storefront/account') &&
        !url.startsWith('/api/storefront/orders');
}

function triggerForcedLogout(): Promise<void> {
    if (!authStoreActions) {
        return Promise.resolve();
    }

    if (!forcedLogoutPromise) {
        forcedLogoutPromise = Promise.resolve().then(() => {
            authStoreActions?.forceLogout();
        });
    }

    return forcedLogoutPromise;
}

/**
 * Handle 401 error with silent refresh attempt
 */
async function handle401Error(error: AxiosError<ApiErrorResponse>): Promise<AuthRefreshResult> {
    // Skip if no auth store configured
    if (!authStoreActions) {
        return 'unauthenticated';
    }

    // Skip refresh for auth endpoints (they handle their own auth)
    const url = error.config?.url || '';
    if (isAuthEndpoint(url)) {
        return 'unauthenticated';
    }

    if (!refreshPromise) {
        refreshPromise = (async () => {
            try {
                const result = await authStoreActions.refreshToken();
                if (result === 'authenticated') {
                    forcedLogoutPromise = null;
                }

                return result;
            } catch {
                return 'unauthenticated';
            } finally {
                refreshPromise = null;
            }
        })();
    }

    return await refreshPromise;
}

/**
 * Create configured Axios instance
 */
const createHttpClient = (): AxiosInstance => {
    const client = axios.create({
        baseURL: BASE_URL,
        timeout: 30000,
        headers: {
            'Content-Type': 'application/json',
        },
        // Include credentials for cookies (refresh token)
        withCredentials: true,
    });

    // Request interceptor - add headers and validate context
    client.interceptors.request.use(
        (config: InternalAxiosRequestConfig) => {
            // Check if request context is ready
            if (!isRequestContextReady()) {
                // Allow store-config fetch during bootstrap (it's needed to complete bootstrap)
                const isBootstrapRequest =
                    config.url?.includes('/store-config') ||
                    config.url?.includes('/theme-preview/config');

                if (!isBootstrapRequest) {
                    const error = new Error(
                        '[HTTP] Request rejected: Tenant not resolved. ' +
                        'Ensure bootstrap completes before making API calls.'
                    );
                    console.error(error.message);
                    return Promise.reject(error);
                }
            }

            const themePreviewToken = getThemePreviewToken();
            if (themePreviewToken && isReadOnlyStorefrontRequest(config)) {
                config.headers[THEME_PREVIEW_TOKEN_HEADER] = themePreviewToken;
            }

            // Attach headers from request context
            try {
                const headers = getRequestHeaders();
                config.headers['X-Tenant-Id'] = headers['X-Tenant-Id'];
                config.headers['X-Session-Id'] = headers['X-Session-Id'];

                // PHASE 10.4: Include customer ID for authenticated requests
                // This is CRITICAL for cart ownership - without this header,
                // cart operations will use session ownership even for logged-in users
                if (headers['X-Customer-Id']) {
                    config.headers['X-Customer-Id'] = headers['X-Customer-Id'];
                }

                // AF-12: Include affiliate referral code for Affiliate Mode activation
                // The SPA captures ?ref=CODE from the browser URL during bootstrap.
                // Without this header, the backend middleware can't see the ref code
                // because the SPA makes its own API calls without ?ref= in the URL.
                const affiliateRef = getAffiliateRefCode();
                if (affiliateRef) {
                    config.headers['X-Affiliate-Ref'] = affiliateRef;
                }

                // LOCALIZATION: Inject current language into every API request
                // This ensures the backend resolves localized content (homepage, CMS, footer, products)
                // for the user's selected language. Matches the pattern used by CommerceHub http-client.
                const currentLanguage = useLanguageStore.getState().language;
                config.headers['Accept-Language'] = currentLanguage;
                config.params = { ...config.params, lang: currentLanguage };
                const csrfHeaders = appendCsrfHeader({}, config.method);
                if (csrfHeaders['X-CSRF-Token']) {
                    config.headers['X-CSRF-Token'] = csrfHeaders['X-CSRF-Token'];
                }
            } catch (error) {
                // During bootstrap, only tenant might not be set yet
                // The bootstrap flow handles this case
                if (import.meta.env.DEV) {
                    console.warn('[HTTP] Could not attach headers:', error);
                }
            }

            if (import.meta.env.DEV) {
                console.log(`[HTTP] ${config.method?.toUpperCase()} ${config.url}`, {
                    tenantId: config.headers['X-Tenant-Id']?.toString().substring(0, 8) + '...',
                    sessionId: config.headers['X-Session-Id']?.toString().substring(0, 8) + '...',
                    customerId: config.headers['X-Customer-Id']
                        ? config.headers['X-Customer-Id']?.toString().substring(0, 8) + '...'
                        : undefined,
                });
            }

            return config;
        },
        (error) => {
            console.error('[HTTP] Request error:', error);
            return Promise.reject(error);
        }
    );

    // Response interceptor - error handling with 401 refresh
    client.interceptors.response.use(
        (response) => {
            if (import.meta.env.DEV) {
                console.log(`[HTTP] Response ${response.status} from ${response.config.url}`);
            }
            return response;
        },
        async (error: AxiosError<ApiErrorResponse>) => {
            const normalized = normalizeError(error);

            if (import.meta.env.DEV) {
                console.error('[HTTP] Response error:', {
                    status: normalized.status,
                    code: normalized.code,
                    message: normalized.message,
                    url: error.config?.url,
                    isNetworkError: normalized.isNetworkError,
                    isAuthError: normalized.isAuthError,
                    retryable: normalized.retryable,
                });
            }

            // Handle 401 with silent refresh attempt
            if (normalized.isAuthError && error.config) {
                // Check if this is already a retry
                const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

                // Skip refresh/logout handling for auth endpoints - they handle their own auth
                const url = error.config?.url || '';

                if (!originalRequest._retry && !isAuthEndpoint(url)) {
                    originalRequest._retry = true;

                    const refreshResult = await handle401Error(error);

                    if (refreshResult === 'authenticated') {
                        // Retry the original request
                        if (import.meta.env.DEV) {
                            console.log('[HTTP] Retrying request after token refresh:', originalRequest.url);
                        }
                        return client(originalRequest);
                    }

                    if (refreshResult === 'unauthenticated') {
                        // Refresh failed - force logout
                        if (authStoreActions) {
                            console.warn('[HTTP] Token refresh failed, forcing logout');
                            await triggerForcedLogout();
                        }
                    }
                }
            }

            // Attach normalized error for easier handling
            (error as any).normalized = normalized;

            return Promise.reject(error);
        }
    );

    return client;
};

export const httpClient = createHttpClient();

/**
 * Check if error is a tenant-related error
 */
export function isTenantError(error: unknown): boolean {
    if (error instanceof AxiosError) {
        const code = error.response?.data?.code;
        return code === 'TENANT_HEADER_MISSING' ||
            code === 'INVALID_TENANT_ID' ||
            code === 'TENANT_NOT_FOUND';
    }
    return false;
}

/**
 * Check if error is retryable
 */
export function isRetryableError(error: unknown): boolean {
    if (error instanceof AxiosError) {
        const normalized = normalizeError(error);
        return normalized.retryable;
    }
    return false;
}

/**
 * Check if error is an auth error (401)
 */
export function isAuthError(error: unknown): boolean {
    if (error instanceof AxiosError) {
        return error.response?.status === 401;
    }
    return false;
}

