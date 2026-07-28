import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState } from '@/components/data';
import type { CategoryGridPresentationModel } from '@/storefront/presentation/models';

const CATEGORY_ICONS = [
    '\u{1F4E6}',
    '\u{1F455}',
    '\u{1F4BB}',
    '\u{1F3E0}',
    '\u{1F3AE}',
    '\u{1F4DA}',
    '\u{1F3A8}',
    '\u{1F37D}\uFE0F',
    '\u{1F484}',
    '\u{1F3C3}',
];

const GRID_COLUMN_CLASSES: Record<number, string> = {
    1: 'grid-cols-1',
    2: 'grid-cols-2',
    3: 'grid-cols-2 md:grid-cols-3',
    4: 'grid-cols-2 lg:grid-cols-4',
};

interface CategoryGridProps {
    categoryGridModel?: CategoryGridPresentationModel;
    columns?: number;
}

const CategoryGrid: React.FC<CategoryGridProps> = ({ categoryGridModel: model, columns = 3 }) => {
    if (!model) {
        return null;
    }

    const getCategoryIcon = (index: number): string => CATEGORY_ICONS[index % CATEGORY_ICONS.length];
    const gridColumnClass = GRID_COLUMN_CLASSES[columns] || GRID_COLUMN_CLASSES[3];

    return (
        <section className="py-12 px-4 container mx-auto">
            <div className="text-center mb-8">
                <h2 className="text-2xl font-bold text-text">{model.title}</h2>
                {model.subtitle ? (
                    <p className="mt-3 mx-auto max-w-2xl text-sm leading-6 text-muted">{model.subtitle}</p>
                ) : null}
            </div>

            {model.isLoading ? (
                <div className={`grid ${gridColumnClass} gap-4 sm:gap-5`}>
                    {[...Array(model.visibleCount)].map((_, index) => (
                        <div key={index} className="h-36 rounded-lg bg-muted/10 animate-pulse sm:h-40" />
                    ))}
                </div>
            ) : model.error ? (
                <div className="text-center py-8 text-muted">{model.labels.failedToLoadCategories}</div>
            ) : model.categories.length === 0 ? (
                <DataEmptyState
                    title={model.emptyStateTitle}
                    message={model.emptyStateMessage}
                />
            ) : (
                <div className={`grid ${gridColumnClass} gap-4 sm:gap-5`}>
                    {model.categories.map((category, index) => (
                        <AffiliateLink
                            key={category.id}
                            href={category.href}
                            className="group relative flex h-36 flex-col items-center justify-center overflow-hidden rounded-lg border border-muted/10 bg-gradient-to-br from-primary/10 to-secondary/10 px-3 text-center transition-all hover:-translate-y-1 hover:border-primary/30 hover:shadow-lg sm:h-40"
                        >
                            <div className="mb-3 text-3xl transition-transform group-hover:scale-110 sm:text-4xl">
                                {getCategoryIcon(index)}
                            </div>
                            <span className="overflow-hidden text-sm font-semibold leading-5 text-text transition-colors [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] group-hover:text-primary sm:text-base">
                                {category.title}
                            </span>
                        </AffiliateLink>
                    ))}
                </div>
            )}
        </section>
    );
};

export default CategoryGrid;
