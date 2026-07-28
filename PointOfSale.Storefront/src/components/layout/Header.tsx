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

import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useLocation } from 'react-router-dom';
import { useStoreConfigStore, selectStoreName, selectLayout, selectBehavior } from '@/store';
import { useScrollPosition, useBodyScrollLock, useFocusTrap } from '@/hooks/useScrollPosition';
import { CartIconButton } from '@/components/common/CartBadge';
import { WishlistIconButton } from '@/components/common/WishlistBadge';
import { AffiliateModeIndicator } from '@/components/common/AffiliateModeIndicator';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { LanguageSwitcher } from '@/components/common/LanguageSwitcher';
import { useNavigationStore, resolveNavLinkUrl } from '@/stores/navigationStore';
import { SearchModal, useSearchShortcut } from '@/components/search/SearchModal';
import { useTranslation } from '@/i18n';
import { AuthNavItems } from '@/auth/components';
import type { NavigationLink } from '@/types';

// ============================================================================
// Types
// ============================================================================

interface HeaderProps {
    // Categories removed - now using filters sidebar on products page
}

interface MobileMenuProps {
    isOpen: boolean;
    onClose: () => void;
    enableSearch: boolean;
}

// ============================================================================
// Mobile Menu Component
// ============================================================================

const MobileMenu: React.FC<MobileMenuProps> = ({ isOpen, onClose, enableSearch }) => {
    const { t, language } = useTranslation();
    const menuRef = useRef<HTMLDivElement>(null);
    const location = useLocation();
    const { getHeaderLinks, loadNavigation } = useNavigationStore();
    const headerLinks = getHeaderLinks();

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

    useEffect(() => {
        loadNavigation();
    }, [loadNavigation]);

    // Check if route is active
    const isActive = useCallback((path: string) => {
        return location.pathname === path || location.pathname.startsWith(path + '/');
    }, [location.pathname]);

    const getNavLabel = useCallback((link: NavigationLink) => {
        if (language === 'ar' && link.labelAr) {
            return link.labelAr;
        }

        return link.label;
    }, [language]);

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
                            <span className="font-semibold text-text">{t('header.menu')}</span>
                            {/* AF-12: Affiliate Mode Indicator - Mobile */}
                            <AffiliateModeIndicator />
                        </div>
                        <button
                            onClick={onClose}
                            className="p-2.5 min-w-[44px] min-h-[44px] flex items-center justify-center text-muted hover:text-text transition-colors rounded-lg hover:bg-muted/10 focus-visible:ring-2 focus-visible:ring-primary"
                            aria-label={t('header.closeMenu')}
                        >
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Search */}
                    {enableSearch && (
                        <div className="p-4 border-b border-muted/10">
                            <a
                                href="/products?focus=search"
                                className="flex items-center gap-3 px-4 py-3 bg-muted/10 rounded-lg text-muted hover:text-text transition-colors"
                            >
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                                </svg>
                                <span>{t('header.searchProducts')}</span>
                            </a>
                        </div>
                    )}

                    {/* Navigation Links */}
                    <nav className="flex-1 overflow-y-auto py-4" aria-label="Main navigation">
                        {headerLinks.map((link, index) => {
                            const url = resolveNavLinkUrl(link);
                            return (
                                <AffiliateLink
                                    key={`${link.label}-${index}`}
                                    href={url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className={`flex items-center px-4 py-3.5 min-h-[44px] transition-colors ${isActive(url)
                                        ? 'bg-primary/10 text-primary border-r-2 border-primary'
                                        : 'text-text hover:bg-primary/10 hover:text-primary'
                                        }`}
                                >
                                    <span className="w-5 ltr:mr-3 rtl:ml-3 flex-shrink-0 text-center text-sm font-semibold">
                                        {index + 1}
                                    </span>
                                    {getNavLabel(link)}
                                </AffiliateLink>
                            );
                        })}
                    </nav>

                    {/* Auth Section */}
                    <div className="px-4 py-2 mt-2 border-t border-muted/10">
                        <span className="text-xs font-semibold text-muted uppercase tracking-wider">
                            {t('header.account')}
                        </span>
                    </div>
                    <AuthNavItems
                        variant="mobile"
                        onLinkClick={onClose}
                    />

                    {/* Footer */}
                    <div className="p-4 border-t border-muted/20 mt-auto">
                        <AffiliateLink
                            href="/cart"
                            className="flex items-center justify-center gap-2 w-full px-4 py-3.5 min-h-[44px] bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors font-medium focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                        >
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z" />
                            </svg>
                            {t('header.viewCart')}
                        </AffiliateLink>
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
    className?: string;
    onClick?: () => void;
}

const SearchButton: React.FC<SearchButtonProps> = ({ className = '', onClick }) => {
    // Detect platform for shortcut hint
    const isMac = typeof navigator !== 'undefined' && /Mac|iPod|iPhone|iPad/.test(navigator.platform);
    const shortcutKey = isMac ? '⌘K' : 'Ctrl+K';

    const handleClick = () => {
        if (onClick) {
            onClick();
        } else {
            // Navigate to products page with search focus
            window.location.href = '/products?focus=search';
        }
    };

    return (
        <button
            onClick={handleClick}
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
        </button>
    );
};

// ============================================================================
// Standard Header Variant
// ============================================================================

const HeaderStandard: React.FC<HeaderProps> = () => {
    const { language } = useTranslation();
    const storeName = useStoreConfigStore(selectStoreName);
    const behavior = useStoreConfigStore(selectBehavior);
    const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
    const [searchModalOpen, setSearchModalOpen] = useState(false);
    const location = useLocation();
    const { isScrolled } = useScrollPosition({ threshold: 10 });

    // Get dynamic header links from navigation store
    const { getHeaderLinks, loadNavigation } = useNavigationStore();
    const headerLinks = getHeaderLinks();

    // Load navigation on mount
    useEffect(() => {
        loadNavigation();
    }, [loadNavigation]);

    // Keyboard shortcut for search (Cmd/Ctrl + K)
    useSearchShortcut(() => {
        if (behavior.enableSearch) {
            setSearchModalOpen(true);
        }
    });

    // Check if route is active
    const isActive = useCallback((path: string) => {
        if (path === '/') return location.pathname === '/';
        return location.pathname.startsWith(path);
    }, [location.pathname]);

    // Get localized navigation label
    const getNavLabel = (link: { label: string; labelAr?: string }) => {
        if (language === 'ar' && link.labelAr) {
            return link.labelAr;
        }
        return link.label;
    };

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
                        <nav className="hidden min-w-0 flex-1 flex-wrap items-center justify-center gap-1 md:flex" aria-label="Main navigation">
                            {/* AF-12: Affiliate Mode Indicator */}
                            <AffiliateModeIndicator className="ltr:mr-2 rtl:ml-2" />

                            {headerLinks.map((link, index) => {
                                const url = resolveNavLinkUrl(link);
                                return (
                                    <AffiliateLink
                                        key={`${link.label}-${index}`}
                                        href={url}
                                        className={`rounded-lg px-3 py-2 text-sm font-medium transition-all focus-visible:ring-2 focus-visible:ring-white/50 lg:px-4 ${isActive(url)
                                            ? 'text-white bg-white/20'
                                            : 'text-white/80 hover:text-white hover:bg-white/10'
                                            }`}
                                        target={link.openInNewTab ? '_blank' : undefined}
                                        rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    >
                                        {getNavLabel(link)}
                                    </AffiliateLink>
                                );
                            })}
                            {/* Categories removed - now using filters sidebar on products page */}
                        </nav>

                        {/* Actions */}
                        <div className="flex shrink-0 items-center gap-1">
                            {behavior.enableSearch && (
                                <SearchButton
                                    className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                    onClick={() => setSearchModalOpen(true)}
                                />
                            )}
                            <LanguageSwitcher
                                variant="text"
                                className="text-white/80 hover:text-white hidden sm:flex"
                                size="sm"
                            />
                            {/* Auth Navigation - Desktop */}
                            <div className="hidden md:flex">
                                <AuthNavItems variant="desktop" />
                            </div>
                            <WishlistIconButton
                                className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                showTooltip
                            />
                            <CartIconButton
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
                enableSearch={behavior.enableSearch}
            />

            {/* Search Modal */}
            {behavior.enableSearch && (
                <SearchModal
                    isOpen={searchModalOpen}
                    onClose={() => setSearchModalOpen(false)}
                />
            )}
        </>
    );
};

// ============================================================================
// Centered Header Variant
// ============================================================================

const HeaderCentered: React.FC<HeaderProps> = () => {
    const { language } = useTranslation();
    const storeName = useStoreConfigStore(selectStoreName);
    const behavior = useStoreConfigStore(selectBehavior);
    const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
    const location = useLocation();
    const { isScrolled } = useScrollPosition({ threshold: 10 });
    const { getHeaderLinks, loadNavigation } = useNavigationStore();
    const headerLinks = getHeaderLinks();

    useEffect(() => {
        loadNavigation();
    }, [loadNavigation]);

    const isActive = useCallback((path: string) => {
        if (path === '/') return location.pathname === '/';
        return location.pathname.startsWith(path);
    }, [location.pathname]);

    const getNavLabel = useCallback((link: NavigationLink) => {
        if (language === 'ar' && link.labelAr) {
            return link.labelAr;
        }

        return link.label;
    }, [language]);

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
                            {behavior.enableSearch && (
                                <SearchButton className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex" />
                            )}
                        </div>

                        {/* Centered Logo */}
                        <a
                            href="/"
                            className="min-w-0 max-w-[54vw] truncate rounded text-2xl font-bold tracking-tight text-white transition-opacity hover:opacity-90 focus-visible:ring-2 focus-visible:ring-white/50 md:max-w-md md:text-3xl"
                        >
                            {storeName}
                        </a>

                        <div className="flex-1 flex justify-end items-center gap-1">
                            <WishlistIconButton
                                className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                showTooltip
                            />
                            <CartIconButton
                                className="text-white/80 hover:text-white hover:bg-white/10"
                                showTooltip
                            />
                            <HamburgerButton onClick={() => setMobileMenuOpen(true)} />
                        </div>
                    </div>

                    {/* Desktop Navigation row */}
                    <nav className="hidden min-h-12 flex-wrap items-center justify-center gap-1 py-2 md:flex" aria-label="Main navigation">
                        {headerLinks.map((link, index) => {
                            const url = resolveNavLinkUrl(link);
                            return (
                                <AffiliateLink
                                    key={`${link.label}-${index}`}
                                    href={url}
                                    target={link.openInNewTab ? '_blank' : undefined}
                                    rel={link.openInNewTab ? 'noopener noreferrer' : undefined}
                                    className={`rounded-lg px-3 py-2 text-sm font-medium transition-all focus-visible:ring-2 focus-visible:ring-white/50 lg:px-4 ${isActive(url)
                                        ? 'text-white bg-white/20'
                                        : 'text-white/80 hover:text-white hover:bg-white/10'
                                        }`}
                                >
                                    {getNavLabel(link)}
                                </AffiliateLink>
                            );
                        })}
                    </nav>
                </div>
            </header>

            <MobileMenu
                isOpen={mobileMenuOpen}
                onClose={() => setMobileMenuOpen(false)}
                enableSearch={behavior.enableSearch}
            />
        </>
    );
};

// ============================================================================
// Minimal Header Variant
// ============================================================================

const HeaderMinimal: React.FC<HeaderProps> = () => {
    const storeName = useStoreConfigStore(selectStoreName);
    const behavior = useStoreConfigStore(selectBehavior);
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
                            <WishlistIconButton
                                className="text-white/80 hover:text-white hover:bg-white/10 hidden sm:flex"
                                showTooltip
                            />
                            <CartIconButton
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
                enableSearch={behavior.enableSearch}
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
    const layout = useStoreConfigStore(selectLayout);
    const HeaderComponent = HEADER_VARIANTS[layout.headerVariant] || HeaderStandard;

    return <HeaderComponent {...props} />;
};
