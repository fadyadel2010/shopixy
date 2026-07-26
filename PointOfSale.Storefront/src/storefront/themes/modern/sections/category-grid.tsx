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

const CATEGORY_ART = [
    'bg-[radial-gradient(circle_at_top,#fef3c7,transparent_35%),linear-gradient(135deg,#f97316_0%,#fb7185_100%)]',
    'bg-[radial-gradient(circle_at_top,#dbeafe,transparent_35%),linear-gradient(135deg,#0f172a_0%,#2563eb_100%)]',
    'bg-[radial-gradient(circle_at_top,#dcfce7,transparent_35%),linear-gradient(135deg,#14532d_0%,#22c55e_100%)]',
    'bg-[radial-gradient(circle_at_top,#f5d0fe,transparent_35%),linear-gradient(135deg,#831843_0%,#c026d3_100%)]',
    'bg-[radial-gradient(circle_at_top,#fde68a,transparent_35%),linear-gradient(135deg,#78350f_0%,#f59e0b_100%)]',
    'bg-[radial-gradient(circle_at_top,#bfdbfe,transparent_35%),linear-gradient(135deg,#1e3a8a_0%,#38bdf8_100%)]',
];

const CATEGORY_ICONS = [
    '\u{1F4E6}',
    '\u{1F455}',
    '\u{1F4BB}',
    '\u{1F3E0}',
    '\u{1F3AE}',
    '\u{1F4DA}',
];

function flattenCategories(categories: CategoryTreeDto[]): CategoryTreeDto[] {
    return categories.flatMap((category) => [category, ...flattenCategories(category.children || [])]);
}

const ModernCategoryCard: React.FC<{ category: CategoryTreeDto; index: number; title: string }> = ({
    category,
    index,
    title,
}) => {
    const artClass = CATEGORY_ART[index % CATEGORY_ART.length];
    const artIcon = CATEGORY_ICONS[index % CATEGORY_ICONS.length];

    return (
        <AffiliateLink
            href={`/products?category=${category.id}`}
            className="group block overflow-hidden rounded-[30px] bg-slate-950 shadow-md shadow-slate-200/70 transition-transform duration-300 hover:-translate-y-1"
        >
            <div className="relative aspect-square overflow-hidden">
                <div className={`absolute inset-0 scale-100 transition-transform duration-500 group-hover:scale-110 ${artClass}`} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(15,23,42,0.06)_0%,rgba(15,23,42,0.82)_100%)]" />

                <div className="absolute right-5 top-5 flex h-14 w-14 items-center justify-center rounded-2xl border border-white/20 bg-white/10 text-3xl backdrop-blur-md">
                    {artIcon}
                </div>

                <div className="absolute inset-x-0 bottom-0 p-6">
                    <div className="rounded-3xl border border-white/15 bg-white/10 p-4 backdrop-blur-md">
                        <h3 className="text-2xl font-semibold text-white">
                            {title}
                        </h3>
                    </div>
                </div>
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
        <section className="py-16 sm:py-20">
            <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
                <div className="mb-10 max-w-2xl">
                    <p className="text-sm font-semibold uppercase tracking-[0.3em] text-primary/80">
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
                    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-3">
                        {[...Array(visibleCount)].map((_, index) => (
                            <div key={index} className="aspect-square animate-pulse rounded-[30px] bg-slate-200/70" />
                        ))}
                    </div>
                ) : error ? (
                    <div className="rounded-[28px] border border-slate-200 bg-white px-6 py-10 text-center text-slate-500">
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
                    <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-3">
                        {visibleCategories.map((category, index) => (
                            <ModernCategoryCard
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
