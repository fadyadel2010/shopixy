/**
 * Order Summary Component
 * 
 * Displays cart items, subtotal, shipping, and total.
 * Sticky on desktop, collapsible on mobile.
 * 
 * Phase 7.7: Checkout UI Implementation
 */

import React, { useState } from 'react';
import { useTranslation } from '@/i18n';
import { formatMoney, resolveProductImage } from '@/utils';
import { OptimizedImage } from '@/components/media';
import { CouponInput } from '@/components/cart';
import {
    useCartStore,
    selectCartItems,
    selectCartCurrency,
    selectAppliedCoupon,
} from '@/store';
import {
    useCheckoutStore,
    selectSelectedShippingMethod,
    selectPricingPreview,
    selectPricingLoading,
    selectPricingError,
    selectHasRequestedPricing,
} from '@/store/checkoutStore';

interface OrderSummaryProps {
    /** Show collapsed mobile view */
    collapsible?: boolean;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Order Summary Component
 * 
 * ARCHITECTURAL RULE: Checkout displays ONLY server-quoted pricing.
 * - Totals come from the backend pricing preview endpoint
 * - Frontend never recomputes shipping, tax, discounts, or total
 */
export const OrderSummary: React.FC<OrderSummaryProps> = ({
    collapsible = true,
    className = '',
}) => {
    const [isExpanded, setIsExpanded] = useState(false);
    const { t } = useTranslation();

    // Cart state - READ-ONLY from cart store
    const items = useCartStore(selectCartItems);
    const currency = useCartStore(selectCartCurrency);
    const appliedCoupon = useCartStore(selectAppliedCoupon);

    // Checkout state
    const shippingMethod = useCheckoutStore(selectSelectedShippingMethod);
    const pricing = useCheckoutStore(selectPricingPreview);
    const pricingLoading = useCheckoutStore(selectPricingLoading);
    const pricingError = useCheckoutStore(selectPricingError);
    const hasRequestedPricing = useCheckoutStore(selectHasRequestedPricing);
    const refreshPricingPreview = useCheckoutStore((state) => state.refreshPricingPreview);

    const totalDisplay = pricing
        ? formatMoney(pricing.total, currency)
        : !hasRequestedPricing
        ? t('checkout.summary.calculating')
        : (pricingError || (!pricingLoading && pricing === null))
        ? t('checkout.summary.unavailable')
        : t('checkout.summary.calculating');

    return (
        <div className={`bg-background border border-muted/10 rounded-xl overflow-hidden ${className}`}>
            {/* Mobile toggle header */}
            {collapsible && (
                <button
                    onClick={() => setIsExpanded(!isExpanded)}
                    className="w-full lg:hidden flex items-center justify-between p-4 bg-muted/5 hover:bg-muted/10 transition-colors"
                    aria-expanded={isExpanded}
                    aria-controls="order-summary-content"
                >
                    <span className="font-medium text-text">{t('checkout.orderSummary')}</span>
                    <div className="flex items-center gap-2">
                        <span className="font-semibold text-text">
                            {totalDisplay}
                        </span>
                        <svg
                            className={`w-5 h-5 text-muted transition-transform ${isExpanded ? 'rotate-180' : ''}`}
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                            aria-hidden="true"
                        >
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                        </svg>
                    </div>
                </button>
            )}

            {/* Content */}
            <div
                id="order-summary-content"
                className={`
                    ${collapsible ? 'lg:block' : ''}
                    ${collapsible && !isExpanded ? 'hidden lg:block' : ''}
                `}
            >
                {/* Header (desktop only) */}
                <div className="hidden lg:block p-4 border-b border-muted/10">
                    <h2 className="font-semibold text-text">{t('checkout.orderSummary')}</h2>
                </div>

                {/* Items list */}
                <div className="p-4 space-y-3 max-h-60 overflow-y-auto">
                    {items.map((item) => {
                        const imageSrc = resolveProductImage(item);
                        const productName = item.productName?.trim() || t('checkout.summary.productUnavailable');
                        const sku = item.sku?.trim();
                        const variantLabel = item.variantAttributes
                            ?.map((attribute) => attribute.value?.trim())
                            .filter(Boolean)
                            .join(' / ');
                        const lineKey = item.variantId || `${item.productId}:${sku || 'default'}`;

                        return (
                        <div key={lineKey} className="flex gap-3">
                            {/* Image */}
                            <OptimizedImage
                                src={imageSrc}
                                alt={productName}
                                sizes="80px"
                                className="w-12 h-12 object-cover rounded-lg bg-muted/10"
                            />

                            {/* Details */}
                            <div className="flex-1 min-w-0">
                                <p className="text-sm font-medium text-text truncate">
                                    {productName}
                                </p>
                                {variantLabel && (
                                    <p className="text-xs text-primary/80 truncate">
                                        {variantLabel}
                                    </p>
                                )}
                                {sku && (
                                    <p className="text-xs text-muted truncate">
                                        {t('checkout.summary.sku')}: {sku}
                                    </p>
                                )}
                                <p className="text-xs text-muted">
                                    {t('checkout.summary.quantity', { count: item.quantity })}
                                </p>
                                <p className="text-xs text-muted">
                                    {t('checkout.summary.unitPrice', {
                                        amount: formatMoney(item.unitPrice, currency),
                                    })}
                                </p>
                                {/* PHASE: Promotions - Show promotion badge if active */}
                                {item.hasActivePromotion && item.promotionBadge && (
                                    <span className="inline-block text-xs bg-red-500 text-white px-1.5 py-0.5 rounded mt-1">
                                        {item.promotionBadge}
                                    </span>
                                )}
                            </div>

                            {/* Price - Show discounted price if promotion is active */}
                            <div className="text-end">
                                {item.hasActivePromotion && item.discountedLineTotal !== undefined && item.discountedLineTotal < item.lineTotal ? (
                                    <>
                                        <p className="text-sm font-medium text-red-500">
                                            {formatMoney(item.discountedLineTotal, currency)}
                                        </p>
                                        <p className="text-xs text-muted line-through">
                                            {formatMoney(item.lineTotal, currency)}
                                        </p>
                                    </>
                                ) : (
                                    <p className="text-sm font-medium text-text">
                                        {formatMoney(item.lineTotal, currency)}
                                    </p>
                                )}
                            </div>
                        </div>
                        );
                    })}
                </div>

                {/* Totals */}
                <div className="p-4 border-t border-muted/10 space-y-3">
                    <CouponInput />

                    <hr className="border-muted/10" />

                    {(pricingLoading && !pricing) || !hasRequestedPricing ? (
                        <div className="rounded-lg bg-muted/5 px-3 py-4 text-sm text-muted">
                            {t('checkout.summary.calculatingTotals')}
                        </div>
                    ) : pricing ? (
                        <>
                            <div className="flex justify-between text-sm">
                                <span className="text-muted">{t('cart.subtotal')}</span>
                                <span className="text-text">{formatMoney(pricing.subtotal, currency)}</span>
                            </div>

                            {pricing.promotionDiscount > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-red-500 flex items-center gap-1">
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
                                        </svg>
                                        {t('checkout.summary.promotionSavings')}
                                    </span>
                                    <span className="text-red-500 font-medium">
                                        -{formatMoney(pricing.promotionDiscount, currency)}
                                    </span>
                                </div>
                            )}

                            {pricing.cartPromotionDiscount > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-orange-600">{t('checkout.summary.cartPromotion')}</span>
                                    <span className="text-orange-600 font-medium">
                                        -{formatMoney(pricing.cartPromotionDiscount, currency)}
                                    </span>
                                </div>
                            )}

                            {pricing.couponDiscount > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-green-600">
                                        {t('checkout.summary.coupon')}
                                        {appliedCoupon && (
                                            <span className="text-xs ms-1">({appliedCoupon.code})</span>
                                        )}
                                    </span>
                                    <span className="text-green-600">
                                        -{formatMoney(pricing.couponDiscount, currency)}
                                    </span>
                                </div>
                            )}

                            <div className="flex justify-between text-sm">
                                <span className="text-muted">
                                    {t('cart.shipping')}
                                    {shippingMethod && (
                                        <span className="text-xs ms-1">({shippingMethod.name})</span>
                                    )}
                                </span>
                                <span className="text-text">
                                    {pricing.shipping > 0 ? formatMoney(pricing.shipping, currency) : t('cart.freeShipping')}
                                </span>
                            </div>

                            <div className="flex justify-between text-sm">
                                <span className="text-muted">{t('cart.tax')}</span>
                                <span className="text-text">{formatMoney(pricing.tax, currency)}</span>
                            </div>

                            {pricing.paymentFee > 0 && (
                                <div className="flex justify-between text-sm">
                                    <span className="text-muted">{t('checkout.summary.paymentFee')}</span>
                                    <span className="text-text">{formatMoney(pricing.paymentFee, currency)}</span>
                                </div>
                            )}
                        </>
                    ) : (
                        <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-4 text-sm text-amber-800">
                            <p>{pricingError || t('checkout.summary.pricingUnavailable')}</p>
                            <button
                                type="button"
                                onClick={() => void refreshPricingPreview()}
                                className="mt-3 inline-flex items-center rounded-lg border border-amber-300 px-3 py-2 font-medium text-amber-900 hover:bg-amber-100"
                            >
                                {t('checkout.summary.retryTotals')}
                            </button>
                        </div>
                    )}

                    {/* Divider */}
                    <hr className="border-muted/10" />

                    {/* Total */}
                    <div className="flex justify-between">
                        <span className="font-semibold text-text">{t('cart.total')}</span>
                        <span className="font-bold text-lg text-text">
                            {totalDisplay}
                        </span>
                    </div>
                </div>
            </div>
        </div>
    );
};
