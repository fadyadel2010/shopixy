/**
 * Product Card - Compact Variant
 * 
 * Smaller, condensed product card for dense product grids.
 * Optimized for mobile and space-constrained layouts.
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

interface ProductCardCompactProps {
    product: ProductListItemDto;
}

export const ProductCardCompact: React.FC<ProductCardCompactProps> = ({ product }) => {
    const { t, localize } = useLocalizedContent();
    const { formatPrice } = useCurrency();
    const displayName = localize(product, 'name');
    const imageSrc = resolveProductImage(product);

    return (
        <div className="group relative flex gap-3 bg-background border border-muted/15 rounded-md p-3 hover:shadow-lg hover:border-primary/25 transition-all duration-200">
            {/* Wishlist Button - Positioned on top right */}
            <div className="absolute top-1 right-1 z-10">
                <WishlistButton productId={product.id} size="small" />
            </div>

            <AffiliateLink
                href={product.slug ? `/products/${product.slug}` : `/products/${product.id}`}
                className="flex gap-3 flex-1 min-w-0"
            >
                {/* Compact Image */}
                <div className="w-20 h-20 flex-shrink-0 bg-muted/5 rounded overflow-hidden relative">
                    <ProductBadges product={product} />
                    <OptimizedImage
                        src={imageSrc}
                        alt={displayName}
                        sizes="80px"
                        className="w-full h-full object-cover group-hover:scale-110 transition-transform duration-200"
                    />

                    {/* Out of Stock Indicator */}
                    {!product.isAvailable && (
                        <div className="absolute inset-0 bg-muted/60 flex items-center justify-center">
                            <span className="text-white text-[10px] font-bold uppercase">{t('product.outOfStock')}</span>
                        </div>
                    )}
                </div>

                {/* Content */}
                <div className="flex-1 min-w-0 flex flex-col justify-between py-0.5">
                    <div>
                        {/* Product Name */}
                        <h3 className="text-text font-medium text-sm leading-tight line-clamp-2 group-hover:text-primary transition-colors duration-150">
                            {displayName}
                        </h3>

                        {/* Category */}
                        {product.categoryName && (
                            <p className="text-muted text-xs mt-0.5 truncate">
                                {product.categoryName}
                            </p>
                        )}
                    </div>

                    {/* Price - with variant-aware and promotion support */}
                    <div className="flex items-baseline gap-1.5 mt-1">
                        {/* CRITICAL: Check displayPriceText first for variant products */}
                        {product.displayPriceText ? (
                            <span className="text-muted font-medium text-sm">
                                {product.displayPriceText}
                            </span>
                        ) : product.pricing.compareAtPrice !== null && product.pricing.compareAtPrice !== undefined ? (
                            <>
                                <span className="text-red-500 font-bold text-sm">
                                    {formatPrice(product.pricing.currentPrice)}
                                </span>
                                <span className="text-muted line-through text-xs">
                                    {formatPrice(product.pricing.compareAtPrice)}
                                </span>
                            </>
                        ) : (
                            <span className="text-primary font-bold text-sm">
                                {formatPrice(product.pricing.currentPrice)}
                            </span>
                        )}
                    </div>
                </div>

                {/* Arrow Indicator */}
                <div className="flex-shrink-0 self-center text-muted/40 group-hover:text-primary transition-colors duration-150 rtl-flip">
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                    </svg>
                </div>
            </AffiliateLink>
        </div>
    );
};

