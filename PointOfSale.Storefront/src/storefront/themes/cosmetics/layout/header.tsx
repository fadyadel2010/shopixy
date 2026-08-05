import React, { useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useScrollPosition } from '@/hooks/useScrollPosition';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';
import { COSMETICS_FONT, cosmeticsNavLinkClass } from './cosmeticsTokens';

const CosmeticsHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { isHome, labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const { isScrolled } = useScrollPosition({ threshold: 12 });
    const [mobileOpen, setMobileOpen] = useState(false);
    const transparent = isHome && !isScrolled;
    const displayStoreName = storeName?.trim() ?? '';

    return (
        <>
            <div className={`${COSMETICS_FONT} relative z-40 flex h-12 items-center justify-center bg-[#E7DFDB] px-10 text-center text-xs font-medium uppercase tracking-[0.12em] text-[#D15F61]`}>
                <button className="absolute left-4 top-1/2 -translate-y-1/2 text-[#D15F61]/70" type="button" aria-label={labels.previous}>
                    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                        <path d="m15 18-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} />
                    </svg>
                </button>
                <span>{labels.announcement}</span>
                <button className="absolute right-4 top-1/2 -translate-y-1/2 text-[#D15F61]/70" type="button" aria-label={labels.next}>
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
                            aria-label={labels.menu}
                            aria-expanded={mobileOpen}
                        >
                            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                                <path strokeLinecap="round" strokeWidth={1.5} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>

                        <SearchSlot className="inline-flex h-10 w-10 items-center justify-center" aria-label={labels.search}>
                            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                                <circle cx="11" cy="11" r="7" strokeWidth={1.5} />
                                <path d="m16.5 16.5 4 4" strokeLinecap="round" strokeWidth={1.5} />
                            </svg>
                        </SearchSlot>

                        <NavigationSlot
                            className="hidden items-center gap-5 lg:flex"
                            ariaLabel={labels.mainNavigation}
                            linkClassName={(link) => transparent
                                ? cosmeticsNavLinkClass(link.isActive)
                                : cosmeticsNavLinkClass(link.isActive).replace('text-white/90', 'text-[#D05D61]')}
                        />
                    </div>

                    <AffiliateLink href="/" className="max-w-[12rem] truncate text-center text-2xl font-light tracking-[-0.03em] md:max-w-[16rem] md:text-3xl">
                        {displayStoreName}
                    </AffiliateLink>

                    <div className="flex items-center justify-end gap-2">
                        <div className="hidden md:block">
                            <LanguageSlot variant="text" size="sm" className={transparent ? 'text-white/80' : 'text-[#D05D61]'} />
                        </div>
                        <div className="hidden lg:block">
                            <AuthenticationSlot variant="desktop" />
                        </div>
                        <div className="hidden md:block">
                            <WishlistSlot className={transparent ? 'text-white hover:bg-white/10' : 'text-[#D05D61] hover:bg-[#E7DFDB]/60'} />
                        </div>
                        <CartSlot className={transparent ? 'text-white hover:bg-white/10' : 'text-[#D05D61] hover:bg-[#E7DFDB]/60'} />
                    </div>
                </div>

                {mobileOpen ? (
                    <div className="absolute inset-x-0 top-full border-t border-[#D05D61]/15 bg-[#E7DFDB] px-5 py-5 text-[#D05D61] shadow-[0_18px_40px_rgba(13,13,18,0.10)] lg:hidden">
                        <div className="mb-5 flex items-center justify-between">
                            <AffiliateLink href="/" className="text-2xl font-light" onClick={() => setMobileOpen(false)}>
                                {displayStoreName}
                            </AffiliateLink>
                            <button type="button" onClick={() => setMobileOpen(false)} className="inline-flex h-10 w-10 items-center justify-center" aria-label={labels.closeMenu}>
                                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" aria-hidden="true">
                                    <path d="m6 6 12 12M18 6 6 18" strokeLinecap="round" strokeWidth={1.8} />
                                </svg>
                            </button>
                        </div>
                        <div className="mb-3 flex items-center gap-2">
                            <LanguageSlot variant="text" size="sm" className="text-[#D05D61]" />
                            <WishlistSlot className="text-[#D05D61] hover:bg-white/30" />
                        </div>
                        <div className="space-y-1">
                            <NavigationSlot
                                className="space-y-1"
                                ariaLabel={labels.menu}
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName="block border-b border-[#D05D61]/15 py-3 text-sm uppercase tracking-[0.14em]"
                            />
                            <SearchSlot onClick={() => setMobileOpen(false)} className="block w-full border-b border-[#D05D61]/15 py-3 text-left text-sm uppercase tracking-[0.14em]">
                                {labels.search}
                            </SearchSlot>
                            <div className="pt-3">
                                <AuthenticationSlot variant="mobile" onLinkClick={() => setMobileOpen(false)} />
                            </div>
                        </div>
                    </div>
                ) : null}
            </header>
        </>
    );
};

export default CosmeticsHeader;
