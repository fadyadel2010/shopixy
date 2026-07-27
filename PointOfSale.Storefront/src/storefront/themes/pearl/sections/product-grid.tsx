/**
 * Pearl Theme — Product Grid (Phase T1.2C)
 *
 * REUSE DECISION
 * --------------
 * ADAPTATION of the inherited `fashion` product-grid template (~90% reuse):
 *   - Full CMS section data contract preserved (title, subtitle, layout,
 *     source, maxItems, productIds, sectionId, columns, rows — forwarded flat
 *     by SectionRenderer). NO new APIs, NO contract changes.
 *   - Product sourcing via `useHomepageSectionProducts` + homepageCollectionUtils
 *     (curated / random, GUID validation) — untouched.
 *   - Loading (SkeletonGrid) / error / DataEmptyState handling — untouched.
 *
 * Pearl identity changes (design report §4/§5/§9), presentation only:
 *   - Centered editorial header: gold eyebrow + navy Newsreader-light H2
 *     (matches the T1.2B category-grid rhythm).
 *   - Cards render via the theme-local `PearlProductCard` (see its header for
 *     the dispatcher gap analysis) instead of the storeConfig-dispatched
 *     `ProductCard` — presentation-only swap; all business logic is imported
 *     from the same platform modules.
 *   - Responsive grid: 2 cols mobile → 3 tablet → 4 desktop (reference site
 *     shows 4-across product rows) using the existing Tailwind system.
 */

import React from 'react';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import {
    normalizeHomepageCollectionLimit,
    normalizeHomepageGuidList,
    resolveHomepageCollectionSource,
} from '@/components/sections/homepageCollectionUtils';
import { useHomepageSectionProducts } from '@/hooks';
import { useTranslation } from '@/i18n';
import { PearlProductCard } from '../components/PearlProductCard';
import { PEARL_FONT_BODY, PEARL_FONT_DISPLAY } from '../layout/pearlTokens';

const GUID_PATTERN =
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** Responsive product grid — 2 / 3 / 4 columns (existing Tailwind responsive system). */
const PEARL_PRODUCT_GRID_CLASS = 'grid grid-cols-2 gap-4 sm:gap-6 md:grid-cols-3 xl:grid-cols-4';

const ProductGrid: React.FC<any> = ({
    title,
    subtitle,
    layout = 'grid',
    source,
    maxItems,
    productIds,
    sectionId,
    columns = 4,
    rows = 2,
}) => {
    const { t } = useTranslation();
    const pageSize = normalizeHomepageCollectionLimit(maxItems, columns * rows, 24);
    const requestedProductIds = normalizeHomepageGuidList(productIds);
    const collectionSource = resolveHomepageCollectionSource(source, productIds);
    const validRequestedIds = requestedProductIds?.filter((productId) => GUID_PATTERN.test(productId)) ?? [];
    const isCuratedConfig = collectionSource === 'curated';
    const isCuratedSelectionInvalid =
        isCuratedConfig &&
        (requestedProductIds?.length ?? 0) > 0 &&
        validRequestedIds.length === 0;
    const { products, isLoading, error } = useHomepageSectionProducts({
        productIds: requestedProductIds,
        pageSize,
        source: collectionSource,
        seedKey: `${sectionId ?? title ?? 'products'}:${layout}`,
    });

    return (
        <section
            className="bg-white py-16 sm:py-24"
            aria-label={title || t('common.products')}
        >
            <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-10">
                {/* Centered editorial section header (Pearl identity) */}
                <div className="mx-auto mb-12 max-w-2xl text-center sm:mb-16">
                    <p className={`text-[11px] font-medium uppercase tracking-[0.4em] text-[#C9A863] ${PEARL_FONT_BODY}`}>
                        {t('common.products')}
                    </p>
                    <h2
                        className={`mt-4 text-[clamp(2rem,4vw,3rem)] font-extralight leading-tight tracking-[0.02em] text-[#313E61] ${PEARL_FONT_DISPLAY}`}
                    >
                        {title || t('common.products')}
                    </h2>
                    {subtitle ? (
                        <p className={`mt-5 text-base leading-7 text-[#131316]/70 ${PEARL_FONT_BODY}`}>
                            {subtitle}
                        </p>
                    ) : null}
                </div>

                {isLoading ? (
                    <SkeletonGrid count={pageSize} />
                ) : error ? (
                    <div className="px-6 py-10 text-center text-slate-500">
                        {t('home.failedToLoad')}
                    </div>
                ) : isCuratedSelectionInvalid ? (
                    <DataEmptyState
                        title="Selected products are still invalid"
                        message="Use valid product identifiers to render this curated shelf."
                    />
                ) : products.length === 0 ? (
                    <DataEmptyState
                        title={isCuratedConfig ? 'No curated products yet' : t('home.noProductsYet')}
                        message={
                            isCuratedConfig
                                ? 'Pick storefront products to populate this section.'
                                : collectionSource === 'random'
                                  ? 'Random shelves pull from your current storefront catalog automatically.'
                                  : t('home.productsWillAppear')
                        }
                    />
                ) : (
                    <div
                        className={
                            layout === 'carousel'
                                ? 'grid auto-cols-[minmax(16rem,1fr)] grid-flow-col gap-4 overflow-x-auto pb-2 sm:gap-6'
                                : PEARL_PRODUCT_GRID_CLASS
                        }
                    >
                        {products.map((product) => (
                            <PearlProductCard key={product.id} product={product} />
                        ))}
                    </div>
                )}
            </div>
        </section>
    );
};

export default ProductGrid;
