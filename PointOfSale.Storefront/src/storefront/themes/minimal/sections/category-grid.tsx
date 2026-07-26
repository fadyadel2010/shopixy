import React from 'react';
import type { CategoryTreeDto } from '@/types';
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

function flattenCategories(categories: CategoryTreeDto[]): CategoryTreeDto[] {
    return categories.flatMap((category) => [category, ...flattenCategories(category.children || [])]);
}

const MinimalCategoryCard: React.FC<{ category: CategoryTreeDto; title: string }> = ({ category, title }) => {
    return (
        <AffiliateLink
            href={`/products?category=${category.id}`}
            className="block border border-slate-200 bg-white p-4"
        >
            <div className="aspect-square border border-slate-200 bg-[linear-gradient(135deg,#ffffff_0%,#f8fafc_100%)]" />
            <div className="mt-4">
                <h3 className="text-lg font-medium text-slate-900">{title}</h3>
            </div>
        </AffiliateLink>
    );
};

const CategoryGrid: React.FC<any> = ({ title, subtitle, source, maxItems, categoryIds, sectionId, columns = 3 }) => {
    const { t, language } = useTranslation();
    const { categories, isLoading, error } = useCategories();
    const requestedCategoryIds = normalizeHomepageGuidList(categoryIds);
    const collectionSource = resolveHomepageCollectionSource(source, categoryIds);
    const categoryLookup = new Map(flattenCategories(categories).map((category) => [category.id, category]));
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

    const getCategoryName = (category: CategoryTreeDto) => {
        if (language === 'ar' && category.nameAr) {
            return category.nameAr;
        }

        return category.name;
    };

    return (
        <section className="bg-white py-14">
            <div className="mx-auto max-w-7xl px-6 lg:px-10">
                <div className="mb-8 border-b border-slate-200 pb-5">
                    <h2 className="text-2xl font-medium tracking-tight text-slate-950">
                        {displayTitle}
                    </h2>
                    {subtitle ? (
                        <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-600">
                            {subtitle}
                        </p>
                    ) : null}
                </div>

                {isLoading ? (
                    <div className="grid grid-cols-1 gap-5 md:grid-cols-2 xl:grid-cols-3">
                        {[...Array(visibleCount)].map((_, index) => (
                            <div key={index} className="border border-slate-200 p-4">
                                <div className="aspect-square animate-pulse border border-slate-200 bg-slate-100" />
                            </div>
                        ))}
                    </div>
                ) : error ? (
                    <div className="border border-slate-200 px-6 py-10 text-center text-slate-500">
                        {t('home.failedToLoadCategories')}
                    </div>
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
                    <div className="grid grid-cols-1 gap-5 md:grid-cols-2 xl:grid-cols-3">
                        {visibleCategories.map((category) => (
                            <MinimalCategoryCard
                                key={category.id}
                                category={category}
                                title={getCategoryName(category)}
                            />
                        ))}
                    </div>
                )}
            </div>
        </section>
    );
};

export default CategoryGrid;
