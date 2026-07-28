import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { ProductMediaGallery, VariantSelector } from '@/components/products';
import type { ProductHeroPresentationModel } from '@/storefront/presentation/models';

interface ProductHeroProps {
    productHeroModel?: ProductHeroPresentationModel;
}

const ProductHero: React.FC<ProductHeroProps> = ({ productHeroModel: model }) => {
    if (!model) {
        return null;
    }

    const { product, profile } = model;

    return (
        <section
            className={`${profile.sectionClassName} px-4 py-8 pb-28 md:pb-8`}
            data-purchase-experience={profile.variant}
            aria-label={profile.philosophy}
        >
            <div className="container mx-auto">
                <div className={profile.heroGridClassName}>
                    {model.showMediaGallery ? (
                        <div className={profile.mediaWrapClassName}>
                            <ProductMediaGallery
                                product={product}
                                preferredMediaId={model.preferredMediaId}
                                preferredImageUrl={model.activeImage}
                                presentationVariant={profile.variant}
                                aspectClassName={profile.galleryAspectClassName}
                                maxThumbnails={profile.galleryMaxThumbnails}
                            />
                        </div>
                    ) : null}

                    <div className={profile.buyBoxClassName}>
                        {product.categoryName ? (
                            <span className={profile.categoryChipClassName}>
                                {product.categoryName}
                            </span>
                        ) : null}

                        <div className="space-y-3">
                            <h1 className={profile.titleClassName}>{model.localizedName}</h1>

                            <div className="space-y-1">
                                {model.pricing.compareAt ? (
                                    <>
                                        <div className={profile.salePriceClassName}>{model.pricing.current}</div>
                                        <div className={profile.comparePriceClassName}>{model.pricing.compareAt}</div>
                                    </>
                                ) : (
                                    <div className={profile.priceClassName}>{model.pricing.current}</div>
                                )}

                                {model.hasActivePromotion && model.promotionBadge ? (
                                    <span className={profile.promoBadgeClassName}>
                                        {model.promotionBadge}
                                    </span>
                                ) : null}
                            </div>
                        </div>

                        {model.showDescription && model.summaryParagraphs.length > 0 ? (
                            <div className="space-y-3 text-base leading-7 text-muted">
                                {model.summaryParagraphs.map((paragraph, index) => (
                                    <p key={`summary-${index}`}>{paragraph}</p>
                                ))}
                            </div>
                        ) : null}

                        {model.featureHighlights.length > 0 ? (
                            <div className={profile.highlightsPanelClassName}>
                                <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">
                                    {profile.highlightsLabel}
                                </p>
                                <ul className="mt-3 grid gap-2 sm:grid-cols-2">
                                    {model.featureHighlights.map((feature, index) => (
                                        <li key={`${feature}-${index}`} className={profile.highlightItemClassName}>
                                            {feature}
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        ) : null}

                        {model.showVariantSelector && model.hasVariants && model.attributeGroups.length > 0 ? (
                            <div className="space-y-3">
                                <VariantSelector
                                    attributeGroups={model.attributeGroups}
                                    selectedAttributes={model.selectedAttributes}
                                    onSelect={model.callbacks.onSelectAttribute}
                                    isValueAvailable={model.callbacks.isValueAvailable}
                                    presentationVariant={profile.variant}
                                />
                                {model.hasVariantResolutionError ? (
                                    <p className="text-sm font-medium text-red-600">
                                        {model.labels.invalidVariantCombination}
                                    </p>
                                ) : null}
                            </div>
                        ) : null}

                        {model.trustItems.length > 0 ? (
                            <div aria-label={profile.trustHeading}>
                                <p className="mb-3 text-xs font-semibold uppercase tracking-[0.16em] text-muted">
                                    {profile.trustHeading}
                                </p>
                                <dl className={profile.trustGridClassName}>
                                    {model.trustItems.map((item) => (
                                        <div key={`${item.label}:${item.value}`} className={profile.trustItemClassName}>
                                            <dt className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">
                                                {item.label}
                                            </dt>
                                            <dd className="mt-1 text-sm font-semibold text-text">{item.value}</dd>
                                        </div>
                                    ))}
                                </dl>
                            </div>
                        ) : null}

                        {model.showStockStatus ? (
                            <div className={`${profile.stockBadgeClassName} ${model.stockToneClass}`}>
                                {model.stockLabel}
                            </div>
                        ) : null}

                        {model.showAddToCart ? (
                            <div className="flex flex-col gap-3 sm:flex-row">
                                <button
                                    type="button"
                                    onClick={model.callbacks.onAddToCart}
                                    disabled={!model.canAddToCart || model.isAddingToCart}
                                    className={`${profile.ctaClassName} min-h-12 flex-1`}
                                >
                                    {model.addToCartLabel}
                                </button>

                                <AffiliateLink
                                    href="/products"
                                    className={`${profile.secondaryCtaClassName} inline-flex min-h-12 items-center justify-center`}
                                >
                                    {model.labels.products}
                                </AffiliateLink>
                            </div>
                        ) : null}
                    </div>
                </div>
            </div>

            {model.showAddToCart ? (
                <div
                    className={`fixed inset-x-0 bottom-0 z-40 border-t px-4 py-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))] md:hidden ${profile.mobileBarClassName}`}
                    role="region"
                    aria-label="Purchase summary"
                >
                    <div className="mx-auto flex max-w-screen-sm items-center gap-3">
                        <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-semibold">{model.localizedName}</p>
                            <p className="text-sm text-muted">{model.pricing.current}</p>
                        </div>
                        <button
                            type="button"
                            onClick={model.callbacks.onAddToCart}
                            disabled={!model.canAddToCart || model.isAddingToCart}
                            className={`${profile.ctaClassName} min-h-11 shrink-0 px-4 py-2 text-sm`}
                        >
                            {model.addToCartLabel}
                        </button>
                    </div>
                </div>
            ) : null}
        </section>
    );
};

export default ProductHero;
