/**
 * Cart Summary Component
 * 
 * Displays cart totals, coupon input, and checkout button.
 * Uses centralized formatMoney utility.
 * 
 * Phase C1: Cart Hardening
 * - Full i18n support
 * - Auth guard for checkout button
 * - Shows login prompt for unauthenticated users
 * 
 * Phase C4: Coupon Integration
 * - CouponInput component integration
 * - Discount display row
 * - Reactive pricing based on applied coupon
 */

import React from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@/components/common';
import { formatMoney } from '@/utils';
import { useTranslation } from '@/i18n';
import { useAuthStore, selectIsAuthenticated, selectAuthStatus } from '@/auth/store/authStore';
import { selectBehavior, useStoreConfigStore } from '@/store';
import { CouponInput } from './CouponInput';
import { useIsAffiliateMode } from '@/contexts/AffiliateContext';
import { canEnterCheckout } from '@/features/checkout/cartCheckoutAdmission';
import { checkoutNavigationTarget } from '@/features/checkout/storefrontTenantNavigation';
import { useCartStore, selectCartIsConfirmed } from '@/store/cartStore';

interface CartSummaryProps {
    /** Cart subtotal (before discount) */
    subtotal: number;
    /** Discount amount (from coupon) */
    discountTotal?: number;
    /** Tax amount from server cart preview */
    taxTotal?: number;
    /** Grand total from server cart preview */
    grandTotal?: number;
    /** PHASE: Promotions Engine - Promotion discount amount */
    promotionDiscountTotal?: number;
    /** Cart-level promotion discount amount */
    cartPromotionDiscountTotal?: number;
    /** Applied cart promotion info */
    appliedCartPromotion?: {
        name: string;
        badge: string;
        discountAmount: number;
    } | null;
    /** Whether any promotion is active in cart */
    hasPromotionDiscount?: boolean;
    /** Whether coupon is applied */
    hasCoupon?: boolean;
    /** Currency code */
    currency: string;
    /** Number of items */
    itemCount: number;
    /** Whether cart is empty */
    isEmpty: boolean;
    /** Clear cart callback */
    onClearCart: () => void;
    /** Whether clearing is in progress */
    isClearing?: boolean;
}

/**
 * Cart Summary Component
 */
export const CartSummary: React.FC<CartSummaryProps> = ({
    subtotal,
    discountTotal = 0,
    taxTotal = 0,
    grandTotal = 0,
    promotionDiscountTotal = 0,
    cartPromotionDiscountTotal = 0,
    appliedCartPromotion = null,
    hasPromotionDiscount = false,
    hasCoupon = false,
    currency,
    itemCount,
    isEmpty,
    onClearCart,
    isClearing = false,
}) => {
    const { t } = useTranslation();
    const cartIsConfirmed = useCartStore(selectCartIsConfirmed);
    const requiresCartRefresh = useCartStore(state => state.requiresRefresh);
    const isRefreshingCart = useCartStore(state => state.isLoading);

    // Auth state for checkout guard
    const isAuthenticated = useAuthStore(selectIsAuthenticated);
    const authStatus = useAuthStore(selectAuthStatus);
    const isAuthChecking = authStatus === 'unknown';
    const isAuthDegraded = authStatus === 'degraded';
    const isAuthUncertain = isAuthChecking || isAuthDegraded;
    const enableGuestCheckout = useStoreConfigStore(selectBehavior).enableGuestCheckout;

    // AF-13B: Affiliate Mode acts as a passport for guest checkout
    const isAffiliateMode = useIsAffiliateMode();
    const canCheckoutAsGuest = enableGuestCheckout || isAffiliateMode;
    const canProceedToCheckout = canEnterCheckout(isAuthenticated, enableGuestCheckout, isAffiliateMode);

    /**
     * Handle checkout click
     */
    const handleCheckoutClick = () => {
        if (!cartIsConfirmed) return;
        window.location.href = checkoutNavigationTarget(canProceedToCheckout, window.location.search);
    };

    return (
        <div className="bg-background border border-muted/10 rounded-xl p-6">
            <h3 className="font-semibold text-lg text-text mb-4">
                {t('cart.orderSummary')}
            </h3>

            {/* Coupon Input Section */}
            <div className="mb-4">
                {isAuthChecking ? (
                    // Show placeholder while auth is loading to avoid flash of guest message
                    <div className="rounded-lg border border-muted/30 bg-muted/5 px-3 py-3 text-sm text-muted animate-pulse">
                        {t('common.loading')}
                    </div>
                ) : isAuthDegraded ? (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-3 text-sm text-amber-800">
                        {t('cart.degradedAuth.message')}
                    </div>
                ) : isAuthenticated ? (
                    <CouponInput />
                ) : (
                    <div className="rounded-lg border border-dashed border-muted/30 bg-muted/5 px-3 py-3 text-sm text-muted">
                        {t('cart.signInToUseCoupons')}
                    </div>
                )}
            </div>

            {/* Divider */}
            <div className="h-px bg-muted/20 my-4" />

            {/* Summary Lines */}
            <div className="space-y-3 mb-4">
                {/* Subtotal */}
                <div className="flex justify-between text-sm">
                    <span className="text-muted">
                        {t('cart.subtotal')} ({t('cart.itemCount', { count: itemCount })})
                    </span>
                    <span className="text-text font-medium">
                        {formatMoney(subtotal, currency)}
                    </span>
                </div>

                {/* Promotion Discount Row - Only shown when promotions are active */}
                {hasPromotionDiscount && promotionDiscountTotal > 0 && (
                    <div className="flex justify-between text-sm">
                        <span className="text-red-500 font-medium flex items-center gap-1">
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
                            </svg>
                            {t('cart.itemSavings')}
                        </span>
                        <span className="text-red-500 font-medium">
                            -{formatMoney(promotionDiscountTotal, currency)}
                        </span>
                    </div>
                )}

                {/* Cart Promotion Row - Only shown when cart-level promotion is applied */}
                {appliedCartPromotion && cartPromotionDiscountTotal > 0 && (
                    <div className="bg-orange-50 dark:bg-orange-900/20 -mx-2 px-2 py-2 rounded-lg">
                        <div className="flex justify-between text-sm">
                            <span className="text-orange-600 font-semibold flex items-center gap-1.5">
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v13m0-13V6a2 2 0 112 2h-2zm0 0V5.5A2.5 2.5 0 109.5 8H12zm-7 4h14M5 12a2 2 0 110-4h14a2 2 0 110 4M5 12v7a2 2 0 002 2h10a2 2 0 002-2v-7" />
                                </svg>
                                🎉 {appliedCartPromotion.name}
                            </span>
                            <span className="text-orange-600 font-bold">
                                -{formatMoney(cartPromotionDiscountTotal, currency)}
                            </span>
                        </div>
                        <p className="text-xs text-orange-500 mt-1">
                            {appliedCartPromotion.badge}
                        </p>
                    </div>
                )}

                {/* Coupon Discount Row - Only shown when coupon is applied */}
                {hasCoupon && discountTotal > 0 && (
                    <div className="flex justify-between text-sm">
                        <span className="text-green-600 font-medium">
                            {t('cart.discount')}
                        </span>
                        <span className="text-green-600 font-medium">
                            -{formatMoney(discountTotal, currency)}
                        </span>
                    </div>
                )}

                {/* Tax */}
                <div className="flex justify-between text-sm">
                    <span className="text-muted">{t('cart.tax')}</span>
                    <span className="text-text font-medium">
                        {formatMoney(taxTotal, currency)}
                    </span>
                </div>

                {/* Shipping */}
                <div className="flex justify-between text-sm">
                    <span className="text-muted">{t('cart.shipping')}</span>
                    <span className="text-muted font-medium">{t('cart.shippingCalculated')}</span>
                </div>
                <p className="text-xs text-muted">
                    {t('cart.previewShippingNotice')}
                </p>
            </div>

            {/* Divider */}
            <div className="h-px bg-muted/20 my-4" />

            {/* Total */}
            <div className="flex justify-between mb-6">
                <div>
                    <span className="text-text font-semibold">{t('cart.previewTotal')}</span>
                    <p className="text-xs text-muted">{t('cart.beforeShipping')}</p>
                </div>
                <span className="text-xl font-bold text-text">
                    {formatMoney(grandTotal, currency)}
                </span>
            </div>

            {/* Checkout Button */}
            <Button
                variant="primary"
                size="lg"
                fullWidth
                disabled={isEmpty || !cartIsConfirmed || (isAuthUncertain && !canCheckoutAsGuest)}
                onClick={handleCheckoutClick}
            >
                {isAuthUncertain && !canCheckoutAsGuest ? (
                    isAuthDegraded
                        ? t('cart.degradedAuth.verifying')
                        : t('common.loading')
                ) : canProceedToCheckout ? (
                    <>
                        {t('cart.checkout')}
                        <svg className="w-5 h-5 ltr:ml-2 rtl:mr-2 rtl-flip" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 8l4 4m0 0l-4 4m4-4H3" />
                        </svg>
                    </>
                ) : (
                    <>
                        {t('cart.signInToCheckout')}
                        <svg className="w-5 h-5 ltr:ml-2 rtl:mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1" />
                        </svg>
                    </>
                )}
            </Button>

            {requiresCartRefresh && (
                <div className="mt-3 text-sm text-amber-800" role="status">
                    <p>{t('cart.refreshRequired')}</p>
                    <button type="button" disabled={isRefreshingCart} onClick={() => void useCartStore.getState().fetchCart()} className="mt-2 underline disabled:opacity-50">
                        {t('common.retry')}
                    </button>
                </div>
            )}

            {/* Guest user message */}
            {!isAuthenticated && !isAuthUncertain && !canCheckoutAsGuest && !isEmpty && (
                <p className="text-xs text-muted text-center mt-3">
                    {t('cart.guestCheckoutHint')}
                </p>
            )}

            {/* Continue Shopping */}
            <Link
                to="/products"
                className="block text-center text-sm text-muted hover:text-primary mt-4 transition-colors"
            >
                {t('cart.continueShopping')}
            </Link>

            {/* Clear Cart */}
            {!isEmpty && (
                <button
                    onClick={onClearCart}
                    disabled={isClearing}
                    className="block w-full text-center text-sm text-red-600 hover:text-red-700 mt-4 transition-colors disabled:opacity-50"
                >
                    {isClearing ? t('common.loading') : t('cart.clearCart')}
                </button>
            )}

            {/* Trust Badges */}
            <div className="mt-6 pt-4 border-t border-muted/10">
                <div className="flex items-center justify-center gap-4 text-muted">
                    <div className="flex items-center gap-1.5 text-xs">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                        </svg>
                        {t('cart.secure')}
                    </div>
                    <div className="flex items-center gap-1.5 text-xs">
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                        </svg>
                        {t('cart.protected')}
                    </div>
                </div>
            </div>
        </div>
    );
};
