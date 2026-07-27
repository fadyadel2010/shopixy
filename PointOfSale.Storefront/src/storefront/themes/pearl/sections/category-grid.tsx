/**
 * Pearl Theme — Category Grid (Phase T1.2B)
 *
 * REUSE DECISION
 * --------------
 * This file is an ADAPTATION of the inherited `fashion` category-grid template
 * (copied verbatim into `pearl/sections` during T1.0 scaffolding). The fashion
 * grid already implements ~85% of the requirement:
 *   - the full CMS section data contract (`title, subtitle, source, maxItems,
 *     categoryIds, sectionId, columns` — forwarded flat by SectionRenderer)
 *   - category sourcing via `useCategories` + homepageCollectionUtils
 *     (curated / random / default) — NO new APIs, NO new data contracts
 *   - loading skeleton / error / DataEmptyState handling
 *   - `AffiliateLink` navigation (affiliate attribution preserved)
 *   - the platform responsive grid utilities (Tailwind grid classes)
 * Per the 80% rule we ADAPT it; no business logic is duplicated and the
 * backend/category DTOs are untouched (CategoryTreeDto has no image field, so
 * the tile art fallback pattern from fashion is retained — platform image
 * handling is not replaced or reimplemented).
 *
 * What changed for Pearl identity (design report §2/§3/§4 item 5):
 *   - Section header centered: navy `#313E61` Newsreader serif light H2 with
 *     a thin gold `#C9A863` uppercase eyebrow (luxury editorial rhythm).
 *   - Cards: SQUARE corners (no pill/rounded chrome), image-first tile with the
 *     title centered BELOW the image in Jost 12px uppercase wide tracking.
 *   - Hover: subtle image scale (duration-700), soft dark overlay lift, and a
 *     title color transition to the red accent — pure CSS transitions only.
 *   - Grid: 2 cols mobile → 3 tablet → 4 desktop (reference site shows 4
 *     equal collection tiles) using the same Tailwind responsive system.
 */

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
import { PEARL_FONT_BODY, PEARL_FONT_DISPLAY } from '../layout/pearlTokens';

/**
 * Pearl tile art — muted dark editorial tints (CategoryTreeDto carries no
 * image URL, so like the fashion original we render a deterministic art
 * background per tile; when the platform adds category imagery this <div>
 * swaps for the platform image pipeline without touching the data flow).
 */
const PEARL_CATEGORY_ART = [
    'bg-[radial-gradient(circle_at_30%_20%,#3a3644_0%,transparent_55%),linear-gradient(180deg,#141218_0%,#26222e_100%)]',
    'bg-[radial-gradient(circle_at_70%_25%,#4a4030_0%,transparent_55%),linear-gradient(180deg,#1d1c1c_0%,#2e2a22_100%)]',
    'bg-[radial-gradient(circle_at_50%_15%,#31394f_0%,transparent_55%),linear-gradient(180deg,#141826_0%,#1e2437_100%)]',
    'bg-[radial-gradient(circle_at_35%_30%,#43303a_0%,transparent_55%),linear-gradient(180deg,#1a1418_0%,#2b2028_100%)]',
];

/** Responsive tile grid — 2 / 3 / 4 columns (existing Tailwind responsive system). */
const PEARL_CATEGORY_GRID_CLASS = 'grid grid-cols-2 gap-4 sm:gap-6 md:grid-cols-3 xl:grid-cols-4';

function flattenCategories(categories: CategoryTreeDto[]): CategoryTreeDto[] {
    return categories.flatMap((category) => [category, ...flattenCategories(category.children || [])]);
}

const PearlCategoryCard: React.FC<{ category: CategoryTreeDto; index: number; title: string }> = ({
    category,
    index,
    title,
}) => {
    const artClass = PEARL_CATEGORY_ART[index % PEARL_CATEGORY_ART.length];

    return (
        <AffiliateLink
            href={`/products?category=${category.id}`}
            className="group block overflow-hidden focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#DC2626]"
            aria-label={title}
        >
            <article className="bg-white">
                {/* Image-first tile — square corners, subtle scale + overlay hover */}
                <div className="relative aspect-[4/5] overflow-hidden bg-[#141218]">
                    <div
                        className={`absolute inset-0 transition-transform duration-700 ease-out group-hover:scale-105 ${artClass}`}
                        aria-hidden="true"
                    />
                    <div className="absolute inset-0 bg-black/0 transition-colors duration-500 group-hover:bg-black/15" />
                </div>

                {/* Centered title — Jost, uppercase, wide tracking (design report §3) */}
                <h3
                    className={`px-3 pb-6 pt-5 text-center text-[12px] uppercase tracking-[0.28em] text-[#212A2F] transition-colors duration-300 group-hover:text-[#DC2626] ${PEARL_FONT_BODY}`}
                >
                    {title}
                </h3>
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
        <section
            className="bg-white py-16 sm:py-24"
            aria-label={t('category.shopByCategory')}
        >
            <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-10">
                {/* Centered editorial section header (Pearl identity) */}
                <div className="mx-auto mb-12 max-w-2xl text-center sm:mb-16">
                    <p className={`text-[11px] font-medium uppercase tracking-[0.4em] text-[#C9A863] ${PEARL_FONT_BODY}`}>
                        {t('category.shopByCategory')}
                    </p>
                    <h2
                        className={`mt-4 text-[clamp(2rem,4vw,3rem)] font-extralight leading-tight tracking-[0.02em] text-[#313E61] ${PEARL_FONT_DISPLAY}`}
                    >
                        {displayTitle}
                    </h2>
                    {subtitle ? (
                        <p className={`mt-5 text-base leading-7 text-[#131316]/70 ${PEARL_FONT_BODY}`}>
                            {subtitle}
                        </p>
                    ) : null}
                </div>

                {isLoading ? (
                    <div className={PEARL_CATEGORY_GRID_CLASS}>
                        {[...Array(visibleCount)].map((_, index) => (
                            <div key={index} className="aspect-[4/5] animate-pulse bg-slate-200" />
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
                    <div className={PEARL_CATEGORY_GRID_CLASS}>
                        {visibleCategories.map((category, index) => (
                            <PearlCategoryCard
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
