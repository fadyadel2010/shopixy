import React from 'react';
import { SkeletonGrid } from '@/components/data';
import { ThemeEmptyState, ThemeSectionHeader } from '@/components/theme-components';
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
        <section className="bg-background py-14 sm:py-20">
            <div className="mx-auto max-w-7xl px-5 sm:px-6 lg:px-8">
                <ThemeSectionHeader
                    themeId="sharp"
                    eyebrow={model.labels.eyebrowProducts}
                    title={model.title}
                    subtitle={model.subtitle}
                />

                {model.isLoading ? (
                    <SkeletonGrid count={model.pageSize} />
                ) : model.error ? (
                    <ThemeEmptyState themeId="sharp" title={model.labels.failedToLoad} />
                ) : model.isCuratedSelectionInvalid ? (
                    <ThemeEmptyState
                        themeId="sharp"
                        title="Selected products are still invalid"
                        message="Use valid product identifiers to render this curated shelf."
                    />
                ) : model.products.length === 0 ? (
                    <ThemeEmptyState
                        themeId="sharp"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                ) : (
                    <div
                        className={
                            model.layout === 'carousel'
                                ? 'grid auto-cols-[minmax(15rem,18rem)] grid-flow-col gap-6 overflow-x-auto pb-2'
                                : 'grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
                        }
                    >
                        {model.products.map(renderProductCard)}
                    </div>
                )}
            </div>
        </section>
    );
};

export default ProductGrid;
