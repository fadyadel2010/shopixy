/**
 * Header Component
 * 
 * Header layout variants: "standard" | "centered" | "minimal"
 * Uses component composition for variants - NOT conditionals scattered everywhere.
 * 
 * Phase 7.6.2: Enhanced with:
 * - Scroll-aware elevation (shadow after scroll)
 * - Cart badge with animation
 * - Improved mobile menu with focus trap & body scroll lock
 * - Keyboard shortcuts for search
 * - Active route highlighting
 * - Accessibility improvements
 * 
 * Phase 10.1: Multi-language support
 * Phase 10.3.6: Wishlist badge in header
 */

import React, { useState, useEffect, useRef } from 'react';
import { useScrollPosition, useBodyScrollLock, useFocusTrap } from '@/hooks/useScrollPosition';
import { AffiliateModeIndicator } from '@/components/common/AffiliateModeIndicator';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import type { HeaderPresentationProps, HeaderRuntimeSlots } from '@/storefront/presentation/models';

// ============================================================================
// Types
// ============================================================================

type HeaderProps = HeaderPresentationProps;

interface MobileMenuProps {
    isOpen: boolean;
    onClose: () => void;
    model: HeaderPresentationProps['model'];
    slots: HeaderRuntimeSlots;
}

// ============================================================================
// Mobile Menu Component
// ============================================================================

const MobileMenu: React.FC<MobileMenuProps> = ({ isOpen, onClose, model, slots }) => {
    const { labels } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const menuRef = useRef<HTMLDivElement>(null);

    // Body scroll lock when menu is open
    useBodyScrollLock(isOpen);

    // Focus trap when menu is open
    useFocusTrap(menuRef, isOpen);

    // Close on ESC key
    useEffect(() => {
        if (!isOpen) return;

        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                e.preventDefault();
                onClose();
            }
        };

        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    return (
        <>
            {/* Backdrop */}
            <div
                className="fixed inset-0 bg-secondary/50 backdrop-blur-sm z-40 md:hidden animate-fade-in"
                onClick={onClose}
                aria-hidden="true"
            />

            {/* Menu Panel */}
            <div
                ref={menuRef}
                className="fixed inset-y-0 right-0 w-72 max-w-[85vw] bg-background shadow-xl z-50 md:hidden animate-slide-in-right"
                role="dialog"
                aria-modal="true"
                aria-label="Navigation menu"
            >
                <div className="flex flex-col h-full safe-area-inset-bottom">
                    {/* Header */}
                    <div className="flex items-center justify-between p-4 border-b border-muted/20">
                        <div className="flex items-center gap-2">
                            <span className="font-semibold text-text">{labels.menu}</span>
                            {/* AF-12: Affiliate Mode Indicator - Mobile */}
                            <AffiliateModeIndicator />
                        </div>
                        <button
                            onClick={onClose}
                            className="p-2.5 min-w-[44px] min-h-[44px] flex items-center justify-center text-muted hover:text-text transition-colors rounded-lg hover:bg-muted/10 focus-visible:ring-2 focus-visible:ring-primary"
                            aria-label={labels.closeMenu}
                        >
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Search */}
                    <div className="p-4 border-b border-muted/10">
                            <SearchSlot
                                onClick={onClose}
                                className="flex w-full items-center gap-3 px-4 py-3 bg-muted/10 rounded-lg text-muted hover:text-text transition-colors"
                            >
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                                </svg>
                                <span>{labels.searchProducts}</span>
                            </SearchSlot>
                    </div>

                    {/* Navigation Links */}
                    <NavigationSlot
                        className="flex-1 overflow-y-auto py-4"
                        ariaLabel={labels.mainNavigation}
                        onLinkClick={onClose}
                        linkClassName={(link) => `flex items-center px-4 py-3.5 min-h-[44px] transition-colors ${link.isActive
                            ? 'bg-primary/10 text-primary border-r-2 border-primary'
                            : 'text-text hover:bg-primary/10 hover:text-primary'
                        }`}
                        renderLinkContent={(link) => (
                            <>
                                <span className="w-5 ltr:mr-3 rtl:ml-3 flex-shrink-0 text-center text-sm font-semibold">
                                    {link.index + 1}
                                </span>
                                {link.label}
                            </>
                        )}
                    />

                    {/* Auth Section */}
                    <div className="px-4 py-2 mt-2 border-t border-muted/10">
                        <span className="text-xs font-semibold text-muted uppercase tracking-wider">
                            {labels.account}
                        </span>
                    </div>
                    <AuthenticationSlot
                        variant="mobile"
                        onLinkClick={onClose}
                    />

                    <div className="flex items-center justify-between gap-2 border-t border-muted/10 px-4 py-3">
                        <LanguageSlot variant="text" size="sm" />
                        <WishlistSlot />
                    </div>

                    {/* Footer */}
                    <div className="p-4 border-t border-muted/20 mt-auto">
                        <CartSlot
                            className="flex items-center justify-center gap-2 w-full px-4 py-3.5 min-h-[44px] bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors font-medium focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                        />
                    </div>
                </div>
            </div>
        </>
    );
};

// ============================================================================
// Hamburger Menu Button
// ============================================================================

interface HamburgerButtonProps {
    onClick: () => void;
}

const HamburgerButton: React.FC<HamburgerButtonProps> = ({ onClick }) => (
    <button
        onClick={onClick}
        className="md:hidden p-2.5 min-w-[44px] min-h-[44px] flex items-center justify-center text-white/90 hover:text-white transition-colors rounded-lg hover:bg-white/10 focus-visible:ring-2 focus-visible:ring-white/50"
        aria-label="Open menu"
        aria-haspopup="dialog"
    >
        <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
        </svg>
    </button>
);

// ============================================================================
// Search Button with Keyboard Hint
// ============================================================================

interface SearchButtonProps {
    SearchSlot: HeaderRuntimeSlots['SearchSlot'];
    className?: string;
    onClick?: () => void;
}

const SearchButton: React.FC<SearchButtonProps> = ({ SearchSlot, className = '', onClick }) => {
    // Detect platform for shortcut hint
    const isMac = typeof navigator !== 'undefined' && /Mac|iPod|iPhone|iPad/.test(navigator.platform);
    const shortcutKey = isMac ? '⌘K' : 'Ctrl+K';

    return (
        <SearchSlot
            onClick={onClick}
            className={`p-2 rounded-lg transition-colors flex items-center gap-2 group ${className}`}
            aria-label="Search products"
        >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            {/* Keyboard shortcut hint - hidden on mobile */}
            <kbd className="hidden lg:inline-flex items-center px-1.5 py-0.5 text-xs text-white/50 bg-white/10 rounded font-mono group-hover:text-white/70 group-hover:bg-white/20 transition-colors">
                {shortcutKey}
            </kbd>
        </SearchSlot>
    );
};

// ============================================================================
// Standard Header Variant
// ============================================================================

const HeaderStandard: React.FC<HeaderProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    return (
        <>
            <header
                className={`
                    bg-secondary text-white sticky top-0 z-30
                    transition-shadow duration-300
                    ${isScrolled ? 'shadow-lg' : 'shadow-none'}
                `}
                role="banner"
            >
                <div className="container mx-auto px-4">
                    <div className="flex h-16 items-center justify-between gap-3 md:h-18">
                        {/* Logo / Store Name */}
                        <AffiliateLink
                            href="/"
                            className="min-w-0 max-w-[52vw] truncate rounded text-xl font-bold tracking-tight text-white transition-opacity hover:opacity-90 focus-visible:ring-2 focus-visible:ring-white/50 md:max-w-xs md:text-2xl"
                        >
                            {storeName}
                        </AffiliateLink>

                        {/* Desktop Navigation - Dynamic from CMS */}
                        <NavigationSlot
                            className="hidden min-w-0 flex-1 flex-wrap items-center justify-center gap-1 md:flex"
                            ariaLabel={labels.mainNavigation}
                            beforeLinks={<AffiliateModeIndicator className="ltr:mr-2 rtl:ml-2" />}
                            linkClassName={(link) => `rounded-lg px-3 py-2 text-sm font-medium transition-all focus-visible:ring-2 focus-visible:ring-white/50 lg:px-4 ${link.isActive
                                ? 'text-white bg-white/20'
                                : 'text-white/80 hover:text-white hover:bg-white/10'
                            }`}
                        />

                        {/* Actions */}
                        <div className="flex shrink-0 items-center gap-1">
                            <SearchButton SearchSlot={SearchSlot} className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex" />
                            <LanguageSlot
                                variant="text"
                                className="text-white/80 hover:text-white hidden sm:flex"
                                size="sm"
                            />
                            {/* Auth Navigation - Desktop */}
                            <div className="hidden md:flex">
                                <AuthenticationSlot variant="desktop" />
                            </div>
                            <WishlistSlot
                                className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                showTooltip
                            />
                            <CartSlot
                                className="text-white/80 hover:text-white hover:bg-white/10"
                                showTooltip
                            />
                            <HamburgerButton onClick={() => setMobileMenuOpen(true)} />
                        </div>
                    </div>
                </div>
            </header>

            <MobileMenu
                isOpen={mobileMenuOpen}
                onClose={() => setMobileMenuOpen(false)}
                model={model}
                slots={slots}
            />
        </>
    );
};

// ============================================================================
// Centered Header Variant
// ============================================================================

const HeaderCentered: React.FC<HeaderProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    return (
        <>
            <header
                className={`
                    bg-secondary text-white sticky top-0 z-30
                    transition-shadow duration-300
                    ${isScrolled ? 'shadow-lg' : 'shadow-none'}
                `}
                role="banner"
            >
                <div className="container mx-auto px-4">
                    {/* Top row - search and cart */}
                    <div className="flex items-center justify-between h-14 border-b border-white/10">
                        <div className="flex-1 flex items-center">
                            <SearchButton SearchSlot={SearchSlot} className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex" />
                        </div>

                        {/* Centered Logo */}
                        <a
                            href="/"
                            className="min-w-0 max-w-[54vw] truncate rounded text-2xl font-bold tracking-tight text-white transition-opacity hover:opacity-90 focus-visible:ring-2 focus-visible:ring-white/50 md:max-w-md md:text-3xl"
                        >
                            {storeName}
                        </a>

                        <div className="flex-1 flex justify-end items-center gap-1">
                            <LanguageSlot variant="text" className="hidden text-white/80 hover:text-white sm:flex" size="sm" />
                            <div className="hidden md:flex">
                                <AuthenticationSlot variant="desktop" />
                            </div>
                            <WishlistSlot
                                className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                showTooltip
                            />
                            <CartSlot
                                className="text-white/80 hover:text-white hover:bg-white/10"
                                showTooltip
                            />
                            <HamburgerButton onClick={() => setMobileMenuOpen(true)} />
                        </div>
                    </div>

                    {/* Desktop Navigation row */}
                    <NavigationSlot
                        className="hidden min-h-12 flex-wrap items-center justify-center gap-1 py-2 md:flex"
                        ariaLabel={labels.mainNavigation}
                        linkClassName={(link) => `rounded-lg px-3 py-2 text-sm font-medium transition-all focus-visible:ring-2 focus-visible:ring-white/50 lg:px-4 ${link.isActive
                            ? 'text-white bg-white/20'
                            : 'text-white/80 hover:text-white hover:bg-white/10'
                        }`}
                    />
                </div>
            </header>

            <MobileMenu
                isOpen={mobileMenuOpen}
                onClose={() => setMobileMenuOpen(false)}
                model={model}
                slots={slots}
            />
        </>
    );
};

// ============================================================================
// Minimal Header Variant
// ============================================================================

const HeaderMinimal: React.FC<HeaderProps> = ({ model, slots }) => {
    const { labels, storeName } = model;
    const { AuthenticationSlot, CartSlot, LanguageSlot, NavigationSlot, SearchSlot, WishlistSlot } = slots;
    const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    return (
        <>
            <header
                className={`
                    bg-secondary text-white sticky top-0 z-30
                    transition-shadow duration-300
                    ${isScrolled ? 'shadow-lg' : 'shadow-none'}
                `}
                role="banner"
            >
                <div className="container mx-auto px-4">
                    <div className="flex items-center justify-between h-14 md:h-16">
                        {/* Logo */}
                        <a
                            href="/"
                            className="text-xl md:text-2xl font-bold text-white hover:opacity-90 transition-opacity tracking-tight focus-visible:ring-2 focus-visible:ring-white/50 rounded"
                        >
                            {storeName}
                        </a>

                        {/* Actions */}
                        <div className="flex items-center gap-1">
                            <SearchButton SearchSlot={SearchSlot} className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex" />
                            <LanguageSlot variant="text" className="hidden text-white/80 hover:text-white sm:flex" size="sm" />
                            <div className="hidden md:flex">
                                <AuthenticationSlot variant="desktop" />
                            </div>
                            <WishlistSlot
                                className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                showTooltip
                            />
                            <CartSlot
                                className="text-white/80 hover:text-white hover:bg-white/10"
                                showTooltip
                            />
                            <HamburgerButton onClick={() => setMobileMenuOpen(true)} />
                        </div>
                    </div>
                    <NavigationSlot
                        className="hidden min-h-12 flex-wrap items-center justify-center gap-1 border-t border-white/10 py-2 md:flex"
                        ariaLabel={labels.mainNavigation}
                        linkClassName={(link) => `rounded-lg px-3 py-2 text-sm font-medium transition-all ${link.isActive
                            ? 'bg-white/20 text-white'
                            : 'text-white/80 hover:bg-white/10 hover:text-white'
                        }`}
                    />
                </div>
            </header>

            <MobileMenu
                isOpen={mobileMenuOpen}
                onClose={() => setMobileMenuOpen(false)}
                model={model}
                slots={slots}
            />
        </>
    );
};

// ============================================================================
// Header Variants Mapping
// ============================================================================

const HEADER_VARIANTS = {
    standard: HeaderStandard,
    centered: HeaderCentered,
    minimal: HeaderMinimal,
} as const;

// ============================================================================
// Main Header Component
// ============================================================================

export const Header: React.FC<HeaderProps> = (props) => {
    const HeaderComponent = HEADER_VARIANTS[props.model.headerVariant] || HeaderStandard;

    return <HeaderComponent {...props} />;
};
