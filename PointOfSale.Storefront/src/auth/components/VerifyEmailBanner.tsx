/**
 * VerifyEmailBanner Component
 * Phase F1: Storefront Auth UI
 * 
 * Enhanced banner shown when customer email is not verified.
 * Uses dynamic theme colors and improved design.
 */

import React, { useState, useCallback } from 'react';
import { useTranslation } from '@/i18n';
import { httpClient } from '@/services/api/httpClient';

interface VerifyEmailBannerProps {
    email: string;
    className?: string;
}

/**
 * Email verification banner with enhanced design
 */
export const VerifyEmailBanner: React.FC<VerifyEmailBannerProps> = ({
    email,
    className = '',
}) => {
    const { t } = useTranslation();

    const [isResending, setIsResending] = useState(false);
    const [resendSuccess, setResendSuccess] = useState(false);
    const [resendError, setResendError] = useState<string | null>(null);
    const [cooldown, setCooldown] = useState(0);

    // Handle resend verification email
    const handleResend = useCallback(async () => {
        if (cooldown > 0 || isResending) return;

        setIsResending(true);
        setResendError(null);
        setResendSuccess(false);

        try {
            await httpClient.post(
                '/api/storefront/auth/resend-verification',
                {},
                { withCredentials: true }
            );

            setResendSuccess(true);
            // Start 60 second cooldown
            setCooldown(60);

            const interval = setInterval(() => {
                setCooldown((prev) => {
                    if (prev <= 1) {
                        clearInterval(interval);
                        return 0;
                    }
                    return prev - 1;
                });
            }, 1000);
        } catch (error) {
            // Don't show specific error to prevent enumeration
            setResendError(t('auth.verifyEmail.resendError', {
                defaultValue: 'Could not send verification email. Please try again later.'
            }));
        } finally {
            setIsResending(false);
        }
    }, [cooldown, isResending, t]);

    // Mask email for privacy
    const maskedEmail = email.replace(/(.{2})(.*)(@.*)/, '$1***$3');

    return (
        <div
            className={`
                relative overflow-hidden
                bg-gradient-to-r from-primary/5 via-primary/10 to-primary/5
                border-b border-primary/20
                ${className}
            `}
            role="status"
            aria-live="polite"
        >
            {/* Decorative gradient line at top */}
            <div className="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r from-primary via-secondary to-primary opacity-80" />

            <div className="container mx-auto px-4 py-4">
                <div className="flex flex-col sm:flex-row items-start sm:items-center gap-4">
                    {/* Icon */}
                    <div className="flex-shrink-0">
                        <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center">
                            <svg className="w-5 h-5 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                            </svg>
                        </div>
                    </div>

                    {/* Content */}
                    <div className="flex-1 min-w-0">
                        <h4 className="text-sm font-semibold text-text">
                            {t('auth.verifyEmail.title', {
                                defaultValue: 'Verify your email'
                            })}
                        </h4>

                        <p className="text-sm text-muted mt-0.5">
                            {t('auth.verifyEmail.description', {
                                email: maskedEmail,
                                defaultValue: `We sent a verification link to ${maskedEmail}. Please check your inbox.`
                            })}
                        </p>

                        {/* Success message */}
                        {resendSuccess && (
                            <p className="text-sm text-green-600 mt-2 flex items-center gap-1.5 animate-fade-in">
                                <svg className="w-4 h-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                {t('auth.verifyEmail.resendSuccess', {
                                    defaultValue: 'Verification email sent! Please check your inbox.'
                                })}
                            </p>
                        )}

                        {/* Error message */}
                        {resendError && (
                            <p className="text-sm text-red-600 mt-2 flex items-center gap-1.5 animate-fade-in">
                                <svg className="w-4 h-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                                </svg>
                                {resendError}
                            </p>
                        )}
                    </div>

                    {/* Resend button */}
                    <div className="flex-shrink-0">
                        <button
                            onClick={handleResend}
                            disabled={cooldown > 0 || isResending}
                            className={`
                                inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium
                                transition-all duration-200
                                ${cooldown > 0 || isResending
                                    ? 'bg-muted/10 text-muted cursor-not-allowed'
                                    : 'bg-primary/10 text-primary hover:bg-primary/20 focus:outline-none focus:ring-2 focus:ring-primary/30 focus:ring-offset-2 focus:ring-offset-background'
                                }
                            `}
                        >
                            {isResending ? (
                                <>
                                    <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                                    </svg>
                                    <span>{t('common.sending', { defaultValue: 'Sending...' })}</span>
                                </>
                            ) : cooldown > 0 ? (
                                <>
                                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                                    </svg>
                                    <span>
                                        {t('auth.verifyEmail.resendCooldown', {
                                            seconds: cooldown,
                                            defaultValue: `Resend in ${cooldown}s`
                                        })}
                                    </span>
                                </>
                            ) : (
                                <>
                                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                    </svg>
                                    <span>
                                        {t('auth.verifyEmail.resendButton', {
                                            defaultValue: 'Resend verification email'
                                        })}
                                    </span>
                                </>
                            )}
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default VerifyEmailBanner;
