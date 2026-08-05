/**
 * Pearl Theme — Header (Phase T1.1)
 *
 * Layout identity: transparent over hero, centered brand, centered uppercase
 * navigation, search + cart icons, sticky with scroll elevation, graceful
 * mobile degradation.
 *
 * Runtime injects localized navigation and mandatory capability slots. Only
 * the visual shell (transparent-over-hero, centered, red accent, Jost
 * uppercase nav) is Pearl-specific.
 */

import React, { useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useScrollPosition } from '@/hooks/useScrollPosition';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';
import {
    PEARL_CONTAINER,
    PEARL_FONT_BODY,
    PEARL_NAV_LINK,
    pearlNavLinkClass,
} from './pearlTokens';
import AnnouncementMarquee from './AnnouncementMarquee';

const PearlHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    return (
        <>
            <AnnouncementMarquee text={labels.announcement} />

            <header
                role="banner"
                className={`sticky top-0 z-30 w-full transition-colors duration-300 ${
                    isScrolled
                        ? 'bg-[#141218]/95 shadow-[0_18px_40px_rgba(0,0,0,0.22)]'
                        : 'bg-transparent'
                } text-white`}
            >
                <div className={`${PEARL_CONTAINER} py-3 sm:py-4`}>
                    {/* Centered brand row with side actions */}
                    <div className="flex items-center justify-between gap-4">
                        <div className="hidden flex-1 items-center gap-2 md:flex">
                            <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-white/70 hover:bg-white/10 hover:text-white" />
                            <WishlistSlot className="text-white/70 hover:bg-white/10 hover:text-white" />
                            <CartSlot className="text-white/70 hover:bg-white/10 hover:text-white" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className={`min-w-0 flex-1 truncate text-center text-2xl font-light uppercase tracking-[0.18em] text-white md:text-3xl lg:tracking-[0.28em] ${PEARL_FONT_BODY}`}
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                            <LanguageSlot variant="text" size="sm" className="text-white/60 hover:text-white" />
                            <div className="border-l border-white/15 pl-3">
                                <AuthenticationSlot variant="desktop" className="!text-white/80" />
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-white/15 text-white/80 md:hidden"
                            aria-label={labels.menu}
                            aria-expanded={mobileOpen}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>

                    {/* Centered primary navigation */}
                    <NavigationSlot
                        className="mt-4 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-white/10 pt-4 md:flex"
                        ariaLabel={labels.mainNavigation}
                        linkClassName={(link) => pearlNavLinkClass(link.isActive)}
                    />
                </div>

                {/* Mobile drawer */}
                {mobileOpen ? (
                    <div className="border-t border-white/10 bg-[#0c0a0e] px-4 py-4 md:hidden">
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-white/70 hover:bg-white/10 hover:text-white" />
                                <WishlistSlot className="text-white/70 hover:bg-white/10 hover:text-white" />
                                <CartSlot className="text-white/70 hover:bg-white/10 hover:text-white" />
                            </div>
                            <LanguageSlot variant="text" size="sm" className="text-white/60 hover:text-white" />
                        </div>
                        <div className="space-y-2">
                            <NavigationSlot
                                className="space-y-2"
                                ariaLabel={labels.menu}
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName={(link) => `block rounded-2xl px-4 py-3 ${PEARL_NAV_LINK} ${
                                    link.isActive ? 'bg-white text-[#141218]' : 'bg-white/5 text-white/70'
                                }`}
                            />
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

export default PearlHeader;
