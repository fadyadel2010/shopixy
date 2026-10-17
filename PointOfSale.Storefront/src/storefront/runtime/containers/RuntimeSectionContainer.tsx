import { useMemo, useState } from 'react';
import type { ComponentType } from 'react';
import { useAnalytics } from '@/analytics';
import { ProductCard } from '@/components/common';
import {
    normalizeHomepageCollectionLimit,
    normalizeHomepageGuidList,
    resolveHomepageCollectionSource,
    selectHomepageCollectionItems,
} from '@/components/sections/homepageCollectionUtils';
import { useCategories, useCurrency, useHomepageSectionProducts, useProducts, useVariantSelector } from '@/hooks';
import { useLocalizedContent, useTranslation } from '@/i18n';
import { useCartStore } from '@/store/cartStore';
import { useToast } from '@/store/toastStore';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import { useTheme } from '@/storefront/themes/ThemeContext';
import type { CategoryTreeDto } from '@/types';
import { parseProductNarrative } from '@/utils';
import type {
    CategoryGridPresentationModel,
    ProductHeroPresentationModel,
    ProductGridPresentationModel,
    ProductPresentationModel,
} from '@/storefront/presentation/models';

const GUID_PATTERN =
    /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const PRODUCT_CATALOG_HREF = '/products';

interface RuntimeSectionContainerProps {
    component: ComponentType<any>;
    sectionType: string;
    themeId: string;
    sectionProps: Record<string, unknown>;
}

function flattenCategories(categories: CategoryTreeDto[]): CategoryTreeDto[] {
    return categories.flatMap((category) => [category, ...flattenCategories(category.children || [])]);
}

function isProductSection(sectionType: string): boolean {
    const normalizedType = sectionType.trim().toLowerCase();
    return normalizedType === 'product-grid' || normalizedType === 'featured-products';
}

function isRelatedProductSection(sectionType: string): boolean {
    return sectionType.trim().toLowerCase() === 'related-products';
}

function isCategorySection(sectionType: string): boolean {
    const normalizedType = sectionType.trim().toLowerCase();
    return normalizedType === 'category-grid';
}

function isSubcategorySection(sectionType: string): boolean {
    return sectionType.trim().toLowerCase() === 'subcategory-grid';
}

function isProductHeroSection(sectionType: string): boolean {
    return sectionType.trim().toLowerCase() === 'product-hero';
}

function isProductGallerySection(sectionType: string): boolean {
    return sectionType.trim().toLowerCase() === 'product-gallery';
}

function isProductSpecsSection(sectionType: string): boolean {
    return sectionType.trim().toLowerCase() === 'product-specs';
}

function isProductVideoSection(sectionType: string): boolean {
    return sectionType.trim().toLowerCase() === 'product-video';
}

function normalizePath(value: unknown): string | null {
    if (typeof value !== 'string') {
        return null;
    }

    const trimmed = value.trim();
    if (!trimmed) {
        return null;
    }

    try {
        const pathname = new URL(trimmed, 'https://storefront.local').pathname;
        return pathname.replace(/\/+$/, '') || '/';
    } catch {
        return trimmed.split(/[?#]/)[0].replace(/\/+$/, '') || '/';
    }
}

function shouldSuppressActionHref(sectionProps: Record<string, unknown>, href: string): boolean {
    const suppressedHref = normalizePath(sectionProps.suppressActionHref);
    const actionHref = normalizePath(href);
    return suppressedHref !== null && actionHref !== null && suppressedHref === actionHref;
}

function ProductGridRuntimeContainer({
    component: Component,
    sectionType,
    sectionProps,
}: RuntimeSectionContainerProps) {
    const { t } = useTranslation();
    const title = typeof sectionProps.title === 'string' ? sectionProps.title : undefined;
    const subtitle = typeof sectionProps.subtitle === 'string' ? sectionProps.subtitle : undefined;
    const normalizedSectionType = sectionType.trim().toLowerCase();
    const isFeaturedProductsSection = normalizedSectionType === 'featured-products';
    const displayStyle = isFeaturedProductsSection && typeof sectionProps.displayStyle === 'string'
        ? sectionProps.displayStyle
        : undefined;
    if (isFeaturedProductsSection && displayStyle !== 'carousel' && displayStyle !== 'grid') {
        throw new Error('[RuntimeSectionContainer] featured-products.displayStyle is required by the section contract.');
    }

    const layout = isFeaturedProductsSection
        ? displayStyle!
        : sectionProps.layout === 'carousel' ? 'carousel' : 'grid';
    const pageSize = normalizeHomepageCollectionLimit(sectionProps.maxItems, 8, 24);
    const productIds = normalizeHomepageGuidList(sectionProps.productIds);
    const collectionSource = resolveHomepageCollectionSource(sectionProps.source, sectionProps.productIds);
    const validRequestedIds = productIds?.filter((productId) => GUID_PATTERN.test(productId)) ?? [];
    const isCuratedConfig = collectionSource === 'curated';
    const isCuratedSelectionInvalid =
        isCuratedConfig &&
        (productIds?.length ?? 0) > 0 &&
        validRequestedIds.length === 0;
    const seedKey = `${String(sectionProps.sectionId ?? title ?? 'products')}:${layout}`;
    const { products, isLoading, error } = useHomepageSectionProducts({
        productIds,
        pageSize,
        source: collectionSource,
        seedKey,
    });
    const displayTitle = title || (isFeaturedProductsSection
        ? t('home.featuredProducts')
        : t('common.products'));
    const productLookup = useMemo(
        () => new Map(products.map((product) => [product.id, product])),
        [products]
    );
    const productModels = useMemo<ProductPresentationModel[]>(
        () => products.map((product) => ({ id: product.id })),
        [products]
    );
    const emptyStateTitle = isCuratedSelectionInvalid
        ? t('home.invalidProductSelection')
        : isCuratedConfig
          ? t('home.noSelectedProductsYet')
          : t('home.noProductsYet');
    const emptyStateMessage = isCuratedSelectionInvalid
        ? t('home.invalidProductSelectionHelp')
        : isCuratedConfig
          ? t('home.selectProductsToPopulate')
        : collectionSource === 'random'
          ? t('home.randomProductsWillAppear')
          : t('home.productsWillAppear');
    const viewAllHref = shouldSuppressActionHref(sectionProps, PRODUCT_CATALOG_HREF)
        ? null
        : PRODUCT_CATALOG_HREF;
    const productGridModel: ProductGridPresentationModel = {
        title: displayTitle,
        subtitle,
        layout,
        viewAllHref,
        pageSize,
        products: productModels,
        isLoading,
        error,
        isCuratedConfig,
        isCuratedSelectionInvalid,
        emptyStateTitle,
        emptyStateMessage,
        labels: {
            eyebrowProducts: t('common.products'),
            viewAll: t('common.viewAll'),
            failedToLoad: t('home.failedToLoad'),
        },
    };

    const renderProductCard = (productModel: ProductPresentationModel) => {
        const product = productLookup.get(productModel.id);
        if (!product) {
            return null;
        }

        return <ProductCard key={product.id} product={product} />;
    };

    return (
        <Component
            {...sectionProps}
            productGridModel={productGridModel}
            renderProductCard={renderProductCard}
        />
    );
}

function CategoryGridRuntimeContainer({
    component: Component,
    sectionProps,
}: RuntimeSectionContainerProps) {
    const { t, language } = useTranslation();
    const { categories, isLoading, error } = useCategories();
    const title = typeof sectionProps.title === 'string' ? sectionProps.title : undefined;
    const subtitle = typeof sectionProps.subtitle === 'string' ? sectionProps.subtitle : undefined;
    const columns = Number(sectionProps.columns ?? 3);
    const maxValue = 18;
    const fallbackCount = Math.max(columns * 2, 6);
    const visibleCount = normalizeHomepageCollectionLimit(sectionProps.maxItems, fallbackCount, maxValue);
    const categoryIds = normalizeHomepageGuidList(sectionProps.categoryIds);
    const collectionSource = resolveHomepageCollectionSource(sectionProps.source, sectionProps.categoryIds);
    const categoryLookup = new Map(flattenCategories(categories).map((category) => [category.id, category]));
    const curatedCategoryIds = categoryIds ?? [];
    const hasCuratedSelection = collectionSource === 'curated';
    const visibleCategories = hasCuratedSelection
        ? curatedCategoryIds
            .map((categoryId) => categoryLookup.get(categoryId))
            .filter((category): category is CategoryTreeDto => Boolean(category))
        : selectHomepageCollectionItems(categories, {
            limit: visibleCount,
            source: collectionSource,
            seedKey: `${String(sectionProps.sectionId ?? title ?? 'categories')}:categories`,
            getKey: (category) => category.id,
        });
    const displayTitle = (!title || title === 'Shop by Category')
        ? t('category.shopByCategory')
        : title;
    const categoryModels = visibleCategories.map((category) => ({
        id: category.id,
        href: `/products?category=${category.id}`,
        title: language === 'ar' && category.nameAr ? category.nameAr : category.name,
    }));
    const emptyStateTitle = hasCuratedSelection ? t('home.noSelectedCategoriesYet') : t('home.noCategoriesYet');
    const emptyStateMessage = hasCuratedSelection
        ? t('home.selectCategoriesToPopulate')
        : collectionSource === 'random'
          ? t('home.randomCategoriesWillAppear')
          : t('home.categoriesWillAppear');
    const categoryGridModel: CategoryGridPresentationModel = {
        title: displayTitle,
        subtitle,
        visibleCount,
        categories: categoryModels,
        isLoading,
        error,
        hasCuratedSelection,
        emptyStateTitle,
        emptyStateMessage,
        labels: {
            eyebrowCategories: t('category.shopByCategory'),
            failedToLoadCategories: t('home.failedToLoadCategories'),
        },
    };

    return <Component {...sectionProps} categoryGridModel={categoryGridModel} />;
}

function SubcategoryGridRuntimeContainer({ component: Component, sectionProps }: RuntimeSectionContainerProps) {
    const { t, language } = useTranslation();
    const { categories, isLoading, error } = useCategories();
    const title = typeof sectionProps.title === 'string' ? sectionProps.title : undefined;
    const subtitle = typeof sectionProps.subtitle === 'string' ? sectionProps.subtitle : undefined;
    const columns = Number(sectionProps.columns ?? 3);
    const visibleCount = normalizeHomepageCollectionLimit(sectionProps.maxItems, Math.max(columns * 2, 6), 18);
    const categoryIds = normalizeHomepageGuidList(sectionProps.categoryIds);
    const collectionSource = resolveHomepageCollectionSource(sectionProps.source, sectionProps.categoryIds);
    const allSubcategories = categories.flatMap((category) => category.children || []);
    const subcategoryLookup = new Map(allSubcategories.map((category) => [category.id, category]));
    const hasCuratedSelection = collectionSource === 'curated';
    const visibleSubcategories = hasCuratedSelection
        ? (categoryIds || [])
            .map((categoryId) => subcategoryLookup.get(categoryId))
            .filter((category): category is CategoryTreeDto => Boolean(category))
        : selectHomepageCollectionItems(allSubcategories, {
            limit: visibleCount,
            source: collectionSource,
            seedKey: `${String(sectionProps.sectionId ?? title ?? 'subcategories')}:subcategories`,
            getKey: (category) => category.id,
        });
    const displayTitle = (!title || title === 'Subcategories') ? t('category.subcategories') : title;
    const categoryGridModel: CategoryGridPresentationModel = {
        title: displayTitle,
        subtitle,
        visibleCount,
        categories: visibleSubcategories.map((category) => ({
            id: category.id,
            href: `/products?category=${category.id}`,
            title: language === 'ar' && category.nameAr ? category.nameAr : category.name,
        })),
        isLoading,
        error,
        hasCuratedSelection,
        emptyStateTitle: hasCuratedSelection ? t('home.noSelectedSubcategoriesYet') : t('home.noCategoriesYet'),
        emptyStateMessage: hasCuratedSelection
            ? t('home.selectSubcategoriesToPopulate')
            : t('home.categoriesWillAppear'),
        labels: {
            eyebrowCategories: t('category.subcategories'),
            failedToLoadCategories: t('home.failedToLoadCategories'),
        },
    };

    return <Component {...sectionProps} categoryGridModel={categoryGridModel} />;
}

function RelatedProductsRuntimeContainer({ component: Component, sectionProps }: RuntimeSectionContainerProps) {
    const { t } = useTranslation();
    const product = sectionProps.product as { id?: string; categoryId?: string } | undefined;
    const limit = Math.max(1, Number(sectionProps.limit || 4));
    const { products, isLoading, error } = useProducts({
        page: 1,
        pageSize: limit + 1,
        categoryId: typeof product?.categoryId === 'string' ? product.categoryId : undefined,
    });
    const visibleProducts = (products || [])
        .filter((item) => item.id !== product?.id)
        .slice(0, limit);
    const productLookup = useMemo(
        () => new Map(visibleProducts.map((item) => [item.id, item])),
        [visibleProducts]
    );
    const productGridModel: ProductGridPresentationModel = {
        title: typeof sectionProps.title === 'string' ? sectionProps.title : '',
        layout: 'grid',
        viewAllHref: shouldSuppressActionHref(sectionProps, PRODUCT_CATALOG_HREF)
            ? null
            : PRODUCT_CATALOG_HREF,
        pageSize: limit,
        products: visibleProducts.map((item) => ({ id: item.id })),
        isLoading,
        error,
        isCuratedConfig: false,
        isCuratedSelectionInvalid: false,
        emptyStateTitle: t('home.noProductsYet'),
        emptyStateMessage: t('home.productsWillAppear'),
        labels: {
            eyebrowProducts: t('common.products'),
            viewAll: t('common.viewAll'),
            failedToLoad: t('home.failedToLoad'),
        },
    };
    const renderProductCard = (productModel: ProductPresentationModel) => {
        const item = productLookup.get(productModel.id);
        return item ? <ProductCard key={item.id} product={item} /> : null;
    };

    return <Component {...sectionProps} productGridModel={productGridModel} renderProductCard={renderProductCard} />;
}

function ProductHeroRuntimeContainer({ component: Component, sectionProps }: RuntimeSectionContainerProps) {
    const product = sectionProps.product as ProductHeroPresentationModel['product'] | null | undefined;
    const showDescription = true;
    const showMediaGallery = true;
    const showVariantSelector = true;
    const showAddToCart = true;
    const showSku = true;
    const showStockStatus = true;
    const layoutVariant = typeof sectionProps.layoutVariant === 'string' ? sectionProps.layoutVariant : 'default';
    const { themeId } = useTheme();
    const profile = getPurchaseExperienceProfile(layoutVariant, themeId);
    const { t, localize } = useLocalizedContent();
    const { formatPrice } = useCurrency();
    const analytics = useAnalytics();
    const addItem = useCartStore((state) => state.addItem);
    const { success: showSuccess, error: showError } = useToast();
    const [isAddingToCart, setIsAddingToCart] = useState(false);
    const {
        attributeGroups,
        selectedAttributes,
        selectedVariant,
        isResolvingVariant,
        hasVariantResolutionError,
        selectAttribute,
        isValueAvailable,
        hasVariants,
        activeImage,
        preferredMediaId,
        pricing,
        hasActivePromotion,
        promotionBadge,
    } = useVariantSelector(product || null);
    const localizedDescription = product ? localize(product, 'description') : '';
    const content = useMemo(
        () => parseProductNarrative(localizedDescription || product?.description),
        [localizedDescription, product?.description]
    );

    if (!product) {
        return <Component {...sectionProps} productHeroModel={undefined} />;
    }

    const selectedVariantLabel = selectedVariant?.attributes?.map((attribute) => attribute.value).join(' / ') || '';
    const availability = hasVariants ? selectedVariant?.availability : product.availability;
    const canAddToCart = availability?.canAddToCart === true && !isResolvingVariant && !hasVariantResolutionError;
    const stockLabel = availability?.stockLabel || t('product.outOfStock') || 'Out of stock';
    const displaySku = selectedVariant?.sku || product.sku;
    const localizedName = localize(product, 'name');
    const trustItems = [
        showStockStatus ? { label: profile.stockLabel, value: stockLabel } : null,
        showSku && displaySku ? { label: profile.skuLabel, value: displaySku } : null,
        product.categoryName ? { label: profile.categoryLabel, value: product.categoryName } : null,
        selectedVariantLabel ? { label: profile.selectedOptionLabel, value: selectedVariantLabel } : null,
    ].filter((item): item is { label: string; value: string } => Boolean(item));

    const handleAddToCart = async () => {
        if (!product.id) {
            return;
        }

        if (hasVariants && (!selectedVariant || hasVariantResolutionError)) {
            showError(t('product.selectVariant') || 'Please select product options');
            return;
        }

        setIsAddingToCart(true);

        try {
            const variantId = selectedVariant?.id;
            const success = await addItem(product.id, 1, variantId);

            if (!success) {
                showError(useCartStore.getState().error || 'Failed to add to cart');
                return;
            }

            analytics.addToCart(product.id, variantId, pricing.currentPrice || 0, 1, product.name);

            const variantInfo = selectedVariant?.attributes?.map((attribute) => attribute.value).join(' / ') || '';
            const message = variantInfo
                ? `${product.name} (${variantInfo}) added to cart!`
                : `${product.name} added to cart!`;

            showSuccess(message);
        } catch (error) {
            console.error('[ProductHeroRuntimeContainer] Failed to add item to cart:', error);
            showError('Failed to add to cart');
        } finally {
            setIsAddingToCart(false);
        }
    };

    const productHeroModel: ProductHeroPresentationModel = {
        product,
        profile,
        showDescription,
        showMediaGallery,
        showVariantSelector,
        showAddToCart,
        showStockStatus,
        localizedName,
        summaryParagraphs: content.summary ? [content.summary] : [],
        featureHighlights: content.features.slice(0, 4),
        attributeGroups,
        selectedAttributes,
        hasVariants,
        hasVariantResolutionError,
        preferredMediaId,
        activeImage,
        pricing: {
            currentPrice: pricing.currentPrice,
            current: formatPrice(pricing.currentPrice),
            compareAt: pricing.compareAtPrice !== null && pricing.compareAtPrice !== undefined
                ? formatPrice(pricing.compareAtPrice)
                : undefined,
        },
        hasActivePromotion,
        promotionBadge,
        trustItems,
        stockLabel,
        stockToneClass: canAddToCart ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800',
        canAddToCart,
        isAddingToCart,
        addToCartLabel: isAddingToCart
            ? t('common.loading')
            : canAddToCart
                ? t('product.addToCart')
                : t('product.outOfStock'),
        labels: {
            invalidVariantCombination: t('product.variant.combination'),
            products: t('common.products'),
        },
        callbacks: {
            onAddToCart: handleAddToCart,
            onSelectAttribute: selectAttribute,
            isValueAvailable,
        },
    };

    return <Component {...sectionProps} productHeroModel={productHeroModel} />;
}

function ProductGalleryRuntimeContainer({ component: Component, sectionProps }: RuntimeSectionContainerProps) {
    return (
        <Component
            {...sectionProps}
            showThumbnails
            maxImages={12}
        />
    );
}

function ProductSpecsRuntimeContainer({ component: Component, sectionProps }: RuntimeSectionContainerProps) {
    return <Component {...sectionProps} useDescription />;
}

function ProductVideoRuntimeContainer({ component: Component, sectionProps }: RuntimeSectionContainerProps) {
    return <Component {...sectionProps} autoplay={false} showControls />;
}

export function RuntimeSectionContainer(props: RuntimeSectionContainerProps) {
    if (isProductHeroSection(props.sectionType)) {
        return <ProductHeroRuntimeContainer {...props} />;
    }

    if (isRelatedProductSection(props.sectionType)) {
        return <RelatedProductsRuntimeContainer {...props} />;
    }

    if (isProductGallerySection(props.sectionType)) {
        return <ProductGalleryRuntimeContainer {...props} />;
    }

    if (isProductSpecsSection(props.sectionType)) {
        return <ProductSpecsRuntimeContainer {...props} />;
    }

    if (isProductVideoSection(props.sectionType)) {
        return <ProductVideoRuntimeContainer {...props} />;
    }

    if (isProductSection(props.sectionType)) {
        return <ProductGridRuntimeContainer {...props} />;
    }

    if (isSubcategorySection(props.sectionType)) {
        return <SubcategoryGridRuntimeContainer {...props} />;
    }

    if (isCategorySection(props.sectionType)) {
        return <CategoryGridRuntimeContainer {...props} />;
    }

    const Component = props.component;
    return <Component {...props.sectionProps} />;
}
