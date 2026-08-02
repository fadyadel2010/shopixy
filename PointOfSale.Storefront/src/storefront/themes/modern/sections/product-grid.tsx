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
            <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
                <div className="mb-10 grid gap-4 border-y border-slate-200 py-5 text-xs font-semibold uppercase tracking-[0.18em] text-slate-500 sm:grid-cols-3">
                    <span>popular now</span>
                    <span className="sm:text-center">fast browsing</span>
                    <span className="sm:text-right">cart-ready picks</span>
                </div>

                <ThemeSectionHeader
                    themeId="modern"
                    eyebrow={model.labels.eyebrowProducts}
                    title={model.title}
                    subtitle={model.subtitle}
                    action={model.viewAllHref ? { label: model.labels.viewAll, href: model.viewAllHref } : undefined}
                />

                {model.isLoading ? (
                    <SkeletonGrid count={model.pageSize} />
                ) : model.error ? (
                    <ThemeEmptyState themeId="modern" title={model.labels.failedToLoad} />
                ) : model.isCuratedSelectionInvalid ? (
                    <ThemeEmptyState
                        themeId="modern"
                        title="Selected products are still invalid"
                        message="Use valid product identifiers to render this curated shelf."
                    />
                ) : model.products.length === 0 ? (
                    <ThemeEmptyState
                        themeId="modern"
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
                        {model.products.map(renderProductCard)}
                    </div>
                )}
            </div>
        </section>
    );
};

export default ProductGrid;
