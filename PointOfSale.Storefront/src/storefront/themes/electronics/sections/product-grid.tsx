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
        <section className="bg-slate-100 py-12 sm:py-16">
            <div className="mx-auto max-w-7xl px-5 sm:px-6 lg:px-8">
                <div className="mb-6 grid gap-3 font-mono text-[11px] font-bold uppercase tracking-[0.08em] text-slate-600 sm:grid-cols-4">
                    <span className="border border-slate-300 bg-white px-3 py-2">price/value</span>
                    <span className="border border-slate-300 bg-white px-3 py-2">availability</span>
                    <span className="border border-slate-300 bg-white px-3 py-2">compatibility</span>
                    <span className="border border-slate-300 bg-white px-3 py-2">warranty</span>
                </div>

                <ThemeSectionHeader
                    themeId="electronics"
                    eyebrow={model.labels.eyebrowProducts}
                    title={model.title}
                    subtitle={model.subtitle}
                    action={model.viewAllHref ? { label: model.labels.viewAll, href: model.viewAllHref } : undefined}
                />

                {model.isLoading ? (
                    <SkeletonGrid count={model.pageSize} />
                ) : model.error ? (
                    <ThemeEmptyState themeId="electronics" title={model.labels.failedToLoad} />
                ) : model.isCuratedSelectionInvalid ? (
                    <ThemeEmptyState
                        themeId="electronics"
                        title="Selected products are still invalid"
                        message="Use valid product identifiers to render this curated shelf."
                    />
                ) : model.products.length === 0 ? (
                    <ThemeEmptyState
                        themeId="electronics"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                ) : (
                    <div
                        className={
                            model.layout === 'carousel'
                                ? 'grid auto-cols-[minmax(15rem,18rem)] grid-flow-col gap-4 overflow-x-auto pb-2'
                                : 'grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
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
