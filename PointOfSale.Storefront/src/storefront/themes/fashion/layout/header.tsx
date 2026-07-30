import React, { useEffect, useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { CartIconButton } from '@/components/common/CartBadge';
import { WishlistIconButton } from '@/components/common/WishlistBadge';
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useNavigationStore, resolveNavLinkUrl } from '@/stores/navigationStore';
import { useTranslation } from '@/i18n';
import { AuthNavItems } from '@/auth/components';

const FashionHeader: React.FC = () => {
    const { t, language } = useTranslation();
    const location = useLocation();
    const storeName = useStoreConfigStore(selectStoreName);
    const loadNavigation = useNavigationStore((state) => state.loadNavigation);
    const headerLinks = useNavigationStore((state) => state.navigation?.header.links ?? []);
    const [mobileOpen, setMobileOpen] = useState(false);

    useEffect(() => {
        loadNavigation();
    }, [loadNavigation]);

    const links = useMemo(() => {
        return headerLinks.map((link, index) => ({
            key: `${link.slug || link.path || link.label}-${index}`,
            label: language === 'ar' && link.labelAr ? link.labelAr : link.label,
            url: resolveNavLinkUrl(link),
            openInNewTab: link.openInNewTab,
        }));
    }, [headerLinks, language]);

    const isActive = (path: string) => {
        if (path === '/') {
            return location.pathname === '/';
        }

        return location.pathname.startsWith(path);
    };

    return (
        <>
            <header className="sticky top-0 z-30 border-b border-white/10 bg-[#161311] text-white shadow-[0_18px_40px_rgba(0,0,0,0.22)]">
                <div className="mx-auto max-w-7xl px-4 py-3 sm:py-4">
                    <div className="flex items-center justify-between text-[11px] uppercase tracking-[0.32em] text-white/60">
                        <span>Editorial storefront</span>
                        <div className="hidden items-center gap-2 md:flex">
                            <LanguageSwitcher variant="text" size="sm" className="text-white/60 hover:text-white" />
                            <div className="border-l border-white/10 pl-3">
                                <AuthNavItems variant="desktop" className="!text-white/80" />
                            </div>
                        </div>
                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-white/15 md:hidden"
                            aria-label={t('header.menu')}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>

                    <div className="mt-5 flex items-center justify-between gap-4">
                        <div className="hidden md:flex md:flex-1">
                            <WishlistIconButton className="text-white/70 hover:bg-white/10 hover:text-white" />
                            <CartIconButton className="text-white/70 hover:bg-white/10 hover:text-white" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className="min-w-0 flex-1 truncate text-center text-2xl font-semibold uppercase tracking-[0.18em] text-white md:text-3xl lg:tracking-[0.28em]"
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden md:flex md:flex-1 md:justify-end">
                            <span className="rounded-full border border-white/10 px-4 py-2 text-[11px] uppercase tracking-[0.28em] text-white/60">
                                New season
                            </span>
                        </div>
                    </div>

                    {links.length > 0 ? (
                        <nav className="mt-5 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-white/10 pt-4 md:flex" aria-label="Fashion navigation">
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className={`text-xs uppercase tracking-[0.18em] transition-colors lg:text-sm lg:tracking-[0.22em] ${
                                        isActive(link.url) ? 'text-white' : 'text-white/60 hover:text-white'
                                    }`}
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </nav>
                    ) : null}
                </div>

                {mobileOpen ? (
                    <div className="border-t border-white/10 bg-[#120f0d] px-4 py-4 md:hidden">
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <WishlistIconButton className="text-white/70 hover:bg-white/10 hover:text-white" />
                                <CartIconButton className="text-white/70 hover:bg-white/10 hover:text-white" />
                            </div>
                            <LanguageSwitcher variant="text" size="sm" className="text-white/60 hover:text-white" />
                        </div>
                        <nav className="space-y-2" aria-label="Fashion mobile navigation">
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    onClick={() => setMobileOpen(false)}
                                    className={`block rounded-2xl px-4 py-3 text-sm uppercase tracking-[0.24em] ${
                                        isActive(link.url) ? 'bg-white text-[#161311]' : 'bg-white/5 text-white/70'
                                    }`}
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                            <div className="pt-3">
                                <AuthNavItems variant="mobile" onLinkClick={() => setMobileOpen(false)} />
                            </div>
                        </nav>
                    </div>
                ) : null}
            </header>
        </>
    );
};

export default FashionHeader;
