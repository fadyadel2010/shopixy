/**
 * RegisterPage Component
 * Phase F1: Storefront Auth UI
 * 
 * Registration page with auth guard for guest-only access.
 */

import React, { useCallback, useState } from 'react';
import { useNavigate, useLocation, Link } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuthGuard } from '../hooks/useAuthGuard';
import { RegisterForm } from '../components/RegisterForm';
import { VerifyEmailBanner } from '../components/VerifyEmailBanner';
import { useStoreConfigStore, selectStoreName } from '@/store';
import type { AuthResponse } from '../types';
import { resolveStorefrontReturnPath } from '../returnPath';

/**
 * Register page component
 */
export const RegisterPage: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const location = useLocation();
    const storeName = useStoreConfigStore(selectStoreName);
    const returnTo = resolveStorefrontReturnPath(new URLSearchParams(location.search).get('return'));

    const [registrationComplete, setRegistrationComplete] = useState(false);
    const [registrationResult, setRegistrationResult] = useState<AuthResponse | null>(null);
    const registeredCustomer = registrationResult?.customer;

    // Guard: redirect if already authenticated
    const { status } = useAuthGuard({
        redirectIfAuthenticated: true,
        authenticatedRedirectTo: returnTo,
    });

    // Handle successful registration
    const handleSuccess = useCallback((result?: AuthResponse) => {
        // Show verification banner instead of immediate redirect
        if (!result || result.customer.emailVerified) {
            navigate(returnTo, { replace: true });
            return;
        }
        setRegistrationResult(result);
        setRegistrationComplete(true);

    }, [navigate, returnTo]);

    // Navigate to login
    const handleLoginClick = useCallback(() => {
        const loginUrl = returnTo !== '/'
            ? `/login?return=${encodeURIComponent(returnTo)}`
            : '/login';
        navigate(loginUrl);
    }, [navigate, returnTo]);

    // Show loading state
    // Submitting is not an initial auth check. Keep the form mounted so errors
    // preserve entered values and its synchronous double-submit guard.
    if (status === 'unknown') {
        return (
            <div className="min-h-screen flex items-center justify-center bg-background" dir={direction}>
                <div className="flex flex-col items-center gap-4">
                    <svg className="w-8 h-8 animate-spin text-[var(--color-primary)]" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                    </svg>
                    <p className="text-muted">{t('common.loading')}</p>
                </div>
            </div>
        );
    }

    // Don't render if being redirected
    if (status !== 'unauthenticated' && !registrationComplete) {
        return null;
    }

    return (
        <div className="min-h-screen flex flex-col bg-background text-text" dir={direction}>
            {/* Header */}
            <header className="py-6 px-4 sm:px-6 lg:px-8">
                <div className="max-w-7xl mx-auto">
                    <Link
                        to="/"
                        className="inline-flex items-center gap-2 text-xl font-bold text-text hover:text-primary-text transition-colors"
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
                    {/* Success state with verify banner */}
                    {registrationComplete && registeredCustomer && !registeredCustomer.emailVerified ? (
                        <div className="bg-background border border-border rounded-2xl shadow-xl p-8 sm:p-10">
                            <div className="text-center mb-6">
                                <div className="mx-auto w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mb-4">
                                    <svg className="w-8 h-8 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                    </svg>
                                </div>
                                <h2 className="text-2xl font-bold text-text">
                                    {t('auth.register.success', { defaultValue: 'Account created!' })}
                                </h2>
                                <p className="text-muted mt-2">
                                    {t('auth.register.successMessage', {
                                        defaultValue: 'Welcome! Your account has been created successfully.'
                                    })}
                                </p>
                            </div>

                            <VerifyEmailBanner email={registeredCustomer.email} emailSent={registrationResult?.verificationEmailSent ?? undefined} canResend={false} />

                            <p className="text-center text-sm text-muted mt-6">
                                <Link to={returnTo} className="text-primary underline">{t('auth.verifyEmail.continueShopping')}</Link>
                            </p>
                        </div>
                    ) : (
                        /* Registration form */
                        <div className="bg-background border border-border rounded-2xl shadow-xl p-8 sm:p-10">
                            <RegisterForm
                                onSuccess={handleSuccess}
                                onLoginClick={handleLoginClick}
                            />
                        </div>
                    )}
                </div>
            </main>

            {/* Footer */}
            <footer className="py-6 px-4 text-center text-sm text-muted">
                <p>
                    © {new Date().getFullYear()} {storeName || t('common.store', { defaultValue: 'Store' })}
                </p>
            </footer>
        </div>
    );
};

export default RegisterPage;
