/**
 * RegisterForm Component
 * Phase F1: Storefront Auth UI
 * 
 * Customer registration form with validation.
 */

import React, { useState, useCallback } from 'react';
import { useTranslation } from '@/i18n';
import { useAuth } from '../hooks/useAuth';
import { AuthErrorRenderer } from './AuthErrorRenderer';
import { SocialLoginButtons } from './SocialLoginButtons';
import type { AuthResponse } from '../types';

interface RegisterFormProps {
    onSuccess?: (result?: AuthResponse) => void;
    onLoginClick?: () => void;
    className?: string;
}

interface FormData {
    displayName: string;
    email: string;
    phone: string;
    password: string;
    confirmPassword: string;
}

interface FormErrors {
    displayName?: string;
    email?: string;
    phone?: string;
    password?: string;
    confirmPassword?: string;
}

/**
 * Registration form component
 */
export const RegisterForm: React.FC<RegisterFormProps> = ({
    onSuccess,
    onLoginClick,
    className = '',
}) => {
    const { t } = useTranslation();
    const { register, error, isLoading, clearError } = useAuth();

    const [formData, setFormData] = useState<FormData>({
        displayName: '',
        email: '',
        phone: '',
        password: '',
        confirmPassword: '',
    });
    const [formErrors, setFormErrors] = useState<FormErrors>({});
    const [showPassword, setShowPassword] = useState(false);
    const [showConfirmPassword, setShowConfirmPassword] = useState(false);

    // Validate form
    const validateForm = useCallback((): boolean => {
        const errors: FormErrors = {};

        // Display name
        if (!formData.displayName.trim()) {
            errors.displayName = t('auth.validation.nameRequired', {
                defaultValue: 'Name is required'
            });
        } else if (formData.displayName.trim().length < 2) {
            errors.displayName = t('auth.validation.nameTooShort', {
                defaultValue: 'Name must be at least 2 characters'
            });
        }

        // Email
        if (!formData.email.trim()) {
            errors.email = t('auth.validation.emailRequired', {
                defaultValue: 'Email is required'
            });
        } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.email)) {
            errors.email = t('auth.validation.emailInvalid', {
                defaultValue: 'Please enter a valid email'
            });
        }

        // Password
        if (!formData.password) {
            errors.password = t('auth.validation.passwordRequired', {
                defaultValue: 'Password is required'
            });
        } else if (formData.password.length < 8) {
            errors.password = t('auth.validation.passwordTooShort', {
                defaultValue: 'Password must be at least 8 characters'
            });
        } else if (!/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)/.test(formData.password)) {
            errors.password = t('auth.validation.passwordWeak', {
                defaultValue: 'Password must include uppercase, lowercase, and a number'
            });
        }

        // Confirm password
        if (!formData.confirmPassword) {
            errors.confirmPassword = t('auth.validation.confirmPasswordRequired', {
                defaultValue: 'Please confirm your password'
            });
        } else if (formData.password !== formData.confirmPassword) {
            errors.confirmPassword = t('auth.validation.passwordMismatch', {
                defaultValue: 'Passwords do not match'
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
            const result = await register({
                displayName: formData.displayName.trim(),
                email: formData.email.trim().toLowerCase(),
                password: formData.password,
                phone: formData.phone.trim() || undefined,
            });
            onSuccess?.(result);
        } catch {
            // Error is handled by useAuth
        }
    }, [formData, validateForm, register, onSuccess]);

    // Handle social login success
    const handleSocialSuccess = useCallback(() => {
        onSuccess?.();
    }, [onSuccess]);

    return (
        <div className={`w-full max-w-md ${className}`}>
            {/* Header */}
            <div className="text-center mb-8">
                <h1 className="text-2xl font-bold text-gray-900">
                    {t('auth.register.title', { defaultValue: 'Create an account' })}
                </h1>
                <p className="text-gray-600 mt-2">
                    {t('auth.register.subtitle', {
                        defaultValue: 'Join us and start shopping today'
                    })}
                </p>
            </div>

            {/* Error display */}
            {error && (
                <AuthErrorRenderer
                    error={error}
                    onDismiss={clearError}
                    className="mb-6"
                />
            )}

            {/* Registration form */}
            <form onSubmit={handleSubmit} className="space-y-5" noValidate>
                {/* Display Name */}
                <div>
                    <label
                        htmlFor="register-name"
                        className="block text-sm font-medium text-gray-700 mb-1.5"
                    >
                        {t('auth.fields.displayName', { defaultValue: 'Full Name' })}
                    </label>
                    <input
                        id="register-name"
                        name="displayName"
                        type="text"
                        autoComplete="name"
                        value={formData.displayName}
                        onChange={handleChange}
                        disabled={isLoading}
                        className={`
                            w-full px-4 py-3 rounded-lg border
                            text-gray-900 placeholder-gray-400
                            transition-all duration-200
                            focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                            disabled:bg-gray-50 disabled:text-gray-500
                            ${formErrors.displayName
                                ? 'border-red-300 focus:ring-red-500'
                                : 'border-gray-200 hover:border-gray-300'
                            }
                        `}
                        placeholder={t('auth.fields.namePlaceholder', {
                            defaultValue: 'John Doe'
                        })}
                        aria-invalid={!!formErrors.displayName}
                        aria-describedby={formErrors.displayName ? 'register-name-error' : undefined}
                    />
                    {formErrors.displayName && (
                        <p id="register-name-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.displayName}
                        </p>
                    )}
                </div>

                {/* Email field */}
                <div>
                    <label
                        htmlFor="register-email"
                        className="block text-sm font-medium text-gray-700 mb-1.5"
                    >
                        {t('auth.fields.email', { defaultValue: 'Email' })}
                    </label>
                    <input
                        id="register-email"
                        name="email"
                        type="email"
                        autoComplete="email"
                        value={formData.email}
                        onChange={handleChange}
                        disabled={isLoading}
                        className={`
                            w-full px-4 py-3 rounded-lg border
                            text-gray-900 placeholder-gray-400
                            transition-all duration-200
                            focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                            disabled:bg-gray-50 disabled:text-gray-500
                            ${formErrors.email
                                ? 'border-red-300 focus:ring-red-500'
                                : 'border-gray-200 hover:border-gray-300'
                            }
                        `}
                        placeholder={t('auth.fields.emailPlaceholder', {
                            defaultValue: 'you@example.com'
                        })}
                        aria-invalid={!!formErrors.email}
                        aria-describedby={formErrors.email ? 'register-email-error' : undefined}
                    />
                    {formErrors.email && (
                        <p id="register-email-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.email}
                        </p>
                    )}
                </div>

                {/* Phone field (optional) */}
                <div>
                    <label
                        htmlFor="register-phone"
                        className="block text-sm font-medium text-gray-700 mb-1.5"
                    >
                        {t('auth.fields.phone', { defaultValue: 'Phone' })}
                        <span className="text-gray-400 font-normal ms-1">
                            ({t('common.optional', { defaultValue: 'Optional' })})
                        </span>
                    </label>
                    <input
                        id="register-phone"
                        name="phone"
                        type="tel"
                        autoComplete="tel"
                        value={formData.phone}
                        onChange={handleChange}
                        disabled={isLoading}
                        className={`
                            w-full px-4 py-3 rounded-lg border
                            text-gray-900 placeholder-gray-400
                            transition-all duration-200
                            focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                            disabled:bg-gray-50 disabled:text-gray-500
                            ${formErrors.phone
                                ? 'border-red-300 focus:ring-red-500'
                                : 'border-gray-200 hover:border-gray-300'
                            }
                        `}
                        placeholder="+1234567890"
                        aria-invalid={!!formErrors.phone}
                        aria-describedby={formErrors.phone ? 'register-phone-error' : undefined}
                    />
                    {formErrors.phone && (
                        <p id="register-phone-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.phone}
                        </p>
                    )}
                </div>

                {/* Password field */}
                <div>
                    <label
                        htmlFor="register-password"
                        className="block text-sm font-medium text-gray-700 mb-1.5"
                    >
                        {t('auth.fields.password', { defaultValue: 'Password' })}
                    </label>
                    <div className="relative">
                        <input
                            id="register-password"
                            name="password"
                            type={showPassword ? 'text' : 'password'}
                            autoComplete="new-password"
                            value={formData.password}
                            onChange={handleChange}
                            disabled={isLoading}
                            className={`
                                w-full px-4 py-3 pe-12 rounded-lg border
                                text-gray-900 placeholder-gray-400
                                transition-all duration-200
                                focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                                disabled:bg-gray-50 disabled:text-gray-500
                                ${formErrors.password
                                    ? 'border-red-300 focus:ring-red-500'
                                    : 'border-gray-200 hover:border-gray-300'
                                }
                            `}
                            placeholder="••••••••"
                            aria-invalid={!!formErrors.password}
                            aria-describedby={formErrors.password ? 'register-password-error' : undefined}
                        />
                        <button
                            type="button"
                            onClick={() => setShowPassword(!showPassword)}
                            className="absolute inset-y-0 end-0 px-3 flex items-center text-gray-400 hover:text-gray-600"
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
                        <p id="register-password-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.password}
                        </p>
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
                        htmlFor="register-confirm-password"
                        className="block text-sm font-medium text-gray-700 mb-1.5"
                    >
                        {t('auth.fields.confirmPassword', { defaultValue: 'Confirm Password' })}
                    </label>
                    <div className="relative">
                        <input
                            id="register-confirm-password"
                            name="confirmPassword"
                            type={showConfirmPassword ? 'text' : 'password'}
                            autoComplete="new-password"
                            value={formData.confirmPassword}
                            onChange={handleChange}
                            disabled={isLoading}
                            className={`
                                w-full px-4 py-3 pe-12 rounded-lg border
                                text-gray-900 placeholder-gray-400
                                transition-all duration-200
                                focus:outline-none focus:ring-2 focus:ring-[var(--color-primary)] focus:border-transparent
                                disabled:bg-gray-50 disabled:text-gray-500
                                ${formErrors.confirmPassword
                                    ? 'border-red-300 focus:ring-red-500'
                                    : 'border-gray-200 hover:border-gray-300'
                                }
                            `}
                            placeholder="••••••••"
                            aria-invalid={!!formErrors.confirmPassword}
                            aria-describedby={formErrors.confirmPassword ? 'register-confirm-password-error' : undefined}
                        />
                        <button
                            type="button"
                            onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                            className="absolute inset-y-0 end-0 px-3 flex items-center text-gray-400 hover:text-gray-600"
                            aria-label={showConfirmPassword
                                ? t('auth.fields.hidePassword', { defaultValue: 'Hide password' })
                                : t('auth.fields.showPassword', { defaultValue: 'Show password' })
                            }
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
                    {formErrors.confirmPassword && (
                        <p id="register-confirm-password-error" className="mt-1.5 text-sm text-red-600">
                            {formErrors.confirmPassword}
                        </p>
                    )}
                </div>

                {/* Submit button */}
                <button
                    type="submit"
                    disabled={isLoading}
                    className={`
                        w-full px-4 py-3 rounded-lg
                        bg-[var(--color-primary)] text-white font-medium
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
                            {t('auth.register.creating', { defaultValue: 'Creating account...' })}
                        </span>
                    ) : (
                        t('auth.register.submit', { defaultValue: 'Create account' })
                    )}
                </button>
            </form>

            {/* Social login */}
            <SocialLoginButtons
                onSuccess={handleSocialSuccess}
                disabled={isLoading}
                className="mt-6"
            />

            {/* Login link */}
            {onLoginClick && (
                <p className="text-center text-sm text-gray-600 mt-8">
                    {t('auth.register.hasAccount', { defaultValue: 'Already have an account?' })}{' '}
                    <button
                        type="button"
                        onClick={onLoginClick}
                        className="text-[var(--color-primary)] font-medium hover:underline focus:outline-none"
                    >
                        {t('auth.register.loginLink', { defaultValue: 'Sign in' })}
                    </button>
                </p>
            )}
        </div>
    );
};

export default RegisterForm;
