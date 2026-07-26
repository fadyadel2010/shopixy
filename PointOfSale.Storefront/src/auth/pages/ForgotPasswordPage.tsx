/**
 * ForgotPasswordPage Component
 * Phase F1: Storefront Auth UI
 * 
 * Forgot password page - request password reset email.
 * SECURITY: Always shows success message (no user enumeration).
 */

import React, { useState, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useStoreConfigStore, selectStoreName } from '@/store';
import * as authApi from '../api/authApi';

/**
 * Forgot password page component
 */
export const ForgotPasswordPage: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const storeName = useStoreConfigStore(selectStoreName);

    const [email, setEmail] = useState('');
    const [emailError, setEmailError] = useState<string | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [isSuccess, setIsSuccess] = useState(false);

    // Validate email
    const validateEmail = useCallback((): boolean => {
        if (!email.trim()) {
            setEmailError(t('auth.validation.emailRequired', {
                defaultValue: 'Email is required'
            }));
            return false;
        }

        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
            setEmailError(t('auth.validation.emailInvalid', {
                defaultValue: 'Please enter a valid email'
            }));
            return false;
        }

        setEmailError(null);
        return true;
    }, [email, t]);

    // Handle submit
    const handleSubmit = useCallback(async (e: React.FormEvent) => {
        e.preventDefault();

        if (!validateEmail()) return;

        setIsSubmitting(true);

        try {
            await authApi.forgotPassword({ email: email.trim().toLowerCase() });
            // Always show success (security: no user enumeration)
            setIsSuccess(true);
        } catch {
            // Even on error, show success to prevent enumeration
            setIsSuccess(true);
        } finally {
            setIsSubmitting(false);
        }
    }, [email, validateEmail]);

    // Handle email change
    const handleEmailChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setEmail(e.target.value);
        if (emailError) setEmailError(null);
    }, [emailError]);

    // Navigate back to login
    const handleBackToLogin = useCallback(() => {
        navigate('/login');
    }, [navigate]);

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
                    <div className="bg-white rounded-2xl shadow-xl p-8 sm:p-10">
                        {isSuccess ? (
                            /* Success state */
                            <div className="text-center">
                                <div className="mx-auto w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mb-4">
                                    <svg className="w-8 h-8 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                            d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                                    </svg>
                                </div>
                                <h2 className="text-2xl font-bold text-gray-900 mb-2">
                                    {t('auth.forgotPassword.successTitle', {
                                        defaultValue: 'Check your email'
                                    })}
                                </h2>
                                <p className="text-gray-600 mb-6">
                                    {t('auth.forgotPassword.successMessage', {
                                        defaultValue: 'If an account exists with this email, you will receive a password reset link shortly.'
                                    })}
                                </p>
                                <button
                                    onClick={handleBackToLogin}
                                    className="w-full px-4 py-3 rounded-lg border border-gray-200 text-gray-700 font-medium hover:bg-gray-50 transition-colors"
                                >
                                    {t('auth.forgotPassword.backToLogin', {
                                        defaultValue: 'Back to login'
                                    })}
                                </button>
                            </div>
                        ) : (
                            /* Form state */
                            <>
                                <div className="text-center mb-8">
                                    <div className="mx-auto w-16 h-16 bg-blue-100 rounded-full flex items-center justify-center mb-4">
                                        <svg className="w-8 h-8 text-blue-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z" />
                                        </svg>
                                    </div>
                                    <h1 className="text-2xl font-bold text-gray-900">
                                        {t('auth.forgotPassword.title', {
                                            defaultValue: 'Forgot your password?'
                                        })}
                                    </h1>
                                    <p className="text-gray-600 mt-2">
                                        {t('auth.forgotPassword.subtitle', {
                                            defaultValue: "Enter your email and we'll send you a reset link"
                                        })}
                                    </p>
                                </div>

                                <form onSubmit={handleSubmit} className="space-y-5" noValidate>
                                    {/* Email field */}
                                    <div>
                                        <label
                                            htmlFor="forgot-email"
                                            className="block text-sm font-medium text-gray-700 mb-1.5"
                                        >
                                            {t('auth.fields.email', { defaultValue: 'Email' })}
                                        </label>
                                        <input
                                            id="forgot-email"
                                            name="email"
                                            type="email"
                                            autoComplete="email"
                                            value={email}
                                            onChange={handleEmailChange}
                                            disabled={isSubmitting}
                                            className={`
                                                w-full px-4 py-3 rounded-lg border
                                                text-gray-900 placeholder-gray-400
                                                transition-all duration-200
                                                focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                                                disabled:bg-gray-50 disabled:text-gray-500
                                                ${emailError
                                                    ? 'border-red-300 focus:ring-red-500'
                                                    : 'border-gray-200 hover:border-gray-300'
                                                }
                                            `}
                                            placeholder={t('auth.fields.emailPlaceholder', {
                                                defaultValue: 'you@example.com'
                                            })}
                                            aria-invalid={!!emailError}
                                            aria-describedby={emailError ? 'forgot-email-error' : undefined}
                                        />
                                        {emailError && (
                                            <p id="forgot-email-error" className="mt-1.5 text-sm text-red-600">
                                                {emailError}
                                            </p>
                                        )}
                                    </div>

                                    {/* Submit button */}
                                    <button
                                        type="submit"
                                        disabled={isSubmitting}
                                        className={`
                                            w-full px-4 py-3 rounded-lg
                                            bg-[var(--color-primary)] text-white font-medium
                                            transition-all duration-200
                                            ${isSubmitting
                                                ? 'opacity-70 cursor-not-allowed'
                                                : 'hover:opacity-90 hover:shadow-md active:scale-[0.98]'
                                            }
                                            focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-[var(--color-primary)]
                                        `}
                                    >
                                        {isSubmitting ? (
                                            <span className="flex items-center justify-center gap-2">
                                                <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                                                </svg>
                                                {t('auth.forgotPassword.sending', { defaultValue: 'Sending...' })}
                                            </span>
                                        ) : (
                                            t('auth.forgotPassword.submit', { defaultValue: 'Send reset link' })
                                        )}
                                    </button>
                                </form>

                                {/* Back to login link */}
                                <div className="text-center mt-6">
                                    <button
                                        type="button"
                                        onClick={handleBackToLogin}
                                        className="text-sm text-gray-600 hover:text-gray-900 inline-flex items-center gap-1"
                                    >
                                        <svg className="w-4 h-4 rtl-flip" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                                        </svg>
                                        {t('auth.forgotPassword.backToLogin', {
                                            defaultValue: 'Back to login'
                                        })}
                                    </button>
                                </div>
                            </>
                        )}
                    </div>
                </div>
            </main>

            {/* Footer */}
            <footer className="py-6 px-4 text-center text-sm text-gray-500">
                <p>
                    © {new Date().getFullYear()} {storeName || t('common.store', { defaultValue: 'Store' })}
                </p>
            </footer>
        </div>
    );
};

export default ForgotPasswordPage;
