/**
 * AuthNavItems Component
 * Phase F1.1: Auth UI Navigation Integration
 * 
 * Shows Login/Register for guests, Account/Logout for authenticated users.
 * Used in Header navigation (desktop and mobile).
 * 
 * SECURITY: All auth state comes from useAuth() hook, never directly from tokens.
 */

import React, { useCallback } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useTranslation } from '@/i18n';
import { useAuth } from '../hooks/useAuth';

interface AuthNavItemsProps {
    /** Variant for styling */
    variant?: 'desktop' | 'mobile';
    /** Additional class names */
    className?: string;
    /** Callback when logout is clicked */
    onLogout?: () => void;
    /** Callback when any link is clicked (mobile menu close) */
    onLinkClick?: () => void;
}

/**
 * Auth navigation items - Guest sees Login/Register, User sees Account/Logout
 */
export const AuthNavItems: React.FC<AuthNavItemsProps> = ({
    variant = 'desktop',
    className = '',
    onLogout,
    onLinkClick,
}) => {
    const { t } = useTranslation();
    const location = useLocation();
    const { isAuthenticated, isLoading, customer, logout, status } = useAuth();

    // Handle logout
    const handleLogout = useCallback(async () => {
        try {
            await logout();
            onLogout?.();
            // Redirect to home after logout
            window.location.href = '/';
        } catch (error) {
            console.error('[Auth] Logout failed:', error);
        }
    }, [logout, onLogout]);

    // Build return URL for auth pages
    const getAuthUrl = (base: string) => {
        const current = location.pathname + location.search;
        if (current !== '/' && !current.startsWith('/login') && !current.startsWith('/register')) {
            return `${base}?return=${encodeURIComponent(current)}`;
        }
        return base;
    };

    // Keep navigation neutral while auth is unresolved or degraded.
    if ((status === 'unknown' && isLoading) || status === 'degraded') {
        return variant === 'desktop' ? (
            <div className="flex items-center gap-1">
                <div className="rounded-lg border border-current px-3 py-2 text-sm text-inherit opacity-80">
                    {status === 'degraded' ? t('auth.reconnecting') : t('auth.loading')}
                </div>
            </div>
        ) : null;
    }

    // Desktop variant
    if (variant === 'desktop') {
        if (isAuthenticated && customer) {
            return (
                <div className={`flex items-center gap-1 ${className}`}>
                    {/* Account dropdown or link */}
                    <Link
                        to="/account"
                        aria-label={t('header.account')}
                        onClick={onLinkClick}
                        className="px-3 py-2 rounded-lg transition-all font-medium text-inherit hover:opacity-80 flex items-center gap-2 focus-visible:ring-2 focus-visible:ring-current"
                    >
                        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                        </svg>
                        <span className="max-w-[100px] truncate hidden lg:inline">
                            {customer.displayName?.split(' ')[0] || t('header.account')}
                        </span>
                    </Link>
                    <button
                        onClick={handleLogout}
                        className="px-3 py-2 rounded-lg transition-all font-medium text-inherit hover:opacity-80 focus-visible:ring-2 focus-visible:ring-current"
                        aria-label={t('header.logout')}
                        title={t('header.logout')}
                    >
                        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                        </svg>
                    </button>
                </div>
            );
        }

        // Guest - show login/register
        return (
            <div className={`flex items-center gap-1 ${className}`}>
                <Link
                    to={getAuthUrl('/login')}
                    onClick={onLinkClick}
                    className="px-3 py-2 rounded-lg transition-all font-medium text-inherit hover:opacity-80 focus-visible:ring-2 focus-visible:ring-current"
                >
                    {t('header.login')}
                </Link>
                <Link
                    to={getAuthUrl('/register')}
                    onClick={onLinkClick}
                    className="px-3 py-2 rounded-lg transition-all font-medium border border-current text-inherit hover:opacity-80 focus-visible:ring-2 focus-visible:ring-current"
                >
                    {t('header.register')}
                </Link>
            </div>
        );
    }

    // Mobile variant
    if (isAuthenticated && customer) {
        return (
            <div className={`space-y-1 ${className}`}>
                {/* Account link */}
                <Link
                    to="/account"
                    onClick={onLinkClick}
                    className="flex items-center px-4 py-3.5 min-h-[44px] text-inherit hover:opacity-80 transition-colors"
                >
                    <svg className="w-5 h-5 ltr:mr-3 rtl:ml-3 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                            d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                    </svg>
                    <span className="flex-1">{t('header.account')}</span>
                    <span className="text-sm text-muted truncate max-w-[120px]">
                        {customer.displayName?.split(' ')[0]}
                    </span>
                </Link>

                {/* Logout button */}
                <button
                    onClick={() => {
                        handleLogout();
                        onLinkClick?.();
                    }}
                    className="flex items-center w-full px-4 py-3.5 min-h-[44px] text-red-600 hover:bg-red-50 transition-colors"
                >
                    <svg className="w-5 h-5 ltr:mr-3 rtl:ml-3 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                            d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                    </svg>
                    {t('header.logout')}
                </button>
            </div>
        );
    }

    // Guest - mobile
    return (
        <div className={`space-y-1 ${className}`}>
            <Link
                to={getAuthUrl('/login')}
                onClick={onLinkClick}
                className="flex items-center px-4 py-3.5 min-h-[44px] text-inherit hover:opacity-80 transition-colors"
            >
                <svg className="w-5 h-5 ltr:mr-3 rtl:ml-3 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1" />
                </svg>
                {t('header.login')}
            </Link>
            <Link
                to={getAuthUrl('/register')}
                onClick={onLinkClick}
                className="flex items-center px-4 py-3.5 min-h-[44px] text-inherit font-medium hover:opacity-80 transition-colors"
            >
                <svg className="w-5 h-5 ltr:mr-3 rtl:ml-3 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                        d="M18 9v3m0 0v3m0-3h3m-3 0h-3m-2-5a4 4 0 11-8 0 4 4 0 018 0zM3 20a6 6 0 0112 0v1H3v-1z" />
                </svg>
                {t('header.register')}
            </Link>
        </div>
    );
};

export default AuthNavItems;
