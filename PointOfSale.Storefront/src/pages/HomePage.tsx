/**
 * Home Page
 * 
 * Fully Dynamic Storefront Homepage
 * 
 * Homepage sections come from the tenant StoreConfig live page layout so
 * storefront rendering matches the POS Admin storefront builder.
 * 
 * A fallback hero is shown ONLY when no sections are configured yet,
 * to ensure a sensible default experience for new stores.
 * 
 * Phase 10.1: Multi-language support
 * Phase F1.1: Auth UI integration (GuestAuthCTA, VerifyEmailBanner)
 */

import React from 'react';
import { Layout } from '@/components/layout';
import { Button } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { SectionRenderer } from '@/components/sections/SectionRenderer';
import { useCategories } from '@/hooks';
import { useStoreConfigStore, selectConfig, selectStoreName } from '@/store';
import { useTranslation } from '@/i18n';
import { GuestAuthCTA, VerifyEmailBanner } from '@/auth/components';
import { useAuth } from '@/auth/hooks';

/**
 * Home Page Component - Fully Dynamic
 */
export const HomePage: React.FC = () => {
    const config = useStoreConfigStore(selectConfig);
    const storeName = useStoreConfigStore(selectStoreName);
    const { isAuthenticated, emailVerified, customer } = useAuth();
    const homeSections = config.pages?.home?.sections ?? [];

    // Categories for Layout header navigation
    const { categories } = useCategories();

    return (
        <Layout categories={categories}>
            {/* Verify Email Banner - shown only for authenticated users with unverified email */}
            {isAuthenticated && !emailVerified && customer && (
                <VerifyEmailBanner
                    email={customer.email}
                    className="sticky top-16 z-20"
                />
            )}

            {/* Dynamic Homepage Sections */}
            {homeSections.length > 0 ? (
                <SectionRenderer sections={homeSections} />
            ) : (
                // Fallback: Default hero when no sections configured yet
                <DefaultHeroFallback storeName={storeName} />
            )}

            {/* Guest CTA - shown only for unauthenticated users */}
            <GuestAuthCTA variant="card" />
        </Layout>
    );
};

/**
 * Default Hero Fallback
 * Shown only when no sections are configured in the admin builder.
 * This ensures new stores have a sensible default display.
 */
const DefaultHeroFallback: React.FC<{ storeName: string }> = ({ storeName }) => {
    const { t, isRTL } = useTranslation();

    return (
        <section className="relative bg-secondary text-white overflow-hidden">
            {/* Background Pattern */}
            <div className="absolute inset-0 opacity-5">
                <div className="absolute inset-0" style={{
                    backgroundImage: `url("data:image/svg+xml,%3Csvg width='60' height='60' viewBox='0 0 60 60' xmlns='http://www.w3.org/2000/svg'%3E%3Cg fill='none' fill-rule='evenodd'%3E%3Cg fill='%23ffffff' fill-opacity='1'%3E%3Cpath d='M36 34v-4h-2v4h-4v2h4v4h2v-4h4v-2h-4zm0-30V0h-2v4h-4v2h4v4h2V6h4V4h-4zM6 34v-4H4v4H0v2h4v4h2v-4h4v-2H6zM6 4V0H4v4H0v2h4v4h2V6h4V4H6z'/%3E%3C/g%3E%3C/g%3E%3C/svg%3E")`,
                }} />
            </div>

            <div className="container mx-auto px-4 py-16 md:py-24 lg:py-32 relative">
                <div className="max-w-3xl mx-auto text-center">
                    {/* Tagline */}
                    <div className="inline-flex items-center gap-2 px-4 py-2 bg-white/10 rounded-full mb-6 backdrop-blur-sm">
                        <span className="w-2 h-2 bg-primary rounded-full animate-pulse" />
                        <span className="text-white/80 text-sm font-medium">{t('home.welcome', { storeName })}</span>
                    </div>

                    {/* Main Heading */}
                    <h1 className="text-4xl md:text-5xl lg:text-6xl font-bold mb-6 leading-tight tracking-tight">
                        {t('home.heroTitle')}{' '}
                        <span className="text-primary">{t('home.heroTitleHighlight')}</span>
                    </h1>

                    {/* Subheading */}
                    <p className="text-white/70 text-lg md:text-xl mb-10 max-w-2xl mx-auto leading-relaxed">
                        {t('home.heroSubtitle')}
                    </p>

                    {/* CTA Buttons */}
                    <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
                        <AffiliateLink href="/products">
                            <Button size="lg" variant="primary">
                                {t('home.shopNow')}
                                <svg className={`w-5 h-5 ${isRTL ? 'mr-2 rotate-180' : 'ml-2'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 8l4 4m0 0l-4 4m4-4H3" />
                                </svg>
                            </Button>
                        </AffiliateLink>
                    </div>

                    {/* Setup Hint for Admin */}
                    <div className="mt-12 pt-8 border-t border-white/10">
                        <p className="text-white/50 text-sm">
                            {t('home.builderHint')}{' '}
                            <span className="text-primary/80">{t('home.storefrontBuilder')}</span>
                        </p>
                    </div>
                </div>
            </div>

            {/* Bottom Wave */}
            <div className="absolute bottom-0 left-0 right-0">
                <svg viewBox="0 0 1440 120" fill="none" xmlns="http://www.w3.org/2000/svg" className="w-full h-auto">
                    <path d="M0 120L60 105C120 90 240 60 360 45C480 30 600 30 720 37.5C840 45 960 60 1080 67.5C1200 75 1320 75 1380 75L1440 75V120H1380C1320 120 1200 120 1080 120C960 120 840 120 720 120C600 120 480 120 360 120C240 120 120 120 60 120H0Z" fill="var(--color-background)" />
                </svg>
            </div>
        </section>
    );
};

