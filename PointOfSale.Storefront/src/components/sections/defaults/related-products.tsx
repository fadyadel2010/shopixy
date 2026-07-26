import React from 'react';
import { ProductCard } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import { useProducts } from '@/hooks';
import { useTranslation } from '@/i18n';
import type { ProductReadDto } from '@/types';

interface RelatedProductsProps {
    product?: ProductReadDto | null;
    title?: string;
    limit?: number;
    showPrice?: boolean;
    showRating?: boolean;
}

const RelatedProducts: React.FC<RelatedProductsProps> = ({
    product,
    title,
    limit = 4,
}) => {
    const { t } = useTranslation();
    const resolvedLimit = Math.max(1, Number(limit || 4));
    const { products, isLoading, error } = useProducts({
        page: 1,
        pageSize: resolvedLimit + 1,
        categoryId: product?.categoryId,
    });

    const visibleProducts = (products || [])
        .filter((item) => item.id !== product?.id)
        .slice(0, resolvedLimit);

    return (
        <section className="container mx-auto px-4 py-8">
            <div className="rounded-3xl border border-muted/20 bg-background p-6 shadow-sm">
                <div className="mb-6 flex items-end justify-between gap-4">
                    <h2 className="text-2xl font-bold text-text">{title || 'Related Products'}</h2>
                    <AffiliateLink href="/products" className="text-sm font-medium text-primary hover:underline">
                        {t('common.viewAll')}
                    </AffiliateLink>
                </div>

                {isLoading ? (
                    <SkeletonGrid count={resolvedLimit} />
                ) : error ? (
                    <div className="py-8 text-center text-muted">{t('home.failedToLoad')}</div>
                ) : visibleProducts.length === 0 ? (
                    <DataEmptyState
                        title={t('home.noProductsYet')}
                        message={t('home.productsWillAppear')}
                    />
                ) : (
                    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                        {visibleProducts.map((item) => (
                            <ProductCard key={item.id} product={item} />
                        ))}
                    </div>
                )}
            </div>
        </section>
    );
};

export default RelatedProducts;
