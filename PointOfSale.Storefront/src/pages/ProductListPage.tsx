/**
 * Product List Page
 * 
 * PHASE: Product Discovery Filters UI
 * 
 * Full-featured product listing with:
 * - URL-driven filter state (single source of truth)
 * - Elasticsearch-powered search, filtering, sorting, faceting
 * - Responsive filters (desktop sidebar + mobile drawer)
 * - Category promotion banners
 * - Keyboard shortcuts
 * 
 * ARCHITECTURE:
 * - All business logic in Elasticsearch
 * - Frontend only renders and passes query params
 * - No frontend filtering, sorting, or price calculations
 */

import React, { useCallback, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { ProductCard } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import {
    SkeletonGrid,
    DataEmptyState,
    InlineError,
    CategoryNav,
    SearchInput,
    Pagination,
    SearchEmptyState,
} from '@/components/data';
import { useProductListDiscovery, useCategories } from '@/hooks';
import { useStoreConfigStore, selectConfig } from '@/store';
import { scrollToTop } from '@/utils';
import type { SearchInputHandle } from '@/components/data/SearchInput';
import { SectionRenderer } from '@/components/sections/SectionRenderer';
import { CategoryPromotionBanner } from '@/components/products/CategoryPromotionBanner';
import {
    ProductFiltersSidebar,
    MobileFilterButton,
    MobileDrawer,
    SortSelector,
    ActiveFiltersBar,
} from '@/components/filters';
import { useTranslation } from '@/i18n/useTranslation';
import { useLanguageStore, selectLanguage } from '@/i18n/languageStore';

// ============================================================================
// Constants
// ============================================================================

const DEFAULT_PAGE_SIZE = 20;

// ============================================================================
// Component
// ============================================================================

/**
 * Product List Page Component
 */
export const ProductListPage: React.FC = () => {
    const config = useStoreConfigStore(selectConfig);
    const layout = config.theme.layout;
    const searchInputRef = useRef<SearchInputHandle>(null);
    const { t } = useTranslation();
    const language = useLanguageStore(selectLanguage);
    const isRTL = language === 'ar';

    // Mobile filter drawer state
    const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false);

    // Get product page sections from config
    const productSections = config.pages?.product?.sections || [];

    // ========================================================================
    // Route params - get category from /category/:slug
    // ========================================================================

    const { slug: routeCategory } = useParams<{ slug?: string }>();

    // ========================================================================
    // Data Hook - All discovery logic
    // ========================================================================

    const {
        // Products
        products,
        totalCount,
        isLoading,
        error,

        // Pagination
        page,
        pageSize,
        totalPages,

        // Facets
        facets,

        // Search
        searchQuery,

        // Filters
        filters,

        // Sort
        sort,

        // Filter counts
        hasActiveFilters,
        activeFilterCount,

        // Actions
        setSearch,
        toggleCategory,
        toggleBrand,
        setPriceRange,
        setHasPromotion,
        setInStock,
        setSort,
        setPage,
        clearAllFilters,
        refetch,

        // Categories
        getCategoryName,
        categoriesLoading,
    } = useProductListDiscovery({ routeCategory });

    // Categories for navigation
    const { categories } = useCategories();

    // ========================================================================
    // Computed State
    // ========================================================================

    // Grid class based on variant
    const gridClass = useMemo(() => {
        switch (layout.productCardVariant) {
            case 'compact':
                return 'grid grid-cols-1 md:grid-cols-2 gap-3';
            case 'detailed':
            case 'default':
            default:
                return 'grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-4 md:gap-6';
        }
    }, [layout.productCardVariant]);

    // ========================================================================
    // Handlers
    // ========================================================================

    const handlePageChange = useCallback((newPage: number) => {
        setPage(newPage);
        scrollToTop();
    }, [setPage]);

    const handleCategoryNavChange = useCallback((categoryId: string | undefined) => {
        if (categoryId) {
            // If selecting a category from nav, clear existing category filters and set single
            // This maintains backward compatibility with CategoryNav behavior
            clearAllFilters();
            toggleCategory(categoryId);
        } else {
            clearAllFilters();
        }
        scrollToTop();
    }, [toggleCategory, clearAllFilters]);

    const handleSearchChange = useCallback((newQuery: string) => {
        setSearch(newQuery || undefined);
    }, [setSearch]);

    const handleSearchClear = useCallback(() => {
        setSearch(undefined);
    }, [setSearch]);

    // ========================================================================
    // Recursive Category Lookup (for promotion banners)
    // ========================================================================

    const findCategoryInTree = useCallback((
        cats: typeof categories,
        idOrSlug: string
    ): typeof categories[0] | null => {
        for (const cat of cats) {
            if (cat.id === idOrSlug || cat.slug === idOrSlug) {
                return cat;
            }
            if (cat.children && cat.children.length > 0) {
                const found = findCategoryInTree(cat.children, idOrSlug);
                if (found) return found;
            }
        }
        return null;
    }, []);

    // Get current category for promotion banner
    const currentCategoryId = filters.categories[0];
    const currentCategory = useMemo(() => {
        if (!currentCategoryId) return null;
        return findCategoryInTree(categories, currentCategoryId);
    }, [currentCategoryId, categories, findCategoryInTree]);

    // Find child category with promotion
    const childWithPromotion = useMemo(() => {
        if (!currentCategory?.children || currentCategory.children.length === 0) {
            return null;
        }

        const findChildWithPromotion = (cats: typeof categories): typeof categories[0] | null => {
            for (const cat of cats) {
                if (cat.hasActivePromotion) return cat;
                if (cat.children?.length) {
                    const found = findChildWithPromotion(cat.children);
                    if (found) return found;
                }
            }
            return null;
        };

        return findChildWithPromotion(currentCategory.children);
    }, [currentCategory, categories]);

    // ========================================================================
    // Render Helpers
    // ========================================================================

    const renderContent = () => {
        // Loading state
        if (isLoading && products.length === 0) {
            return <SkeletonGrid count={DEFAULT_PAGE_SIZE} />;
        }

        // Error state
        if (error && products.length === 0) {
            return (
                <InlineError
                    message={error}
                    onRetry={refetch}
                />
            );
        }

        // Empty state - search
        if (products.length === 0 && searchQuery) {
            return (
                <SearchEmptyState
                    query={searchQuery}
                    onClear={handleSearchClear}
                />
            );
        }

        // Empty state - filtered
        if (products.length === 0 && hasActiveFilters) {
            return (
                <DataEmptyState
                    variant="category"
                    title={t('filters.noResults') || 'No products found'}
                    message={t('filters.tryDifferent') || 'Try adjusting your filters or clearing them.'}
                    action={{
                        label: t('filters.clearAll'),
                        onClick: clearAllFilters,
                    }}
                />
            );
        }

        // Empty state - default
        if (products.length === 0) {
            return (
                <DataEmptyState
                    variant="default"
                    title={t('home.noProductsYet')}
                    message={t('home.productsWillAppear')}
                />
            );
        }

        // Success state - products grid
        return (
            <>
                {/* Loading overlay for subsequent fetches */}
                <div className={`relative ${isLoading ? 'opacity-70' : ''} transition-opacity duration-200`}>
                    <div className={gridClass}>
                        {products.map((product) => (
                            <ProductCard
                                key={product.id}
                                product={product}
                            />
                        ))}
                    </div>
                </div>

                {/* Pagination */}
                {totalPages > 1 && (
                    <div className="mt-10 pt-8 border-t border-muted/20">
                        <Pagination
                            currentPage={page}
                            totalPages={totalPages}
                            totalCount={totalCount}
                            pageSize={pageSize}
                            onPageChange={handlePageChange}
                        />
                    </div>
                )}
            </>
        );
    };

    // ========================================================================
    // Render
    // ========================================================================

    return (
        <Layout categories={categories}>
            {productSections.length > 0 ? (
                <div className="product-list-page">
                    {productSections
                        .filter(s => s.enabled)
                        .sort((a, b) => a.order - b.order)
                        .map((section) => (
                            <SectionRenderer
                                key={section.id}
                                section={section}
                            />
                        ))}
                </div>
            ) : (
                <div className="container mx-auto px-4 py-6 md:py-10">
                    {/* Page Header */}
                    <div className="mb-6">
                        {/* Breadcrumb */}
                        <nav
                            className={`flex items-center gap-2 text-sm mb-4 ${isRTL ? 'flex-row-reverse' : ''}`}
                            aria-label="Breadcrumb"
                        >
                            <AffiliateLink href="/" className="text-muted hover:text-primary transition-colors">
                                {t('common.home')}
                            </AffiliateLink>
                            <svg className={`w-4 h-4 text-muted/50 ${isRTL ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                            </svg>
                            {currentCategory ? (
                                <>
                                    <AffiliateLink
                                        href="/products"
                                        className="text-muted hover:text-primary transition-colors"
                                        onClick={(e) => {
                                            e.preventDefault();
                                            handleCategoryNavChange(undefined);
                                        }}
                                    >
                                        {t('common.products')}
                                    </AffiliateLink>
                                    <svg className={`w-4 h-4 text-muted/50 ${isRTL ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                                    </svg>
                                    <span className="text-text font-medium">{currentCategory.name}</span>
                                </>
                            ) : (
                                <span className="text-text font-medium">{t('common.products')}</span>
                            )}
                        </nav>

                        {/* Title and Controls */}
                        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
                            <div>
                                <h1 className="text-2xl md:text-3xl font-bold text-text">
                                    {currentCategory ? currentCategory.name : t('header.allProducts')}
                                </h1>
                                {!isLoading && (
                                    <p className="text-muted text-sm mt-1">
                                        {t('search.results', { count: totalCount })}
                                    </p>
                                )}
                            </div>

                            {/* Top Controls: Search, Sort, Mobile Filter Button */}
                            <div className={`flex items-center gap-3 flex-wrap ${isRTL ? 'flex-row-reverse' : ''}`}>
                                {/* Search Input */}
                                <div className="w-full sm:w-64 lg:w-72">
                                    <SearchInput
                                        ref={searchInputRef}
                                        value={searchQuery || ''}
                                        onChange={handleSearchChange}
                                        onClear={handleSearchClear}
                                        status={isLoading ? 'searching' : 'idle'}
                                        placeholder={t('header.searchProducts')}
                                        id="product-search"
                                    />
                                </div>

                                {/* Sort Selector */}
                                <SortSelector
                                    value={sort}
                                    onChange={setSort}
                                />

                                {/* Mobile Filter Button */}
                                <MobileFilterButton
                                    activeCount={activeFilterCount}
                                    onClick={() => setIsFilterDrawerOpen(true)}
                                />
                            </div>
                        </div>
                    </div>

                    {/* Category Navigation */}
                    <div className={`transition-opacity duration-200 ${hasActiveFilters ? 'opacity-50' : ''}`}>
                        <CategoryNav
                            categories={categories}
                            selectedId={currentCategoryId}
                            onSelect={handleCategoryNavChange}
                            isLoading={categoriesLoading}
                            className="mb-6"
                        />
                    </div>

                    {/* Category Promotion Banner */}
                    {currentCategory?.hasActivePromotion && (
                        <CategoryPromotionBanner
                            hasActivePromotion={currentCategory.hasActivePromotion}
                            bannerText={currentCategory.promotionBannerText}
                            endDate={currentCategory.promotionEndDate}
                            categoryName={currentCategory.name}
                            bannerStyle={currentCategory.promotionBannerStyle}
                        />
                    )}

                    {/* Child Category Promotion Banner (cascade) */}
                    {!currentCategory?.hasActivePromotion && childWithPromotion && (
                        <div className="mb-6 bg-gradient-to-r from-amber-500 to-orange-500 rounded-xl p-4 shadow-lg">
                            <div className={`flex items-center justify-between flex-wrap gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <div className={`flex items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                    <div className="w-10 h-10 bg-white/20 rounded-full flex items-center justify-center">
                                        <svg className="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                                        </svg>
                                    </div>
                                    <div className="text-white">
                                        <p className="font-bold text-lg">{childWithPromotion.promotionBannerText}</p>
                                        <p className="text-sm text-white/90">
                                            {t('promotion.availableIn')} <strong>{childWithPromotion.name}</strong>
                                        </p>
                                    </div>
                                </div>
                                <AffiliateLink
                                    href={`/products?category=${childWithPromotion.slug || childWithPromotion.id}`}
                                    className="px-4 py-2 bg-white/20 hover:bg-white/30 text-white font-medium rounded-lg transition-colors flex items-center gap-2"
                                >
                                    {t('promotion.viewCategory', { category: childWithPromotion.name })}
                                    <svg className={`w-4 h-4 ${isRTL ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                                    </svg>
                                </AffiliateLink>
                            </div>
                        </div>
                    )}

                    {/* Active Filters Bar */}
                    {hasActiveFilters && (
                        <div className="mb-6">
                            <ActiveFiltersBar
                                filters={{
                                    q: searchQuery,
                                    ...filters,
                                }}
                                getCategoryName={getCategoryName}
                                onRemoveCategory={toggleCategory}
                                onRemoveBrand={toggleBrand}
                                onRemovePrice={() => setPriceRange(undefined, undefined)}
                                onRemovePromotion={() => setHasPromotion(undefined)}
                                onRemoveStock={() => setInStock(undefined)}
                                onRemoveSearch={handleSearchClear}
                                onClearAll={clearAllFilters}
                                currency={config.currency}
                            />
                        </div>
                    )}

                    {/* Main Content with Sidebar */}
                    <div className={`flex gap-6 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        {/* Desktop Filters Sidebar */}
                        <div className="hidden lg:block">
                            <ProductFiltersSidebar
                                facets={facets}
                                filters={filters}
                                actions={{
                                    toggleCategory,
                                    toggleBrand,
                                    setPriceRange,
                                    setHasPromotion,
                                    setInStock,
                                    clearAllFilters,
                                }}
                                categories={categories}
                                activeFilterCount={activeFilterCount}
                                currency={config.currency}
                                isLoading={isLoading}
                            />
                        </div>

                        {/* Products Grid */}
                        <div className="flex-1 min-w-0">
                            {renderContent()}
                        </div>
                    </div>

                    {/* Mobile Filter Drawer */}
                    <MobileDrawer
                        isOpen={isFilterDrawerOpen}
                        onClose={() => setIsFilterDrawerOpen(false)}
                        facets={facets}
                        filters={filters}
                        actions={{
                            toggleCategory,
                            toggleBrand,
                            setPriceRange,
                            setHasPromotion,
                            setInStock,
                            clearAllFilters,
                        }}
                        categories={categories}
                        activeFilterCount={activeFilterCount}
                        currency={config.currency}
                        isLoading={isLoading}
                    />
                </div>
            )}
        </Layout>
    );
};
