/**
 * Product Discovery Filter Components
 * 
 * PHASE: Product Discovery Filters UI
 * 
 * All filter components for the product listing page.
 * These components ONLY render UI - all business logic is in Elasticsearch.
 * 
 * ARCHITECTURE:
 * - Renders facets from API response
 * - Calls filter actions to update URL
 * - Never performs filtering locally
 */

import React, { useMemo, useState } from 'react';
import { useLanguageStore, selectLanguage } from '@/i18n/languageStore';
import { useTranslation } from '@/i18n/useTranslation';
import type {
    FacetBucket,
    CategoryFacetBucket,
    PriceRangeBucket,
    PromotionFacet,
    StockFacet,
    ProductSortMode,
} from '@/types';

// ============================================================================
// Common Components
// ============================================================================

interface FilterSectionProps {
    title: string;
    children: React.ReactNode;
    isCollapsible?: boolean;
    defaultOpen?: boolean;
    count?: number;
}

/**
 * Collapsible filter section wrapper
 */
export const FilterSection: React.FC<FilterSectionProps> = ({
    title,
    children,
    isCollapsible = true,
    defaultOpen = true,
    count,
}) => {
    const [isOpen, setIsOpen] = useState(defaultOpen);

    return (
        <div className="border-b border-muted/10 py-4 last:border-b-0">
            <button
                type="button"
                className="flex w-full items-center justify-between text-left"
                onClick={() => isCollapsible && setIsOpen(!isOpen)}
                disabled={!isCollapsible}
            >
                <span className="text-sm font-semibold text-text flex items-center gap-2">
                    {title}
                    {count !== undefined && count > 0 && (
                        <span className="px-1.5 py-0.5 text-xs font-medium bg-primary/10 text-primary rounded-full">
                            {count}
                        </span>
                    )}
                </span>
                {isCollapsible && (
                    <svg
                        className={`w-4 h-4 text-muted transition-transform ${isOpen ? 'rotate-180' : ''}`}
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                    </svg>
                )}
            </button>
            {isOpen && (
                <div className="mt-3 space-y-2">
                    {children}
                </div>
            )}
        </div>
    );
};

// ============================================================================
// Brand Filter
// ============================================================================

interface BrandFilterProps {
    facets: FacetBucket[];
    selectedBrands: string[];
    onToggleBrand: (brand: string) => void;
}

/**
 * Brand filter with checkboxes
 */
export const BrandFilter: React.FC<BrandFilterProps> = ({
    facets,
    selectedBrands,
    onToggleBrand,
}) => {
    const { t } = useTranslation();
    const [showAll, setShowAll] = useState(false);
    const maxVisible = 5;

    const visibleFacets = showAll ? facets : facets.slice(0, maxVisible);
    const hasMore = facets.length > maxVisible;

    if (facets.length === 0) {
        return null;
    }

    return (
        <FilterSection title={t('filters.brand')} count={selectedBrands.length}>
            <div className="space-y-2 max-h-48 overflow-y-auto">
                {visibleFacets.map((facet) => {
                    const isSelected = selectedBrands.includes(facet.key);
                    const isDisabled = facet.count === 0 && !isSelected;

                    return (
                        <label
                            key={facet.key}
                            className={`flex items-center gap-3 cursor-pointer group ${isDisabled ? 'opacity-50 cursor-not-allowed' : ''}`}
                        >
                            <input
                                type="checkbox"
                                checked={isSelected}
                                onChange={() => !isDisabled && onToggleBrand(facet.key)}
                                disabled={isDisabled}
                                className="w-4 h-4 rounded border-muted/30 text-primary focus:ring-primary/20"
                            />
                            <span className={`text-sm flex-1 ${isSelected ? 'text-text font-medium' : 'text-muted group-hover:text-text'}`}>
                                {facet.key}
                            </span>
                            <span className="text-xs text-muted/70 tabular-nums">
                                ({facet.count})
                            </span>
                        </label>
                    );
                })}
            </div>
            {hasMore && (
                <button
                    type="button"
                    onClick={() => setShowAll(!showAll)}
                    className="mt-2 text-sm text-primary hover:text-primary/80 font-medium"
                >
                    {showAll ? t('filters.showLess') : t('filters.showMore', { count: facets.length })}
                </button>
            )}
        </FilterSection>
    );
};

// ============================================================================
// Category Filter
// ============================================================================

interface CategoryFilterProps {
    facets: CategoryFacetBucket[];
    selectedCategories: string[];
    onToggleCategory: (categoryId: string) => void;
    /** Category name resolver (from useCategories cache) */
    getCategoryName?: (id: string) => string | undefined;
}

/**
 * Category filter with checkboxes
 */
export const CategoryFilter: React.FC<CategoryFilterProps> = ({
    facets,
    selectedCategories,
    onToggleCategory,
    getCategoryName,
}) => {
    const { t } = useTranslation();
    const [showAll, setShowAll] = useState(false);
    const maxVisible = 5;

    const visibleFacets = showAll ? facets : facets.slice(0, maxVisible);
    const hasMore = facets.length > maxVisible;

    if (facets.length === 0) {
        return null;
    }

    return (
        <FilterSection title={t('filters.category')} count={selectedCategories.length}>
            <div className="space-y-2 max-h-48 overflow-y-auto">
                {visibleFacets.map((facet) => {
                    const isSelected = selectedCategories.includes(facet.id);
                    const isDisabled = facet.count === 0 && !isSelected;
                    const displayName = getCategoryName?.(facet.id) || facet.name || facet.id;

                    return (
                        <label
                            key={facet.id}
                            className={`flex items-center gap-3 cursor-pointer group ${isDisabled ? 'opacity-50 cursor-not-allowed' : ''}`}
                        >
                            <input
                                type="checkbox"
                                checked={isSelected}
                                onChange={() => !isDisabled && onToggleCategory(facet.id)}
                                disabled={isDisabled}
                                className="w-4 h-4 rounded border-muted/30 text-primary focus:ring-primary/20"
                            />
                            <span className={`text-sm flex-1 ${isSelected ? 'text-text font-medium' : 'text-muted group-hover:text-text'}`}>
                                {displayName}
                            </span>
                            <span className="text-xs text-muted/70 tabular-nums">
                                ({facet.count})
                            </span>
                        </label>
                    );
                })}
            </div>
            {hasMore && (
                <button
                    type="button"
                    onClick={() => setShowAll(!showAll)}
                    className="mt-2 text-sm text-primary hover:text-primary/80 font-medium"
                >
                    {showAll ? t('filters.showLess') : t('filters.showMore', { count: facets.length })}
                </button>
            )}
        </FilterSection>
    );
};

// ============================================================================
// Price Range Filter
// ============================================================================

interface PriceRangeFilterProps {
    facets: PriceRangeBucket[];
    selectedFrom?: number;
    selectedTo?: number;
    onSelectRange: (from?: number, to?: number) => void;
    currency?: string;
}

/**
 * Price range filter with radio buttons
 */
export const PriceRangeFilter: React.FC<PriceRangeFilterProps> = ({
    facets,
    selectedFrom,
    selectedTo,
    onSelectRange,
    currency = '',
}) => {
    const { t } = useTranslation();

    if (facets.length === 0) {
        return null;
    }

    const isRangeSelected = (from?: number, to?: number) => {
        return selectedFrom === from && selectedTo === to;
    };

    const hasSelection = selectedFrom !== undefined || selectedTo !== undefined;

    return (
        <FilterSection
            title={t('filters.priceRange')}
            count={hasSelection ? 1 : 0}
        >
            <div className="space-y-2">
                {/* All prices option */}
                <label className="flex items-center gap-3 cursor-pointer group">
                    <input
                        type="radio"
                        name="priceRange"
                        checked={!hasSelection}
                        onChange={() => onSelectRange(undefined, undefined)}
                        className="w-4 h-4 border-muted/30 text-primary focus:ring-primary/20"
                    />
                    <span className={`text-sm ${!hasSelection ? 'text-text font-medium' : 'text-muted group-hover:text-text'}`}>
                        {t('filters.allPrices')}
                    </span>
                </label>

                {facets.map((facet) => {
                    const isSelected = isRangeSelected(
                        facet.from ?? undefined,
                        facet.to ?? undefined
                    );
                    const isDisabled = facet.count === 0 && !isSelected;

                    return (
                        <label
                            key={facet.label}
                            className={`flex items-center gap-3 cursor-pointer group ${isDisabled ? 'opacity-50 cursor-not-allowed' : ''}`}
                        >
                            <input
                                type="radio"
                                name="priceRange"
                                checked={isSelected}
                                onChange={() => !isDisabled && onSelectRange(
                                    facet.from ?? undefined,
                                    facet.to ?? undefined
                                )}
                                disabled={isDisabled}
                                className="w-4 h-4 border-muted/30 text-primary focus:ring-primary/20"
                            />
                            <span className={`text-sm flex-1 ${isSelected ? 'text-text font-medium' : 'text-muted group-hover:text-text'}`}>
                                {facet.label} {currency}
                            </span>
                            <span className="text-xs text-muted/70 tabular-nums">
                                ({facet.count})
                            </span>
                        </label>
                    );
                })}
            </div>
        </FilterSection>
    );
};

// ============================================================================
// Stock Filter
// ============================================================================

interface StockFilterProps {
    facet: StockFacet;
    inStock?: boolean;
    onToggle: (value: boolean | undefined) => void;
}

/**
 * Stock availability toggle
 */
export const StockFilter: React.FC<StockFilterProps> = ({
    facet,
    inStock,
    onToggle,
}) => {
    const { t } = useTranslation();

    return (
        <FilterSection title={t('filters.availability')} isCollapsible={false}>
            <label className="flex items-center gap-3 cursor-pointer group">
                <input
                    type="checkbox"
                    checked={inStock === true}
                    onChange={() => onToggle(inStock ? undefined : true)}
                    className="w-4 h-4 rounded border-muted/30 text-primary focus:ring-primary/20"
                />
                <span className={`text-sm flex-1 ${inStock ? 'text-text font-medium' : 'text-muted group-hover:text-text'}`}>
                    {t('filters.inStockOnly')}
                </span>
                <span className="text-xs text-muted/70 tabular-nums">
                    ({facet.inStockCount})
                </span>
            </label>
        </FilterSection>
    );
};

// ============================================================================
// Promotion Filter
// ============================================================================

interface PromotionFilterProps {
    facet: PromotionFacet;
    hasPromotion?: boolean;
    onToggle: (value: boolean | undefined) => void;
}

/**
 * On sale toggle
 */
export const PromotionFilter: React.FC<PromotionFilterProps> = ({
    facet,
    hasPromotion,
    onToggle,
}) => {
    const { t } = useTranslation();

    return (
        <FilterSection title={t('filters.offers')} isCollapsible={false}>
            <label className="flex items-center gap-3 cursor-pointer group">
                <input
                    type="checkbox"
                    checked={hasPromotion === true}
                    onChange={() => onToggle(hasPromotion ? undefined : true)}
                    className="w-4 h-4 rounded border-muted/30 text-primary focus:ring-primary/20"
                />
                <span className={`text-sm flex-1 ${hasPromotion ? 'text-text font-medium' : 'text-muted group-hover:text-text'}`}>
                    {t('filters.onSaleOnly')}
                </span>
                <span className="text-xs text-muted/70 tabular-nums">
                    ({facet.promotedCount})
                </span>
            </label>
        </FilterSection>
    );
};

// ============================================================================
// Active Filters Bar
// ============================================================================

interface ActiveFiltersBarProps {
    filters: {
        q?: string;
        categories: string[];
        brands: string[];
        priceFrom?: number;
        priceTo?: number;
        hasPromotion?: boolean;
        inStock?: boolean;
    };
    getCategoryName?: (id: string) => string | undefined;
    onRemoveCategory: (id: string) => void;
    onRemoveBrand: (brand: string) => void;
    onRemovePrice: () => void;
    onRemovePromotion: () => void;
    onRemoveStock: () => void;
    onRemoveSearch: () => void;
    onClearAll: () => void;
    currency?: string;
}

/**
 * Active filters display with chips
 */
export const ActiveFiltersBar: React.FC<ActiveFiltersBarProps> = ({
    filters,
    getCategoryName,
    onRemoveCategory,
    onRemoveBrand,
    onRemovePrice,
    onRemovePromotion,
    onRemoveStock,
    onRemoveSearch,
    onClearAll,
    currency = '',
}) => {
    const { t } = useTranslation();
    const language = useLanguageStore(selectLanguage);
    const isRTL = language === 'ar';

    const chips = useMemo(() => {
        const result: { key: string; label: string; onRemove: () => void }[] = [];

        if (filters.q) {
            result.push({
                key: 'search',
                label: `"${filters.q}"`,
                onRemove: onRemoveSearch,
            });
        }

        filters.categories.forEach(id => {
            result.push({
                key: `cat-${id}`,
                label: getCategoryName?.(id) || id,
                onRemove: () => onRemoveCategory(id),
            });
        });

        filters.brands.forEach(brand => {
            result.push({
                key: `brand-${brand}`,
                label: brand,
                onRemove: () => onRemoveBrand(brand),
            });
        });

        if (filters.priceFrom !== undefined || filters.priceTo !== undefined) {
            const from = filters.priceFrom ?? 0;
            const to = filters.priceTo ? `${filters.priceTo}` : '∞';
            result.push({
                key: 'price',
                label: `${from} - ${to} ${currency}`,
                onRemove: onRemovePrice,
            });
        }

        if (filters.hasPromotion) {
            result.push({
                key: 'promotion',
                label: t('filters.onSaleOnly'),
                onRemove: onRemovePromotion,
            });
        }

        if (filters.inStock) {
            result.push({
                key: 'stock',
                label: t('filters.inStockOnly'),
                onRemove: onRemoveStock,
            });
        }

        return result;
    }, [filters, getCategoryName, onRemoveCategory, onRemoveBrand, onRemovePrice, onRemovePromotion, onRemoveStock, onRemoveSearch, t, currency]);

    if (chips.length === 0) {
        return null;
    }

    return (
        <div className={`flex flex-wrap items-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
            {chips.map(chip => (
                <span
                    key={chip.key}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 bg-primary/10 text-primary text-sm rounded-full"
                >
                    {chip.label}
                    <button
                        type="button"
                        onClick={chip.onRemove}
                        className="hover:bg-primary/20 rounded-full p-0.5 transition-colors"
                        aria-label={t('filters.removeFilter')}
                    >
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </span>
            ))}
            <button
                type="button"
                onClick={onClearAll}
                className="text-sm text-muted hover:text-text font-medium"
            >
                {t('filters.clearAll')}
            </button>
        </div>
    );
};

// ============================================================================
// Sort Selector
// ============================================================================

interface SortSelectorProps {
    value: ProductSortMode;
    onChange: (value: ProductSortMode) => void;
}

/**
 * Sort mode selector dropdown
 */
export const SortSelector: React.FC<SortSelectorProps> = ({
    value,
    onChange,
}) => {
    const { t } = useTranslation();
    const language = useLanguageStore(selectLanguage);
    const isRTL = language === 'ar';

    const options = [
        { value: 'relevance', label: t('sort.relevance') },
        { value: 'price_asc', label: t('sort.priceLowToHigh') },
        { value: 'price_desc', label: t('sort.priceHighToLow') },
        { value: 'newest', label: t('sort.newest') },
        { value: 'highest_discount', label: t('sort.highestDiscount') },
    ];

    return (
        <div className={`flex items-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
            <label htmlFor="sort-select" className="text-sm text-muted">
                {t('sort.sortBy')}:
            </label>
            <select
                id="sort-select"
                value={value}
                onChange={(e) => onChange(e.target.value as ProductSortMode)}
                className="text-sm border border-muted/20 rounded-lg px-3 py-2 bg-background text-text focus:ring-2 focus:ring-primary/20 focus:border-primary"
            >
                {options.map(option => (
                    <option key={option.value} value={option.value}>
                        {option.label}
                    </option>
                ))}
            </select>
        </div>
    );
};
