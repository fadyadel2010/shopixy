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
            <AnnouncementMarquee {...model.announcement} text={labels.announcement} />

            <header
                role="banner"
                className={`sticky top-0 z-30 w-full transition-colors duration-300 ${
                    isScrolled
                        ? 'bg-background shadow-[0_4px_20px_rgba(0,0,0,0.08)]'
                        : 'bg-background'
                } ${ALIZON_FONT_BODY} text-primary-text`}
            >
                <div className={`${ALIZON_CONTAINER} py-3 sm:py-4`}>
                    {/* Brand row with side actions */}
                    <div className="flex items-center justify-between gap-4">
                        <div className="hidden flex-1 items-center gap-2 md:flex">
                            <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-primary-text hover:bg-primary/10" />
                            <WishlistSlot className="text-primary-text hover:bg-primary/10" />
                            <CartSlot className="text-primary-text hover:bg-primary/10" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className={`min-w-0 flex-1 truncate text-center text-2xl font-medium uppercase tracking-[0.16em] text-primary-text md:text-3xl lg:tracking-[0.22em] ${ALIZON_FONT_BODY}`}
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                            <LanguageSlot variant="text" size="sm" className="text-primary-text" />
                            <div className="border-l border-border pl-3">
                                <AuthenticationSlot variant="desktop" className="text-primary-text" />
                            </div>
                        </div>

                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-border text-primary-text md:hidden"
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
                        className="mt-4 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-border pt-3 md:flex"
                        ariaLabel={labels.mainNavigation}
                        linkClassName={(link) => alizonNavLinkClass(link.isActive)}
                    />
                </div>

                {/* Mobile drawer */}
                {mobileOpen ? (
                    <div className="border-t border-border bg-background px-4 py-4 md:hidden">
                        <div className="mb-4 flex items-center justify-between">
                            <div className="flex items-center gap-2">
                                <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-primary-text hover:bg-primary/10" />
                                <WishlistSlot className="text-primary-text hover:bg-primary/10" />
                                <CartSlot className="text-primary-text hover:bg-primary/10" />
                            </div>
                            <LanguageSlot variant="text" size="sm" className="text-primary-text" />
                        </div>
                        <div className="space-y-2">
                            <NavigationSlot
                                className="space-y-2"
                                ariaLabel={labels.menu}
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName={(link) => `block rounded-md px-4 py-3 ${ALIZON_NAV_LINK} ${
                                    link.isActive ? 'bg-primary text-on-primary' : 'bg-primary/5 text-primary-text'
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
