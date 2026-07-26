/**
 * Product Card - Default Variant
 * 
 * Standard product card with balanced image and content display.
 * Uses CSS variables for all theming.
 * Phase 10.1: Multi-language support
 * Phase 10.3.6: Wishlist button integration
 * Phase: Promotions Engine - Badge display
 */

import React from 'react';
import type { ProductListItemDto } from '@/types';
import { useLocalizedContent } from '@/i18n';
import { useCurrency } from '@/hooks';
import { WishlistButton } from './WishlistButton';
import ProductBadges from './ProductBadges';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { resolveProductImage } from '@/utils';
import { OptimizedImage } from '@/components/media';

interface ProductCardDefaultProps {
    product: ProductListItemDto;
}

export const ProductCardDefault: React.FC<ProductCardDefaultProps> = ({ product }) => {
    const { t, localize } = useLocalizedContent();
    const { formatPrice } = useCurrency();
    const imageSrc = resolveProductImage(product);

    // Get localized product name (Arabic if available and language is AR)
    const displayName = localize(product, 'name');

    return (
        <div className="group relative bg-background border border-muted/20 rounded-lg overflow-hidden hover:shadow-xl hover:border-primary/30 transition-all duration-300">
            {/* Wishlist Button - Positioned on top right of card */}
            <div className="absolute top-2 right-2 z-10">
                <WishlistButton productId={product.id} size="small" />
            </div>

            <AffiliateLink
                href={product.slug ? `/products/${product.slug}` : `/products/${product.id}`}
                className="block"
            >
                {/* Image Container */}
                <div className="aspect-square bg-muted/5 relative overflow-hidden">
                    <ProductBadges product={product} />
                    <OptimizedImage
                        src={imageSrc}
                        alt={displayName}
                        sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                    />

                    {/* Availability/Stock Badge */}
                    {(!product.isAvailable || !product.inStock) && (
                        <div className="absolute bottom-3 left-3 bg-muted/90 text-white text-xs font-medium px-3 py-1.5 rounded-full backdrop-blur-sm">
                            {t('product.outOfStock')}
                        </div>
                    )}

                    {/* Quick Action Overlay - Only show if in stock */}
                    {product.isAvailable && product.inStock && (
                        <div className="absolute inset-0 bg-secondary/0 group-hover:bg-secondary/10 transition-colors duration-300 flex items-center justify-center opacity-0 group-hover:opacity-100">
                            <span className="bg-primary text-white px-4 py-2 rounded-md text-sm font-medium transform translate-y-2 group-hover:translate-y-0 transition-transform duration-300">
                                {t('product.addToCart')}
                            </span>
                        </div>
                    )}
                </div>

                {/* Content */}
                <div className="p-4 space-y-2">
                    {/* Category */}
                    {product.categoryName && (
                        <p className="text-muted text-xs font-medium uppercase tracking-wider">
                            {product.categoryName}
                        </p>
                    )}

                    {/* Product Name */}
                    <h3 className="text-text font-semibold text-base leading-snug line-clamp-2 group-hover:text-primary transition-colors duration-200">
                        {displayName}
                    </h3>

                    {/* Price - with variant-aware and promotion support */}
                    <div className="flex items-baseline gap-2 pt-1">
                        {/* CRITICAL: Check displayPriceText first for variant products */}
                        {product.displayPriceText ? (
                            <span className="text-muted font-medium text-base">
                                {product.displayPriceText}
                            </span>
                        ) : product.pricing.compareAtPrice !== null && product.pricing.compareAtPrice !== undefined ? (
                            <>
                                <span className="text-red-500 font-bold text-lg">
                                    {formatPrice(product.pricing.currentPrice)}
                                </span>
                                <span className="text-muted line-through text-sm">
                                    {formatPrice(product.pricing.compareAtPrice)}
                                </span>
                            </>
                        ) : (
                            <span className="text-primary font-bold text-lg">
                                {formatPrice(product.pricing.currentPrice)}
                            </span>
                        )}
                    </div>
                </div>
            </AffiliateLink>
        </div>
    );
};

