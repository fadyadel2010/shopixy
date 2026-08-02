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
import { COSMETICS_FONT, cosmeticsNavLinkClass } from './cosmeticsTokens';

const CosmeticsHeader: React.FC = () => {
    const { t, language } = useTranslation();
    const location = useLocation();
    const storeName = useStoreConfigStore(selectStoreName);
    const loadNavigation = useNavigationStore((state) => state.loadNavigation);
    const headerLinks = useNavigationStore((state) => state.navigation?.header.links ?? []);
    const { isScrolled } = useScrollPosition({ threshold: 12 });
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

    const isHome = location.pathname === '/';
    const transparent = isHome && !isScrolled;
    const displayStoreName = storeName?.trim() ?? '';
    const searchLabel = t('common.search');

    const isActive = (path: string) => {
        if (path === '/') {
            return location.pathname === '/';
        }
        return location.pathname.startsWith(path);
    };

    return (
        <>
            <div className={`${COSMETICS_FONT} relative z-40 flex h-12 items-center justify-center bg-[#E7DFDB] px-10 text-center text-xs font-medium uppercase tracking-[0.12em] text-[#D15F61]`}>
                <button className="absolute left-4 top-1/2 -translate-y-1/2 text-[#D15F61]/70" type="button" aria-label={t('common.previous')}>
                    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                        <path d="m15 18-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} />
                    </svg>
                </button>
                <span>{t('announcement.defaultText')}</span>
                <button className="absolute right-4 top-1/2 -translate-y-1/2 text-[#D15F61]/70" type="button" aria-label={t('common.next')}>
                    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                        <path d="m9 18 6-6-6-6" strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} />
                    </svg>
                </button>
            </div>

            <header
                role="banner"
                className={`${COSMETICS_FONT} sticky top-0 z-40 h-[52px] border-b transition-all duration-300 md:h-[58px] ${
                    transparent
                        ? 'border-transparent bg-white/[0.05] text-white'
                        : 'border-black/10 bg-white text-[#D05D61] shadow-[0_4px_20px_rgba(13,13,18,0.06)]'
                }`}
            >
                <div className="grid h-full grid-cols-[1fr_auto_1fr] items-center px-4 md:px-10">
                    <div className="flex items-center gap-3">
                        <button
                            type="button"
                            className="inline-flex h-10 w-10 items-center justify-center md:hidden"
                            onClick={() => setMobileOpen((value) => !value)}
                            aria-label={t('header.menu')}
                            aria-expanded={mobileOpen}
                        >
                            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                                <path strokeLinecap="round" strokeWidth={1.5} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>

                        <AffiliateLink href="/search" className="inline-flex h-10 w-10 items-center justify-center" aria-label={searchLabel}>
                            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                                <circle cx="11" cy="11" r="7" strokeWidth={1.5} />
                                <path d="m16.5 16.5 4 4" strokeLinecap="round" strokeWidth={1.5} />
                            </svg>
                        </AffiliateLink>

                        {links.length > 0 ? (
                            <nav className="hidden items-center gap-5 lg:flex" aria-label={t('accessibility.mainNavigation')}>
                                {links.slice(0, 5).map((link) => (
                                    <AffiliateLink
                                        key={link.key}
                                        href={link.url}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                        className={transparent ? cosmeticsNavLinkClass(isActive(link.url)) : cosmeticsNavLinkClass(isActive(link.url)).replace('text-white/90', 'text-[#D05D61]')}
                                    >
                                        {link.label}
                                    </AffiliateLink>
                                ))}
                            </nav>
                        ) : null}
                    </div>

                    <AffiliateLink href="/" className="max-w-[12rem] truncate text-center text-2xl font-light tracking-[-0.03em] md:max-w-[16rem] md:text-3xl">
                        {displayStoreName}
                    </AffiliateLink>

                    <div className="flex items-center justify-end gap-2">
                        <div className="hidden md:block">
                            <LanguageSwitcher variant="text" size="sm" className={transparent ? 'text-white/80' : 'text-[#D05D61]'} />
                        </div>
                        <div className="hidden md:block">
                            <WishlistIconButton className={transparent ? 'text-white hover:bg-white/10' : 'text-[#D05D61] hover:bg-[#E7DFDB]/60'} />
                        </div>
                        <CartIconButton className={transparent ? 'text-white hover:bg-white/10' : 'text-[#D05D61] hover:bg-[#E7DFDB]/60'} />
                    </div>
                </div>

                {mobileOpen ? (
                    <div className="absolute inset-x-0 top-full border-t border-[#D05D61]/15 bg-[#E7DFDB] px-5 py-5 text-[#D05D61] shadow-[0_18px_40px_rgba(13,13,18,0.10)] lg:hidden">
                        <div className="mb-5 flex items-center justify-between">
                            <AffiliateLink href="/" className="text-2xl font-light" onClick={() => setMobileOpen(false)}>
                                {displayStoreName}
                            </AffiliateLink>
                            <button type="button" onClick={() => setMobileOpen(false)} className="inline-flex h-10 w-10 items-center justify-center" aria-label={t('header.closeMenu')}>
                                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                                    <path d="m6 6 12 12M18 6 6 18" strokeLinecap="round" strokeWidth={1.8} />
                                </svg>
                            </button>
                        </div>
                        <nav className="space-y-1" aria-label={t('header.menu')}>
                            {links.map((link) => (
                                <AffiliateLink
                                    key={link.key}
                                    href={link.url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    onClick={() => setMobileOpen(false)}
                                    className="block border-b border-[#D05D61]/15 py-3 text-sm uppercase tracking-[0.14em]"
                                >
                                    {link.label}
                                </AffiliateLink>
                            ))}
                            <AffiliateLink href="/search" onClick={() => setMobileOpen(false)} className="block border-b border-[#D05D61]/15 py-3 text-sm uppercase tracking-[0.14em]">
                                {searchLabel}
                            </AffiliateLink>
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

export default CosmeticsHeader;
