/**
 * LoginForm Component
 * Phase F1: Storefront Auth UI
 * 
 * Email/password login form with validation and error handling.
 */

import React, { useState, useCallback, useEffect } from 'react';
import { useTranslation } from '@/i18n';
import { useAuth } from '../hooks/useAuth';
import { AuthErrorRenderer } from './AuthErrorRenderer';
import { SocialLoginButtons } from './SocialLoginButtons';
import { VerifyEmailBanner } from './VerifyEmailBanner';

interface LoginFormProps {
    onSuccess?: () => void;
    onRegisterClick?: () => void;
    onForgotPasswordClick?: () => void;
    className?: string;
}

interface FormData {
    email: string;
    password: string;
}

interface FormErrors {
    email?: string;
    password?: string;
}

/**
 * Login form component
 */
export const LoginForm: React.FC<LoginFormProps> = ({
    onSuccess,
    onRegisterClick,
    onForgotPasswordClick,
    className = '',
}) => {
    const { t } = useTranslation();
    const { login, error, isLoading, clearError } = useAuth();

    const [formData, setFormData] = useState<FormData>({
        email: '',
        password: '',
    });
    const [formErrors, setFormErrors] = useState<FormErrors>({});
    const [showPassword, setShowPassword] = useState(false);
    const [showVerifyBanner, setShowVerifyBanner] = useState(false);

    // Show verify banner when error is EMAIL_NOT_VERIFIED
    useEffect(() => {
        if (error?.code === 'EMAIL_NOT_VERIFIED') {
            setShowVerifyBanner(true);
        }
    }, [error]);

    // Validate form
    const validateForm = useCallback((): boolean => {
        const errors: FormErrors = {};

        if (!formData.email.trim()) {
            errors.email = t('auth.validation.emailRequired', {
                defaultValue: 'Email is required'
            });
        } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.email)) {
            errors.email = t('auth.validation.emailInvalid', {
                defaultValue: 'Please enter a valid email'
            });
        }

        if (!formData.password) {
            errors.password = t('auth.validation.passwordRequired', {
                defaultValue: 'Password is required'
            });
        }

        setFormErrors(errors);
        return Object.keys(errors).length === 0;
    }, [formData, t]);

    // Handle input change
    const handleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value } = e.target;
        setFormData((prev) => ({ ...prev, [name]: value }));

        // Clear field error on change
        if (formErrors[name as keyof FormErrors]) {
            setFormErrors((prev) => ({ ...prev, [name]: undefined }));
        }

        // Clear API error on change
        if (error) {
            clearError();
        }
    }, [formErrors, error, clearError]);

    // Handle form submit
    const handleSubmit = useCallback(async (e: React.FormEvent) => {
        e.preventDefault();

        if (!validateForm()) return;

        try {
            await login({
                email: formData.email.trim().toLowerCase(),
                password: formData.password,
            });
            onSuccess?.();
        } catch {
            // Error is handled by useAuth
        }
    }, [formData, validateForm, login, onSuccess]);

    // Handle social login success
    const handleSocialSuccess = useCallback(() => {
        onSuccess?.();
    }, [onSuccess]);

    return (
        <div className={`w-full max-w-md ${className}`}>
            {/* Header */}
            <div className="text-center mb-8">
                <h1 className="text-2xl font-bold text-text">
                    {t('auth.login.title', { defaultValue: 'Welcome back' })}
                </h1>
                <p className="text-muted mt-2">
                    {t('auth.login.subtitle', {
                        defaultValue: 'Sign in to your account to continue'
                    })}
                </p>
            </div>

            {/* Error display */}
            {error && error.code !== 'EMAIL_NOT_VERIFIED' && (
                <AuthErrorRenderer
                    error={error}
                    onDismiss={clearError}
                    className="mb-6"
                />
            )}

            {/* Email verification banner */}
            {showVerifyBanner && (
                <VerifyEmailBanner
                    email={formData.email}
                    className="mb-6"
                />
            )}

            {/* Login form */}
            <form onSubmit={handleSubmit} className="space-y-5" noValidate>
                {/* Email field */}
                <div>
                    <label
                        htmlFor="login-email"
                        className="block text-sm font-medium text-text mb-1.5"
                    >
                        {t('auth.fields.email', { defaultValue: 'Email' })}
                    </label>
                    <input
                        id="login-email"
                        name="email"
                        type="email"
                        autoComplete="email"
                        value={formData.email}
                        onChange={handleChange}
                        disabled={isLoading}
                        className={`
                            w-full px-4 py-3 rounded-lg border
                            bg-background text-text placeholder:text-muted
                            transition-all duration-200
                            focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                            disabled:bg-subtle disabled:text-muted
                            ${formErrors.email
                                ? 'border-red-300 focus:ring-red-500'
                                : 'border-border hover:border-text'
                            }
                        `}
                        placeholder={t('auth.fields.emailPlaceholder', {
                            defaultValue: 'you@example.com'
                        })}
                        aria-invalid={!!formErrors.email}
                        aria-describedby={formErrors.email ? 'login-email-error' : undefined}
                    />
                    {formErrors.email && (
                        <p id="login-email-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.email}
                        </p>
                    )}
                </div>

                {/* Password field */}
                <div>
                    <div className="flex items-center justify-between mb-1.5">
                        <label
                            htmlFor="login-password"
                            className="block text-sm font-medium text-text"
                        >
                            {t('auth.fields.password', { defaultValue: 'Password' })}
                        </label>
                        {onForgotPasswordClick && (
                            <button
                                type="button"
                                onClick={onForgotPasswordClick}
                                className="text-sm text-[var(--color-primary)] hover:underline focus:outline-none"
                            >
                                {t('auth.login.forgotPassword', {
                                    defaultValue: 'Forgot password?'
                                })}
                            </button>
                        )}
                    </div>
                    <div className="relative">
                        <input
                            id="login-password"
                            name="password"
                            type={showPassword ? 'text' : 'password'}
                            autoComplete="current-password"
                            value={formData.password}
                            onChange={handleChange}
                            disabled={isLoading}
                            className={`
                                w-full px-4 py-3 pe-12 rounded-lg border
                                bg-background text-text placeholder:text-muted
                                transition-all duration-200
                                focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                                disabled:bg-subtle disabled:text-muted
                                ${formErrors.password
                                    ? 'border-red-300 focus:ring-red-500'
                                    : 'border-border hover:border-text'
                                }
                            `}
                            placeholder="••••••••"
                            aria-invalid={!!formErrors.password}
                            aria-describedby={formErrors.password ? 'login-password-error' : undefined}
                        />
                        <button
                            type="button"
                            onClick={() => setShowPassword(!showPassword)}
                            className="absolute inset-y-0 end-0 px-3 flex items-center text-muted hover:text-text"
                            aria-label={showPassword
                                ? t('auth.fields.hidePassword', { defaultValue: 'Hide password' })
                                : t('auth.fields.showPassword', { defaultValue: 'Show password' })
                            }
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
                    {formErrors.password && (
                        <p id="login-password-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.password}
                        </p>
                    )}
                </div>

                {/* Submit button */}
                <button
                    type="submit"
                    disabled={isLoading}
                    className={`
                        w-full px-4 py-3 rounded-lg
                        bg-primary text-on-primary font-medium
                        transition-all duration-200
                        ${isLoading
                            ? 'opacity-70 cursor-not-allowed'
                            : 'hover:opacity-90 hover:shadow-md active:scale-[0.98]'
                        }
                        focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-[var(--color-primary)]
                    `}
                >
                    {isLoading ? (
                        <span className="flex items-center justify-center gap-2">
                            <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                            </svg>
                            {t('auth.login.signingIn', { defaultValue: 'Signing in...' })}
                        </span>
                    ) : (
                        t('auth.login.submit', { defaultValue: 'Sign in' })
                    )}
                </button>
            </form>

            {/* Social login */}
            <SocialLoginButtons
                onSuccess={handleSocialSuccess}
                disabled={isLoading}
                className="mt-6"
            />

            {/* Register link */}
            {onRegisterClick && (
                <p className="text-center text-sm text-muted mt-8">
                    {t('auth.login.noAccount', { defaultValue: "Don't have an account?" })}{' '}
                    <button
                        type="button"
                        onClick={onRegisterClick}
                        className="text-[var(--color-primary)] font-medium hover:underline focus:outline-none"
                    >
                        {t('auth.login.registerLink', { defaultValue: 'Sign up' })}
                    </button>
                </p>
            )}
        </div>
    );
};

export default LoginForm;
