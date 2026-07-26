import React from 'react';
import { ProductCard } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import {
    normalizeHomepageCollectionLimit,
    normalizeHomepageGuidList,
    resolveHomepageCollectionSource,
} from '@/components/sections/homepageCollectionUtils';
import { useHomepageSectionProducts } from '@/hooks';
import { useTranslation } from '@/i18n';

const GUID_PATTERN =
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

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
        <section className="py-12 px-4 container mx-auto">
            <div className="flex justify-between items-end mb-8">
                <div className="max-w-2xl">
                    <h2 className="text-2xl font-bold text-text">{title || t('common.products')}</h2>
                    {subtitle ? (
                        <p className="mt-3 text-sm leading-6 text-muted">{subtitle}</p>
                    ) : null}
                </div>

                <AffiliateLink href="/products" className="text-primary hover:underline rtl-flip shrink-0">
                    {t('common.viewAll')} {'\u2192'}
                </AffiliateLink>
            </div>

            {isLoading ? (
                <SkeletonGrid count={pageSize} />
            ) : error ? (
                <div className="text-center py-8 text-muted">{t('home.failedToLoad')}</div>
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
                            ? 'grid auto-cols-[minmax(16rem,1fr)] grid-flow-col gap-6 overflow-x-auto pb-2'
                            : 'grid grid-cols-1 gap-6 sm:grid-cols-2 xl:grid-cols-4'
                    }
                >
                    {products.map((product) => (
                        <ProductCard key={product.id} product={product} />
                    ))}
                </div>
            )}
        </section>
    );
};

export default ProductGrid;
