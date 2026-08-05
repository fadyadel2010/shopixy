/**
 * Alizon Theme — Header (Phase A1.1)
 *
 * Layout identity: white sticky bar, red brand wordmark (left), inline
 * red navigation (center/right), search + wishlist + cart icon cluster
 * (right), graceful mobile degradation.
 *
 * This differs from Hype (which uses orange #F06736). Alizon uses
 * red #FF0000 for nav, matching the ALIZON reference site.
 *
 * Runtime injects localized navigation and mandatory capability slots.
 */

import React, { useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useScrollPosition } from '@/hooks/useScrollPosition';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';
import {
    ALIZON_CONTAINER,
    ALIZON_FONT_BODY,
    ALIZON_NAV_LINK,
    alizonNavLinkClass,
} from './alizonTokens';
import AnnouncementMarquee from './AnnouncementMarquee';

const AlizonHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
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
                        ? 'bg-white shadow-[0_4px_20px_rgba(0,0,0,0.08)]'
                        : 'bg-white'
                } ${ALIZON_FONT_BODY} text-[#FF0000]`}
            >
                <div className={`${ALIZON_CONTAINER} py-3 sm:py-4`}>
                    {/* Brand row with side actions */}
                    <div className="flex items-center justify-between gap-4">
                        <div className="hidden flex-1 items-center gap-2 md:flex">
                            <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-[#FF0000]/70 hover:bg-[#FF0000]/10 hover:text-[#FF0000]" />
                            <WishlistSlot className="text-[#FF0000]/70 hover:bg-[#FF0000]/10 hover:text-[#FF0000]" />
                            <CartSlot className="text-[#FF0000]/70 hover:bg-[#FF0000]/10 hover:text-[#FF0000]" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className={`min-w-0 flex-1 truncate text-center text-2xl font-medium uppercase tracking-[0.16em] text-[#FF0000] md:text-3xl lg:tracking-[0.22em] ${ALIZON_FONT_BODY}`}
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                            <LanguageSlot variant="text" size="sm" className="text-[#FF0000]/60 hover:text-[#FF0000]" />
                            <div className="border-l border-[#FF0000]/15 pl-3">
                                <AuthenticationSlot variant="desktop" className="!text-[#FF0000]/80" />
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-[#FF0000]/20 text-[#FF0000] md:hidden"
                            aria-label={labels.menu}
                            aria-expanded={mobileOpen}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>

                    {/* Primary navigation */}
                    <NavigationSlot
                        className="mt-4 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-[#FF0000]/10 pt-3 md:flex"
                        ariaLabel={labels.mainNavigation}
                        linkClassName={(link) => alizonNavLinkClass(link.isActive)}
                    />
                </div>

                {/* Mobile drawer */}
                {mobileOpen ? (
                    <div className="border-t border-[#FF0000]/10 bg-white px-4 py-4 md:hidden">
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-[#FF0000]/70 hover:bg-[#FF0000]/10 hover:text-[#FF0000]" />
                                <WishlistSlot className="text-[#FF0000]/70 hover:bg-[#FF0000]/10 hover:text-[#FF0000]" />
                                <CartSlot className="text-[#FF0000]/70 hover:bg-[#FF0000]/10 hover:text-[#FF0000]" />
                            </div>
                            <LanguageSlot variant="text" size="sm" className="text-[#FF0000]/60 hover:text-[#FF0000]" />
                        </div>
                        <div className="space-y-2">
                            <NavigationSlot
                                className="space-y-2"
                                ariaLabel={labels.menu}
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName={(link) => `block rounded-md px-4 py-3 ${ALIZON_NAV_LINK} ${
                                    link.isActive ? 'bg-[#FF0000] text-white' : 'bg-[#FF0000]/5 text-[#FF0000]'
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

export default AlizonHeader;
