import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import type { ProductGridPresentationModel, RenderProductCard } from '@/storefront/presentation/models';

interface ProductGridProps {
    productGridModel?: ProductGridPresentationModel;
    renderProductCard?: RenderProductCard;
}

const ProductGrid: React.FC<ProductGridProps> = ({ productGridModel: model, renderProductCard }) => {
    if (!model || !renderProductCard) {
        return null;
    }

    return (
        <section className="py-12 px-4 container mx-auto">
            <div className="flex justify-between items-end mb-8">
                <div className="max-w-2xl">
                    <h2 className="text-2xl font-bold text-text">{model.title}</h2>
                    {model.subtitle ? (
                        <p className="mt-3 text-sm leading-6 text-muted">{model.subtitle}</p>
                    ) : null}
                </div>

                {model.viewAllHref ? (
                    <AffiliateLink href={model.viewAllHref} className="text-primary hover:underline rtl-flip shrink-0">
                        {model.labels.viewAll} {'\u2192'}
                    </AffiliateLink>
                ) : null}
            </div>

            {model.isLoading ? (
                <SkeletonGrid count={model.pageSize} />
            ) : model.error ? (
                <div className="text-center py-8 text-muted">{model.labels.failedToLoad}</div>
            ) : model.isCuratedSelectionInvalid ? (
                <DataEmptyState
                    title={model.emptyStateTitle}
                    message={model.emptyStateMessage}
                />
            ) : model.products.length === 0 ? (
                <DataEmptyState
                    title={model.emptyStateTitle}
                    message={model.emptyStateMessage}
                />
            ) : (
                <div
                    className={
                        model.layout === 'carousel'
                            ? 'grid auto-cols-[minmax(16rem,1fr)] grid-flow-col gap-6 overflow-x-auto pb-2'
                            : 'grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-4'
                    }
                >
                    {model.products.map(renderProductCard)}
                </div>
            )}
        </section>
    );
};

export default ProductGrid;
