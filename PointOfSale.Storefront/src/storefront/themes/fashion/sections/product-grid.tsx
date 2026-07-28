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
                <div className="mb-10 bg-slate-950 px-5 py-4 text-xs font-black uppercase tracking-[0.28em] text-white sm:flex sm:items-center sm:justify-between">
                    <span>Runway shelf</span>
                    <span className="mt-2 block text-white/60 sm:mt-0">new shapes / styled edits / campaign buys</span>
                </div>

                <ThemeSectionHeader
                    themeId="fashion"
                    eyebrow={model.labels.eyebrowProducts}
                    title={model.title}
                    subtitle={model.subtitle}
                />

                {model.isLoading ? (
                    <SkeletonGrid count={model.pageSize} />
                ) : model.error ? (
                    <ThemeEmptyState themeId="fashion" title={model.labels.failedToLoad} />
                ) : model.isCuratedSelectionInvalid ? (
                    <ThemeEmptyState
                        themeId="fashion"
                        title="Selected products are still invalid"
                        message="Use valid product identifiers to render this curated shelf."
                    />
                ) : model.products.length === 0 ? (
                    <ThemeEmptyState
                        themeId="fashion"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                ) : (
                    <div
                        className={
                            model.layout === 'carousel'
                                ? 'grid auto-cols-[minmax(15rem,18rem)] grid-flow-col gap-5 overflow-x-auto pb-2'
                                : 'grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
                        }
                    >
                        {model.products.map((product, index) => (
                            <div key={product.id} className={index % 4 === 1 ? 'lg:mt-8' : undefined}>
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
