import React, { useCallback, useMemo, useState } from 'react';
import type { ProductListItemDto } from '@/types';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { OptimizedImage } from '@/components/media';
import {
    ThemeAddToCartButton,
    ThemeImageFrame,
    ThemePrice,
    type ThemePricePresentation,
} from '@/components/theme-components';
import { useCurrency } from '@/hooks';
import { useLocalizedContent } from '@/i18n';
import { useCartStore, useToast } from '@/store';
import type { ProductCardLayout } from '@/storefront/utils/resolveProductCardLayout';
import { resolveProductImage } from '@/utils';
import ProductBadges from './ProductBadges';
import { WishlistButton } from './WishlistButton';

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
    const toast = useToast();
    const [isAddingToCart, setIsAddingToCart] = useState(false);

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

    const canQuickAdd = product.isAvailable && product.inStock && !product.hasVariants;

    const handleQuickAdd = useCallback(async (event: React.MouseEvent<HTMLButtonElement>) => {
        event.preventDefault();
        event.stopPropagation();

        if (!canQuickAdd || isAddingToCart) {
            return;
        }

        setIsAddingToCart(true);
        try {
            const added = await addItem(product.id, 1);

            if (added) {
                toast.success(t('cart.itemAdded'));
            } else {
                toast.error(QUICK_ADD_FAILED_MESSAGE);
            }
        } catch (error) {
            console.error('[ProductCardRuntime] Quick add failed:', error);
            toast.error(QUICK_ADD_FAILED_MESSAGE);
        } finally {
            setIsAddingToCart(false);
        }
    }, [addItem, canQuickAdd, isAddingToCart, product.id, t, toast]);

    return {
        product,
        layout,
        themeId,
        displayName,
        alternateName,
        href: product.slug ? `/products/${product.slug}` : `/products/${product.id}`,
        imageSrc,
        price,
        categoryName: product.categoryName,
        isAvailable: product.isAvailable,
        inStock: product.inStock,
        canQuickAdd,
        isAddingToCart,
        labels: {
            addToCart: t('product.addToCart'),
            inStock: t('product.inStock'),
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
    return (
        <ThemeAddToCartButton
            themeId={runtime.themeId}
            label={runtime.labels.addToCart}
            onClick={runtime.handleQuickAdd}
            disabled={!runtime.canQuickAdd || runtime.isAddingToCart}
            className={className}
        />
    );
}
