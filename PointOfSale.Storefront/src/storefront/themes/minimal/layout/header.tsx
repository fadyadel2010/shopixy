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

const MinimalHeader: React.FC = () => {
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
            <header className="sticky top-0 z-30 border-b border-black/10 bg-white/95 backdrop-blur">
                <div className="mx-auto flex max-w-7xl items-center justify-between gap-6 px-4 py-4">
                    <AffiliateLink href="/" className="text-xl font-semibold tracking-[0.12em] text-slate-900 uppercase">
                        {storeName}
                    </AffiliateLink>

                    <nav className="hidden items-center gap-6 md:flex" aria-label="Minimal navigation">
                        {links.map((link) => (
                            <AffiliateLink
                                key={link.key}
                                href={link.url}
                                target={link.openInNewTab ? '_blank' : undefined}
                                rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                className={`text-sm uppercase tracking-[0.22em] transition-colors ${
                                    isActive(link.url) ? 'text-slate-900' : 'text-slate-500 hover:text-slate-900'
                                }`}
                            >
                                {link.label}
                            </AffiliateLink>
                        ))}
                    </nav>

                    <div className="flex items-center gap-1">
                        <LanguageSwitcher variant="text" size="sm" className="hidden text-slate-500 hover:text-slate-900 md:flex" />
                        <div className="hidden rounded-full bg-slate-900 px-1 md:flex">
                            <AuthNavItems variant="desktop" />
                        </div>
                        <WishlistIconButton className="text-slate-500 hover:bg-slate-100 hover:text-slate-900" />
                        <CartIconButton className="text-slate-500 hover:bg-slate-100 hover:text-slate-900" />
                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-slate-200 text-slate-700 md:hidden"
                            aria-label={t('header.menu')}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>
                </div>

                {mobileOpen ? (
                    <div className="border-t border-slate-200 bg-white md:hidden">
                        <nav className="space-y-1 px-4 py-4" aria-label="Minimal mobile navigation">
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    onClick={() => setMobileOpen(false)}
                                    className={`block rounded-xl px-3 py-3 text-sm uppercase tracking-[0.18em] ${
                                        isActive(link.url) ? 'bg-slate-100 text-slate-900' : 'text-slate-600'
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

export default MinimalHeader;
