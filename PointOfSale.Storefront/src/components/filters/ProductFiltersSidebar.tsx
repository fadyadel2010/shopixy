/**
 * ProductFiltersSidebar Component
 * 
 * PHASE: Product Discovery Filters UI
 * 
 * Responsive filters sidebar:
 * - Desktop: Sticky left sidebar
 * - Mobile: Slide-in drawer
 * 
 * ARCHITECTURE:
 * - Renders all filter components
 * - Uses facets from API response
 * - Calls filter actions to update URL
 */

import React, { useState, useEffect, useCallback } from 'react';
import { useLanguageStore, selectLanguage } from '@/i18n/languageStore';
import { useTranslation } from '@/i18n/useTranslation';
import { useBodyScrollLock } from '@/hooks';
import type { ProductDiscoveryFacets, CategoryTreeDto } from '@/types';
import {
    BrandFilter,
    CategoryFilter,
    PriceRangeFilter,
    StockFilter,
    PromotionFilter,
} from './FilterComponents';

interface ProductFiltersSidebarProps {
    /** Facets from discovery API response */
    facets: ProductDiscoveryFacets | null;
    /** Current filter values */
    filters: {
        categories: string[];
        brands: string[];
        priceFrom?: number;
        priceTo?: number;
        hasPromotion?: boolean;
        inStock?: boolean;
    };
    /** Filter action callbacks */
    actions: {
        toggleCategory: (id: string) => void;
        toggleBrand: (brand: string) => void;
        setPriceRange: (from?: number, to?: number) => void;
        setHasPromotion: (value: boolean | undefined) => void;
        setInStock: (value: boolean | undefined) => void;
        clearAllFilters: () => void;
    };
    /** Categories cache for name resolution */
    categories?: CategoryTreeDto[];
    /** Active filter count */
    activeFilterCount: number;
    /** Store currency */
    currency?: string;
    /** Loading state */
    isLoading?: boolean;
}

/**
 * Build a category name lookup map from nested category tree
 */
function buildCategoryNameMap(categories: CategoryTreeDto[]): Map<string, string> {
    const map = new Map<string, string>();

    const traverse = (cats: CategoryTreeDto[]) => {
        for (const cat of cats) {
            map.set(cat.id, cat.name);
            if (cat.children?.length) {
                traverse(cat.children);
            }
        }
    };

    traverse(categories);
    return map;
}

/**
 * Filters sidebar for desktop
 */
const DesktopSidebar: React.FC<ProductFiltersSidebarProps> = ({
    facets,
    filters,
    actions,
    categories = [],
    activeFilterCount,
    currency,
    isLoading,
}) => {
    const { t } = useTranslation();
    const language = useLanguageStore(selectLanguage);
    const isRTL = language === 'ar';

    const categoryNameMap = React.useMemo(
        () => buildCategoryNameMap(categories),
        [categories]
    );

    const getCategoryName = useCallback(
        (id: string) => categoryNameMap.get(id),
        [categoryNameMap]
    );

    if (!facets) {
        return (
            <aside className={`w-64 flex-shrink-0 ${isRTL ? 'ml-6' : 'mr-6'}`}>
                <div className="sticky top-6 bg-surface rounded-xl p-4 shadow-sm animate-pulse">
                    <div className="h-6 bg-muted/20 rounded w-24 mb-4"></div>
                    <div className="space-y-3">
                        {[...Array(5)].map((_, i) => (
                            <div key={i} className="h-4 bg-muted/10 rounded w-full"></div>
                        ))}
                    </div>
                </div>
            </aside>
        );
    }

    return (
        <aside className={`w-64 flex-shrink-0 ${isRTL ? 'ml-6' : 'mr-6'}`}>
            <div className="sticky top-6 bg-surface rounded-xl shadow-sm overflow-hidden">
                {/* Header */}
                <div className="px-4 py-3 border-b border-muted/10 flex items-center justify-between">
                    <h3 className="font-semibold text-text flex items-center gap-2">
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
                        </svg>
                        {t('filters.title')}
                        {activeFilterCount > 0 && (
                            <span className="px-2 py-0.5 text-xs font-medium bg-primary text-white rounded-full">
                                {activeFilterCount}
                            </span>
                        )}
                    </h3>
                    {activeFilterCount > 0 && (
                        <button
                            type="button"
                            onClick={actions.clearAllFilters}
                            className="text-sm text-primary hover:text-primary/80 font-medium"
                        >
                            {t('filters.clearAll')}
                        </button>
                    )}
                </div>

                {/* Filter content */}
                <div className={`px-4 max-h-[calc(100vh-200px)] overflow-y-auto ${isLoading ? 'opacity-60' : ''}`}>
                    {/* Category Filter */}
                    {facets.categories && facets.categories.length > 0 && (
                        <CategoryFilter
                            facets={facets.categories}
                            selectedCategories={filters.categories}
                            onToggleCategory={actions.toggleCategory}
                            getCategoryName={getCategoryName}
                        />
                    )}

                    {/* Brand Filter */}
                    {facets.brands && facets.brands.length > 0 && (
                        <BrandFilter
                            facets={facets.brands}
                            selectedBrands={filters.brands}
                            onToggleBrand={actions.toggleBrand}
                        />
                    )}

                    {/* Price Range Filter */}
                    {facets.priceRanges && facets.priceRanges.length > 0 && (
                        <PriceRangeFilter
                            facets={facets.priceRanges}
                            selectedFrom={filters.priceFrom}
                            selectedTo={filters.priceTo}
                            onSelectRange={actions.setPriceRange}
                            currency={currency}
                        />
                    )}

                    {/* Stock Filter */}
                    {facets.stock && (
                        <StockFilter
                            facet={facets.stock}
                            inStock={filters.inStock}
                            onToggle={actions.setInStock}
                        />
                    )}

                    {/* Promotion Filter */}
                    {facets.promotions && facets.promotions.promotedCount > 0 && (
                        <PromotionFilter
                            facet={facets.promotions}
                            hasPromotion={filters.hasPromotion}
                            onToggle={actions.setHasPromotion}
                        />
                    )}

                    {/* Bottom padding */}
                    <div className="h-4"></div>
                </div>
            </div>
        </aside>
    );
};

/**
 * Filters drawer for mobile
 */
interface MobileDrawerProps extends ProductFiltersSidebarProps {
    isOpen: boolean;
    onClose: () => void;
}

const MobileDrawer: React.FC<MobileDrawerProps> = ({
    isOpen,
    onClose,
    facets,
    filters,
    actions,
    categories = [],
    activeFilterCount,
    currency,
    isLoading,
}) => {
    const { t } = useTranslation();
    const language = useLanguageStore(selectLanguage);
    const isRTL = language === 'ar';

    // Lock body scroll when drawer is open
    useBodyScrollLock(isOpen);

    const categoryNameMap = React.useMemo(
        () => buildCategoryNameMap(categories),
        [categories]
    );

    const getCategoryName = useCallback(
        (id: string) => categoryNameMap.get(id),
        [categoryNameMap]
    );

    // Close on escape key
    useEffect(() => {
        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && isOpen) {
                onClose();
            }
        };
        document.addEventListener('keydown', handleEscape);
        return () => document.removeEventListener('keydown', handleEscape);
    }, [isOpen, onClose]);

    if (!isOpen) return null;

    return (
        <>
            {/* Backdrop */}
            <div
                className="fixed inset-0 bg-black/50 z-40 transition-opacity"
                onClick={onClose}
            />

            {/* Drawer */}
            <div
                className={`fixed inset-y-0 ${isRTL ? 'left-0' : 'right-0'} w-80 max-w-full bg-surface z-50 shadow-xl flex flex-col transform transition-transform duration-300 ${isOpen ? 'translate-x-0' : isRTL ? '-translate-x-full' : 'translate-x-full'
                    }`}
            >
                {/* Header */}
                <div className="px-4 py-4 border-b border-muted/10 flex items-center justify-between">
                    <h3 className="font-semibold text-text flex items-center gap-2">
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
                        </svg>
                        {t('filters.title')}
                        {activeFilterCount > 0 && (
                            <span className="px-2 py-0.5 text-xs font-medium bg-primary text-white rounded-full">
                                {activeFilterCount}
                            </span>
                        )}
                    </h3>
                    <button
                        type="button"
                        onClick={onClose}
                        className="p-2 hover:bg-muted/10 rounded-lg transition-colors"
                        aria-label={t('common.close')}
                    >
                        <svg className="w-5 h-5 text-muted" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>

                {/* Filter content */}
                <div className={`flex-1 overflow-y-auto px-4 ${isLoading ? 'opacity-60' : ''}`}>
                    {facets ? (
                        <>
                            {/* Category Filter */}
                            {facets.categories && facets.categories.length > 0 && (
                                <CategoryFilter
                                    facets={facets.categories}
                                    selectedCategories={filters.categories}
                                    onToggleCategory={actions.toggleCategory}
                                    getCategoryName={getCategoryName}
                                />
                            )}

                            {/* Brand Filter */}
                            {facets.brands && facets.brands.length > 0 && (
                                <BrandFilter
                                    facets={facets.brands}
                                    selectedBrands={filters.brands}
                                    onToggleBrand={actions.toggleBrand}
                                />
                            )}

                            {/* Price Range Filter */}
                            {facets.priceRanges && facets.priceRanges.length > 0 && (
                                <PriceRangeFilter
                                    facets={facets.priceRanges}
                                    selectedFrom={filters.priceFrom}
                                    selectedTo={filters.priceTo}
                                    onSelectRange={actions.setPriceRange}
                                    currency={currency}
                                />
                            )}

                            {/* Stock Filter */}
                            {facets.stock && (
                                <StockFilter
                                    facet={facets.stock}
                                    inStock={filters.inStock}
                                    onToggle={actions.setInStock}
                                />
                            )}

                            {/* Promotion Filter */}
                            {facets.promotions && facets.promotions.promotedCount > 0 && (
                                <PromotionFilter
                                    facet={facets.promotions}
                                    hasPromotion={filters.hasPromotion}
                                    onToggle={actions.setHasPromotion}
                                />
                            )}
                        </>
                    ) : (
                        <div className="py-8 text-center text-muted">
                            {t('common.loading')}
                        </div>
                    )}
                </div>

                {/* Footer with actions */}
                <div className="px-4 py-4 border-t border-muted/10 flex items-center gap-3">
                    {activeFilterCount > 0 && (
                        <button
                            type="button"
                            onClick={actions.clearAllFilters}
                            className="flex-1 px-4 py-2.5 border border-muted/20 text-text rounded-lg font-medium hover:bg-muted/10 transition-colors"
                        >
                            {t('filters.clearAll')}
                        </button>
                    )}
                    <button
                        type="button"
                        onClick={onClose}
                        className="flex-1 px-4 py-2.5 bg-primary text-white rounded-lg font-medium hover:bg-primary/90 transition-colors"
                    >
                        {t('filters.apply')}
                    </button>
                </div>
            </div>
        </>
    );
};

/**
 * Mobile filter button
 */
interface FilterButtonProps {
    activeCount: number;
    onClick: () => void;
}

export const MobileFilterButton: React.FC<FilterButtonProps> = ({
    activeCount,
    onClick,
}) => {
    const { t } = useTranslation();

    return (
        <button
            type="button"
            onClick={onClick}
            className="flex items-center gap-2 px-4 py-2.5 border border-muted/20 rounded-lg text-sm font-medium text-text hover:bg-muted/10 transition-colors lg:hidden"
        >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
            </svg>
            {t('filters.title')}
            {activeCount > 0 && (
                <span className="px-2 py-0.5 text-xs font-medium bg-primary text-white rounded-full">
                    {activeCount}
                </span>
            )}
        </button>
    );
};

/**
 * Combined responsive filter sidebar component
 */
export const ProductFiltersSidebar: React.FC<ProductFiltersSidebarProps> = (props) => {
    const [isMobileOpen, setIsMobileOpen] = useState(false);

    return (
        <>
            {/* Desktop Sidebar */}
            <div className="hidden lg:block">
                <DesktopSidebar {...props} />
            </div>

            {/* Mobile Drawer */}
            <MobileDrawer
                {...props}
                isOpen={isMobileOpen}
                onClose={() => setIsMobileOpen(false)}
            />
        </>
    );
};

// Export the mobile button and drawer toggle hook
export const useFilterDrawer = () => {
    const [isOpen, setIsOpen] = useState(false);
    return {
        isOpen,
        open: () => setIsOpen(true),
        close: () => setIsOpen(false),
        toggle: () => setIsOpen(prev => !prev),
    };
};

export { MobileDrawer };
