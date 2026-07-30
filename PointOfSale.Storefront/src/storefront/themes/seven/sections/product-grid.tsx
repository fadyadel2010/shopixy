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
        <section className="bg-white py-12 sm:py-16">
            <div className="mx-auto max-w-7xl px-5 sm:px-6 lg:px-8">
                <ThemeSectionHeader
                    themeId="seven"
                    eyebrow={model.labels.eyebrowProducts}
                    title={model.title}
                    subtitle={model.subtitle}
                />

                {model.isLoading ? (
                    <SkeletonGrid count={model.pageSize} />
                ) : model.error ? (
                    <ThemeEmptyState themeId="seven" title={model.labels.failedToLoad} />
                ) : model.isCuratedSelectionInvalid ? (
                    <ThemeEmptyState
                        themeId="seven"
                        title="Selected products are still invalid"
                        message="Use valid product identifiers to render this curated shelf."
                    />
                ) : model.products.length === 0 ? (
                    <ThemeEmptyState
                        themeId="seven"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                ) : (
                    <div
                        className={
                            model.layout === 'carousel'
                                ? 'grid auto-cols-[minmax(15rem,18rem)] grid-flow-col gap-5 overflow-x-auto pb-2'
                                : 'grid grid-cols-1 gap-x-6 gap-y-12 sm:grid-cols-2 xl:grid-cols-3'
                        }
                    >
                        {model.products.map((product, index) => (
                            <div key={product.id}>
                                {renderProductCard(product, index)}
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </section>
    );
};

export default ProductGrid;
