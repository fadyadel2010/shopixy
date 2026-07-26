import React from 'react';
import { useLocalizedContent } from '@/i18n';
import type { ProductReadDto } from '@/types';
import { parseProductNarrative } from '@/utils';

interface ProductSpecsProps {
    product?: ProductReadDto | null;
    title?: string;
    useDescription?: boolean;
    layoutVariant?: 'default' | 'electronics' | 'fashion' | 'cosmetics' | string;
}

const ProductSpecs: React.FC<ProductSpecsProps> = ({
    product,
    title,
    useDescription = true,
    layoutVariant = 'default',
}) => {
    const { localize } = useLocalizedContent();

    if (!product) {
        return null;
    }

    const content = parseProductNarrative(localize(product, 'description') || product.description);
    const detailParagraphs = content.overviewParagraphs.length > 1
        ? content.detailParagraphs
        : [];
    const hasOverview = useDescription && detailParagraphs.length > 0;
    const hasFeatures = content.features.length > 0;
    const hasFaq = content.faqItems.length > 0;
    const hasRenderableContent = hasOverview || hasFeatures || hasFaq;
    const isElectronics = layoutVariant === 'electronics';
    const isFashion = layoutVariant === 'fashion';
    const isCosmetics = layoutVariant === 'cosmetics';
    const shellClass = isElectronics
        ? 'rounded-[2rem] border border-slate-200 bg-[linear-gradient(180deg,_#ffffff_0%,_#f8fbff_100%)] p-6 shadow-[0_24px_70px_-44px_rgba(15,23,42,0.35)]'
        : isFashion
            ? 'rounded-[2rem] border border-stone-200 bg-[linear-gradient(180deg,_#fffdf8_0%,_#ffffff_100%)] p-6 shadow-[0_24px_70px_-44px_rgba(120,53,15,0.28)]'
            : isCosmetics
                ? 'rounded-[2rem] border border-rose-100 bg-[linear-gradient(180deg,_#fff7fb_0%,_#ffffff_100%)] p-6 shadow-[0_24px_70px_-44px_rgba(190,24,93,0.22)]'
                : 'rounded-3xl border border-muted/20 bg-background p-6 shadow-sm';
    const featureCardClass = isElectronics
        ? 'rounded-2xl border border-slate-200 bg-slate-50/80 px-4 py-4 text-sm leading-7 text-text'
        : isFashion
            ? 'rounded-2xl border border-stone-200 bg-stone-50 px-4 py-4 text-sm leading-7 text-stone-800'
            : isCosmetics
                ? 'rounded-2xl border border-rose-100 bg-rose-50/80 px-4 py-4 text-sm leading-7 text-rose-900'
                : 'rounded-2xl border border-muted/15 bg-muted/5 px-4 py-4 text-sm leading-7 text-text';
    const faqCardClass = isElectronics
        ? 'rounded-2xl border border-slate-200 bg-white px-5 py-4 shadow-sm'
        : isFashion
            ? 'rounded-2xl border border-stone-200 bg-stone-50/60 px-5 py-4 shadow-sm'
            : isCosmetics
                ? 'rounded-2xl border border-rose-100 bg-white px-5 py-4 shadow-sm'
                : 'rounded-2xl border border-muted/15 bg-background px-5 py-4 shadow-sm';

    if (!hasRenderableContent) {
        return null;
    }

    return (
        <section className="container mx-auto px-4 py-8">
            <div className={shellClass}>
                <h2 className="text-2xl font-bold text-text">{title || 'Product Details'}</h2>

                {hasOverview ? (
                    <div className="mt-6 space-y-4 text-base leading-8 text-muted">
                        {detailParagraphs.map((paragraph, index) => (
                            <p key={`detail-paragraph-${index}`}>{paragraph}</p>
                        ))}
                    </div>
                ) : null}

                {hasFeatures ? (
                    <div className="mt-8">
                        <div className="mb-4 flex items-center justify-between gap-3">
                            <h3 className="text-lg font-semibold text-text">Features</h3>
                            <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-primary">
                                {content.features.length} highlights
                            </span>
                        </div>
                        <div className="grid gap-3 md:grid-cols-2">
                            {content.features.map((feature, index) => (
                                <div
                                    key={`${feature}-${index}`}
                                    className={featureCardClass}
                                >
                                    {feature}
                                </div>
                            ))}
                        </div>
                    </div>
                ) : null}

                {hasFaq ? (
                    <div className="mt-8">
                        <h3 className="text-lg font-semibold text-text">Frequently Asked Questions</h3>
                        <div className="mt-4 space-y-3">
                            {content.faqItems.map((item, index) => (
                                <div
                                    key={`${item.question || 'faq'}-${index}`}
                                    className={faqCardClass}
                                >
                                    {item.question ? (
                                        <p className="text-sm font-semibold text-text">{item.question}</p>
                                    ) : null}
                                    {item.answer ? (
                                        <p className="mt-2 text-sm leading-7 text-muted">{item.answer}</p>
                                    ) : null}
                                </div>
                            ))}
                        </div>
                    </div>
                ) : null}
            </div>
        </section>
    );
};

export default ProductSpecs;
