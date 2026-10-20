import React, { useCallback, useMemo, useRef, useState } from 'react';
import type { ProductListItemDto } from '@/types';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { OptimizedImage } from '@/components/media';
import {
    ThemeAddToCartButton,
    ThemeActionLink,
    ThemeImageFrame,
    ThemeLabel,
    ThemePrice,
    type ThemePricePresentation,
} from '@/components/theme-components';
import { useCurrency } from '@/hooks';
import { useLocalizedContent } from '@/i18n';
import { useCartStore, useToast } from '@/store';
import type { ProductCardLayout } from '@/storefront/utils/resolveProductCardLayout';
import { resolveProductImage } from '@/utils';
import { getProductCardHref } from './productCardLinks';
import ProductBadges from './ProductBadges';
import { WishlistButton } from './WishlistButton';
import { useAnalytics } from '@/analytics/hooks/useAnalytics';

const QUICK_ADD_FAILED_MESSAGE = 'Failed to add item to cart';

export interface ProductCardRuntime {
    product: ProductListItemDto;
    layout: ProductCardLayout;
    themeId: string;
    displayName: string;
    alternateName?: {
        value: string;
        dir: 'ltr' | 'rtl';
    };
    href: string;
    imageSrc: string;
    price: ThemePricePresentation;
    categoryName?: string;
    isAvailable: boolean;
    inStock: boolean;
    canQuickAdd: boolean;
    isAddingToCart: boolean;
    labels: {
        addToCart: string;
        inStock: string;
        outOfStock: string;
    };
    handleQuickAdd: (event: React.MouseEvent<HTMLButtonElement>) => Promise<void>;
}

interface UseProductCardRuntimeOptions {
    product: ProductListItemDto;
    layout: ProductCardLayout;
    themeId: string;
}

export function useProductCardRuntime({
    product,
    layout,
    themeId,
}: UseProductCardRuntimeOptions): ProductCardRuntime {
    const { t, localize, language } = useLocalizedContent();
    const { formatPrice } = useCurrency();
    const addItem = useCartStore((state) => state.addItem);
    const analytics = useAnalytics();
    const toast = useToast();
    const [isAddingToCart, setIsAddingToCart] = useState(false);
    const addingRef = useRef(false);

    const displayName = localize(product, 'name');
    const imageSrc = useMemo(() => resolveProductImage(product), [product]);

    const price = useMemo<ThemePricePresentation>(() => {
        if (product.displayPriceText) {
            return { kind: 'text', text: product.displayPriceText };
        }

        if (product.pricing.compareAtPrice !== null && product.pricing.compareAtPrice !== undefined) {
            return {
                kind: 'sale',
                current: formatPrice(product.pricing.currentPrice),
                compareAt: formatPrice(product.pricing.compareAtPrice),
            };
        }

        return {
            kind: 'regular',
            current: formatPrice(product.pricing.currentPrice),
        };
    }, [
        formatPrice,
        product.displayPriceText,
        product.pricing.compareAtPrice,
        product.pricing.currentPrice,
    ]);

    const alternateName = useMemo(() => {
        if (language === 'en' && product.nameAr) {
            return { value: product.nameAr, dir: 'rtl' as const };
        }

        if (language === 'ar' && product.name && product.name !== displayName) {
            return { value: product.name, dir: 'ltr' as const };
        }

        return undefined;
    }, [displayName, language, product.name, product.nameAr]);

    const canQuickAdd = product.availability?.canAddToCart === true;

    const handleQuickAdd = useCallback(async (event: React.MouseEvent<HTMLButtonElement>) => {
        event.preventDefault();
        event.stopPropagation();

        if (!canQuickAdd || addingRef.current) {
            return;
        }

        addingRef.current = true;
        setIsAddingToCart(true);
        try {
            const added = await addItem(product.id, 1);

            if (added) {
                analytics.addToCart(product.id, undefined, product.pricing.currentPrice, 1, product.name);
                toast.success(t('cart.itemAdded'));
            } else {
                toast.error(QUICK_ADD_FAILED_MESSAGE);
            }
        } catch (error) {
            console.error('[ProductCardRuntime] Quick add failed:', error);
            toast.error(QUICK_ADD_FAILED_MESSAGE);
        } finally {
            addingRef.current = false;
            setIsAddingToCart(false);
        }
    }, [addItem, analytics, canQuickAdd, product.id, product.name, product.pricing.currentPrice, t, toast]);

    return {
        product,
        layout,
        themeId,
        displayName,
        alternateName,
        href: getProductCardHref(product),
        imageSrc,
        price,
        categoryName: product.categoryName,
        isAvailable: product.availability?.isAvailable ?? product.isAvailable,
        inStock: product.inStock,
        canQuickAdd,
        isAddingToCart,
        labels: {
            addToCart: t('product.addToCart'),
            inStock: product.availability?.stockLabel ?? t('product.inStock'),
            outOfStock: t('product.outOfStock'),
        },
        handleQuickAdd,
    };
}

export function ProductCardLink({
    runtime,
    className,
    children,
}: {
    runtime: ProductCardRuntime;
    className?: string;
    children: React.ReactNode;
}) {
    return (
        <AffiliateLink href={runtime.href} className={className}>
            {children}
        </AffiliateLink>
    );
}

export function ProductCardWishlist({
    runtime,
    size = 'small',
    className = '',
}: {
    runtime: ProductCardRuntime;
    size?: 'small' | 'medium' | 'large';
    className?: string;
}) {
    return (
        <WishlistButton
            productId={runtime.product.id}
            size={size}
            className={className}
        />
    );
}

export function ProductCardMedia({
    runtime,
    className,
    imageClassName,
    sizes,
    showUnavailableOverlay = true,
}: {
    runtime: ProductCardRuntime;
    className: string;
    imageClassName: string;
    sizes: string;
    showUnavailableOverlay?: boolean;
}) {
    return (
        <ThemeImageFrame themeId={runtime.themeId} className={className}>
            <ProductBadges product={runtime.product} themeId={runtime.themeId} />
            <OptimizedImage
                src={runtime.imageSrc}
                alt={runtime.displayName}
                sizes={sizes}
                className={imageClassName}
            />

            {showUnavailableOverlay && (!runtime.isAvailable || !runtime.inStock) ? (
                <div className="absolute inset-x-3 bottom-3 rounded-full bg-secondary/85 px-3 py-1.5 text-center text-xs font-semibold uppercase text-white shadow-sm backdrop-blur-sm">
                    {runtime.labels.outOfStock}
                </div>
            ) : null}
        </ThemeImageFrame>
    );
}

export function ProductCardPrice({
    runtime,
    className = 'flex items-baseline gap-2',
    textClassName = 'text-muted font-medium text-base',
    currentClassName = 'text-primary font-bold text-lg',
    saleClassName = 'text-primary font-bold text-lg',
    compareClassName = 'text-muted line-through text-sm',
}: {
    runtime: ProductCardRuntime;
    className?: string;
    textClassName?: string;
    currentClassName?: string;
    saleClassName?: string;
    compareClassName?: string;
}) {
    return (
        <ThemePrice
            themeId={runtime.themeId}
            price={runtime.price}
            className={className}
            textClassName={textClassName}
            currentClassName={currentClassName}
            saleClassName={saleClassName}
            compareClassName={compareClassName}
        />
    );
}

export function ProductCardQuickAddButton({
    runtime,
    className,
}: {
    runtime: ProductCardRuntime;
    className: string;
}) {
    const { t } = useLocalizedContent();
    // Selection-required is an authoritative backend outcome, not an unavailable product.
    if (runtime.product.availability?.unavailableReason === 'VARIANT_SELECTION_REQUIRED') {
        return (
            <ThemeActionLink themeId={runtime.themeId} appearance="custom" href={runtime.href} className={className}>
                {t('product.selectOptions')}
            </ThemeActionLink>
        );
    }
    return (
        <ThemeAddToCartButton
            themeId={runtime.themeId}
            appearance="custom"
            label={runtime.labels.addToCart}
            onClick={runtime.handleQuickAdd}
            disabled={!runtime.canQuickAdd || runtime.isAddingToCart}
            className={className}
        />
    );
}

const CONTROL_PANEL_CLASSES: Record<string, string> = {
    cosmetics: 'border-x border-b border-border bg-background px-3 pb-3 text-xs',
    fashion: 'border-t border-border bg-background px-3 py-3',
    minimal: 'border-t border-muted/15 bg-background px-3 py-3',
    pearl: 'border-t border-secondary/30 bg-background px-3 py-3',
    phoenix: 'border-t border-black/10 bg-background px-3 py-3',
    seven: 'border-t border-muted/15 bg-background px-3 py-3',
};

const QUICK_ADD_CLASSES: Record<string, string> = {
    alizon: 'inline-flex min-h-9 items-center justify-center rounded-none bg-primary px-3 py-2 text-xs font-semibold uppercase tracking-[0.12em] text-on-primary hover:opacity-90 disabled:opacity-50',
    cosmetics: 'inline-flex min-h-9 items-center justify-center rounded-full bg-primary px-3 py-2 text-xs font-medium text-on-primary hover:opacity-90 disabled:opacity-50',
    hype: 'inline-flex min-h-9 items-center justify-center rounded-full bg-primary px-3 py-2 text-xs font-bold uppercase tracking-[0.12em] text-on-primary hover:opacity-90 disabled:opacity-50',
    seven: 'inline-flex min-h-9 items-center justify-center bg-background text-xs font-black uppercase tracking-[0.12em] text-primary-text hover:bg-subtle disabled:text-muted',
};

/**
 * Canonical functional control layer for every Product Card presentation.
 * Theme renderers own the card's appearance; this runtime alone owns the
 * presence and behavior of wishlist, stock, and quick-add controls.
 */
export function ProductCardInteractionLayer({ runtime }: { runtime: ProductCardRuntime }) {
    const stockLabel = runtime.isAvailable && runtime.inStock
        ? runtime.labels.inStock
        : runtime.labels.outOfStock;
    const panelClassName = CONTROL_PANEL_CLASSES[runtime.themeId]
        ?? 'border-t border-muted/15 bg-background px-3 py-3';
    const quickAddClassName = QUICK_ADD_CLASSES[runtime.themeId]
        ?? 'inline-flex min-h-9 items-center justify-center rounded-md bg-primary px-3 py-2 text-xs font-semibold uppercase tracking-[0.12em] text-on-primary hover:opacity-90 disabled:opacity-50';

    return (
        <>
            <div className="absolute right-3 top-3 z-20">
                <ProductCardWishlist runtime={runtime} size="small" />
            </div>
            <div
                className={`flex items-center justify-between gap-3 ${panelClassName}`}
                data-product-card-control-owner="ProductCardRuntime"
            >
                <ThemeLabel themeId={runtime.themeId}>{stockLabel}</ThemeLabel>
                <ProductCardQuickAddButton runtime={runtime} className={quickAddClassName} />
            </div>
        </>
    );
}
