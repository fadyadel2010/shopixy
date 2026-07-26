/**
 * Account Page
 * Phase F1.1: Auth UI Integration
 * 
 * Account page for authenticated users with dynamic theme colors.
 */

import React from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { useCategories } from '@/hooks';
import { useTranslation } from '@/i18n';
import { useAuth, useAccountStatusGuard } from '@/auth/hooks';
import { VerifyEmailBanner, SecuritySection, SessionsSection, OrderHistorySection } from '@/auth/components';
import { ProtectedRoute } from '@/core/guards';

/**
 * Account Page - Protected route
 */
const AccountPageContent: React.FC = () => {
    const { t, direction } = useTranslation();
    const navigate = useNavigate();
    const { customer, emailVerified, logout } = useAuth();
    const { categories } = useCategories();

    // GUARD: Check for account deactivation
    useAccountStatusGuard();

    const handleLogout = async () => {
        try {
            await logout();
            navigate('/');
        } catch {
            // Error handled by store
        }
    };

    return (
        <Layout categories={categories}>
            <div dir={direction} className="min-h-[60vh] bg-background">
                {/* Email verification banner */}
                {customer && !emailVerified && (
                    <VerifyEmailBanner email={customer.email} />
                )}

                <div className="container mx-auto px-4 py-12">
                    {/* Header */}
                    <div className="mb-8 flex flex-col md:flex-row md:items-end justify-between gap-4">
                        <div>
                            <h1 className="text-3xl font-bold text-text mb-2">
                                {t('account.title', { defaultValue: 'My Account' })}
                            </h1>
                            <p className="text-muted">
                                {t('account.welcome', {
                                    name: customer?.displayName?.split(' ')[0] || 'there',
                                    defaultValue: 'Welcome back, {name}!'
                                })}
                            </p>
                        </div>

                        <button
                            onClick={handleLogout}
                            className="flex items-center gap-2 px-6 py-2.5 border border-red-200 text-red-600 rounded-lg hover:bg-red-50 transition-colors self-start md:self-auto"
                        >
                            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                    d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                            </svg>
                            {t('header.logout', { defaultValue: 'Logout' })}
                        </button>
                    </div>

                    {/* Main Content Grid */}
                    <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
                        {/* Left Column: Profile & Orders */}
                        <div className="lg:col-span-2 space-y-8">
                            {/* Profile Card */}
                            <div className="bg-secondary/5 border border-muted/10 rounded-xl p-6 shadow-sm">
                                <div className="flex items-center gap-4 mb-6">
                                    <div className="w-12 h-12 bg-primary/10 rounded-full flex items-center justify-center">
                                        <svg className="w-6 h-6 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                                        </svg>
                                    </div>
                                    <div>
                                        <h2 className="text-lg font-semibold text-text">
                                            {t('account.profile.title', { defaultValue: 'Profile' })}
                                        </h2>
                                        <p className="text-sm text-muted">
                                            {t('account.profile.description', { defaultValue: 'View and edit your info' })}
                                        </p>
                                    </div>
                                </div>
                                <div className="space-y-4">
                                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                                        <div className="p-4 bg-background/50 border border-muted/10 rounded-lg">
                                            <span className="text-xs font-medium text-muted uppercase tracking-wider block mb-1">
                                                {t('auth.fields.displayName', { defaultValue: 'Full Name' })}
                                            </span>
                                            <span className="text-text font-medium text-lg">{customer?.displayName || '-'}</span>
                                        </div>
                                        <div className="p-4 bg-background/50 border border-muted/10 rounded-lg">
                                            <span className="text-xs font-medium text-muted uppercase tracking-wider block mb-1">
                                                {t('auth.fields.email', { defaultValue: 'Email' })}
                                            </span>
                                            <span className="text-text font-medium text-lg truncate block" title={customer?.email}>
                                                {customer?.email || '-'}
                                            </span>
                                        </div>
                                        {customer?.phone && (
                                            <div className="p-4 bg-background/50 border border-muted/10 rounded-lg">
                                                <span className="text-xs font-medium text-muted uppercase tracking-wider block mb-1">
                                                    {t('auth.fields.phone', { defaultValue: 'Phone' })}
                                                </span>
                                                <span className="text-text font-medium text-lg">{customer.phone}</span>
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>

                            {/* Orders Section */}
                            <OrderHistorySection />

                            {/* Wishlist Card (Phase 10.3.6) */}
                            <Link
                                to="/wishlist"
                                className="block bg-secondary/5 border border-muted/10 rounded-xl p-6 shadow-sm hover:shadow-md hover:border-primary/20 transition-all"
                            >
                                <div className="flex items-center gap-4">
                                    <div className="w-12 h-12 bg-red-50 rounded-full flex items-center justify-center">
                                        <svg className="w-6 h-6 text-red-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
                                                d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z" />
                                        </svg>
                                    </div>
                                    <div className="flex-1">
                                        <h2 className="text-lg font-semibold text-text">
                                            {t('wishlist.title', { defaultValue: 'My Wishlist' })}
                                        </h2>
                                        <p className="text-sm text-muted">
                                            {t('home.guestCTA.benefit2', { defaultValue: 'Save your favorites' })}
                                        </p>
                                    </div>
                                    <svg className="w-5 h-5 text-muted" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                                    </svg>
                                </div>
                            </Link>
                        </div>

                        {/* Right Column: Security & Sessions */}
                        <div className="space-y-8">
                            {/* Security Section */}
                            <SecuritySection />

                            {/* Sessions Section */}
                            <SessionsSection />
                        </div>
                    </div>

                    {/* Actions */}
                    <div className="mt-12 pt-8 border-t border-muted/20">
                        <div className="flex flex-col sm:flex-row items-center justify-between gap-4">
                            <Link
                                to="/"
                                className="text-muted hover:text-text transition-colors flex items-center gap-2"
                            >
                                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 19l-7-7m0 0l7-7m-7 7h18" />
                                </svg>
                                {t('common.back', { defaultValue: 'Back to store' })}
                            </Link>
                        </div>
                    </div>
                </div>
            </div>
        </Layout>
    );
};

/**
 * Account Page with ProtectedRoute wrapper
 */
export const AccountPage: React.FC = () => {
    return (
        <ProtectedRoute>
            <AccountPageContent />
        </ProtectedRoute>
    );
};

export default AccountPage;
