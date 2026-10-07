import React, { useState } from 'react';
import { HeaderAnnouncement } from '@/storefront/presentation/HeaderAnnouncement';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import type { HeaderPresentationProps } from '@/storefront/presentation/models';

const MinimalHeader: React.FC<HeaderPresentationProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileOpen, setMobileOpen] = useState(false);

    return (
        <>
            <HeaderAnnouncement model={model} className="bg-slate-900 text-white" />
            <header className="sticky top-0 z-30 border-b border-black/10 bg-white/95 backdrop-blur">
                <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-3 sm:py-4">
                    <AffiliateLink href="/" className="min-w-0 max-w-[44vw] truncate text-lg font-semibold uppercase tracking-[0.08em] text-slate-900 sm:text-xl md:max-w-xs">
                        {storeName}
                    </AffiliateLink>

                    <NavigationSlot
                        className="hidden min-w-0 flex-1 flex-wrap items-center justify-center gap-x-5 gap-y-2 md:flex"
                        ariaLabel="Minimal navigation"
                        linkClassName={(link) => `text-xs uppercase tracking-[0.14em] transition-colors lg:text-sm lg:tracking-[0.18em] ${
                            link.isActive ? 'text-slate-900' : 'text-slate-500 hover:text-slate-900'
                        }`}
                    />

                    <div className="flex shrink-0 items-center gap-1">
                        <SearchSlot className="inline-flex h-10 w-10 items-center justify-center rounded-full text-slate-500 hover:bg-slate-100 hover:text-slate-900" />
                        <LanguageSlot variant="text" size="sm" className="hidden text-slate-500 hover:text-slate-900 md:flex" />
                        <div className="hidden rounded-full bg-slate-900 px-1 md:flex">
                            <AuthenticationSlot variant="desktop" />
                        </div>
                        <WishlistSlot className="text-slate-500 hover:bg-slate-100 hover:text-slate-900" />
                        <CartSlot className="text-slate-500 hover:bg-slate-100 hover:text-slate-900" />
                        <button
                            type="button"
                            onClick={() => setMobileOpen((value) => !value)}
                            className="flex h-10 w-10 items-center justify-center rounded-full border border-slate-200 text-slate-700 md:hidden"
                            aria-label={labels.menu}
                        >
                            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 7h16M4 12h16M4 17h16" />
                            </svg>
                        </button>
                    </div>
                </div>

                {mobileOpen ? (
                    <div className="border-t border-slate-200 bg-white md:hidden">
                        <nav className="space-y-1 px-4 py-4" aria-label="Minimal mobile navigation">
                            <SearchSlot onClick={() => setMobileOpen(false)} className="block w-full rounded-xl px-3 py-3 text-left text-sm uppercase tracking-[0.18em] text-slate-600">
                                {labels.search}
                            </SearchSlot>
                            <LanguageSlot variant="text" size="sm" className="text-slate-600 hover:text-slate-900" />
                            <NavigationSlot
                                className="space-y-1"
                                ariaLabel="Minimal mobile navigation"
                                onLinkClick={() => setMobileOpen(false)}
                                linkClassName={(link) => `block rounded-xl px-3 py-3 text-sm uppercase tracking-[0.18em] ${
                                    link.isActive ? 'bg-slate-100 text-slate-900' : 'text-slate-600'
                                }`}
                            />
                            <div className="pt-3">
                                <AuthenticationSlot variant="mobile" onLinkClick={() => setMobileOpen(false)} />
                            </div>
                        </nav>
                    </div>
                ) : null}
            </header>
        </>
    );
};

export default MinimalHeader;
