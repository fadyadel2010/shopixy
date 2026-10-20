/**
 * SmartSearchAutocomplete Component
 * 
 * SMART Header Search (Autocomplete) dropdown with:
 * - Products only (no categories or CMS pages)
 * - Variant-aware price display ("Multiple options")
 * - Promotion badges
 * - Image thumbnails
 * - Keyboard navigation
 * - Loading/empty/error states
 * - Click outside to close
 * - Works from 1 character
 * 
 * Phase: Smart Promotion-Aware Header Search
 */

import React, { useRef, useEffect, useState, useCallback } from 'react';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';
import type { HeaderSearchItem } from '@/services/api/searchApi';
import type { SearchStatus } from '@/stores/searchStore';
import { useTranslation } from '@/i18n';
import { useStoreConfigStore, selectCurrency } from '@/store';
import { resolveProductImage } from '@/utils';

interface SmartSearchAutocompleteProps {
    query: string;
    results: HeaderSearchItem[];
    status: SearchStatus;
    isOpen: boolean;
    onClose: () => void;
    onSelect?: (item: HeaderSearchItem) => void;
    error?: string | null;
}

export const SmartSearchAutocomplete: React.FC<SmartSearchAutocompleteProps> = ({
    query,
    results,
    status,
    isOpen,
    onClose,
    onSelect,
    error,
}) => {
    const navigate = useAffiliateNavigate();
    const containerRef = useRef<HTMLDivElement>(null);
    const [highlightedIndex, setHighlightedIndex] = useState(-1);
    const { t } = useTranslation();
    const currency = useStoreConfigStore(selectCurrency);
    const isRtl = typeof document !== 'undefined' && document.documentElement.dir === 'rtl';

    // Reset highlight when results change
    useEffect(() => {
        setHighlightedIndex(-1);
    }, [results]);

    // Handle item selection
    const handleSelect = useCallback((item: HeaderSearchItem) => {
        onClose();

        if (onSelect) {
            onSelect(item);
        } else {
            // Navigate to product
            const path = `/products/${item.slug || item.productId}`;
            navigate(path);
        }
    }, [navigate, onClose, onSelect]);

    // Keyboard navigation
    useEffect(() => {
        if (!isOpen) return;

        const handleKeyDown = (e: KeyboardEvent) => {
            switch (e.key) {
                case 'ArrowDown':
                    e.preventDefault();
                    setHighlightedIndex(prev =>
                        prev < results.length - 1 ? prev + 1 : 0
                    );
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    setHighlightedIndex(prev =>
                        prev > 0 ? prev - 1 : results.length - 1
                    );
                    break;
                case 'Enter':
                    e.preventDefault();
                    if (highlightedIndex >= 0 && results[highlightedIndex]) {
                        handleSelect(results[highlightedIndex]);
                    } else if (query.trim().length >= 1) {
                        // Navigate to search page
                        onClose();
                        navigate(`/search?q=${encodeURIComponent(query)}`);
                    }
                    break;
                case 'Escape':
                    e.preventDefault();
                    onClose();
                    break;
            }
        };

        document.addEventListener('keydown', handleKeyDown);
        return () => document.removeEventListener('keydown', handleKeyDown);
    }, [isOpen, results, highlightedIndex, handleSelect, navigate, query, onClose]);

    // Click outside to close
    useEffect(() => {
        if (!isOpen) return;

        const handleClickOutside = (e: MouseEvent) => {
            if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
                onClose();
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, [isOpen, onClose]);

    // Format price
    const formatPrice = (price: number): string => {
        return new Intl.NumberFormat(isRtl ? 'ar-SA' : 'en-US', {
            style: 'currency',
            currency: currency,
            minimumFractionDigits: 0,
            maximumFractionDigits: 2,
        }).format(price);
    };

    if (!isOpen) return null;

    return (
        <div
            ref={containerRef}
            className="absolute top-full left-0 right-0 mt-2 bg-background border border-muted/20 rounded-xl shadow-xl z-50 overflow-hidden max-h-[70vh] overflow-y-auto"
            role="listbox"
            aria-label={t('search.suggestions')}
        >
            {/* Loading State */}
            {status === 'loading' && (
                <div className="flex items-center justify-center gap-2 py-8 text-muted">
                    <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    <span>{t('search.searching')}</span>
                </div>
            )}

            {/* Error State */}
            {status === 'error' && (
                <div className="py-8 px-4 text-center text-error">
                    <svg className="w-8 h-8 mx-auto mb-2 text-error/50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                    </svg>
                    <p>{error || t('search.unavailable')}</p>
                </div>
            )}

            {/* Empty State */}
            {status === 'empty' && (
                <div className="py-8 px-4 text-center text-muted">
                    <svg className="w-8 h-8 mx-auto mb-2 text-muted/50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                    </svg>
                    <p>{t('search.noResultsFor', { query })}</p>
                </div>
            )}

            {/* Results */}
            {status === 'success' && results.length > 0 && (
                <>
                    {/* Products Header */}
                    <div className="px-4 py-2 text-xs font-semibold text-muted uppercase tracking-wider bg-muted/5">
                        {t('search.products')}
                    </div>

                    {/* Product Items */}
                    {results.map((item, index) => {
                        const imageSrc = resolveProductImage(item);

                        return (
                        <button
                            key={item.productId}
                            onClick={() => handleSelect(item)}
                            className={`
                                w-full flex items-center gap-3 px-4 py-3 text-left
                                transition-colors duration-150
                                ${index === highlightedIndex
                                    ? 'bg-primary/10'
                                    : 'hover:bg-muted/10'
                                }
                            `}
                            role="option"
                            aria-selected={index === highlightedIndex}
                        >
                            {/* Product Image */}
                            <img
                                src={imageSrc}
                                alt=""
                                className="w-12 h-12 object-contain rounded-lg flex-shrink-0"
                                loading="lazy"
                            />

                            {/* Product Info */}
                            <div className="flex-1 min-w-0">
                                {/* Name */}
                                <div className="font-medium text-text truncate">
                                    {item.name}
                                </div>

                                {/* Price/Variants + Promotion */}
                                <div className="flex items-center gap-2 mt-0.5 text-sm">
                                    {/* Price or "Multiple options" */}
                                    {item.displayPriceText ? (
                                        <span className="text-muted">{item.displayPriceText}</span>
                                    ) : item.price !== undefined && item.price !== null ? (
                                        <span className="font-semibold text-primary">{formatPrice(item.price)}</span>
                                    ) : null}

                                    {/* Promotion Badge */}
                                    {item.hasActivePromotion && item.promotionBadge && (
                                        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 bg-gradient-to-r from-orange-500 to-red-500 text-white text-xs font-bold rounded">
                                            🔥 {item.promotionBadge}
                                        </span>
                                    )}
                                </div>

                                {/* Category */}
                                {item.categoryName && (
                                    <div className="text-xs text-muted/70 mt-0.5 truncate">
                                        {item.categoryName}
                                    </div>
                                )}
                            </div>

                            {/* Arrow indicator */}
                            <svg className="w-4 h-4 text-muted/50 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d={isRtl ? "M15 19l-7-7 7-7" : "M9 5l7 7-7 7"} />
                            </svg>
                        </button>
                        );
                    })}

                    {/* View All Results Link */}
                    <button
                        onClick={() => {
                            onClose();
                            navigate(`/search?q=${encodeURIComponent(query)}`);
                        }}
                        className="w-full py-3 px-4 text-sm text-primary font-medium hover:bg-primary/5 transition-colors border-t border-muted/10"
                    >
                        {t('search.viewAllResults', { query })}
                    </button>
                </>
            )}
        </div>
    );
};
