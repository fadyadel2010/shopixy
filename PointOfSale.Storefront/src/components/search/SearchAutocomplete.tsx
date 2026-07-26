/**
 * SearchAutocomplete Component
 * 
 * Autocomplete dropdown with:
 * - Grouped results by entity type
 * - Keyboard navigation
 * - Loading/empty/error states
 * - Click outside to close
 * - Accessible announcements
 * - Phase 10.1: Multi-language support
 */

import React, { useRef, useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { SearchResultItem } from './SearchResultItem';
import type { UnifiedSearchItem } from '@/services/api/searchApi';
import type { SearchStatus } from '@/stores/searchStore';
import { useTranslation } from '@/i18n';

interface SearchAutocompleteProps {
    query: string;
    results: UnifiedSearchItem[];
    status: SearchStatus;
    isOpen: boolean;
    onClose: () => void;
    onSelect?: (item: UnifiedSearchItem) => void;
    error?: string | null;
}

export const SearchAutocomplete: React.FC<SearchAutocompleteProps> = ({
    query,
    results,
    status,
    isOpen,
    onClose,
    onSelect,
    error,
}) => {
    const navigate = useNavigate();
    const containerRef = useRef<HTMLDivElement>(null);
    const [highlightedIndex, setHighlightedIndex] = useState(-1);
    const { t } = useTranslation();

    // Reset highlight when results change
    useEffect(() => {
        setHighlightedIndex(-1);
    }, [results]);

    // Get grouped results
    const groupedResults = React.useMemo(() => {
        const groups: Record<string, UnifiedSearchItem[]> = {
            product: [],
            category: [],
            cms: [],
        };

        results.forEach(item => {
            if (groups[item.entityType]) {
                groups[item.entityType].push(item);
            }
        });

        return groups;
    }, [results]);

    // Flatten results for keyboard navigation
    const flatResults = React.useMemo(() => {
        return [
            ...groupedResults.product,
            ...groupedResults.category,
            ...groupedResults.cms,
        ];
    }, [groupedResults]);

    // Handle item selection
    const handleSelect = useCallback((item: UnifiedSearchItem) => {
        onClose();

        if (onSelect) {
            onSelect(item);
        } else {
            // Navigate to item
            const path = item.entityType === 'product'
                ? `/products/${item.slug || item.entityId}`
                : item.entityType === 'category'
                    ? `/category/${item.slug || item.entityId}`
                    : `/${item.slug || item.entityId}`;
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
                        prev < flatResults.length - 1 ? prev + 1 : 0
                    );
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    setHighlightedIndex(prev =>
                        prev > 0 ? prev - 1 : flatResults.length - 1
                    );
                    break;
                case 'Enter':
                    e.preventDefault();
                    if (highlightedIndex >= 0 && flatResults[highlightedIndex]) {
                        handleSelect(flatResults[highlightedIndex]);
                    } else if (query.trim().length >= 2) {
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
    }, [isOpen, flatResults, highlightedIndex, handleSelect, navigate, query, onClose]);

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

    // Group labels from translations
    const groupLabels = {
        product: t('search.products'),
        category: t('search.categories'),
        cms: t('search.pages'),
    };

    if (!isOpen) return null;

    return (
        <div
            ref={containerRef}
            className="absolute top-full left-0 right-0 mt-2 bg-background border border-muted/20 rounded-xl shadow-xl z-50 overflow-hidden max-h-[70vh] overflow-y-auto"
            role="listbox"
            aria-label="Search suggestions"
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
            {status === 'success' && flatResults.length > 0 && (
                <>
                    {/* Products */}
                    {groupedResults.product.length > 0 && (
                        <div>
                            <div className="px-4 py-2 text-xs font-semibold text-muted uppercase tracking-wider bg-muted/5">
                                {groupLabels.product}
                            </div>
                            {groupedResults.product.map((item) => (
                                <SearchResultItem
                                    key={`product-${item.entityId}`}
                                    item={item}
                                    query={query}
                                    isHighlighted={flatResults.indexOf(item) === highlightedIndex}
                                    onClick={() => handleSelect(item)}
                                    compact
                                />
                            ))}
                        </div>
                    )}

                    {/* Categories */}
                    {groupedResults.category.length > 0 && (
                        <div>
                            <div className="px-4 py-2 text-xs font-semibold text-muted uppercase tracking-wider bg-muted/5">
                                {groupLabels.category}
                            </div>
                            {groupedResults.category.map((item) => (
                                <SearchResultItem
                                    key={`category-${item.entityId}`}
                                    item={item}
                                    query={query}
                                    isHighlighted={flatResults.indexOf(item) === highlightedIndex}
                                    onClick={() => handleSelect(item)}
                                    compact
                                />
                            ))}
                        </div>
                    )}

                    {/* CMS Pages */}
                    {groupedResults.cms.length > 0 && (
                        <div>
                            <div className="px-4 py-2 text-xs font-semibold text-muted uppercase tracking-wider bg-muted/5">
                                {groupLabels.cms}
                            </div>
                            {groupedResults.cms.map((item) => (
                                <SearchResultItem
                                    key={`cms-${item.entityId}`}
                                    item={item}
                                    query={query}
                                    isHighlighted={flatResults.indexOf(item) === highlightedIndex}
                                    onClick={() => handleSelect(item)}
                                    compact
                                />
                            ))}
                        </div>
                    )}

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
