/**
 * Auth Types
 * Phase F1: Storefront Auth UI
 * 
 * Type definitions for authentication flows.
 */

/**
 * Authentication status states
 */
export type AuthStatus = 'unknown' | 'authenticated' | 'unauthenticated' | 'degraded';

/**
 * Online Customer DTO
 * Matches backend StorefrontCustomerDto
 */
export interface OnlineCustomer {
    id: string;
    email: string;
    displayName: string;
    phone?: string;
    provider: 'Local' | 'Google' | 'Facebook';
    emailVerified: boolean;
    createdAtUtc: string;
}

/**
 * Auth response from backend
 */
export interface AuthResponse {
    customer: OnlineCustomer;
    expiresAt: string;
    verificationEmailSent?: boolean | null;
}

/**
 * Login request payload
 */
export interface LoginRequest {
    email: string;
    password: string;
}

/**
 * Registration request payload
 */
export interface RegisterRequest {
    email: string;
    password: string;
    displayName: string;
    phone?: string;
}

/**
 * Social login providers
 */
export interface AvailableProviders {
    google: boolean;
    facebook: boolean;
    /** Google OAuth Client ID for initializing Sign-In */
    googleClientId?: string;
    /** Facebook App ID for initializing SDK */
    facebookAppId?: string;
}

/**
 * Forgot password request
 */
export interface ForgotPasswordRequest {
    email: string;
}

/**
 * Reset password request
 */
export interface ResetPasswordRequest {
    token: string;
    newPassword: string;
}

/**
 * Auth error from backend
 */
export interface AuthError {
    code: AuthErrorCode;
    message: string;
    traceId?: string;
    retryAfter?: number;
}

/**
 * Known auth error codes from backend
 */
export type AuthErrorCode =
    | 'INVALID_CREDENTIALS'
    | 'EMAIL_NOT_VERIFIED'
    | 'ACCOUNT_DEACTIVATED'
    | 'EMAIL_EXISTS'
    | 'EMAIL_ALREADY_EXISTS'
    | 'WEAK_PASSWORD'
    | 'INVALID_REQUEST'
    | 'INVALID_EMAIL'
    | 'INVALID_TENANT'
    | 'INVALID_TOKEN'
    | 'TOKEN_EXPIRED'
    | 'TOKEN_REVOKED'
    | 'TOKEN_REUSE_DETECTED'
    | 'TOO_MANY_ATTEMPTS'
    | 'PROVIDER_DISABLED'
    | 'MISSING_TOKEN'
    | 'REFRESH_FAILED'
    | 'CUSTOMER_NOT_FOUND'
    | 'REGISTRATION_FAILED'
    | 'LOGIN_FAILED'
    | 'GOOGLE_LOGIN_FAILED'
    | 'FACEBOOK_LOGIN_FAILED'
    | 'PHONE_BLOCKED'
    | 'WRONG_AUTH_METHOD'
    | 'SERVICE_UNAVAILABLE'
    | 'NETWORK_ERROR'
    | 'UNKNOWN_ERROR';

/**
 * Check if error code requires force logout
 */
export function isForceLogoutError(code: AuthErrorCode): boolean {
    return code === 'TOKEN_REUSE_DETECTED' ||
        code === 'TOKEN_REVOKED' ||
        code === 'ACCOUNT_DEACTIVATED';
}

/**
 * Check if error code is retryable
 */
export function isRetryableError(code: AuthErrorCode): boolean {
    return code === 'TOKEN_EXPIRED' ||
        code === 'NETWORK_ERROR';
}
