/**
 * Order Items List Component
 * 
 * Displays list of products in an order with images,
 * names, quantities, and prices.
 * Read-only - no quantity changes allowed.
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import { formatMoney, resolveProductImage } from '@/utils';
import type { OrderItemDto } from '@/types/order';

// ============================================================================
// Types
// ============================================================================

interface OrderItemsListProps {
    items: OrderItemDto[];
    currency: string;
    className?: string;
}

// ============================================================================
// Product Image Component
// ============================================================================

interface ProductImageProps {
    src?: string;
    alt: string;
}

const ProductImage: React.FC<ProductImageProps> = ({ src, alt }) => {
    const [imageError, setImageError] = React.useState(false);

    if (!src || imageError) {
        return (
            <div
                className="w-16 h-16 bg-muted/10 rounded-lg flex items-center justify-center flex-shrink-0"
                aria-label={`Image for ${alt}`}
            >
                <svg
                    className="w-8 h-8 text-muted/30"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                >
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={1.5}
                        d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"
                    />
                </svg>
            </div>
        );
    }

    return (
        <img
            src={src}
            alt={alt}
            className="w-16 h-16 object-cover rounded-lg flex-shrink-0"
            onError={() => setImageError(true)}
            loading="lazy"
        />
    );
};

// ============================================================================
// Order Item Row Component
// ============================================================================

interface OrderItemRowProps {
    item: OrderItemDto;
    currency: string;
}

const OrderItemRow: React.FC<OrderItemRowProps> = ({ item, currency }) => {
    const { t } = useTranslation();
    const imageSrc = resolveProductImage(item);

    return (
        <li className="flex gap-4 py-4 border-b border-muted/10 last:border-b-0">
            {/* Product Image */}
            <ProductImage
                src={imageSrc}
                alt={item.productName}
            />

            {/* Product Details */}
            <div className="flex-1 min-w-0">
                <div className="flex justify-between items-start gap-2">
                    <div className="min-w-0">
                        <h3 className="font-medium text-text line-clamp-2">
                            {item.productName}
                        </h3>
                        <p className="text-xs text-muted mt-0.5 font-mono">
                            SKU: {item.sku}
                        </p>
                    </div>

                    {/* Line Total - Desktop */}
                    <div className="hidden sm:block text-right flex-shrink-0">
                        <p className="font-semibold text-text">
                            {formatMoney(item.lineTotal, currency)}
                        </p>
                    </div>
                </div>

                {/* Quantity and Price */}
                <div className="flex items-center justify-between mt-2">
                    <div className="flex items-center gap-3 text-sm">
                        <span className="text-muted">
                            {t('product.quantity')}: <span className="text-text font-medium">{item.quantity}</span>
                        </span>
                        <span className="text-muted hidden sm:inline">
                            × {formatMoney(item.unitPrice, currency)}
                        </span>
                    </div>

                    {/* Line Total - Mobile */}
                    <div className="sm:hidden">
                        <p className="font-semibold text-text">
                            {formatMoney(item.lineTotal, currency)}
                        </p>
                    </div>
                </div>
            </div>
        </li>
    );
};

// ============================================================================
// Main Component
// ============================================================================

export const OrderItemsList: React.FC<OrderItemsListProps> = ({
    items,
    currency,
    className = '',
}) => {
    const { t } = useTranslation();
    if (!items || items.length === 0) {
        return (
            <div className={`bg-muted/5 rounded-xl p-8 text-center ${className}`}>
                <p className="text-muted">{t('order.tracking.noItems')}</p>
            </div>
        );
    }

    const totalItems = items.reduce((sum, item) => sum + item.quantity, 0);

    return (
        <div
            className={`
                bg-background border border-muted/10 rounded-xl overflow-hidden
                ${className}
            `}
        >
            {/* Header */}
            <div className="bg-muted/5 px-5 py-4 border-b border-muted/10">
                <h2 className="text-lg font-semibold text-text flex items-center gap-2">
                    <svg
                        className="w-5 h-5 text-primary"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4"
                        />
                    </svg>
                    {t('order.tracking.items')}
                    <span className="text-sm font-normal text-muted ms-auto">
                        {t(totalItems === 1 ? 'order.tracking.itemCountOne' : 'order.tracking.itemCountMany', { count: totalItems })}
                    </span>
                </h2>
            </div>

            {/* Items List */}
            <ul className="px-5" role="list" aria-label={t('order.tracking.items')}>
                {items.map((item) => (
                    <OrderItemRow
                        key={item.productId}
                        item={item}
                        currency={currency}
                    />
                ))}
            </ul>
        </div>
    );
};
