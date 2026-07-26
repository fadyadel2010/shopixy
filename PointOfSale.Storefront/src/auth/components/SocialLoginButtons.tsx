/**
 * SocialLoginButtons Component
 * Phase F1: Storefront Auth UI
 * 
 * Social login buttons (Google, Facebook).
 * Dynamically shown based on provider availability from backend.
 */

import React, { useCallback, useState } from 'react';
import { useTranslation } from '@/i18n';
import { useAuth } from '../hooks/useAuth';

interface SocialLoginButtonsProps {
    onSuccess?: () => void;
    onError?: (error: string) => void;
    className?: string;
    disabled?: boolean;
}

/**
 * Social login buttons component
 */
export const SocialLoginButtons: React.FC<SocialLoginButtonsProps> = ({
    onSuccess,
    onError,
    className = '',
    disabled = false,
}) => {
    const { t } = useTranslation();
    const { providers, googleLogin, facebookLogin, isLoading } = useAuth();

    const [loadingProvider, setLoadingProvider] = useState<'google' | 'facebook' | null>(null);

    // Check if any providers are available
    const hasProviders = providers && (providers.google || providers.facebook);

    // Handle Google login
    const handleGoogleLogin = useCallback(async () => {
        if (!window.google?.accounts?.id) {
            onError?.(t('auth.social.googleNotLoaded', {
                defaultValue: 'Google Sign-In is not available. Please try again later.'
            }));
            return;
        }

        // Use dynamic Client ID from backend
        const googleClientId = providers?.googleClientId;
        if (!googleClientId) {
            onError?.(t('auth.social.googleNotConfigured', {
                defaultValue: 'Google Sign-In is not configured. Please contact support.'
            }));
            return;
        }

        setLoadingProvider('google');

        try {
            // Initialize Google Sign-In with dynamic Client ID
            window.google.accounts.id.initialize({
                client_id: googleClientId,
                callback: async (response: { credential: string }) => {
                    try {
                        await googleLogin(response.credential);
                        onSuccess?.();
                    } catch (error) {
                        onError?.(t('auth.social.googleFailed', {
                            defaultValue: 'Google Sign-In failed. Please try again.'
                        }));
                    } finally {
                        setLoadingProvider(null);
                    }
                },
            });

            // Prompt user to sign in
            window.google.accounts.id.prompt();
        } catch (error) {
            setLoadingProvider(null);
            onError?.(t('auth.social.googleFailed', {
                defaultValue: 'Google Sign-In failed. Please try again.'
            }));
        }
    }, [providers?.googleClientId, googleLogin, onSuccess, onError, t]);

    // Handle Facebook login
    const handleFacebookLogin = useCallback(async () => {
        if (!window.FB) {
            onError?.(t('auth.social.facebookNotLoaded', {
                defaultValue: 'Facebook Login is not available. Please try again later.'
            }));
            return;
        }

        // Use dynamic App ID from backend
        const facebookAppId = providers?.facebookAppId;
        if (!facebookAppId) {
            onError?.(t('auth.social.facebookNotConfigured', {
                defaultValue: 'Facebook Login is not configured. Please contact support.'
            }));
            return;
        }

        setLoadingProvider('facebook');

        try {
            // Initialize Facebook SDK with dynamic App ID
            window.FB.init({
                appId: facebookAppId,
                cookie: true,
                xfbml: true,
                version: 'v18.0'
            });

            window.FB.login(async (response: { authResponse?: { accessToken: string } }) => {
                if (response.authResponse?.accessToken) {
                    try {
                        await facebookLogin(response.authResponse.accessToken);
                        onSuccess?.();
                    } catch (error) {
                        onError?.(t('auth.social.facebookFailed', {
                            defaultValue: 'Facebook Login failed. Please try again.'
                        }));
                    }
                }
                setLoadingProvider(null);
            }, { scope: 'email,public_profile' });
        } catch (error) {
            setLoadingProvider(null);
            onError?.(t('auth.social.facebookFailed', {
                defaultValue: 'Facebook Login failed. Please try again.'
            }));
        }
    }, [providers?.facebookAppId, facebookLogin, onSuccess, onError, t]);

    // Don't render if no providers available
    if (!hasProviders) {
        return null;
    }

    const isDisabled = disabled || isLoading || loadingProvider !== null;

    return (
        <div className={`space-y-3 ${className}`}>
            {/* Divider */}
            <div className="relative">
                <div className="absolute inset-0 flex items-center">
                    <div className="w-full border-t border-gray-200" />
                </div>
                <div className="relative flex justify-center text-sm">
                    <span className="px-4 bg-white text-gray-500">
                        {t('auth.social.orContinueWith', {
                            defaultValue: 'Or continue with'
                        })}
                    </span>
                </div>
            </div>

            {/* Social buttons */}
            <div className="grid grid-cols-2 gap-3">
                {/* Google */}
                {providers?.google && (
                    <button
                        type="button"
                        onClick={handleGoogleLogin}
                        disabled={isDisabled}
                        className={`
                            relative flex items-center justify-center gap-2
                            w-full px-4 py-3 rounded-lg
                            border border-gray-200 bg-white
                            text-sm font-medium text-gray-700
                            transition-all duration-200
                            ${isDisabled
                                ? 'opacity-50 cursor-not-allowed'
                                : 'hover:bg-gray-50 hover:border-gray-300 hover:shadow-sm'
                            }
                            focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-[var(--color-primary)]
                        `}
                        aria-label={t('auth.social.loginWithGoogle', { defaultValue: 'Sign in with Google' })}
                    >
                        {loadingProvider === 'google' ? (
                            <svg className="w-5 h-5 animate-spin text-gray-400" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                            </svg>
                        ) : (
                            <svg className="w-5 h-5" viewBox="0 0 24 24">
                                <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" />
                                <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
                                <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" />
                                <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" />
                            </svg>
                        )}
                        <span>Google</span>
                    </button>
                )}

                {/* Facebook */}
                {providers?.facebook && (
                    <button
                        type="button"
                        onClick={handleFacebookLogin}
                        disabled={isDisabled}
                        className={`
                            relative flex items-center justify-center gap-2
                            w-full px-4 py-3 rounded-lg
                            bg-[#1877F2] border border-[#1877F2]
                            text-sm font-medium text-white
                            transition-all duration-200
                            ${isDisabled
                                ? 'opacity-50 cursor-not-allowed'
                                : 'hover:bg-[#166FE5] hover:shadow-sm'
                            }
                            focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-[#1877F2]
                        `}
                        aria-label={t('auth.social.loginWithFacebook', { defaultValue: 'Sign in with Facebook' })}
                    >
                        {loadingProvider === 'facebook' ? (
                            <svg className="w-5 h-5 animate-spin text-white/70" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                            </svg>
                        ) : (
                            <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 24 24">
                                <path d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z" />
                            </svg>
                        )}
                        <span>Facebook</span>
                    </button>
                )}
            </div>
        </div>
    );
};

// Type declarations for social SDK globals
declare global {
    interface Window {
        google?: {
            accounts: {
                id: {
                    initialize: (config: {
                        client_id: string;
                        callback: (response: { credential: string }) => void;
                    }) => void;
                    prompt: () => void;
                };
            };
        };
        FB?: {
            init: (config: {
                appId: string;
                cookie: boolean;
                xfbml: boolean;
                version: string;
            }) => void;
            login: (
                callback: (response: { authResponse?: { accessToken: string } }) => void,
                options: { scope: string }
            ) => void;
        };
    }
}

export default SocialLoginButtons;
