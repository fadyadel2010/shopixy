import React from 'react';
import { SkeletonGrid } from '@/components/data';
import { ThemeEmptyState, ThemeSectionHeader } from '@/components/theme-components';
import type { ProductGridPresentationModel, RenderProductCard } from '@/storefront/presentation/models';
import { COSMETICS_FONT } from '../layout/cosmeticsTokens';

interface ProductGridProps {
    productGridModel?: ProductGridPresentationModel;
    renderProductCard?: RenderProductCard;
}

const ProductGrid: React.FC<ProductGridProps> = ({ productGridModel: model, renderProductCard }) => {
    if (!model || !renderProductCard) {
        return null;
    }

    const isCarousel = model.layout === 'carousel';

    return (
        <section className={`${COSMETICS_FONT} bg-background`}>
            <ThemeSectionHeader
                themeId="cosmetics"
                eyebrow={model.labels.eyebrowProducts}
                title={model.title}
                subtitle={model.subtitle}
                action={model.viewAllHref ? { label: model.labels.viewAll, href: model.viewAllHref } : undefined}
            />

            {model.isLoading ? (
                <div className="px-3 py-6">
                    <SkeletonGrid count={model.pageSize} />
                </div>
            ) : model.error ? (
                <div className="px-4 py-8">
                    <ThemeEmptyState themeId="cosmetics" title={model.labels.failedToLoad} />
                </div>
            ) : model.isCuratedSelectionInvalid ? (
                <div className="px-4 py-8">
                    <ThemeEmptyState
                        themeId="cosmetics"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                </div>
            ) : model.products.length === 0 ? (
                <div className="px-4 py-8">
                    <ThemeEmptyState
                        themeId="cosmetics"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                </div>
            ) : (
                <div
                    className={
                        isCarousel
                            ? 'grid auto-cols-[minmax(20rem,25rem)] grid-flow-col overflow-x-auto pb-2 [scrollbar-width:none]'
                            : 'grid grid-cols-2 gap-0 md:grid-cols-3 xl:grid-cols-4'
                    }
                >
                    {model.products.map(renderProductCard)}
                </div>
            )}
        </section>
    );
};

export default ProductGrid;
