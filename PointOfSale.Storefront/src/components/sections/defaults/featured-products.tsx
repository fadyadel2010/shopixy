import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import type { ProductGridPresentationModel, RenderProductCard } from '@/storefront/presentation/models';

interface FeaturedProductsProps {
    productGridModel?: ProductGridPresentationModel;
    renderProductCard?: RenderProductCard;
    displayStyle?: string;
    showBadge?: boolean;
    badgeText?: string;
}

const FeaturedProducts: React.FC<FeaturedProductsProps> = ({
    productGridModel: model,
    renderProductCard,
    displayStyle,
    showBadge,
    badgeText,
}) => {
    if (!model || !renderProductCard) {
        return null;
    }

    const isCarousel = displayStyle === 'carousel';

    return (
        <section className="py-12 px-4 container mx-auto">
            <div className="flex justify-between items-end mb-8">
                <h2 className="text-2xl font-bold text-text">{model.title}</h2>
                <AffiliateLink href="/products" className="text-primary hover:underline rtl-flip">
                    {model.labels.viewAll} {'\u2192'}
                </AffiliateLink>
            </div>

            {model.isLoading ? (
                <SkeletonGrid count={model.pageSize} />
            ) : model.error ? (
                <div className="text-center py-8 text-muted">{model.labels.failedToLoad}</div>
            ) : model.products.length === 0 ? (
                <DataEmptyState
                    title={model.emptyStateTitle}
                    message={model.emptyStateMessage}
                />
            ) : (
                <div className={isCarousel ? 'flex gap-6 overflow-x-auto pb-2' : 'grid grid-cols-2 md:grid-cols-4 gap-6'}>
                    {model.products.map((product, index) => (
                        <div
                            key={product.id}
                            className="relative"
                            style={{ minWidth: isCarousel ? 240 : undefined }}
                        >
                            {showBadge !== false && badgeText && index < Math.min(4, model.products.length) ? (
                                <span className="pointer-events-none absolute right-3 top-3 z-20 rounded-full bg-primary/90 px-2.5 py-1 text-xs font-medium text-white">
                                    {badgeText}
                                </span>
                            ) : null}
                            {renderProductCard(product, index)}
                        </div>
                    ))}
                </div>
            )}
        </section>
    );
};

export default FeaturedProducts;
