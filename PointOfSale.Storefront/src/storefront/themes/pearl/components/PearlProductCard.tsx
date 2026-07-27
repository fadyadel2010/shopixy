/**
 * Pearl Theme — Product Card (Phase T1.2C, presentation-only)
 *
 * REUSE DECISION / GAP ANALYSIS
 * -----------------------------
 * Existing component: `@/components/products/ProductCardDefault` (dispatched by
 * `@/components/common/ProductCard` via `resolveProductCardLayout`).
 *
 * The platform dispatcher's variant map (`PRODUCT_CARD_VARIANTS`) is a
 * platform-global registry; registering a 'pearl' variant would require
 * editing Theme-SDK files (common/ProductCard.tsx + resolveProductCardLayout.ts),
 * which this phase forbids. Therefore Pearl ships a THEME-LOCAL presentation
 * card that ADAPTS ProductCardDefault (~85% survives verbatim) and reuses
 * every platform capability by IMPORT — no business logic is duplicated:
 *
 *   - Image pipeline:   OptimizedImage (lazy/responsive/placeholder/fallback)
 *                       + resolveProductImage — untouched.
 *   - Pricing:          useCurrency().formatPrice + the exact three-branch
 *                       precedence from ProductCardDefault
 *                       (displayPriceText > compareAtPrice > currentPrice).
 *   - Badges:           ProductBadges (discount/featured logic) — reused;
 *                       Pearl only restyles it via CSS descendant utilities
 *                       (sharp red #CD1515 badge per design report §5).
 *   - Wishlist:         WishlistButton — platform behavior untouched.
 *   - Routing/affiliate: AffiliateLink + slug-or-id href — identical.
 *   - Localization:     useLocalizedContent — identical.
 *   - Analytics:        the dispatcher's `data-ai-product-id` marker is
 *                       preserved on the wrapper.
 *
 * Pearl identity (design report §5 / §9), presentation only:
 *   - SQUARE corners everywhere (no rounded chrome), white card, no border.
 *   - Portrait image, aspect ~0.87 (aspect-[7/8]).
 *   - Name: Jost 12px UPPERCASE wide tracking, `#212A2F`, centered.
 *   - Price: Jost 16px `#131316`; compare-at struck-through muted; sale price red.
 *   - Discount badge: sharp red `#CD1515`, white 11px.
 *   - CTA overlay: solid red `#DC2626`, square, uppercase (same visual-span
 *     pattern as ProductCardDefault — links to the product page; NO new cart logic).
 *   - Hover: CSS-only image scale + soft shadow + title color transition.
 *
 * Future reusability: if the platform later accepts a 'pearl' card variant in
 * PRODUCT_CARD_VARIANTS, this file can be promoted verbatim to
 * `@/components/products/ProductCardPearl.tsx` and this theme-local copy deleted.
 */

import React from 'react';
import type { ProductListItemDto } from '@/types';
import { useLocalizedContent } from '@/i18n';
import { useCurrency } from '@/hooks';
import { WishlistButton } from '@/components/products/WishlistButton';
import ProductBadges from '@/components/products/ProductBadges';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { resolveProductImage } from '@/utils';
import { OptimizedImage } from '@/components/media';
import { PEARL_FONT_BODY } from '../layout/pearlTokens';

interface PearlProductCardProps {
    product: ProductListItemDto;
}

/**
 * CSS-only restyle of the reused ProductBadges output (sharp red #CD1515,
 * square corners) using Tailwind descendant utilities — the badge LOGIC
 * (what to show and when) stays 100% platform-owned.
 */
const PEARL_BADGE_RESTYLE =
    '[&_span]:!rounded-none [&_span]:!bg-[#CD1515] [&_span]:!text-[11px] [&_span]:!font-normal';

export const PearlProductCard: React.FC<PearlProductCardProps> = ({ product }) => {
    const { t, localize } = useLocalizedContent();
    const { formatPrice } = useCurrency();
    const imageSrc = resolveProductImage(product);
    const displayName = localize(product, 'name');

    return (
        <div
            data-ai-product-id={product.id}
            className="group relative bg-white transition-shadow duration-300 hover:shadow-[0_12px_32px_-16px_rgba(19,19,22,0.35)]"
        >
            {/* Wishlist Button — platform behavior untouched */}
            <div className="absolute top-2 right-2 z-10">
                <WishlistButton productId={product.id} size="small" />
            </div>

            <AffiliateLink
                href={product.slug ? `/products/${product.slug}` : `/products/${product.id}`}
                className="block focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#DC2626]"
                aria-label={displayName}
            >
                {/* Image Container — portrait, square corners, platform image pipeline */}
                <div className={`relative aspect-[7/8] overflow-hidden bg-[#F5F4F2] ${PEARL_BADGE_RESTYLE}`}>
                    <ProductBadges product={product} />
                    <OptimizedImage
                        src={imageSrc}
                        alt={displayName}
                        sizes="(max-width: 768px) 50vw, (max-width: 1280px) 33vw, 320px"
                        className="h-full w-full object-cover transition-transform duration-700 ease-out group-hover:scale-105"
                    />

                    {/* Availability/Stock Badge (same logic as ProductCardDefault) */}
                    {(!product.isAvailable || !product.inStock) && (
                        <div className={`absolute bottom-3 left-3 bg-[#131316]/85 px-3 py-1.5 text-[11px] uppercase tracking-[0.14em] text-white backdrop-blur-sm ${PEARL_FONT_BODY}`}>
                            {t('product.outOfStock')}
                        </div>
                    )}

                    {/* CTA overlay — visual only, square red Pearl accent; in-stock only */}
                    {product.isAvailable && product.inStock && (
                        <div className="absolute inset-0 flex items-end justify-center bg-black/0 pb-5 opacity-0 transition-all duration-300 group-hover:bg-black/10 group-hover:opacity-100">
                            <span className={`translate-y-2 bg-[#DC2626] px-6 py-2.5 text-[12px] uppercase tracking-[0.2em] text-white transition-transform duration-300 group-hover:translate-y-0 ${PEARL_FONT_BODY}`}>
                                {t('product.addToCart')}
                            </span>
                        </div>
                    )}
                </div>

                {/* Content — centered, luxury spacing */}
                <div className="space-y-2 px-3 pb-6 pt-4 text-center">
                    {/* Product Name — Jost 12px uppercase (design report §5) */}
                    <h3
                        className={`line-clamp-2 text-[12px] uppercase leading-relaxed tracking-[0.18em] text-[#212A2F] transition-colors duration-300 group-hover:text-[#DC2626] ${PEARL_FONT_BODY}`}
                    >
                        {displayName}
                    </h3>

                    {/* Price — same variant-aware precedence as ProductCardDefault */}
                    <div className={`flex items-baseline justify-center gap-2 text-[16px] ${PEARL_FONT_BODY}`}>
                        {/* CRITICAL: Check displayPriceText first for variant products */}
                        {product.displayPriceText ? (
                            <span className="text-[#131316]/70">
                                {product.displayPriceText}
                            </span>
                        ) : product.pricing.compareAtPrice !== null && product.pricing.compareAtPrice !== undefined ? (
                            <>
                                <span className="text-[#CD1515]">
                                    {formatPrice(product.pricing.currentPrice)}
                                </span>
                                <span className="text-[13px] text-[#131316]/50 line-through">
                                    {formatPrice(product.pricing.compareAtPrice)}
                                </span>
                            </>
                        ) : (
                            <span className="text-[#131316]">
                                {formatPrice(product.pricing.currentPrice)}
                            </span>
                        )}
                    </div>
                </div>
            </AffiliateLink>
        </div>
    );
};

export default PearlProductCard;
