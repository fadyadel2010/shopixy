import React, { useMemo, useState } from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { ProductMediaGallery, VariantSelector } from '@/components/products';
import { useAnalytics } from '@/analytics';
import { useCartStore } from '@/store/cartStore';
import { useToast } from '@/store/toastStore';
import { useCurrency, useVariantSelector } from '@/hooks';
import { useLocalizedContent } from '@/i18n';
import type { ProductReadDto } from '@/types';
import { parseProductNarrative } from '@/utils';

interface ProductHeroProps {
    product?: ProductReadDto | null;
    showDescription?: boolean;
    showMediaGallery?: boolean;
    showVariantSelector?: boolean;
    showAddToCart?: boolean;
    showSku?: boolean;
    showStockStatus?: boolean;
    layoutVariant?: 'default' | 'electronics' | 'fashion' | 'cosmetics' | string;
}

const ProductHero: React.FC<ProductHeroProps> = ({
    product,
    showDescription = true,
    showMediaGallery = true,
    showVariantSelector = true,
    showAddToCart = true,
    showSku = true,
    showStockStatus = true,
    layoutVariant = 'default',
}) => {
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
    const summaryParagraphs = content.summary ? [content.summary] : [];
    const featureHighlights = content.features.slice(0, 4);
    const selectedVariantLabel = selectedVariant?.attributes?.map((attribute) => attribute.value).join(' / ') || '';

    if (!product) {
        return null;
    }

    const availability = hasVariants ? selectedVariant?.availability : product.availability;
    const canAddToCart = availability?.canAddToCart === true && !isResolvingVariant && !hasVariantResolutionError;
    const stockLabel = availability?.stockLabel || t('product.outOfStock') || 'Out of stock';
    const displaySku = selectedVariant?.sku || product.sku;
    const isElectronics = layoutVariant === 'electronics';
    const isFashion = layoutVariant === 'fashion';
    const isCosmetics = layoutVariant === 'cosmetics';
    const sectionShellClass = isElectronics
        ? 'overflow-hidden rounded-[2rem] border border-slate-200 bg-[radial-gradient(circle_at_top_left,_rgba(59,130,246,0.12),_transparent_38%),linear-gradient(180deg,_#ffffff_0%,_#f8fbff_100%)] shadow-[0_30px_80px_-45px_rgba(15,23,42,0.45)]'
        : isFashion
            ? 'overflow-hidden rounded-[2rem] border border-stone-200 bg-[linear-gradient(135deg,_#fffdf8_0%,_#fff7ed_45%,_#ffffff_100%)] shadow-[0_30px_80px_-45px_rgba(120,53,15,0.35)]'
            : isCosmetics
                ? 'overflow-hidden rounded-[2rem] border border-rose-100 bg-[linear-gradient(135deg,_#fff7fb_0%,_#fff1f2_45%,_#ffffff_100%)] shadow-[0_30px_80px_-45px_rgba(190,24,93,0.28)]'
                : 'rounded-3xl border border-muted/20 bg-background shadow-sm';
    const contentPanelClass = isElectronics
        ? 'space-y-5 rounded-[1.75rem] border border-slate-200/70 bg-white/90 p-6 backdrop-blur'
        : isFashion
            ? 'space-y-6 rounded-[1.75rem] border border-stone-200/80 bg-white/85 p-6 backdrop-blur'
            : isCosmetics
                ? 'space-y-5 rounded-[1.75rem] border border-rose-100/80 bg-white/90 p-6 backdrop-blur'
                : 'space-y-5';
    const categoryChipClass = isFashion
        ? 'inline-flex rounded-full border border-stone-200 bg-stone-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.24em] text-stone-500'
        : isElectronics
            ? 'inline-flex rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.24em] text-sky-700'
            : isCosmetics
                ? 'inline-flex rounded-full border border-rose-200 bg-rose-50 px-3 py-1 text-xs font-semibold uppercase tracking-[0.24em] text-rose-700'
                : 'inline-flex rounded-full border border-muted/20 bg-muted/5 px-3 py-1 text-xs font-semibold uppercase tracking-[0.24em] text-muted';
    const actionButtonClass = isFashion
        ? 'rounded-full bg-stone-900 px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50'
        : isCosmetics
            ? 'rounded-full bg-rose-500 px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50'
            : 'rounded-full bg-primary px-6 py-3 font-semibold text-white transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50';
    const secondaryButtonClass = isFashion
        ? 'rounded-full border border-stone-300 px-6 py-3 font-semibold text-stone-700 transition-colors hover:border-stone-900 hover:text-stone-900'
        : isCosmetics
            ? 'rounded-full border border-rose-200 px-6 py-3 font-semibold text-rose-700 transition-colors hover:border-rose-400 hover:text-rose-800'
            : 'rounded-full border border-muted/30 px-6 py-3 font-semibold text-text transition-colors hover:border-primary hover:text-primary';

    const handleAddToCart = async () => {
        if (!product?.id) {
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

            analytics.addToCart(
                product.id,
                variantId,
                pricing.currentPrice || 0,
                1,
                product.name
            );

            const variantInfo = selectedVariant?.attributes?.map((attribute) => attribute.value).join(' / ') || '';
            const message = variantInfo
                ? `${product.name} (${variantInfo}) added to cart!`
                : `${product.name} added to cart!`;

            showSuccess(message);
        } catch (error) {
            console.error('[ProductHero] Failed to add item to cart:', error);
            showError('Failed to add to cart');
        } finally {
            setIsAddingToCart(false);
        }
    };

    return (
        <section className="container mx-auto px-4 py-8">
            <div className={`${sectionShellClass} p-6`}>
                <div className={`grid gap-8 ${showMediaGallery ? 'md:grid-cols-2' : ''}`}>
                    {showMediaGallery ? (
                        <div className="space-y-4">
                            <ProductMediaGallery
                                product={product}
                                preferredMediaId={preferredMediaId}
                                preferredImageUrl={activeImage}
                            />
                        </div>
                    ) : null}

                    <div className={contentPanelClass}>
                    {product.categoryName ? (
                        <span className={categoryChipClass}>
                            {product.categoryName}
                        </span>
                    ) : null}

                    <div>
                        <h1 className={`font-bold text-text ${isFashion ? 'text-4xl md:text-5xl' : 'text-3xl md:text-4xl'}`}>
                            {localize(product, 'name')}
                        </h1>
                    </div>

                    <div className={`space-y-1 ${isFashion ? 'border-b border-stone-200/80 pb-5' : ''}`}>
                        {pricing.compareAtPrice !== null && pricing.compareAtPrice !== undefined ? (
                            <>
                                <div className={`font-bold text-red-500 ${isFashion ? 'text-4xl' : 'text-3xl'}`}>{formatPrice(pricing.currentPrice)}</div>
                                <div className="text-lg text-muted line-through">{formatPrice(pricing.compareAtPrice)}</div>
                            </>
                        ) : (
                            <div className={`font-bold ${isFashion ? 'text-4xl text-stone-900' : 'text-3xl text-primary'}`}>{formatPrice(pricing.currentPrice)}</div>
                        )}

                        {hasActivePromotion && promotionBadge ? (
                            <span className="inline-flex rounded-full bg-red-500 px-3 py-1 text-xs font-semibold text-white">
                                {promotionBadge}
                            </span>
                        ) : null}
                    </div>

                    {showDescription && summaryParagraphs.length > 0 ? (
                        <div className="space-y-3 text-base leading-7 text-muted">
                            {summaryParagraphs.map((paragraph, index) => (
                                <p key={`summary-${index}`}>{paragraph}</p>
                            ))}
                        </div>
                    ) : null}

                    {featureHighlights.length > 0 ? (
                        <div className={`rounded-2xl p-4 ${isElectronics ? 'border border-sky-100 bg-sky-50/70' : isFashion ? 'border border-stone-200 bg-stone-50/80' : isCosmetics ? 'border border-rose-100 bg-rose-50/70' : 'border border-muted/15 bg-muted/5'}`}>
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted">
                                {t('product.keyHighlights') || 'Key Highlights'}
                            </p>
                            <ul className="mt-3 grid gap-2 sm:grid-cols-2">
                                {featureHighlights.map((feature, index) => (
                                    <li
                                        key={`${feature}-${index}`}
                                        className="rounded-xl bg-background px-3 py-2 text-sm font-medium text-text shadow-sm"
                                    >
                                        {feature}
                                    </li>
                                ))}
                            </ul>
                        </div>
                    ) : null}

                    {showVariantSelector && hasVariants && attributeGroups.length > 0 ? (
                        <>
                            <VariantSelector
                                attributeGroups={attributeGroups}
                                selectedAttributes={selectedAttributes}
                                onSelect={selectAttribute}
                                isValueAvailable={isValueAvailable}
                            />
                            {hasVariantResolutionError ? (
                                <p className="text-sm font-medium text-red-600">
                                    {t('product.invalidVariantCombination') || 'This option combination is unavailable'}
                                </p>
                            ) : null}
                        </>
                    ) : null}

                    {selectedVariantLabel || (showSku && displaySku) ? (
                        <div className="grid gap-3 sm:grid-cols-2">
                            {selectedVariantLabel ? (
                                <div className={`rounded-2xl px-4 py-3 shadow-sm ${isFashion ? 'border border-stone-200 bg-stone-50/70' : 'border border-muted/15 bg-background'}`}>
                                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted">
                                        {t('product.selectedOption') || 'Selected Option'}
                                    </p>
                                    <p className="mt-2 text-sm font-semibold text-text">{selectedVariantLabel}</p>
                                </div>
                            ) : null}

                            {showSku && displaySku ? (
                                <div className={`rounded-2xl px-4 py-3 shadow-sm ${isElectronics ? 'border border-slate-200 bg-slate-50/80' : 'border border-muted/15 bg-background'}`}>
                                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-muted">SKU</p>
                                    <p className="mt-2 text-sm font-semibold text-text">{displaySku}</p>
                                </div>
                            ) : null}
                        </div>
                    ) : null}

                    {showStockStatus ? (
                        <div className={`inline-flex items-center rounded-full px-3 py-1 text-sm ${canAddToCart ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}`}>
                            {stockLabel}
                        </div>
                    ) : null}

                    {showAddToCart ? (
                        <div className="flex flex-wrap gap-3">
                            <button
                                type="button"
                                onClick={handleAddToCart}
                                disabled={!canAddToCart || isAddingToCart}
                                className={actionButtonClass}
                            >
                                {isAddingToCart ? t('common.loading') : canAddToCart ? t('product.addToCart') : t('product.outOfStock')}
                            </button>

                            <AffiliateLink
                                href="/products"
                                className={secondaryButtonClass}
                            >
                                {t('common.products')}
                            </AffiliateLink>
                        </div>
                    ) : null}
                </div>
                </div>
            </div>
        </section>
    );
};

export default ProductHero;
