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
 * Runtime injects localized navigation and mandatory capability slots.
 */

import React, { useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useScrollPosition } from '@/hooks/useScrollPosition';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';
import {
    HYPE_CONTAINER,
    HYPE_FONT_BODY,
    HYPE_NAV_LINK,
    hypeNavLinkClass,
} from './hypeTokens';
import AnnouncementMarquee from './AnnouncementMarquee';

const HypeHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    return (
        <>
            <AnnouncementMarquee {...model.announcement} text={labels.announcement} />

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
                            <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                            <WishlistSlot className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                            <CartSlot className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className={`min-w-0 flex-1 truncate text-center text-2xl font-medium uppercase tracking-[0.16em] text-[#F06736] md:text-3xl lg:tracking-[0.22em] ${HYPE_FONT_BODY}`}
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                            <LanguageSlot variant="text" size="sm" className="text-[#F06736]/60 hover:text-[#F06736]" />
                            <div className="border-l border-[#F06736]/15 pl-3">
                                <AuthenticationSlot variant="desktop" className="!text-[#F06736]/80" />
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-[#F06736]/20 text-[#F06736] md:hidden"
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
                        className="mt-4 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-[#F06736]/10 pt-3 md:flex"
                        ariaLabel={labels.mainNavigation}
                        linkClassName={(link) => hypeNavLinkClass(link.isActive)}
                    />
                </div>

                {/* Mobile drawer */}
                {mobileOpen ? (
                    <div className="border-t border-[#F06736]/10 bg-white px-4 py-4 md:hidden">
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                                <WishlistSlot className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                                <CartSlot className="text-[#F06736]/70 hover:bg-[#F06736]/10 hover:text-[#F06736]" />
                            </div>
                            <LanguageSlot variant="text" size="sm" className="text-[#F06736]/60 hover:text-[#F06736]" />
                        </div>
                        <div className="space-y-2">
                            <NavigationSlot
                                className="space-y-2"
                                ariaLabel={labels.menu}
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName={(link) => `block rounded-md px-4 py-3 ${HYPE_NAV_LINK} ${
                                    link.isActive ? 'bg-[#F06736] text-white' : 'bg-[#F06736]/5 text-[#F06736]'
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

export default HypeHeader;
