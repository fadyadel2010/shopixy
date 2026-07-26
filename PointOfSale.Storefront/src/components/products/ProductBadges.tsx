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

export default function ProductBadges({ product }: ProductBadgesProps) {
    const discountBadge = getDiscountBadge(product);

    if (!product.isFeatured && !discountBadge) {
        return null;
    }

    return (
        <div className="pointer-events-none absolute left-3 top-3 z-10 flex flex-col gap-2">
            {product.isFeatured ? (
                <span className="rounded-full bg-amber-500 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-white shadow-sm">
                    Featured
                </span>
            ) : null}

            {discountBadge ? (
                <span className="rounded-full bg-rose-500 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-white shadow-sm">
                    {discountBadge}
                </span>
            ) : null}
        </div>
    );
}
