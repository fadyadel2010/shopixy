/**
 * useAuthGuard Hook
 * Phase F1: Storefront Auth UI
 * 
 * Hook for route protection logic.
 * Handles redirects and loading states.
 */

import { useEffect, useMemo } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuthStore, selectAuthStatus, selectIsLoading } from '../store/authStore';

export interface UseAuthGuardOptions {
    /** Redirect path for unauthenticated users (default: /login) */
    redirectTo?: string;
    /** Whether to redirect authenticated users away (for login/register pages) */
    redirectIfAuthenticated?: boolean;
    /** Path to redirect authenticated users to (default: /) */
    authenticatedRedirectTo?: string;
}

export interface UseAuthGuardReturn {
    /** Whether auth check is in progress */
    isLoading: boolean;
    /** Whether user is authenticated */
    isAuthenticated: boolean;
    /** Whether user is unauthenticated */
    isUnauthenticated: boolean;
    /** Whether auth verification is degraded and awaiting confirmation */
    isDegraded: boolean;
    /** Current auth status */
    status: 'unknown' | 'authenticated' | 'unauthenticated' | 'degraded';
    /** Whether to show the protected content */
    showContent: boolean;
}

/**
 * Hook for protecting routes based on auth status
 * 
 * @example
 * ```tsx
 * // In a protected page component
 * const { isLoading, showContent } = useAuthGuard();
 * 
 * if (isLoading) return <LoadingSpinner />;
 * if (!showContent) return null; // Will redirect
 * 
 * return <ProtectedContent />;
 * ```
 * 
 * @example
 * ```tsx
 * // In login page (redirect if already authenticated)
 * const { showContent } = useAuthGuard({ 
 *   redirectIfAuthenticated: true,
 *   authenticatedRedirectTo: '/' 
 * });
 * 
 * if (!showContent) return null;
 * return <LoginForm />;
 * ```
 */
export function useAuthGuard(options: UseAuthGuardOptions = {}): UseAuthGuardReturn {
    const {
        redirectTo = '/login',
        redirectIfAuthenticated = false,
        authenticatedRedirectTo = '/',
    } = options;

    const navigate = useNavigate();
    const location = useLocation();

    const status = useAuthStore(selectAuthStatus);
    const isLoading = useAuthStore(selectIsLoading);
    const checkAuth = useAuthStore((state) => state.checkAuth);

    const isAuthenticated = status === 'authenticated';
    const isUnauthenticated = status === 'unauthenticated';
    const isDegraded = status === 'degraded';

    // Check auth on mount if status is unknown
    useEffect(() => {
        if (status === 'unknown') {
            checkAuth();
        }
    }, [status, checkAuth]);

    // Handle redirects
    useEffect(() => {
        if (status === 'unknown' || status === 'degraded' || isLoading) {
            return; // Wait for auth check to complete
        }

        if (redirectIfAuthenticated && isAuthenticated) {
            // Redirect authenticated users away from login/register
            const returnTo = new URLSearchParams(location.search).get('return') || authenticatedRedirectTo;
            navigate(returnTo, { replace: true });
            return;
        }

        if (!redirectIfAuthenticated && isUnauthenticated) {
            // Redirect unauthenticated users to login
            const currentPath = location.pathname + location.search;
            const loginUrl = currentPath !== '/'
                ? `${redirectTo}?return=${encodeURIComponent(currentPath)}`
                : redirectTo;
            navigate(loginUrl, { replace: true });
        }
    }, [
        status,
        isLoading,
        isAuthenticated,
        isUnauthenticated,
        redirectIfAuthenticated,
        redirectTo,
        authenticatedRedirectTo,
        navigate,
        location,
    ]);

    // Determine if content should be shown
    const showContent = useMemo(() => {
        if (status === 'unknown' || status === 'degraded' || isLoading) {
            return false;
        }

        if (redirectIfAuthenticated) {
            // For login/register pages - show if unauthenticated
            return isUnauthenticated;
        }

        // For protected pages - show if authenticated
        return isAuthenticated;
    }, [status, isLoading, isAuthenticated, isUnauthenticated, redirectIfAuthenticated]);

    return {
        isLoading: isLoading || status === 'unknown',
        isAuthenticated,
        isUnauthenticated,
        isDegraded,
        status,
        showContent,
    };
}
