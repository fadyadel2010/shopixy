/**
 * ProtectedRoute Component
 * Phase F1: Storefront Auth UI
 * 
 * Route guard that protects routes requiring authentication.
 * Shows loader while checking auth, redirects if unauthenticated.
 */

import React, { useState } from 'react';
import { useTranslation } from '@/i18n';
import { useAuthGuard } from '@/auth/hooks/useAuthGuard';
import { useAuthStore } from '@/auth/store/authStore';

interface ProtectedRouteProps {
    children: React.ReactNode;
    /** Path to redirect unauthenticated users to (default: /login) */
    redirectTo?: string;
}

/**
 * Protected route wrapper
 * 
 * @example
 * ```tsx
 * <Route 
 *   path="/account" 
 *   element={
 *     <ProtectedRoute>
 *       <AccountPage />
 *     </ProtectedRoute>
 *   } 
 * />
 * ```
 */
export const ProtectedRoute: React.FC<ProtectedRouteProps> = ({
    children,
    redirectTo = '/login',
}) => {
    const { t, direction } = useTranslation();
    const [isRetrying, setIsRetrying] = useState(false);

    const { isLoading, showContent, status, isDegraded } = useAuthGuard({
        redirectTo,
        redirectIfAuthenticated: false,
    });
    const checkAuth = useAuthStore((state) => state.checkAuth);

    const handleRetry = async () => {
        if (isRetrying || isLoading) return;
        
        setIsRetrying(true);
        try {
            await checkAuth();
        } finally {
            setIsRetrying(false);
        }
    };

    if (isDegraded) {
        return (
            <div
                className="min-h-screen flex items-center justify-center bg-gray-50"
                dir={direction}
            >
                <div className="max-w-md rounded-2xl border border-amber-200 bg-white p-6 text-center shadow-sm">
                    <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-amber-100 text-amber-700">
                        <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                    </div>
                    <p className="mt-4 text-lg font-semibold text-gray-900">
                        {t('auth.guard.degradedTitle', {
                            defaultValue: 'We are verifying your session'
                        })}
                    </p>
                    <p className="mt-2 text-sm text-gray-600">
                        {t('auth.guard.degradedMessage', {
                            defaultValue: 'Sensitive actions stay paused until we can confirm your account safely.'
                        })}
                    </p>
                    <button
                        type="button"
                        onClick={handleRetry}
                        disabled={isRetrying || isLoading}
                        aria-busy={isRetrying || isLoading}
                        aria-disabled={isRetrying || isLoading}
                        className="mt-5 inline-flex items-center justify-center rounded-xl bg-[var(--color-primary)] px-4 py-2 text-sm font-medium text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50 disabled:pointer-events-none"
                    >
                        {isRetrying || isLoading ? t('common.loading', { defaultValue: 'Loading...' }) : t('common.retry', { defaultValue: 'Retry' })}
                    </button>
                </div>
            </div>
        );
    }

    // Show loading state while checking auth
    if (isLoading || status === 'unknown') {
        return (
            <div
                className="min-h-screen flex items-center justify-center bg-gray-50"
                dir={direction}
            >
                <div className="flex flex-col items-center gap-4">
                    <div className="relative">
                        <svg
                            className="w-12 h-12 animate-spin text-[var(--color-primary)]"
                            fill="none"
                            viewBox="0 0 24 24"
                        >
                            <circle
                                className="opacity-25"
                                cx="12"
                                cy="12"
                                r="10"
                                stroke="currentColor"
                                strokeWidth="4"
                            />
                            <path
                                className="opacity-75"
                                fill="currentColor"
                                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                            />
                        </svg>
                    </div>
                    <p className="text-gray-500 text-sm">
                        {t('auth.guard.checking', {
                            defaultValue: 'Verifying authentication...'
                        })}
                    </p>
                </div>
            </div>
        );
    }

    // Don't render if being redirected to login
    if (!showContent) {
        return null;
    }

    // User is authenticated - render the protected content
    return <>{children}</>;
};

export default ProtectedRoute;
