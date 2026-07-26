/**
 * ResetPasswordPage Component
 * Phase F1: Storefront Auth UI
 * 
 * Reset password page - set new password with token from URL.
 */

import React, { useState, useCallback, useEffect } from 'react';
import { useNavigate, Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { AuthErrorRenderer } from '../components/AuthErrorRenderer';
import * as authApi from '../api/authApi';
import type { AuthError } from '../types';

/**
 * Reset password page component
 */
export const ResetPasswordPage: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const storeName = useStoreConfigStore(selectStoreName);

    // Get token from URL
    const token = searchParams.get('token');

    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [passwordError, setPasswordError] = useState<string | null>(null);
    const [confirmPasswordError, setConfirmPasswordError] = useState<string | null>(null);
    const [showPassword, setShowPassword] = useState(false);
    const [showConfirmPassword, setShowConfirmPassword] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [isSuccess, setIsSuccess] = useState(false);
    const [apiError, setApiError] = useState<AuthError | null>(null);

    // Check for token on mount
    useEffect(() => {
        if (!token) {
            setApiError({
                code: 'INVALID_TOKEN',
                message: t('auth.resetPassword.invalidToken', {
                    defaultValue: 'Invalid or missing reset token. Please request a new password reset.'
                }),
            });
        }
    }, [token, t]);

    // Validate form
    const validateForm = useCallback((): boolean => {
        let isValid = true;

        // Password validation
        if (!password) {
            setPasswordError(t('auth.validation.passwordRequired', {
                defaultValue: 'Password is required'
            }));
            isValid = false;
        } else if (password.length < 8) {
            setPasswordError(t('auth.validation.passwordTooShort', {
                defaultValue: 'Password must be at least 8 characters'
            }));
            isValid = false;
        } else if (!/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/.test(password)) {
            setPasswordError(t('auth.validation.passwordWeak', {
                defaultValue: 'Password must include uppercase, lowercase, and a number'
            }));
            isValid = false;
        } else {
            setPasswordError(null);
        }

        // Confirm password validation
        if (!confirmPassword) {
            setConfirmPasswordError(t('auth.validation.confirmPasswordRequired', {
                defaultValue: 'Please confirm your password'
            }));
            isValid = false;
        } else if (password !== confirmPassword) {
            setConfirmPasswordError(t('auth.validation.passwordMismatch', {
                defaultValue: 'Passwords do not match'
            }));
            isValid = false;
        } else {
            setConfirmPasswordError(null);
        }

        return isValid;
    }, [password, confirmPassword, t]);

    // Handle submit
    const handleSubmit = useCallback(async (e: React.FormEvent) => {
        e.preventDefault();

        if (!token || !validateForm()) return;

        setIsSubmitting(true);
        setApiError(null);

        try {
            await authApi.resetPassword({
                token,
                newPassword: password,
            });
            setIsSuccess(true);

            // Redirect to login after delay
            setTimeout(() => {
                navigate('/login', { replace: true });
            }, 3000);
        } catch (error) {
            const authError = authApi.normalizeAuthError(error);
            setApiError(authError);
        } finally {
            setIsSubmitting(false);
        }
    }, [token, password, validateForm, navigate]);

    // Navigate to login
    const handleGoToLogin = useCallback(() => {
        navigate('/login');
    }, [navigate]);

    // Navigate to forgot password
    const handleRequestNewLink = useCallback(() => {
        navigate('/forgot-password');
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
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                    </svg>
                                </div>
                                <h2 className="text-2xl font-bold text-gray-900 mb-2">
                                    {t('auth.resetPassword.successTitle', {
                                        defaultValue: 'Password updated!'
                                    })}
                                </h2>
                                <p className="text-gray-600 mb-6">
                                    {t('auth.resetPassword.successMessage', {
                                        defaultValue: 'Your password has been reset successfully. You can now sign in with your new password.'
                                    })}
                                </p>
                                <button
                                    onClick={handleGoToLogin}
                                    className="w-full px-4 py-3 rounded-lg bg-[var(--color-primary)] text-white font-medium hover:opacity-90 transition-all"
                                >
                                    {t('auth.resetPassword.goToLogin', {
                                        defaultValue: 'Sign in'
                                    })}
                                </button>
                                <p className="text-sm text-gray-500 mt-4">
                                    {t('auth.resetPassword.redirecting', {
                                        defaultValue: 'Redirecting to login...'
                                    })}
                                </p>
                            </div>
                        ) : !token || apiError?.code === 'INVALID_TOKEN' ? (
                            /* Invalid token state */
                            <div className="text-center">
                                <div className="mx-auto w-16 h-16 bg-red-100 rounded-full flex items-center justify-center mb-4">
                                    <svg className="w-8 h-8 text-red-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                    </svg>
                                </div>
                                <h2 className="text-2xl font-bold text-gray-900 mb-2">
                                    {t('auth.resetPassword.invalidTokenTitle', {
                                        defaultValue: 'Invalid link'
                                    })}
                                </h2>
                                <p className="text-gray-600 mb-6">
                                    {t('auth.resetPassword.invalidTokenMessage', {
                                        defaultValue: 'This password reset link is invalid or has expired. Please request a new one.'
                                    })}
                                </p>
                                <button
                                    onClick={handleRequestNewLink}
                                    className="w-full px-4 py-3 rounded-lg bg-[var(--color-primary)] text-white font-medium hover:opacity-90 transition-all"
                                >
                                    {t('auth.resetPassword.requestNewLink', {
                                        defaultValue: 'Request new link'
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
                                        {t('auth.resetPassword.title', {
                                            defaultValue: 'Reset your password'
                                        })}
                                    </h1>
                                    <p className="text-gray-600 mt-2">
                                        {t('auth.resetPassword.subtitle', {
                                            defaultValue: 'Enter your new password below'
                                        })}
                                    </p>
                                </div>

                                {/* API Error */}
                                {apiError && (
                                    <AuthErrorRenderer
                                        error={apiError}
                                        onDismiss={() => setApiError(null)}
                                        className="mb-6"
                                    />
                                )}

                                <form onSubmit={handleSubmit} className="space-y-5" noValidate>
                                    {/* Password field */}
                                    <div>
                                        <label
                                            htmlFor="reset-password"
                                            className="block text-sm font-medium text-gray-700 mb-1.5"
                                        >
                                            {t('auth.fields.newPassword', { defaultValue: 'New Password' })}
                                        </label>
                                        <div className="relative">
                                            <input
                                                id="reset-password"
                                                name="password"
                                                type={showPassword ? 'text' : 'password'}
                                                autoComplete="new-password"
                                                value={password}
                                                onChange={(e) => {
                                                    setPassword(e.target.value);
                                                    if (passwordError) setPasswordError(null);
                                                }}
                                                disabled={isSubmitting}
                                                className={`
                                                    w-full px-4 py-3 pe-12 rounded-lg border
                                                    text-gray-900 placeholder-gray-400
                                                    transition-all duration-200
                                                    focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                                                    disabled:bg-gray-50 disabled:text-gray-500
                                                    ${passwordError
                                                        ? 'border-red-300 focus:ring-red-500'
                                                        : 'border-gray-200 hover:border-gray-300'
                                                    }
                                                `}
                                                placeholder="••••••••"
                                            />
                                            <button
                                                type="button"
                                                onClick={() => setShowPassword(!showPassword)}
                                                className="absolute inset-y-0 end-0 px-3 flex items-center text-gray-400 hover:text-gray-600"
                                            >
                                                {showPassword ? (
                                                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                            d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" />
                                                    </svg>
                                                ) : (
                                                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                            d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                            d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                                                    </svg>
                                                )}
                                            </button>
                                        </div>
                                        {passwordError && (
                                            <p className="mt-1.5 text-sm text-red-600">{passwordError}</p>
                                        )}
                                        <p className="mt-1.5 text-xs text-gray-500">
                                            {t('auth.register.passwordHint', {
                                                defaultValue: 'At least 8 characters with uppercase, lowercase, and a number'
                                            })}
                                        </p>
                                    </div>

                                    {/* Confirm Password field */}
                                    <div>
                                        <label
                                            htmlFor="reset-confirm-password"
                                            className="block text-sm font-medium text-gray-700 mb-1.5"
                                        >
                                            {t('auth.fields.confirmPassword', { defaultValue: 'Confirm Password' })}
                                        </label>
                                        <div className="relative">
                                            <input
                                                id="reset-confirm-password"
                                                name="confirmPassword"
                                                type={showConfirmPassword ? 'text' : 'password'}
                                                autoComplete="new-password"
                                                value={confirmPassword}
                                                onChange={(e) => {
                                                    setConfirmPassword(e.target.value);
                                                    if (confirmPasswordError) setConfirmPasswordError(null);
                                                }}
                                                disabled={isSubmitting}
                                                className={`
                                                    w-full px-4 py-3 pe-12 rounded-lg border
                                                    text-gray-900 placeholder-gray-400
                                                    transition-all duration-200
                                                    focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                                                    disabled:bg-gray-50 disabled:text-gray-500
                                                    ${confirmPasswordError
                                                        ? 'border-red-300 focus:ring-red-500'
                                                        : 'border-gray-200 hover:border-gray-300'
                                                    }
                                                `}
                                                placeholder="••••••••"
                                            />
                                            <button
                                                type="button"
                                                onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                                                className="absolute inset-y-0 end-0 px-3 flex items-center text-gray-400 hover:text-gray-600"
                                            >
                                                {showConfirmPassword ? (
                                                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                            d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" />
                                                    </svg>
                                                ) : (
                                                    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                            d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                            d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                                                    </svg>
                                                )}
                                            </button>
                                        </div>
                                        {confirmPasswordError && (
                                            <p className="mt-1.5 text-sm text-red-600">{confirmPasswordError}</p>
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
                                                {t('auth.resetPassword.resetting', { defaultValue: 'Resetting...' })}
                                            </span>
                                        ) : (
                                            t('auth.resetPassword.submit', { defaultValue: 'Reset password' })
                                        )}
                                    </button>
                                </form>
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

export default ResetPasswordPage;
