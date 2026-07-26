/**
 * Cart Page
 * 
 * Full cart experience with:
 * - Cart items list
 * - Quantity controls
 * - Remove items
 * - Clear cart with confirmation
 * - Order summary
 * - Checkout navigation
 * - Toast notifications for feedback
 * 
 * Phase 7.6.x: Polished Cart UX.
 * Phase 10.1: Multi-language support
 */

import React, { useEffect, useCallback, useState } from 'react';
import { Layout } from '@/components/layout';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import {
    CartSkeleton,
    CartEmptyState,
    CartItemRow,
    CartSummary
} from '@/components/cart';
import { InlineError } from '@/components/data';
import { ConfirmDialog } from '@/components/feedback';
import { useCategories } from '@/hooks';
import { useToast } from '@/store';
import { useTranslation } from '@/i18n';
import {
    useCartStore,
    selectCartItems,
    selectCartSubtotal,
    selectCartDiscountTotal,
    selectCartTaxTotal,
    selectCartGrandTotal,
    selectCartCurrency,
    selectIsCartLoading,
    selectCartError,
    selectCartRecoveryMessage,
    selectIsCartEmpty,
    selectUpdatingItemId,
    selectIsCartInitialized,
    selectCartItemCount,
    selectHasCoupon,
    selectCartPromotionDiscountTotal,
    selectHasPromotionDiscount,
    selectCartCartPromotionDiscountTotal,
    selectAppliedCartPromotion,
} from '@/store';

/**
 * Cart Page Component
 */
export const CartPage: React.FC = () => {
    const { t } = useTranslation();

    // Categories for layout
    const { categories } = useCategories();

    // Toast notifications
    const toast = useToast();

    // Clear cart confirmation state
    const [showClearConfirm, setShowClearConfirm] = useState(false);
    const [isClearing, setIsClearing] = useState(false);

    // Cart state
    const items = useCartStore(selectCartItems);
    const subtotal = useCartStore(selectCartSubtotal);
    const discountTotal = useCartStore(selectCartDiscountTotal);
    const taxTotal = useCartStore(selectCartTaxTotal);
    const grandTotal = useCartStore(selectCartGrandTotal);
    const currency = useCartStore(selectCartCurrency);
    const isLoading = useCartStore(selectIsCartLoading);
    const error = useCartStore(selectCartError);
    const recoveryMessage = useCartStore(selectCartRecoveryMessage);
    const isEmpty = useCartStore(selectIsCartEmpty);
    const updatingItemId = useCartStore(selectUpdatingItemId);
    const isMutatingCart = !!updatingItemId;
    const isInitialized = useCartStore(selectIsCartInitialized);
    const hasCoupon = useCartStore(selectHasCoupon);
    const itemCount = useCartStore(selectCartItemCount);
    // PHASE: Promotions Engine - Promotion discount state
    const promotionDiscountTotal = useCartStore(selectCartPromotionDiscountTotal);
    const hasPromotionDiscount = useCartStore(selectHasPromotionDiscount);
    // CART PROMOTIONS ENGINE: Cart-level promotion state
    const cartPromotionDiscountTotal = useCartStore(selectCartCartPromotionDiscountTotal);
    const appliedCartPromotion = useCartStore(selectAppliedCartPromotion);

    // Cart actions
    const fetchCart = useCartStore((state) => state.fetchCart);
    const updateQuantity = useCartStore((state) => state.updateQuantity);
    const removeItem = useCartStore((state) => state.removeItem);
    const clearCart = useCartStore((state) => state.clearCart);
    const clearError = useCartStore((state) => state.clearError);
    const clearRecoveryMessage = useCartStore((state) => state.clearRecoveryMessage);

    // Fetch cart on mount
    useEffect(() => {
        if (!isInitialized) {
            fetchCart();
        }
    }, [isInitialized, fetchCart]);

    /**
     * Handle quantity change with toast feedback
     * PHASE 11.7: variantId is now passed for variant-centric operations
     */
    const handleQuantityChange = useCallback(async (productId: string, quantity: number, variantId?: string) => {
        const success = await updateQuantity(productId, quantity, variantId);
        if (success) {
            toast.success(t('cart.quantityUpdated'));
        } else {
            toast.error(t('cart.updateFailed'));
        }
    }, [updateQuantity, toast, t]);

    /**
     * Handle remove item with toast feedback
     * PHASE 11.7: variantId is now passed for variant-centric operations
     */
    const handleRemoveItem = useCallback(async (productId: string, variantId?: string) => {
        const success = await removeItem(productId, variantId);
        if (success) {
            toast.success(t('cart.itemRemoved'));
        } else {
            toast.error(t('cart.removeFailed'));
        }
    }, [removeItem, toast, t]);

    /**
     * Handle clear cart button click - show confirmation
     */
    const handleClearCartClick = useCallback(() => {
        setShowClearConfirm(true);
    }, []);

    /**
     * Handle clear cart confirmation
     */
    const handleClearCartConfirm = useCallback(async () => {
        setIsClearing(true);
        const success = await clearCart();
        setIsClearing(false);
        setShowClearConfirm(false);

        if (success) {
            toast.success(t('cart.cartCleared'));
        } else {
            toast.error(t('cart.clearFailed'));
        }
    }, [clearCart, toast, t]);

    /**
     * Handle clear cart cancel
     */
    const handleClearCartCancel = useCallback(() => {
        setShowClearConfirm(false);
    }, []);

    /**
     * Render cart content based on state
     */
    const renderContent = () => {
        // Loading state
        if (isLoading && !isInitialized) {
            return <CartSkeleton rows={3} />;
        }

        // Error state (with data)
        if (error && isEmpty) {
            return (
                <InlineError
                    message={error}
                    onRetry={() => {
                        clearError();
                        fetchCart();
                    }}
                />
            );
        }

        // Empty state
        if (isEmpty) {
            return <CartEmptyState />;
        }

        // Cart with items
        return (
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 lg:gap-8">
                {/* Cart Items */}
                <div className="lg:col-span-2">
                    {/* Error banner */}
                    {error && (
                        <div className="mb-4 p-3 bg-red-50 border border-red-200 rounded-lg">
                            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                <div className="flex items-center gap-2 text-red-600 text-sm">
                                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                                    </svg>
                                    <span>{error}</span>
                                </div>
                                <div className="flex items-center gap-2 sm:justify-end">
                                    <button
                                        type="button"
                                        onClick={() => {
                                            clearError();
                                            void fetchCart();
                                        }}
                                        className="rounded-md border border-red-300 px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-100"
                                    >
                                        {t('cart.retrySync')}
                                    </button>
                                    <button
                                        type="button"
                                        onClick={clearError}
                                        className="text-red-600 hover:text-red-700"
                                        aria-label={t('cart.dismissError')}
                                    >
                                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                        </svg>
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* Items list */}
                    <div className="bg-background border border-muted/10 rounded-xl overflow-hidden">
                        {/* PHASE 11.7: Use variantId as key when available for better React reconciliation */}
                        {items.map((item) => (
                            <CartItemRow
                                key={item.variantId || item.productId}
                                item={item}
                                isUpdating={isMutatingCart}
                                currency={currency}
                                onQuantityChange={handleQuantityChange}
                                onRemove={handleRemoveItem}
                            />
                        ))}
                    </div>
                </div>

                {/* Order Summary */}
                <div className="lg:col-span-1">
                    <div className="lg:sticky lg:top-24">
                        <CartSummary
                            subtotal={subtotal}
                            discountTotal={discountTotal}
                            taxTotal={taxTotal}
                            grandTotal={grandTotal}
                            promotionDiscountTotal={promotionDiscountTotal}
                            cartPromotionDiscountTotal={cartPromotionDiscountTotal}
                            appliedCartPromotion={appliedCartPromotion ? {
                                name: appliedCartPromotion.name,
                                badge: appliedCartPromotion.badge,
                                discountAmount: appliedCartPromotion.discountAmount
                            } : null}
                            hasPromotionDiscount={hasPromotionDiscount}
                            hasCoupon={hasCoupon}
                            currency={currency}
                            itemCount={itemCount}
                            isEmpty={isEmpty}
                            onClearCart={handleClearCartClick}
                            isClearing={isClearing}
                        />
                    </div>
                </div>
            </div>
        );
    };

    return (
        <Layout categories={categories}>
            <div className="container mx-auto px-4 py-6 md:py-10">
                {/* Page Header */}
                <div className="mb-8">
                    {/* Breadcrumb */}
                    <nav className="flex items-center gap-2 text-sm mb-4" aria-label="Breadcrumb">
                        <AffiliateLink href="/" className="text-muted hover:text-primary transition-colors">
                            {t('common.home')}
                        </AffiliateLink>
                        <svg className="w-4 h-4 text-muted/50 rtl-flip" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                        </svg>
                        <span className="text-text font-medium">{t('cart.title')}</span>
                    </nav>

                    {/* Title */}
                    <h1 className="text-2xl md:text-3xl font-bold text-text">
                        {t('cart.title')}
                        {!isEmpty && itemCount > 0 && (
                            <span className="text-muted font-normal text-lg ml-2">
                                ({t('cart.itemCount', { count: itemCount })})
                            </span>
                        )}
                    </h1>
                </div>

                {/* Cart Content */}
                {recoveryMessage && (
                    <div className="mb-4 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
                        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                            <div className="flex items-center gap-2 text-sm text-emerald-700">
                                <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                                </svg>
                                <span>{recoveryMessage}</span>
                            </div>
                            <button
                                type="button"
                                onClick={clearRecoveryMessage}
                                className="self-start text-sm font-medium text-emerald-700 hover:text-emerald-800 sm:self-auto"
                            >
                                {t('common.dismiss')}
                            </button>
                        </div>
                    </div>
                )}

                {renderContent()}
            </div>

            {/* Clear Cart Confirmation Dialog */}
            <ConfirmDialog
                isOpen={showClearConfirm}
                title={t('cart.clearCart')}
                message={t('cart.clearConfirmMessage')}
                confirmText={t('cart.clearCart')}
                cancelText={t('common.cancel')}
                confirmVariant="primary"
                isLoading={isClearing}
                onConfirm={handleClearCartConfirm}
                onCancel={handleClearCartCancel}
            />
        </Layout>
    );
};

