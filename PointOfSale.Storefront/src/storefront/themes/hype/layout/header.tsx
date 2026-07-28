/**
 * Hype Theme — Header (Phase H1.1)
 *
 * Layout identity: white sticky bar, orange brand wordmark (left), inline
 * orange navigation (center/right), search + wishlist + cart icon cluster
 * (right), graceful mobile degradation.
 *
 * This differs from Pearl (which is transparent-over-hero, centered, dark text
 * on dark). Hype uses the reference site's white header with orange (#F06736)
 * nav. Only the visual shell is Hype-specific.
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
    HYPE_CONTAINER,
    HYPE_FONT_BODY,
    HYPE_NAV_LINK,
    hypeNavLinkClass,
} from './hypeTokens';
import AnnouncementMarquee from './AnnouncementMarquee';

const HypeHeader: React.FC = () => {
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

            <header
                role="banner"
                className={`sticky top-0 z-30 w-full transition-colors duration-300 ${
                    isScrolled
                        ? 'bg-white shadow-[0_4px_20px_rgba(0,0,0,0.08)]'
                        : 'bg-white'
                } ${HYPE_FONT_BODY} text-[#F06736]`}
            >
                <div className={`${HYPE_CONTAINER} py-3 sm:py-4`}>
                    {/* Brand row with side actions */}
                    <div className="flex items-center justify-between gap-4">
                        <div className="hidden flex-1 items-center gap-2 md:flex">
                            <WishlistIconButton className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                            <CartIconButton className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className={`min-w-0 flex-1 truncate text-center text-2xl font-medium uppercase tracking-[0.16em] text-[#F06736] md:text-3xl lg:tracking-[0.22em] ${HYPE_FONT_BODY}`}
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                            <LanguageSwitcher variant="text" size="sm" className="text-[#F06736]/60 hover:text-[#F06736]" />
                            <div className="border-l border-[#F06736]/15 pl-3">
                                <AuthNavItems variant="desktop" className="!text-[#F06736]/80" />
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-[#F06736]/20 text-[#F06736] md:hidden"
                            aria-label={t('header.menu')}
                            aria-expanded={mobileOpen}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>

                    {/* Primary navigation */}
                    <nav
                        className="mt-4 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-[#F06736]/10 pt-3 md:flex"
                        aria-label={t('accessibility.mainNavigation')}
                    >
                        {links.map((link) => (
                            <AffiliateLink
                                key={link.key}
                                href={link.url}
                                target={link.openInNewTab ? '_blank' : undefined}
                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                className={hypeNavLinkClass(isActive(link.url))}
                            >
                                {link.label}
                            </AffiliateLink>
                        ))}
                    </nav>
                </div>

                {/* Mobile drawer */}
                {mobileOpen ? (
                    <div className="border-t border-[#F06736]/10 bg-white px-4 py-4 md:hidden">
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <WishlistIconButton className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                                <CartIconButton className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                            </div>
                            <LanguageSwitcher variant="text" size="sm" className="text-[#F06736]/60 hover:text-[#F06736]" />
                        </div>
                        <nav className="space-y-2" aria-label={t('header.menu')}>
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    onClick={() => setMobileOpen(false)}
                                    className={`block rounded-md px-4 py-3 ${HYPE_NAV_LINK} ${
                                        isActive(link.url) ? 'bg-[#F06736] text-white' : 'bg-[#F06736]/5 text-[#F06736]'
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

export default HypeHeader;
