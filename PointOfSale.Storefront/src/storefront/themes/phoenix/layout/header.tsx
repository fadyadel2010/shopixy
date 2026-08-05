/**
 * Phoenix Sports Theme — Header (Phase T1.1)
 *
 * Layout identity: floating white pill header centered on the warm off-white
 * canvas, black ink, centered brand wordmark (Playfair Display), centered
 * uppercase Jost navigation, search + cart icons, sticky with scroll elevation,
 * graceful mobile degradation (slide-in drawer).
 *
 * Runtime injects localized navigation and mandatory capability slots. Only
 * the visual shell (floating white pill, black ink, Playfair wordmark, Jost
 * uppercase nav) is Phoenix-specific.
 */

import React, { useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useScrollPosition } from '@/hooks/useScrollPosition';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';
import {
    PHOENIX_CONTAINER,
    PHOENIX_FONT_DISPLAY,
    PHOENIX_NAV_LINK,
    PHOENIX_HEADER_SHELL,
    phoenixNavLinkClass,
} from './phoenixTokens';
import AnnouncementMarquee from './AnnouncementMarquee';

const PhoenixHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    return (
        <>
            <AnnouncementMarquee text={labels.announcement} />

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
                                <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-black/70 hover:bg-black/5 hover:text-black" />
                                <WishlistSlot className="text-black/70 hover:bg-black/5 hover:text-black" />
                                <CartSlot className="text-black/70 hover:bg-black/5 hover:text-black" />
                            </div>

                            <AffiliateLink
                                href="/"
                                className={`min-w-0 flex-1 truncate text-center text-2xl font-medium uppercase tracking-[0.12em] text-black md:text-3xl ${PHOENIX_FONT_DISPLAY}`}
                            >
                                {storeName}
                            </AffiliateLink>

                            <div className="hidden flex-1 items-center justify-end gap-3 md:flex">
                                <LanguageSlot variant="text" size="sm" className="text-black/60 hover:text-black" />
                                <div className="border-l border-black/10 pl-3">
                                    <AuthenticationSlot variant="desktop" className="!text-black/80" />
                                </div>
                            </div>

                            <button
                                type="button"
                                onClick={() => setMobileOpen((value) => !value)}
                                className="flex h-10 w-10 items-center justify-center rounded-full border border-black/10 text-black/80 md:hidden"
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
                            className="mt-2 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-black/5 pt-3 md:flex"
                            ariaLabel={labels.mainNavigation}
                            linkClassName={(link) => phoenixNavLinkClass(link.isActive)}
                        />
                    </div>

                    {/* Mobile drawer */}
                    {mobileOpen ? (
                        <div className="border-t border-black/5 bg-white px-4 py-4 md:hidden">
                            <div className="mb-4 flex items-center justify-between">
                                <div className="flex items-center gap-2">
                                    <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-black/70 hover:bg-black/5 hover:text-black" />
                                    <WishlistSlot className="text-black/70 hover:bg-black/5 hover:text-black" />
                                    <CartSlot className="text-black/70 hover:bg-black/5 hover:text-black" />
                                </div>
                                <LanguageSlot variant="text" size="sm" className="text-black/60 hover:text-black" />
                            </div>
                            <div className="space-y-2">
                                <NavigationSlot
                                    className="space-y-2"
                                    ariaLabel={labels.menu}
                                    onLinkClick={() => setMobileOpen(false)}
                                    linkClassName={(link) => `block rounded-xl px-4 py-3 ${PHOENIX_NAV_LINK} ${
                                        link.isActive ? 'bg-black text-white' : 'bg-black/5 text-black/70'
                                    }`}
                                />
                                <div className="pt-3">
                                    <AuthenticationSlot variant="mobile" onLinkClick={() => setMobileOpen(false)} />
                                </div>
                            </div>
                        </div>
                    ) : null}
                </header>
            </div>
        </>
    );
};

export default PhoenixHeader;
