import React from 'react';
import { useLocalizedContent } from '@/i18n';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import { useTheme } from '@/storefront/themes/ThemeContext';
import type { ProductReadDto } from '@/types';
import { parseProductNarrative } from '@/utils';

interface ProductSpecsProps {
    product?: ProductReadDto | null;
    title?: string;
    useDescription?: boolean;
    layoutVariant?: 'default' | 'modern' | 'minimal' | 'electronics' | 'fashion' | 'pearl' | 'cosmetics' | string;
}

const ProductSpecs: React.FC<ProductSpecsProps> = ({
    product,
    title,
    useDescription = true,
    layoutVariant = 'default',
}) => {
    const { themeId } = useTheme();
    const profile = getPurchaseExperienceProfile(layoutVariant, themeId);
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
    if (!hasRenderableContent) {
        return null;
    }

    return (
        <section className={`${profile.sectionClassName} px-4 py-8`} data-purchase-experience={profile.variant}>
            <div className="container mx-auto">
                <div className={profile.specsShellClassName}>
                    <h2 className="text-2xl font-bold text-text">{title || profile.specsTitle}</h2>

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
                                <h3 className="text-lg font-semibold text-text">{profile.featureHeading}</h3>
                                <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] text-primary">
                                    {content.features.length} highlights
                                </span>
                            </div>
                            <div className="grid gap-3 md:grid-cols-2">
                                {content.features.map((feature, index) => (
                                    <div
                                        key={`${feature}-${index}`}
                                        className={profile.specsFeatureCardClassName}
                                    >
                                        {feature}
                                    </div>
                                ))}
                            </div>
                        </div>
                    ) : null}

                    {hasFaq ? (
                        <div className="mt-8">
                            <h3 className="text-lg font-semibold text-text">{profile.faqHeading}</h3>
                            <div className="mt-4 space-y-3">
                                {content.faqItems.map((item, index) => (
                                    <div
                                        key={`${item.question || 'faq'}-${index}`}
                                        className={profile.specsFaqCardClassName}
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
            </div>
        </section>
    );
};

export default ProductSpecs;
