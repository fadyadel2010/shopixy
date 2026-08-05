import React, { useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const SevenHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);

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
                        <NavigationSlot
                            className="hidden items-center gap-x-6 md:flex lg:gap-x-8"
                            ariaLabel="Seven navigation"
                            linkClassName="text-xs font-medium uppercase tracking-[0.16em] text-white transition-colors hover:opacity-80 lg:text-sm"
                        />

                        {/* Right Icons */}
                        <div className="flex items-center gap-2 lg:gap-3">
                            <SearchSlot className="inline-flex h-9 w-9 items-center justify-center rounded-full text-white hover:bg-white/10 hover:text-white" />
                            <LanguageSlot variant="text" size="sm" className="text-white hover:text-white/80" />
                            <div className="hidden md:block">
                                <AuthenticationSlot variant="desktop" />
                            </div>
                            <WishlistSlot className="text-white hover:bg-white/10 hover:text-white" />
                            <CartSlot className="text-white hover:bg-white/10 hover:text-white" />
                            <button
                                type="button"
                                onClick={() => setMobileOpen((value) => !value)}
                                className="flex h-9 w-9 items-center justify-center rounded-full text-white md:hidden"
                                aria-label={labels.menu}
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
                        <div className="space-y-2">
                            <SearchSlot onClick={() => setMobileOpen(false)} className="block w-full rounded-lg px-4 py-3 text-left text-sm uppercase tracking-[0.16em] text-white transition-colors hover:opacity-80">
                                {labels.search}
                            </SearchSlot>
                            <NavigationSlot
                                className="space-y-2"
                                ariaLabel="Seven mobile navigation"
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName="block rounded-lg px-4 py-3 text-sm uppercase tracking-[0.16em] text-white transition-colors hover:opacity-80"
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

export default SevenHeader;
