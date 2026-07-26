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

const FASHION_CATEGORY_ART = [
    'bg-[radial-gradient(circle_at_top,#fbcfe8,transparent_30%),linear-gradient(180deg,#111827_0%,#9d174d_100%)]',
    'bg-[radial-gradient(circle_at_top,#d8b4fe,transparent_30%),linear-gradient(180deg,#172554_0%,#6d28d9_100%)]',
    'bg-[radial-gradient(circle_at_top,#fde68a,transparent_30%),linear-gradient(180deg,#422006_0%,#f97316_100%)]',
    'bg-[radial-gradient(circle_at_top,#bfdbfe,transparent_30%),linear-gradient(180deg,#0f172a_0%,#0ea5e9_100%)]',
    'bg-[radial-gradient(circle_at_top,#fecdd3,transparent_30%),linear-gradient(180deg,#4c0519_0%,#e11d48_100%)]',
    'bg-[radial-gradient(circle_at_top,#bbf7d0,transparent_30%),linear-gradient(180deg,#052e16_0%,#16a34a_100%)]',
];

function flattenCategories(categories: CategoryTreeDto[]): CategoryTreeDto[] {
    return categories.flatMap((category) => [category, ...flattenCategories(category.children || [])]);
}

const FashionCategoryCard: React.FC<{ category: CategoryTreeDto; index: number; title: string }> = ({
    category,
    index,
    title,
}) => {
    const artClass = FASHION_CATEGORY_ART[index % FASHION_CATEGORY_ART.length];

    return (
        <AffiliateLink
            href={`/products?category=${category.id}`}
            className="group block overflow-hidden bg-slate-950"
        >
            <article className="relative aspect-[3/4.2] overflow-hidden">
                <div className={`absolute inset-0 transition-transform duration-700 group-hover:scale-105 ${artClass}`} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(15,23,42,0.12)_15%,rgba(15,23,42,0.8)_100%)]" />

                <div className="absolute inset-0 flex items-center justify-center p-6">
                    <div className="rounded-full border border-white/15 bg-black/20 px-8 py-4 text-center backdrop-blur-sm">
                        <h3 className="text-2xl font-semibold tracking-[0.08em] text-white sm:text-3xl">
                            {title}
                        </h3>
                    </div>
                </div>
            </article>
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
        <section className="bg-white py-16 sm:py-20">
            <div className="mx-auto max-w-7xl px-6 lg:px-10">
                <div className="mb-10 max-w-2xl">
                    <p className="text-sm font-medium uppercase tracking-[0.34em] text-slate-500">
                        {t('category.shopByCategory')}
                    </p>
                    <h2 className="mt-3 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                        {displayTitle}
                    </h2>
                    {subtitle ? (
                        <p className="mt-4 text-base leading-7 text-slate-600">
                            {subtitle}
                        </p>
                    ) : null}
                </div>

                {isLoading ? (
                    <div className="grid grid-cols-1 gap-6 md:grid-cols-2 xl:grid-cols-3">
                        {[...Array(visibleCount)].map((_, index) => (
                            <div key={index} className="aspect-[3/4.2] animate-pulse bg-slate-200" />
                        ))}
                    </div>
                ) : error ? (
                    <div className="px-6 py-10 text-center text-slate-500">
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
                    <div className="grid grid-cols-1 gap-6 md:grid-cols-2 xl:grid-cols-3">
                        {visibleCategories.map((category, index) => (
                            <FashionCategoryCard
                                key={category.id}
                                category={category}
                                index={index}
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
