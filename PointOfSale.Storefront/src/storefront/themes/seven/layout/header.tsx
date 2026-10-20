import React, { useState } from 'react';
import { HeaderAnnouncement } from '@/storefront/presentation/HeaderAnnouncement';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const SevenHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);

    return (
        <>
            <HeaderAnnouncement model={model} className="bg-primary text-on-primary" />
            <header className="sticky top-0 z-30 bg-primary text-on-primary shadow-md">
                <div className="mx-auto max-w-7xl px-4 py-3 sm:py-4">
                    <div className="flex items-center justify-between">
                        {/* Logo */}
                        <AffiliateLink
                            href="/"
                            className="min-w-0 flex-1 truncate text-center text-2xl font-medium tracking-[-0.02em] text-on-primary md:text-left md:text-3xl"
                        >
                            {storeName}
                        </AffiliateLink>

                        {/* Desktop Nav */}
                        <NavigationSlot
                            className="hidden items-center gap-x-6 md:flex lg:gap-x-8"
                            ariaLabel="Seven navigation"
                            linkClassName="text-xs font-medium uppercase tracking-[0.16em] text-on-primary hover:underline lg:text-sm"
                        />

                        {/* Right Icons */}
                        <div className="flex items-center gap-2 lg:gap-3">
                            <SearchSlot className="inline-flex h-9 w-9 items-center justify-center rounded-full text-on-primary hover:bg-on-primary/10" />
                            <div className="hidden md:block"><LanguageSlot variant="text" size="sm" className="text-on-primary" /></div>
                            <div className="hidden md:block">
                                <AuthenticationSlot variant="desktop" />
                            </div>
                            <div className="hidden md:block"><WishlistSlot className="text-on-primary hover:bg-on-primary/10" /></div>
                            <CartSlot className="text-on-primary hover:bg-on-primary/10" />
                            <button
                                type="button"
                                onClick={() => setMobileOpen((value) => !value)}
                                className="flex h-9 w-9 items-center justify-center rounded-full text-on-primary md:hidden"
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
                    <div className="border-t border-current bg-primary px-4 py-4 md:hidden">
                        <div className="space-y-2">
                            <div className="flex items-center gap-3">
                                <LanguageSlot variant="text" size="sm" className="text-on-primary" />
                                <WishlistSlot className="text-on-primary hover:bg-on-primary/10" />
                            </div>
                            <SearchSlot onClick={() => setMobileOpen(false)} className="block w-full rounded-lg px-4 py-3 text-left text-sm uppercase tracking-[0.16em] text-on-primary hover:underline">
                                {labels.search}
                            </SearchSlot>
                            <NavigationSlot
                                className="space-y-2"
                                ariaLabel="Seven mobile navigation"
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName="block rounded-lg px-4 py-3 text-sm uppercase tracking-[0.16em] text-on-primary hover:underline"
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
