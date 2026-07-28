import { ThemeBadge } from '@/components/theme-components';

type BadgeProduct = {
    isFeatured?: boolean;
    hasActivePromotion?: boolean;
    promotionBadge?: string;
    pricing?: {
        discountBadge?: string | null;
    };
};

interface ProductBadgesProps {
    product: BadgeProduct;
    themeId?: string | null;
}

function getDiscountBadge(product: BadgeProduct): string | null {
    if (!product.hasActivePromotion) {
        return null;
    }

    const badge = product.pricing?.discountBadge ?? product.promotionBadge;
    if (badge?.trim()) {
        return badge.trim();
    }

    return null;
}

export default function ProductBadges({ product, themeId }: ProductBadgesProps) {
    const discountBadge = getDiscountBadge(product);

    if (!product.isFeatured && !discountBadge) {
        return null;
    }

    return (
        <div className="pointer-events-none absolute left-3 top-3 z-10 flex flex-col gap-2">
            {product.isFeatured ? (
                <ThemeBadge themeId={themeId}>
                    Featured
                </ThemeBadge>
            ) : null}

            {discountBadge ? (
                <ThemeBadge themeId={themeId} tone="accent">
                    {discountBadge}
                </ThemeBadge>
            ) : null}
        </div>
    );
}
