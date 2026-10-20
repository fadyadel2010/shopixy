/**
 * SearchModal Component
 * 
 * Full-screen search modal with:
 * - Search input with autocomplete
 * - Keyboard shortcut activation (Cmd/Ctrl + K)
 * - Focus trap and body scroll lock
 * - Smooth animations
 * - Full mobile support
 */

import React, { useEffect, useCallback, useRef } from 'react';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';
import { useBodyScrollLock, useFocusTrap } from '@/hooks/useScrollPosition';
import { SearchInput } from './SearchInput';
import {
    useSearchStore,
    selectQuery,
    selectAutocompleteResults,
    selectAutocompleteStatus,
    selectError
} from '@/stores/searchStore';
import { useStoreConfigStore, selectCurrency } from '@/store';
import { resolveProductImage } from '@/utils';

interface SearchModalProps {
    isOpen: boolean;
    onClose: () => void;
}

export const SearchModal: React.FC<SearchModalProps> = ({ isOpen, onClose }) => {
    const navigate = useAffiliateNavigate();
    const modalRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    // Search store
    const query = useSearchStore(selectQuery);
    const results = useSearchStore(selectAutocompleteResults);
    const status = useSearchStore(selectAutocompleteStatus);
    const error = useSearchStore(selectError);
    const setQuery = useSearchStore(state => state.setQuery);
    const fetchAutocomplete = useSearchStore(state => state.fetchAutocomplete);
    const clearQuery = useSearchStore(state => state.clearQuery);

    // Get currency from store config
    const currency = useStoreConfigStore(selectCurrency);

    // Body scroll lock
    useBodyScrollLock(isOpen);

    // Focus trap
    useFocusTrap(modalRef, isOpen);

    // Focus input on open
    useEffect(() => {
        if (isOpen) {
            // Small delay to ensure DOM is ready
            setTimeout(() => {
                inputRef.current?.focus();
            }, 100);
        }
    }, [isOpen]);

    // Handle close with cleanup
    const handleClose = useCallback(() => {
        clearQuery();
        onClose();
    }, [clearQuery, onClose]);

    // Handle ESC key
    useEffect(() => {
        if (!isOpen) return;

        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                e.preventDefault();
                handleClose();
            }
        };

        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, [isOpen, handleClose]);

    // Handle search submit
    const handleSearch = useCallback((searchQuery: string) => {
        fetchAutocomplete(searchQuery);
    }, [fetchAutocomplete]);

    // Handle go to search page
    const handleGoToSearch = useCallback(() => {
        // Header search works from 1 character now!
        if (query.trim().length >= 1) {
            handleClose();
            navigate(`/search?q=${encodeURIComponent(query.trim())}`);
        }
    }, [query, handleClose, navigate]);

    // Handle input key down
    const handleKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
        // Header search works from 1 character now!
        if (e.key === 'Enter' && query.trim().length >= 1) {
            // Only go to search page if no autocomplete item is highlighted
            // The autocomplete component handles Enter for highlighted items
            handleGoToSearch();
        }
    }, [query, handleGoToSearch]);

    // Get RTL state
    const isRtl = document.documentElement.dir === 'rtl';

    if (!isOpen) return null;

    return (
        <div
            className="fixed inset-0 z-50 animate-fade-in"
            role="dialog"
            aria-modal="true"
            aria-label={isRtl ? 'البحث' : 'Search'}
        >
            {/* Backdrop */}
            <div
                className="absolute inset-0 bg-secondary/80 backdrop-blur-sm"
                onClick={handleClose}
                aria-hidden="true"
            />

            {/* Modal Container */}
            <div
                ref={modalRef}
                className="relative w-full max-w-2xl mx-auto mt-[8vh] px-4"
            >
                {/* Search Card */}
                <div className="bg-background rounded-2xl shadow-2xl animate-slide-in-up">
                    {/* Header with Search Input */}
                    <div className="flex items-center gap-4 p-4 border-b border-muted/10">
                        {/* Search Input Container */}
                        <div className="flex-1">
                            <SearchInput
                                value={query}
                                onChange={setQuery}
                                onSearch={handleSearch}
                                onKeyDown={handleKeyDown}
                                isLoading={status === 'loading'}
                                autoFocus
                                size="lg"
                            />
                        </div>

                        {/* Close Button */}
                        <button
                            onClick={handleClose}
                            className="flex-shrink-0 p-2 text-muted hover:text-text transition-colors rounded-lg hover:bg-muted/10"
                            aria-label={isRtl ? 'إغلاق' : 'Close'}
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Autocomplete Results - Smart Header Search (works from 1 char) */}
                    {query.trim().length >= 1 && (
                        <div className="max-h-[50vh] overflow-y-auto">
                            {/* Loading State */}
                            {status === 'loading' && (
                                <div className="flex items-center justify-center gap-2 py-8 text-muted">
                                    <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                    </svg>
                                    <span>{isRtl ? 'جاري البحث...' : 'Searching...'}</span>
                                </div>
                            )}

                            {/* Error State */}
                            {status === 'error' && (
                                <div className="py-8 px-4 text-center text-error">
                                    <svg className="w-8 h-8 mx-auto mb-2 text-error/50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                    </svg>
                                    <p>{error || (isRtl ? 'خدمة البحث غير متوفرة' : 'Search service unavailable')}</p>
                                </div>
                            )}

                            {/* Empty State */}
                            {status === 'empty' && (
                                <div className="py-8 px-4 text-center text-muted">
                                    <svg className="w-8 h-8 mx-auto mb-2 text-muted/50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                                    </svg>
                                    <p>
                                        {isRtl
                                            ? `لا توجد نتائج لـ "${query}"`
                                            : `No results found for "${query}"`
                                        }
                                    </p>
                                </div>
                            )}

                            {/* Smart Results - Products with Variant-Aware Display */}
                            {status === 'success' && results.length > 0 && (
                                <div className="divide-y divide-muted/10">
                                    {/* Products Header */}
                                    <div className="px-4 py-2 text-xs font-semibold text-muted uppercase tracking-wider bg-muted/5">
                                        {isRtl ? 'المنتجات' : 'Products'}
                                    </div>

                                    {results.map(item => {
                                        const imageSrc = resolveProductImage(item);

                                        return (
                                        <a
                                            key={item.productId}
                                            href={`/products/${item.slug || item.productId}`}
                                            onClick={(e) => {
                                                e.preventDefault();
                                                handleClose();
                                                navigate(`/products/${item.slug || item.productId}`);
                                            }}
                                            className="flex items-center gap-3 px-4 py-3 hover:bg-muted/10 transition-colors"
                                        >
                                            {/* Product Image */}
                                            <img
                                                src={imageSrc}
                                                alt=""
                                                className="w-12 h-12 object-contain rounded-lg flex-shrink-0"
                                                loading="lazy"
                                            />

                                            {/* Content */}
                                            <div className="flex-1 min-w-0">
                                                <div className="font-medium text-text truncate">
                                                    {item.name}
                                                </div>
                                                <div className="flex items-center gap-2 mt-0.5 text-sm">
                                                    {/* Variant-Aware Price Display */}
                                                    {item.displayPriceText ? (
                                                        <span className="text-muted">{item.displayPriceText}</span>
                                                    ) : item.price !== undefined && item.price !== null ? (
                                                        <span className="font-semibold text-primary">
                                                            {new Intl.NumberFormat(isRtl ? 'ar-SA' : 'en-US', {
                                                                style: 'currency',
                                                                currency: currency,
                                                                minimumFractionDigits: 0,
                                                            }).format(item.price)}
                                                        </span>
                                                    ) : null}

                                                    {/* Promotion Badge */}
                                                    {item.hasActivePromotion && item.promotionBadge && (
                                                        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 bg-gradient-to-r from-orange-500 to-red-500 text-white text-xs font-bold rounded">
                                                            🔥 {item.promotionBadge}
                                                        </span>
                                                    )}
                                                </div>
                                                {item.categoryName && (
                                                    <div className="text-xs text-muted/70 mt-0.5 truncate">
                                                        {item.categoryName}
                                                    </div>
                                                )}
                                            </div>

                                            {/* Arrow */}
                                            <svg className="w-4 h-4 text-muted/50 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d={isRtl ? "M15 19l-7-7 7-7" : "M9 5l7 7-7 7"} />
                                            </svg>
                                        </a>
                                        );
                                    })}

                                    {/* View All Link */}
                                    <button
                                        onClick={() => {
                                            handleClose();
                                            navigate(`/search?q=${encodeURIComponent(query)}`);
                                        }}
                                        className="w-full py-3 px-4 text-sm text-primary font-medium hover:bg-primary/5 transition-colors text-center"
                                    >
                                        {isRtl
                                            ? `عرض جميع النتائج لـ "${query}"`
                                            : `View all results for "${query}"`
                                        }
                                    </button>
                                </div>
                            )}

                            {/* Idle State - Show hint */}
                            {status === 'idle' && query.trim().length >= 1 && (
                                <div className="py-6 px-4 text-center text-muted">
                                    <p>{isRtl ? 'اكتب للبحث...' : 'Type to search...'}</p>
                                </div>
                            )}
                        </div>
                    )}

                    {/* Footer Hint */}
                    <div className="px-4 py-3 bg-muted/5 text-xs text-muted flex items-center justify-between border-t border-muted/10">
                        <div className="flex items-center gap-4">
                            <span className="flex items-center gap-1">
                                <kbd className="px-1.5 py-0.5 bg-muted/10 rounded text-[10px] font-mono">↑↓</kbd>
                                <span>{isRtl ? 'للتنقل' : 'to navigate'}</span>
                            </span>
                            <span className="flex items-center gap-1">
                                <kbd className="px-1.5 py-0.5 bg-muted/10 rounded text-[10px] font-mono">↵</kbd>
                                <span>{isRtl ? 'للاختيار' : 'to select'}</span>
                            </span>
                            <span className="flex items-center gap-1">
                                <kbd className="px-1.5 py-0.5 bg-muted/10 rounded text-[10px] font-mono">esc</kbd>
                                <span>{isRtl ? 'للإغلاق' : 'to close'}</span>
                            </span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};

// ============================================================================
// Global Keyboard Shortcut Hook
// ============================================================================

/**
 * Hook to handle global Cmd/Ctrl + K shortcut for opening search
 */
export function useSearchShortcut(onOpen: () => void): void {
    useEffect(() => {
        const handleKeyDown = (e: KeyboardEvent) => {
            // Cmd+K (Mac) or Ctrl+K (Windows/Linux)
            if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
                e.preventDefault();
                onOpen();
            }
        };

        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, [onOpen]);
}
