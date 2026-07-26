/**
 * Product Card - Detailed Variant
 * 
 * Rich product card with additional information display.
 * Shows more product details like description and additional metadata.
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

interface ProductCardDetailedProps {
    product: ProductListItemDto;
}

export const ProductCardDetailed: React.FC<ProductCardDetailedProps> = ({ product }) => {
    const { t, localize, language } = useLocalizedContent();
    const { formatPrice } = useCurrency();
    const displayName = localize(product, 'name');
    const imageSrc = resolveProductImage(product);

    return (
        <div className="group relative block bg-background border border-muted/20 rounded-xl overflow-hidden hover:shadow-2xl hover:border-primary/40 transition-all duration-300">
            {/* Wishlist Button - Positioned on top right of image */}
            <div className="absolute top-3 right-3 z-10">
                <WishlistButton productId={product.id} size="medium" />
            </div>

            <AffiliateLink
                href={product.slug ? `/products/${product.slug}` : `/products/${product.id}`}
                className="block"
            >
                {/* Image Container - Larger aspect ratio */}
                <div className="aspect-[4/3] bg-muted/5 relative overflow-hidden">
                    <ProductBadges product={product} />
                    <OptimizedImage
                        src={imageSrc}
                        alt={displayName}
                        sizes="(max-width: 768px) 100vw, (max-width: 1280px) 40vw, 420px"
                        className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
                    />

                    {product.categoryName ? (
                        <div className="absolute bottom-3 left-3">
                            <span className="bg-secondary/80 text-white text-xs font-medium px-3 py-1 rounded-full backdrop-blur-sm">
                                {product.categoryName}
                            </span>
                        </div>
                    ) : null}

                    {!product.isAvailable ? (
                        <div className="absolute bottom-3 right-3">
                            <span className="bg-muted/90 text-white text-xs font-medium px-3 py-1 rounded-full backdrop-blur-sm">
                                {t('product.outOfStock')}
                            </span>
                        </div>
                    ) : null}

                    {/* Bottom Gradient Overlay */}
                    <div className="absolute bottom-0 left-0 right-0 h-16 bg-gradient-to-t from-black/20 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-300" />
                </div>
            </AffiliateLink>

            {/* Content */}
            <div className="p-5 space-y-3">
                {/* Product Name */}
                <h3 className="text-text font-bold text-lg leading-snug line-clamp-2 group-hover:text-primary transition-colors duration-200">
                    {displayName}
                </h3>

                {/* Show alternate language name if available and not currently displayed */}
                {language === 'en' && product.nameAr && (
                    <p className="text-muted text-sm font-medium" dir="rtl">
                        {product.nameAr}
                    </p>
                )}
                {language === 'ar' && product.name && product.name !== displayName && (
                    <p className="text-muted text-sm font-medium" dir="ltr">
                        {product.name}
                    </p>
                )}

                {/* Price & Actions Row */}
                <div className="flex items-center justify-between pt-2 border-t border-muted/10">
                    <div className="flex items-baseline gap-2">
                        {/* CRITICAL: Check displayPriceText first for variant products */}
                        {product.displayPriceText ? (
                            <span className="text-muted font-medium text-lg">
                                {product.displayPriceText}
                            </span>
                        ) : product.pricing.compareAtPrice !== null && product.pricing.compareAtPrice !== undefined ? (
                            <>
                                <span className="text-red-500 font-bold text-xl">
                                    {formatPrice(product.pricing.currentPrice)}
                                </span>
                                <span className="text-muted line-through text-sm">
                                    {formatPrice(product.pricing.compareAtPrice)}
                                </span>
                            </>
                        ) : (
                            <span className="text-primary font-bold text-xl">
                                {formatPrice(product.pricing.currentPrice)}
                            </span>
                        )}
                    </div>

                    {/* Quick Add Button */}
                    <button
                        onClick={(e) => {
                            e.preventDefault();
                            // Placeholder for add to cart
                        }}
                        disabled={!product.isAvailable}
                        className="flex items-center gap-2 px-4 py-2 bg-primary text-white text-sm font-medium rounded-lg hover:bg-primary/90 disabled:bg-muted disabled:cursor-not-allowed transition-colors duration-200"
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 6v6m0 0v6m0-6h6m-6 0H6" />
                        </svg>
                        <span>{t('product.addToCart')}</span>
                    </button>
                </div>

                {/* Availability Status */}
                <div className="flex items-center gap-2 text-xs">
                    <span className={`w-2 h-2 rounded-full ${product.isAvailable ? 'bg-green-500' : 'bg-muted'}`} />
                    <span className={product.isAvailable ? 'text-green-600' : 'text-muted'}>
                        {product.isAvailable ? t('product.inStock') : t('product.outOfStock')}
                    </span>
                </div>
            </div>
        </div>
    );
};

