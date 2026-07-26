import React from 'react';
import { ProductCard } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import { useProducts } from '@/hooks';
import { useTranslation } from '@/i18n';

const FeaturedProducts: React.FC<any> = ({
    title,
    heading,
    limit,
    displayStyle,
    showBadge,
    badgeText,
}) => {
    const { t } = useTranslation();
    const resolvedLimit = Math.max(1, Number(limit || 8));
    const { products, isLoading, error } = useProducts({ page: 1, pageSize: resolvedLimit });
    const productItems = Array.isArray(products) ? products : [];

    const displayHeading = (title || heading || t('home.featuredProducts'));
    const visibleProducts = productItems.slice(0, resolvedLimit);
    const isCarousel = displayStyle === 'carousel';

    return (
        <section className="py-12 px-4 container mx-auto">
            <div className="flex justify-between items-end mb-8">
                <h2 className="text-2xl font-bold text-text">{displayHeading}</h2>
                <AffiliateLink href="/products" className="text-primary hover:underline rtl-flip">
                    {t('common.viewAll')} {'\u2192'}
                </AffiliateLink>
            </div>

            {isLoading ? (
                <SkeletonGrid count={resolvedLimit} />
            ) : error ? (
                <div className="text-center py-8 text-muted">{t('home.failedToLoad')}</div>
            ) : visibleProducts.length === 0 ? (
                <DataEmptyState
                    title={t('home.noProductsYet')}
                    message={t('home.productsWillAppear')}
                />
            ) : (
                <div className={isCarousel ? 'flex gap-6 overflow-x-auto pb-2' : 'grid grid-cols-2 md:grid-cols-4 gap-6'}>
                    {visibleProducts.map((product, index) => (
                        <div
                            key={product.id}
                            className="relative"
                            style={{ minWidth: isCarousel ? 240 : undefined }}
                        >
                            {showBadge !== false && badgeText && index < Math.min(4, visibleProducts.length) ? (
                                <span className="pointer-events-none absolute right-3 top-3 z-20 rounded-full bg-primary/90 px-2.5 py-1 text-xs font-medium text-white">
                                    {badgeText}
                                </span>
                            ) : null}
                            <ProductCard product={product} />
                        </div>
                    ))}
                </div>
            )}
        </section>
    );
};

export default FeaturedProducts;
