import React, { useEffect, useMemo, useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { CartIconButton } from '@/components/common/CartBadge';
import { WishlistIconButton } from '@/components/common/WishlistBadge';
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher';
import { useStoreConfigStore, selectStoreName } from '@/store';
import { useNavigationStore, resolveNavLinkUrl } from '@/stores/navigationStore';
import { useTranslation } from '@/i18n';
import { AuthNavItems } from '@/auth/components';

const SevenHeader: React.FC = () => {
    const { t, language } = useTranslation();
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

    return (
        <>
            <header className="sticky top-0 z-30 bg-[#9D7877] text-white shadow-md">
                <div className="mx-auto max-w-7xl px-4 py-3 sm:py-4">
                    <div className="flex items-center justify-between">
                        {/* Logo */}
                        <AffiliateLink
                            href="/"
                            className="min-w-0 flex-1 truncate text-center text-2xl font-medium tracking-[-0.02em] text-white md:text-left md:text-3xl"
                        >
                            {storeName || 'La Femmea'}
                        </AffiliateLink>

                        {/* Desktop Nav */}
                        <nav className="hidden md:flex items-center gap-x-6 lg:gap-x-8" aria-label="Seven navigation">
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className={`text-xs font-medium uppercase tracking-[0.16em] text-white transition-colors hover:opacity-80 lg:text-sm`}
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                        </nav>

                        {/* Right Icons */}
                        <div className="flex items-center gap-2 lg:gap-3">
                            <LanguageSwitcher variant="text" size="sm" className="text-white hover:text-white/80" />
                            <WishlistIconButton className="text-white hover:bg-white/10 hover:text-white" />
                            <CartIconButton className="text-white hover:bg-white/10 hover:text-white" />
                            <button
                                type="button"
                                onClick={() => setMobileOpen((value) => !value)}
                                className="flex h-9 w-9 items-center justify-center rounded-full text-white md:hidden"
                                aria-label={t('header.menu')}
                            >
                                <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                                </svg>
                            </button>
                        </div>
                    </div>
                </div>

                {/* Mobile Menu */}
                {mobileOpen ? (
                    <div className="border-t border-white/20 bg-[#9D7877] px-4 py-4 md:hidden">
                        <nav className="space-y-2" aria-label="Seven mobile navigation">
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    onClick={() => setMobileOpen(false)}
                                    className={`block rounded-lg px-4 py-3 text-sm uppercase tracking-[0.16em] text-white transition-colors hover:opacity-80`}
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

export default SevenHeader;
