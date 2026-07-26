/**
 * GuestAuthCTA Component
 * Phase F1.1: Auth UI Visibility Enhancement
 * 
 * A call-to-action section for guests to login or register.
 * Only shown to unauthenticated users.
 * Disappears automatically after login.
 */

import React from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuth } from '../hooks/useAuth';

interface GuestAuthCTAProps {
    /** Variant for different display styles */
    variant?: 'banner' | 'card' | 'inline';
    /** Additional class names */
    className?: string;
}

/**
 * Guest CTA - Shown only to non-authenticated users
 */
export const GuestAuthCTA: React.FC<GuestAuthCTAProps> = ({
    variant = 'card',
    className = '',
}) => {
    const { t, isRTL } = useTranslation();
    const location = useLocation();
    const { isAuthenticated, status, isLoading } = useAuth();

    // Don't show guest messaging while auth is unresolved or degraded.
    if (isAuthenticated || status === 'unknown' || status === 'degraded' || isLoading) {
        return null;
    }

    // Build return URL for auth pages
    const getAuthUrl = (base: string) => {
        const current = location.pathname + location.search;
        if (current !== '/' && !current.startsWith('/login') && !current.startsWith('/register')) {
            return `${base}?return=${encodeURIComponent(current)}`;
        }
        return base;
    };

    // Banner variant - slim announcement bar style
    if (variant === 'banner') {
        return (
            <div className={`bg-gradient-to-r from-primary/10 via-primary/5 to-transparent py-3 px-4 ${className}`}>
                <div className="container mx-auto flex items-center justify-between flex-wrap gap-3">
                    <p className="text-sm text-text flex items-center gap-2">
                        <svg className="w-5 h-5 text-primary flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                        <span>
                            {t('home.guestBanner.message', {
                                defaultValue: 'Sign in to access your account and track your orders.'
                            })}
                        </span>
                    </p>
                    <div className="flex items-center gap-2">
                        <Link
                            to={getAuthUrl('/login')}
                            className="text-sm font-medium text-primary hover:underline"
                        >
                            {t('header.login')}
                        </Link>
                        <span className="text-muted">|</span>
                        <Link
                            to={getAuthUrl('/register')}
                            className="text-sm font-medium text-primary hover:underline"
                        >
                            {t('header.register')}
                        </Link>
                    </div>
                </div>
            </div>
        );
    }

    // Inline variant - simple link style
    if (variant === 'inline') {
        return (
            <p className={`text-sm text-muted ${className}`}>
                {t('home.guestInline.message', {
                    defaultValue: 'Already have an account?'
                })}{' '}
                <Link
                    to={getAuthUrl('/login')}
                    className="text-primary font-medium hover:underline"
                >
                    {t('header.login')}
                </Link>
            </p>
        );
    }

    // Card variant (default) - prominent CTA card
    return (
        <section className={`py-12 px-4 ${className}`}>
            <div className="container mx-auto">
                <div className="max-w-3xl mx-auto bg-gradient-to-br from-primary/5 via-background to-secondary/5 rounded-2xl p-8 md:p-12 text-center border border-muted/10 shadow-sm">
                    {/* Icon */}
                    <div className="mx-auto w-16 h-16 bg-primary/10 rounded-full flex items-center justify-center mb-6">
                        <svg className="w-8 h-8 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
                        </svg>
                    </div>

                    {/* Title */}
                    <h2 className="text-2xl md:text-3xl font-bold text-text mb-3">
                        {t('home.guestCTA.title', {
                            defaultValue: 'Join Our Community'
                        })}
                    </h2>

                    {/* Description */}
                    <p className="text-muted text-lg mb-8 max-w-xl mx-auto">
                        {t('home.guestCTA.description', {
                            defaultValue: 'Create an account to enjoy personalized shopping, order tracking, and exclusive member benefits.'
                        })}
                    </p>

                    {/* CTA Buttons */}
                    <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
                        <Link
                            to={getAuthUrl('/register')}
                            className="inline-flex items-center justify-center gap-2 px-8 py-3.5 bg-primary text-white font-medium rounded-lg hover:bg-primary/90 transition-all shadow-lg shadow-primary/20 hover:shadow-xl hover:shadow-primary/30 focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                        >
                            {t('home.guestCTA.createAccount', {
                                defaultValue: 'Create Account'
                            })}
                            <svg
                                className={`w-5 h-5 ${isRTL ? 'rotate-180' : ''}`}
                                fill="none"
                                viewBox="0 0 24 24"
                                stroke="currentColor"
                            >
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 8l4 4m0 0l-4 4m4-4H3" />
                            </svg>
                        </Link>
                        <Link
                            to={getAuthUrl('/login')}
                            className="inline-flex items-center justify-center gap-2 px-8 py-3.5 bg-background border border-muted/20 text-text font-medium rounded-lg hover:bg-muted/10 transition-all focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                        >
                            {t('home.guestCTA.signIn', {
                                defaultValue: 'Sign In'
                            })}
                        </Link>
                    </div>

                    {/* Benefits */}
                    <div className="mt-10 pt-8 border-t border-muted/10">
                        <div className="grid grid-cols-1 sm:grid-cols-3 gap-6 text-sm text-muted">
                            <div className="flex items-center justify-center gap-2">
                                <svg className="w-5 h-5 text-green-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                {t('home.guestCTA.benefit1', {
                                    defaultValue: 'Track your orders'
                                })}
                            </div>
                            <div className="flex items-center justify-center gap-2">
                                <svg className="w-5 h-5 text-green-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                {t('home.guestCTA.benefit2', {
                                    defaultValue: 'Save your favorites'
                                })}
                            </div>
                            <div className="flex items-center justify-center gap-2">
                                <svg className="w-5 h-5 text-green-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                {t('home.guestCTA.benefit3', {
                                    defaultValue: 'Faster checkout'
                                })}
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default GuestAuthCTA;
