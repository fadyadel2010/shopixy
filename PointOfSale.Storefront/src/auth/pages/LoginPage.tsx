/**
 * LoginPage Component
 * Phase F1: Storefront Auth UI
 * 
 * Login page with auth guard for guest-only access.
 */

import React, { useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuthGuard } from '../hooks/useAuthGuard';
import { LoginForm } from '../components/LoginForm';
import { useStoreConfigStore, selectStoreName } from '@/store';

/**
 * Login page component
 */
export const LoginPage: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const storeName = useStoreConfigStore(selectStoreName);

    // Guard: redirect if already authenticated
    const { isLoading, showContent } = useAuthGuard({
        redirectIfAuthenticated: true,
        authenticatedRedirectTo: '/',
    });

    // Handle successful login
    const handleSuccess = useCallback(() => {
        // Get return URL from query params or go to home
        const params = new URLSearchParams(window.location.search);
        const returnTo = params.get('return') || '/';
        navigate(returnTo, { replace: true });
    }, [navigate]);

    // Navigate to register
    const handleRegisterClick = useCallback(() => {
        const params = new URLSearchParams(window.location.search);
        const returnTo = params.get('return');
        const registerUrl = returnTo
            ? `/register?return=${encodeURIComponent(returnTo)}`
            : '/register';
        navigate(registerUrl);
    }, [navigate]);

    // Navigate to forgot password
    const handleForgotPasswordClick = useCallback(() => {
        navigate('/forgot-password');
    }, [navigate]);

    // Show loading state
    if (isLoading) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gray-50" dir={direction}>
                <div className="flex flex-col items-center gap-4">
                    <svg className="w-8 h-8 animate-spin text-[var(--color-primary)]" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                    </svg>
                    <p className="text-gray-500">{t('common.loading')}</p>
                </div>
            </div>
        );
    }

    // Don't render if being redirected
    if (!showContent) {
        return null;
    }

    return (
        <div className="min-h-screen flex flex-col bg-gray-50" dir={direction}>
            {/* Header */}
            <header className="py-6 px-4 sm:px-6 lg:px-8">
                <div className="max-w-7xl mx-auto">
                    <Link
                        to="/"
                        className="inline-flex items-center gap-2 text-xl font-bold text-gray-900 hover:text-[var(--color-primary)] transition-colors"
                    >
                        <svg className="w-8 h-8 text-[var(--color-primary)]" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
                        </svg>
                        {storeName || t('common.store', { defaultValue: 'Store' })}
                    </Link>
                </div>
            </header>

            {/* Main content */}
            <main className="flex-1 flex items-center justify-center px-4 sm:px-6 lg:px-8 py-12">
                <div className="w-full max-w-md">
                    {/* Card */}
                    <div className="bg-white rounded-2xl shadow-xl p-8 sm:p-10">
                        <LoginForm
                            onSuccess={handleSuccess}
                            onRegisterClick={handleRegisterClick}
                            onForgotPasswordClick={handleForgotPasswordClick}
                        />
                    </div>
                </div>
            </main>

            {/* Footer */}
            <footer className="py-6 px-4 text-center text-sm text-gray-500">
                <p>
                    © {new Date().getFullYear()} {storeName || t('common.store', { defaultValue: 'Store' })}.
                    {' '}{t('footer.copyright', { year: '', storeName: '' }).replace('©', '').trim()}
                </p>
            </footer>
        </div>
    );
};

export default LoginPage;
