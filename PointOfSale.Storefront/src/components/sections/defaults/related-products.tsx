import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { DataEmptyState, SkeletonGrid } from '@/components/data';
import { useTranslation } from '@/i18n';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import type { ProductGridPresentationModel, RenderProductCard } from '@/storefront/presentation/models';
import { useTheme } from '@/storefront/themes/ThemeContext';
import type { ProductReadDto } from '@/types';

interface RelatedProductsProps {
    product?: ProductReadDto | null;
    title?: string;
    limit?: number;
    layoutVariant?: string;
    productGridModel?: ProductGridPresentationModel;
    renderProductCard?: RenderProductCard;
}

const RelatedProducts: React.FC<RelatedProductsProps> = ({
    title,
    layoutVariant = 'default',
    productGridModel: model,
    renderProductCard,
}) => {
    const { themeId } = useTheme();
    const profile = getPurchaseExperienceProfile(layoutVariant, themeId);
    const { t } = useTranslation();

    if (!model || !renderProductCard) {
        return null;
    }

    return (
        <section className={`${profile.sectionClassName} px-4 py-8`} data-purchase-experience={profile.variant}>
            <div className="container mx-auto">
                <div className={profile.relatedShellClassName}>
                    <div className="mb-6 flex items-end justify-between gap-4">
                        <h2 className="text-2xl font-bold text-text">{title || profile.relatedTitle}</h2>
                        {model.viewAllHref ? (
                            <AffiliateLink href={model.viewAllHref} className="text-sm font-medium text-primary hover:underline">
                                {t('common.viewAll')}
                            </AffiliateLink>
                        ) : null}
                    </div>

                    {model.isLoading ? (
                        <SkeletonGrid count={model.pageSize} />
                    ) : model.error ? (
                        <div className="py-8 text-center text-muted">{model.labels.failedToLoad}</div>
                    ) : model.products.length === 0 ? (
                        <DataEmptyState
                            title={model.emptyStateTitle}
                            message={model.emptyStateMessage}
                        />
                    ) : (
                        <div className={profile.relatedGridClassName}>
                            {model.products.map(renderProductCard)}
                        </div>
                    )}
                </div>
            </div>
        </section>
    );
};

export default RelatedProducts;
