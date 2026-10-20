/**
 * Auth API Module
 * Phase F1: Storefront Auth UI
 * 
 * API calls for authentication.
 * Tenant scope is resolved server-side; shared auth headers are handled by httpClient.
 * 
 * SECURITY:
 * - Access and refresh tokens are stored in HttpOnly cookies
 * - Tokens are never exposed to browser JavaScript
 * - All requests use credentials: 'include' for cookies
 */

import { httpClient, isHttpClientError } from '@/services/api/httpClient';
import type {
    AuthResponse,
    LoginRequest,
    RegisterRequest,
    AvailableProviders,
    ForgotPasswordRequest,
    ResetPasswordRequest,
    OnlineCustomer,
    AuthError,
    AuthErrorCode,
} from '../types';

// API base path
const AUTH_BASE = '/api/storefront/auth';

/**
 * Normalize API error to AuthError
 */
function normalizeAuthError(error: unknown): AuthError {
    if (isHttpClientError(error) && error.response?.data) {
        const data = error.response.data;
        return {
            code: (data.code || 'UNKNOWN_ERROR') as AuthErrorCode,
            message: data.message || 'An unexpected error occurred',
            traceId: data.traceId,
            // Extract retry-after header for rate limiting
            retryAfter: error.response.headers['retry-after']
                ? parseInt(error.response.headers['retry-after'], 10)
                : undefined,
        };
    }

    if (isHttpClientError(error) && !error.response) {
        return {
            code: 'NETWORK_ERROR',
            message: 'Network error. Please check your connection.',
        };
    }

    return {
        code: 'UNKNOWN_ERROR',
        message: error instanceof Error ? error.message : 'An unexpected error occurred',
    };
}

/**
 * Login with email and password
 */
export async function login(request: LoginRequest): Promise<AuthResponse> {
    const response = await httpClient.post<AuthResponse>(
        `${AUTH_BASE}/login`,
        request,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Register a new customer account
 */
export async function register(request: RegisterRequest): Promise<AuthResponse> {
    const response = await httpClient.post<AuthResponse>(
        `${AUTH_BASE}/register`,
        { displayName: request.displayName, email: request.email, password: request.password },
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Get current authenticated customer
 * Uses secure auth cookies automatically
 */
export async function getMe(): Promise<OnlineCustomer> {
    const response = await httpClient.get<OnlineCustomer>(
        `${AUTH_BASE}/me`,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Refresh access token
 * Uses HttpOnly cookie for refresh token
 */
export async function refreshToken(): Promise<AuthResponse> {
    const response = await httpClient.post<AuthResponse>(
        `${AUTH_BASE}/refresh`,
        {},
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Logout - revokes tokens
 */
export async function logout(): Promise<void> {
    await httpClient.post(
        `${AUTH_BASE}/logout`,
        {},
        { withCredentials: true }
    );
}

/**
 * Get available social login providers for this tenant
 */
export async function getProviders(): Promise<AvailableProviders> {
    const response = await httpClient.get<AvailableProviders>(
        `${AUTH_BASE}/providers`,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Login with Google ID token
 */
export async function startSocialLogin(provider: 'google' | 'facebook', signal?: AbortSignal): Promise<{ state: string; nonce: string }> {
    return (await httpClient.post(`${AUTH_BASE}/social/challenge/${provider}`, {}, { signal })).data;
}

export async function googleLogin(idToken: string, state?: string, signal?: AbortSignal): Promise<AuthResponse> {
    const response = await httpClient.post<AuthResponse>(
        `${AUTH_BASE}/google`,
        { idToken, state },
        { withCredentials: true, signal }
    );
    return response.data;
}

/**
 * Login with Facebook access token
 */
export async function facebookLogin(accessToken: string, state?: string, signal?: AbortSignal): Promise<AuthResponse> {
    const response = await httpClient.post<AuthResponse>(
        `${AUTH_BASE}/facebook`,
        { accessToken, state },
        { withCredentials: true, signal }
    );
    return response.data;
}

/**
 * Request password reset email
 * SECURITY: Always returns success to prevent user enumeration
 */
export async function forgotPassword(request: ForgotPasswordRequest): Promise<{ success: boolean; message: string }> {
    const response = await httpClient.post<{ success: boolean; message: string }>(
        `${AUTH_BASE}/forgot-password`,
        request,
        { withCredentials: true }
    );
    return response.data;
}

/**
 * Reset password with token
 */
export async function resetPassword(request: ResetPasswordRequest): Promise<{ success: boolean; message: string }> {
    const response = await httpClient.post<{ success: boolean; message: string }>(
        `${AUTH_BASE}/reset-password`,
        request,
        { withCredentials: true }
    );
    return response.data;
}

// Export error normalization for use in hooks/store
export { normalizeAuthError };
