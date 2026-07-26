/**
 * SearchResultItem Component
 * 
 * Displays a single search result item with:
 * - Entity type icon
 * - Highlighted text matching
 * - Language-aware name display
 * - Price (for products)
 * - Category name (for products)
 */

import React from 'react';
import type { UnifiedSearchItem } from '@/services/api/searchApi';
import { useStoreConfigStore, selectCurrency } from '@/store';
import { AffiliateLink } from '@/components/common/AffiliateLink';

interface SearchResultItemProps {
    item: UnifiedSearchItem;
    query: string;
    onClick?: () => void;
    isHighlighted?: boolean;
    compact?: boolean;
}

export const SearchResultItem: React.FC<SearchResultItemProps> = ({
    item,
    query,
    onClick,
    isHighlighted = false,
    compact = false,
}) => {
    // Get currency from store config
    const currency = useStoreConfigStore(selectCurrency);
    // Get language-aware name
    const isRtl = document.documentElement.dir === 'rtl';
    const displayName = isRtl && item.nameAr ? item.nameAr : item.name;

    // Build link based on entity type
    const getLink = (): string => {
        switch (item.entityType) {
            case 'product':
                return `/products/${item.slug || item.entityId}`;
            case 'category':
                return `/category/${item.slug || item.entityId}`;
            case 'cms':
                return `/${item.slug || item.entityId}`;
            default:
                return '/';
        }
    };

    // Get entity type icon
    const getIcon = () => {
        switch (item.entityType) {
            case 'product':
                return (
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
                    </svg>
                );
            case 'category':
                return (
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                    </svg>
                );
            case 'cms':
                return (
                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                );
        }
    };

    // Get entity type label
    const getTypeLabel = (): string => {
        const labels = {
            product: isRtl ? 'منتج' : 'Product',
            category: isRtl ? 'قسم' : 'Category',
            cms: isRtl ? 'صفحة' : 'Page',
        };
        return labels[item.entityType] || '';
    };

    // Highlight matched text
    const highlightMatch = (text: string): React.ReactNode => {
        if (!query || query.length < 2) return text;

        const lowerText = text.toLowerCase();
        const lowerQuery = query.toLowerCase();
        const index = lowerText.indexOf(lowerQuery);

        if (index === -1) return text;

        return (
            <>
                {text.slice(0, index)}
                <mark className="bg-primary/20 text-primary px-0.5 rounded">
                    {text.slice(index, index + query.length)}
                </mark>
                {text.slice(index + query.length)}
            </>
        );
    };

    // Format price
    const formatPrice = (price: number): string => {
        return new Intl.NumberFormat(isRtl ? 'ar-SA' : 'en-US', {
            style: 'currency',
            currency: currency,
            minimumFractionDigits: 0,
            maximumFractionDigits: 2,
        }).format(price);
    };

    return (
        <AffiliateLink
            href={getLink()}
            onClick={(e) => {
                if (onClick) {
                    e.preventDefault();
                    onClick();
                }
            }}
            className={`
                flex items-start gap-3 px-4 py-3
                transition-colors duration-150
                ${isHighlighted
                    ? 'bg-primary/10 text-primary'
                    : 'hover:bg-muted/10'
                }
                ${compact ? 'py-2' : ''}
            `}
        >
            {/* Entity Type Icon */}
            <div className={`
                flex-shrink-0 mt-0.5
                ${item.entityType === 'product'
                    ? 'text-primary'
                    : item.entityType === 'category'
                        ? 'text-success'
                        : 'text-muted'
                }
            `}>
                {getIcon()}
            </div>

            {/* Content */}
            <div className="flex-1 min-w-0">
                {/* Name */}
                <div className="font-medium text-text truncate">
                    {highlightMatch(displayName)}
                </div>

                {/* Meta Info */}
                <div className="flex items-center gap-2 mt-0.5 text-sm text-muted">
                    {/* Type Badge */}
                    <span className={`
                        text-xs px-1.5 py-0.5 rounded
                        ${item.entityType === 'product'
                            ? 'bg-primary/10 text-primary'
                            : item.entityType === 'category'
                                ? 'bg-success/10 text-success'
                                : 'bg-muted/10 text-muted'
                        }
                    `}>
                        {getTypeLabel()}
                    </span>

                    {/* Category (for products) */}
                    {item.entityType === 'product' && item.categoryName && (
                        <span className="truncate">
                            {item.categoryName}
                        </span>
                    )}

                    {/* Description excerpt (for CMS pages) */}
                    {item.entityType === 'cms' && item.description && !compact && (
                        <span className="truncate">
                            {item.description}
                        </span>
                    )}
                </div>
            </div>

            {/* Price (for products) */}
            {item.entityType === 'product' && item.price !== undefined && (
                <div className="flex-shrink-0 text-sm font-semibold text-primary">
                    {formatPrice(item.price)}
                </div>
            )}
        </AffiliateLink>
    );
};
