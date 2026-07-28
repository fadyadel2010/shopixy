/**
 * Phoenix Sports Theme — Header (Phase T1.1)
 *
 * Layout identity: floating white pill header centered on the warm off-white
 * canvas, black ink, centered brand wordmark (Playfair Display), centered
 * uppercase Jost navigation, search + cart icons, sticky with scroll elevation,
 * graceful mobile degradation (slide-in drawer).
 *
 * REUSE: This component reuses the platform's existing navigation/header
 * infrastructure rather than reimplementing it:
 *   - AffiliateLink            (themed link, affiliate-aware)
 *   - CartIconButton           (cart badge, animated)
 *   - WishlistIconButton       (wishlist badge)
 *   - LanguageSwitcher         (i18n switch)
 *   - AuthNavItems             (account/auth links)
 *   - useNavigationStore       (dynamic CMS nav links)
 *   - useScrollPosition        (sticky elevation, platform hook)
 *   - useTranslation           (i18n + RTL)
 * Only the visual shell (floating white pill, black ink, Playfair wordmark,
 * Jost uppercase nav) is Phoenix-specific. No business logic is duplicated.
 */

import React, { useEffect, useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { CartIconButton } from '@/components/common/CartBadge';
import { WishlistIconButton } from '@/components/common/WishlistBadge';
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useNavigationStore, resolveNavLinkUrl } from '@/stores/navigationStore';
import { useScrollPosition } from '@/hooks/useScrollPosition';
import { useTranslation } from '@/i18n';
import { AuthNavItems } from '@/auth/components';
import {
    PHOENIX_CONTAINER,
    PHOENIX_FONT_DISPLAY,
    PHOENIX_NAV_LINK,
    PHOENIX_HEADER_SHELL,
    phoenixNavLinkClass,
} from './phoenixTokens';
import AnnouncementMarquee from './AnnouncementMarquee';

const PhoenixHeader: React.FC = () => {
    const { t, language } = useTranslation();
    const location = useLocation();
    const storeName = useStoreConfigStore(selectStoreName);
    const loadNavigation = useNavigationStore((state) => state.loadNavigation);
    const headerLinks = useNavigationStore((state) => state.navigation?.header.links ?? []);
    const [mobileOpen, setMobileOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

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
            <AnnouncementMarquee text={t('announcement.defaultText')} />

            {/* Floating white pill, centered on the canvas, sticky. */}
            <div className={`sticky top-0 z-30 w-full ${PHOENIX_HEADER_SHELL}`}>
                <header
                    role="banner"
                    className={`w-full rounded-xl bg-white shadow-[0_1px_4px_rgba(0,0,0,0.08)] transition-transform duration-300 text-black ${
                        isScrolled ? 'shadow-[0_8px_24px_rgba(0,0,0,0.12)]' : ''
                    }`}
                >
                    <div className={`${PHOENIX_CONTAINER} py-3`}>
                        {/* Centered brand row with side actions */}
                        <div className="flex items-center justify-between gap-4">
                            <div className="hidden flex-1 items-center gap-2 md:flex">
                                <WishlistIconButton className="text-black/70 hover:bg-black/5 hover:text-black" />
                                <CartIconButton className="text-black/70 hover:bg-black/5 hover:text-black" />
                            </div>

                            <AffiliateLink
                                href="/"
                                className={`min-w-0 flex-1 truncate text-center text-2xl font-medium uppercase tracking-[0.12em] text-black md:text-3xl ${PHOENIX_FONT_DISPLAY}`}
                            >
                                {storeName}
                            </AffiliateLink>

                            <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                                <LanguageSwitcher variant="text" size="sm" className="text-black/60 hover:text-black" />
                                <div className="border-l border-black/10 pl-3">
                                    <AuthNavItems variant="desktop" className="!text-black/80" />
                                </div>
                            </div>

                            <button
                                type="button"
                                onClick={() => setMobileOpen((value) => !value)}
                                className="flex h-10 w-10 items-center justify-center rounded-full border border-black/10 text-black/80 md:hidden"
                                aria-label={t('header.menu')}
                                aria-expanded={mobileOpen}
                            >
                                <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                                </svg>
                            </button>
                        </div>

                        {/* Centered primary navigation */}
                        <nav
                            className="mt-2 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-black/5 pt-3 md:flex"
                            aria-label={t('accessibility.mainNavigation')}
                        >
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className={phoenixNavLinkClass(isActive(link.url))}
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </nav>
                    </div>

                    {/* Mobile drawer */}
                    {mobileOpen ? (
                        <div className="border-t border-black/5 bg-white px-4 py-4 md:hidden">
                            <div className="mb-4 flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <WishlistIconButton className="text-black/70 hover:bg-black/5 hover:text-black" />
                                    <CartIconButton className="text-black/70 hover:bg-black/5 hover:text-black" />
                                </div>
                                <LanguageSwitcher variant="text" size="sm" className="text-black/60 hover:text-black" />
                            </div>
                            <nav className="space-y-2" aria-label={t('header.menu')}>
                                {links.map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.url}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                        onClick={() => setMobileOpen(false)}
                                        className={`block rounded-xl px-4 py-3 ${PHOENIX_NAV_LINK} ${
                                            isActive(link.url) ? 'bg-black text-white' : 'bg-black/5 text-black/70'
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
            </div>
        </>
    );
};

export default PhoenixHeader;
