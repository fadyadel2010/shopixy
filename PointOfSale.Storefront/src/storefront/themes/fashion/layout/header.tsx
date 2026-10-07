import React, { useState } from 'react';
import { HeaderAnnouncement } from '@/storefront/presentation/HeaderAnnouncement';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const FashionHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);

    return (
        <>
            <HeaderAnnouncement model={model} className="bg-[#161311] text-white" />
            <header className="sticky top-0 z-30 border-b border-white/10 bg-[#161311] text-white shadow-[0_18px_40px_rgba(0,0,0,0.22)]">
                <div className="mx-auto max-w-7xl px-4 py-3 sm:py-4">
                    <div className="flex items-center justify-end text-[11px] uppercase tracking-[0.32em] text-white/60">
                        <div className="hidden items-center gap-2 md:flex">
                            <LanguageSlot variant="text" size="sm" className="text-white/60 hover:text-white" />
                            <div className="border-l border-white/10 pl-3">
                                <AuthenticationSlot variant="desktop" className="!text-white/80" />
                            </div>
                        </div>
                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-white/15 md:hidden"
                            aria-label={labels.menu}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>

                    <div className="mt-5 flex items-center justify-between gap-4">
                        <div className="hidden md:flex md:flex-1">
                            <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-white/70 hover:bg-white/10 hover:text-white" />
                            <WishlistSlot className="text-white/70 hover:bg-white/10 hover:text-white" />
                            <CartSlot className="text-white/70 hover:bg-white/10 hover:text-white" />
                        </div>

                        <AffiliateLink
                            href="/"
                            className="min-w-0 flex-1 truncate text-center text-2xl font-semibold uppercase tracking-[0.18em] text-white md:text-3xl lg:tracking-[0.28em]"
                        >
                            {storeName}
                        </AffiliateLink>

                        <div className="hidden md:block md:flex-1" aria-hidden="true" />
                    </div>

                    <NavigationSlot
                        className="mt-5 hidden flex-wrap items-center justify-center gap-x-6 gap-y-2 border-t border-white/10 pt-4 md:flex"
                        ariaLabel="Fashion navigation"
                        linkClassName={(link) => `text-xs uppercase tracking-[0.18em] transition-colors lg:text-sm lg:tracking-[0.22em] ${
                            link.isActive ? 'text-white' : 'text-white/60 hover:text-white'
                        }`}
                    />
                </div>

                {mobileOpen ? (
                    <div className="border-t border-white/10 bg-[#120f0d] px-4 py-4 md:hidden">
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
                                ariaLabel="Fashion mobile navigation"
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName={(link) => `block rounded-2xl px-4 py-3 text-sm uppercase tracking-[0.24em] ${
                                    link.isActive ? 'bg-white text-[#161311]' : 'bg-white/5 text-white/70'
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

export default FashionHeader;
