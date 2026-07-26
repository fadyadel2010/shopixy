import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { ProductDetailSkeleton } from '@/components/common';
import { SectionRenderer } from '@/components/sections/SectionRenderer';
import { fetchCategories, fetchProductBySlug } from '@/services/api';
import { useAnalytics } from '@/analytics';
import { useLocalizedContent } from '@/i18n';
import { useStoreConfigStore, selectConfig } from '@/store';
import { resolveProductTemplate } from '@/storefront/products/ProductTemplateResolver';
import type { CategoryTreeDto, ProductReadDto } from '@/types';

export const ProductDetailsPage: React.FC = () => {
    const { slug } = useParams<{ slug: string }>();
    const { t } = useLocalizedContent();
    const config = useStoreConfigStore(selectConfig);
    const [product, setProduct] = useState<ProductReadDto | null>(null);
    const [categories, setCategories] = useState<CategoryTreeDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const analytics = useAnalytics();
    const productViewedRef = useRef<string | null>(null);

    useEffect(() => {
        const loadProduct = async () => {
            if (!slug) {
                return;
            }

            try {
                setLoading(true);

                const [productResult, categoriesResult] = await Promise.all([
                    fetchProductBySlug(slug),
                    fetchCategories(),
                ]);

                setProduct(productResult);
                setCategories(categoriesResult);
                setError(null);
            } catch (loadError) {
                console.error('[ProductDetailsPage] Failed to load product:', loadError);
                setError('Product not found');
            } finally {
                setLoading(false);
            }
        };

        loadProduct();
    }, [slug]);

    useEffect(() => {
        if (product && product.id && productViewedRef.current !== product.id) {
            analytics.productView(product.id, product.name);
            productViewedRef.current = product.id;
        }
    }, [analytics, product]);

    const resolvedTemplate = useMemo(
        () => resolveProductTemplate(product?.productTemplate || product?.storefrontLayout, config),
        [config, product?.productTemplate, product?.storefrontLayout]
    );

    useEffect(() => {
        if (!import.meta.env.DEV || !product) {
            return;
        }

        console.info('[ProductDetailsPage] Product template payload', {
            productId: product.id,
            slug: product.slug,
            productTemplate: product.productTemplate ?? null,
            storefrontLayout: product.storefrontLayout ?? null,
            resolvedTemplateId: resolvedTemplate.templateId,
            resolvedSectionTypes: resolvedTemplate.sections.map((section) => section.type),
        });
    }, [product, resolvedTemplate]);

    return (
        <Layout categories={categories}>
            {loading ? (
                <div className="container mx-auto px-4 py-8">
                    <ProductDetailSkeleton />
                </div>
            ) : error || !product ? (
                <div className="container mx-auto px-4 py-16 text-center">
                    <div className="mb-4 text-6xl">🔍</div>
                    <h1 className="mb-2 text-2xl font-bold text-text">{t('product.notFound')}</h1>
                    <p className="mb-6 text-muted">{t('product.notFoundDesc')}</p>
                    <a
                        href="/products"
                        className="inline-block rounded bg-primary px-6 py-2 text-white transition-opacity hover:opacity-90"
                    >
                        {t('common.products')}
                    </a>
                </div>
            ) : (
                <div data-product-template={resolvedTemplate.templateId} data-product-media-count={product.media?.length ?? 0}>
                    <div id="ai-product-context" data-ai-product-id={product.id} hidden aria-hidden="true" />
                    {resolvedTemplate.sections
                        .filter((section) => section.enabled)
                        .sort((left, right) => left.order - right.order)
                        .map((section) => (
                            <SectionRenderer
                                key={section.id}
                                section={section}
                                sectionProps={{ product }}
                            />
                        ))}
                </div>
            )}
        </Layout>
    );
};

export default ProductDetailsPage;
