import React from 'react';
import { ThemeCategoryCard, ThemeEmptyState, ThemeLoadingSkeleton, ThemeSectionHeader } from '@/components/theme-components';
import type { CategoryGridPresentationModel } from '@/storefront/presentation/models';
import { COSMETICS_FONT } from '../layout/cosmeticsTokens';

interface CategoryGridProps {
    categoryGridModel?: CategoryGridPresentationModel;
}

const CategoryGrid: React.FC<CategoryGridProps> = ({ categoryGridModel: model }) => {
    if (!model) {
        return null;
    }

    return (
        <section className={`${COSMETICS_FONT} bg-white`}>
            <ThemeSectionHeader
                themeId="cosmetics"
                eyebrow={model.labels.eyebrowCategories}
                title={model.title}
                subtitle={model.subtitle}
            />

            {model.isLoading ? (
                <div className="flex snap-x gap-3 overflow-x-auto px-4 pb-5 [scrollbar-width:none] md:grid md:grid-cols-4 md:gap-3 md:overflow-visible md:px-0">
                    {[...Array(model.visibleCount)].map((_, index) => (
                        <ThemeLoadingSkeleton key={index} themeId="cosmetics" className="h-80 min-w-[82vw] md:h-[28rem] md:min-w-0" />
                    ))}
                </div>
            ) : model.error ? (
                <div className="px-4 py-8">
                    <ThemeEmptyState themeId="cosmetics" title={model.labels.failedToLoadCategories} />
                </div>
            ) : model.categories.length === 0 ? (
                <div className="px-4 py-8">
                    <ThemeEmptyState
                        themeId="cosmetics"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                </div>
            ) : (
                <div className="flex snap-x snap-mandatory gap-3 overflow-x-auto px-4 pb-5 [scrollbar-width:none] md:grid md:grid-cols-4 md:gap-3 md:overflow-visible md:px-0">
                    {model.categories.map((category, index) => (
                        <div key={category.id} className="min-w-[82vw] snap-start md:min-w-0">
                            <ThemeCategoryCard
                                themeId="cosmetics"
                                href={category.href}
                                index={index}
                                title={category.title}
                            />
                        </div>
                    ))}
                </div>
            )}
        </section>
    );
};

export default CategoryGrid;
