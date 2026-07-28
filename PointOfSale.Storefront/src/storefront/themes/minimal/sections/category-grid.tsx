import React from 'react';
import { ThemeCategoryCard, ThemeEmptyState, ThemeLoadingSkeleton, ThemeSectionHeader } from '@/components/theme-components';
import type { CategoryGridPresentationModel } from '@/storefront/presentation/models';

interface CategoryGridProps {
    categoryGridModel?: CategoryGridPresentationModel;
}

const CategoryGrid: React.FC<CategoryGridProps> = ({ categoryGridModel: model }) => {
    if (!model) {
        return null;
    }

    return (
        <section className="bg-white py-14 sm:py-20">
            <div className="mx-auto max-w-6xl px-5 sm:px-6 lg:px-8">
                <ThemeSectionHeader themeId="minimal" title={model.title} subtitle={model.subtitle} />

                {model.isLoading ? (
                    <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
                        {[...Array(model.visibleCount)].map((_, index) => (
                            <ThemeLoadingSkeleton key={index} themeId="minimal" className="aspect-[4/3] p-4" />
                        ))}
                    </div>
                ) : model.error ? (
                    <ThemeEmptyState themeId="minimal" title={model.labels.failedToLoadCategories} />
                ) : model.categories.length === 0 ? (
                    <ThemeEmptyState
                        themeId="minimal"
                        title={model.emptyStateTitle}
                        message={model.emptyStateMessage}
                    />
                ) : (
                    <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
                        {model.categories.map((category, index) => (
                            <ThemeCategoryCard
                                key={category.id}
                                themeId="minimal"
                                href={category.href}
                                index={index}
                                title={category.title}
                            />
                        ))}
                    </div>
                )}
            </div>
        </section>
    );
};

export default CategoryGrid;
