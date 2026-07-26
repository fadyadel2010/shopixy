import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState } from '@/components/data';
import {
    normalizeHomepageCollectionLimit,
    normalizeHomepageGuidList,
    resolveHomepageCollectionSource,
    selectHomepageCollectionItems,
} from '@/components/sections/homepageCollectionUtils';
import { useCategories } from '@/hooks';
import { useTranslation } from '@/i18n';
import type { CategoryTreeDto } from '@/types';

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

function flattenCategories(categories: CategoryTreeDto[]): CategoryTreeDto[] {
    return categories.flatMap((category) => [category, ...flattenCategories(category.children || [])]);
}

const CategoryGrid: React.FC<any> = ({ title, subtitle, source, maxItems, categoryIds, sectionId, columns = 3 }) => {
    const { t, language } = useTranslation();
    const { categories, isLoading, error } = useCategories();
    const requestedCategoryIds = normalizeHomepageGuidList(categoryIds);
    const collectionSource = resolveHomepageCollectionSource(source, categoryIds);
    const categoryLookup = new Map(flattenCategories(categories).map((category) => [category.id, category]));

    const getCategoryIcon = (index: number): string => CATEGORY_ICONS[index % CATEGORY_ICONS.length];

    const getCategoryName = (category: { name: string; nameAr?: string }) => {
        if (language === 'ar' && category.nameAr) {
            return category.nameAr;
        }

        return category.name;
    };

    const displayTitle = (!title || title === 'Shop by Category')
        ? t('category.shopByCategory')
        : title;
    const visibleCount = normalizeHomepageCollectionLimit(maxItems, Math.max(columns * 2, 6), 18);
    const curatedCategoryIds = requestedCategoryIds ?? [];
    const hasCuratedSelection = collectionSource === 'curated';
    const visibleCategories = hasCuratedSelection
        ? curatedCategoryIds
            .map((categoryId) => categoryLookup.get(categoryId))
            .filter((category): category is CategoryTreeDto => Boolean(category))
        : selectHomepageCollectionItems(categories, {
            limit: visibleCount,
            source: collectionSource,
            seedKey: `${sectionId ?? title ?? 'categories'}:categories`,
            getKey: (category) => category.id,
        });

    return (
        <section className="py-12 px-4 container mx-auto">
            <div className="text-center mb-8">
                <h2 className="text-2xl font-bold text-text">{displayTitle}</h2>
                {subtitle ? (
                    <p className="mt-3 mx-auto max-w-2xl text-sm leading-6 text-muted">{subtitle}</p>
                ) : null}
            </div>

            {isLoading ? (
                <div className={`grid grid-cols-2 md:grid-cols-${columns} gap-6`}>
                    {[...Array(visibleCount)].map((_, index) => (
                        <div key={index} className="h-40 bg-muted/10 rounded-xl animate-pulse" />
                    ))}
                </div>
            ) : error ? (
                <div className="text-center py-8 text-muted">{t('home.failedToLoadCategories')}</div>
            ) : visibleCategories.length === 0 ? (
                <DataEmptyState
                    title={hasCuratedSelection ? 'No curated categories yet' : t('home.noCategoriesYet')}
                    message={
                        hasCuratedSelection
                            ? 'Pick storefront categories to populate this section.'
                            : collectionSource === 'random'
                              ? 'Random category grids pull from your storefront category tree automatically.'
                              : t('home.categoriesWillAppear')
                    }
                />
            ) : (
                <div className={`grid grid-cols-2 md:grid-cols-${columns} gap-6`}>
                    {visibleCategories.map((category, index) => (
                        <AffiliateLink
                            key={category.id}
                            href={`/products?category=${category.id}`}
                            className="group relative rounded-xl overflow-hidden h-40 bg-gradient-to-br from-primary/10 to-secondary/10 flex flex-col items-center justify-center hover:shadow-lg transition-all border border-muted/10 hover:border-primary/30"
                        >
                            <div className="text-4xl mb-3 group-hover:scale-110 transition-transform">
                                {getCategoryIcon(index)}
                            </div>
                            <span className="text-lg font-semibold text-text group-hover:text-primary transition-colors">
                                {getCategoryName(category)}
                            </span>
                        </AffiliateLink>
                    ))}
                </div>
            )}
        </section>
    );
};

export default CategoryGrid;
