/**
 * RecoverCheckoutPage
 *
 * Handles /recover-checkout/:token URLs.
 * Fetches the saved session data from the API, restores the cart,
 * and redirects the customer to /checkout to continue.
 */

import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { setSessionId } from '@/bootstrap';
import { recoverCheckout } from '@/services/api';
import { useCartStore } from '@/store/cartStore';
import { useCheckoutStore } from '@/store/checkoutStore';
import type { CartDto, CartItemDto, RecoveredCheckoutResponse } from '@/types';

interface RecoveryError {
    emoji: string;
    title: string;
    message: string;
    ctaLabel: string;
    ctaHref: string;
}

const ERROR_MESSAGES: Record<string, RecoveryError> = {
    SESSION_CONVERTED: {
        emoji: 'OK',
        title: 'Already Completed',
        message: 'This checkout was already completed successfully.',
        ctaLabel: 'Continue Shopping',
        ctaHref: '/',
    },
    SESSION_EXPIRED: {
        emoji: 'TIME',
        title: 'Link Expired',
        message: 'This recovery link has expired. Please start a new checkout from your cart.',
        ctaLabel: 'Go to Cart',
        ctaHref: '/cart',
    },
    CART_SNAPSHOT_CORRUPTED: {
        emoji: 'WARN',
        title: 'Recovery Needs Attention',
        message: 'We could not safely restore the saved checkout items. Please reopen your cart and review the latest contents before starting checkout again.',
        ctaLabel: 'Go to Cart',
        ctaHref: '/cart',
    },
    RECOVERY_CART_REVALIDATION_FAILED: {
        emoji: 'WARN',
        title: 'Review Your Cart',
        message: 'We could not safely verify the latest cart, stock, and pricing for this checkout. Please reopen your cart before starting checkout again.',
        ctaLabel: 'Go to Cart',
        ctaHref: '/cart',
    },
    DEFAULT: {
        emoji: 'WARN',
        title: 'Session Expired',
        message: 'This checkout recovery link has expired or is no longer available. Please start a new checkout from your cart.',
        ctaLabel: 'Go to Cart',
        ctaHref: '/cart',
    },
};

function normalizeCouponType(couponType: string | null | undefined): 'percentage' | 'fixed' {
    if (!couponType) return 'fixed';

    const normalized = couponType.toLowerCase().trim();
    if (normalized === 'percentage' || normalized === 'percent') return 'percentage';
    if (normalized === 'fixed' || normalized === 'amount') return 'fixed';

    console.warn(`Unknown coupon type received: "${couponType}". Falling back to "fixed".`);
    return 'fixed';
}

function buildRecoveredCart(session: RecoveredCheckoutResponse): CartDto {
    const items: CartItemDto[] = session.cart.map((item, index) => {
        const productId = item.variantId?.trim() || `recovered-item-${index + 1}`;
        const unitPrice = Number(item.price || 0);
        const quantity = Number(item.quantity || 0);

        return {
            productId,
            variantId: item.variantId || undefined,
            productName: item.productName || 'Recovered item',
            sku: item.variantId || `RECOVERED-${index + 1}`,
            unitPrice,
            quantity,
            lineTotal: unitPrice * quantity,
            imageUrl: item.imageUrl || undefined,
            variantAttributes: item.variantName
                ? [{ name: 'Variant', value: item.variantName }]
                : undefined,
        };
    });

    const subtotal = items.reduce((sum, item) => sum + item.lineTotal, 0);
    const itemCount = items.reduce((sum, item) => sum + item.quantity, 0);
    const now = new Date().toISOString();
    const couponDiscount = Number(session.couponDiscount ?? 0);
    const cartPromotionDiscount = Number(session.cartPromotionDiscount ?? 0);
    const totalDiscount = couponDiscount + cartPromotionDiscount;
    const grandTotal = session.cartTotal ?? Math.max(subtotal - totalDiscount, 0);

    const appliedCoupon = session.couponCode
        ? {
            code: session.couponCode,
            type: normalizeCouponType(session.couponType),
            value: Number(session.couponValue ?? couponDiscount),
            amount: couponDiscount,
        }
        : null;

    const appliedCartPromotion = session.promotionId
        ? {
            promotionId: session.promotionId,
            name: session.promotionName || 'Promotion',
            description: session.promotionDescription || undefined,
            discountType: session.promotionDiscountType || 'fixed',
            discountValue: Number(session.promotionDiscountValue ?? cartPromotionDiscount),
            discountAmount: cartPromotionDiscount,
            badge: session.promotionBadge || session.promotionName || 'Promotion',
        }
        : null;

    return {
        cartId: session.cartId,
        tenantId: '',
        ownerType: 'session',
        ownerId: session.guestSessionId || '',
        items,
        itemCount,
        subtotal,
        discountTotal: couponDiscount,
        taxTotal: 0,
        grandTotal,
        currency: session.currency || '',
        lastUpdatedUtc: now,
        expiresAtUtc: now,
        appliedCouponCode: session.couponCode ?? null,
        appliedCoupon,
        cartPromotionDiscountTotal: cartPromotionDiscount,
        appliedCartPromotion,
        totalDiscount,
    };
}

export default function RecoverCheckoutPage() {
    const { token } = useParams<{ token: string }>();
    const navigate = useNavigate();
    const [status, setStatus] = useState<'loading' | 'error'>('loading');
    const [errorInfo, setErrorInfo] = useState<RecoveryError>(ERROR_MESSAGES.DEFAULT);

    useEffect(() => {
        if (!token) {
            setStatus('error');
            return;
        }

        const recover = async () => {
            try {
                const session = await recoverCheckout(token);

                if (!session?.cartId || !session.address) {
                    setStatus('error');
                    return;
                }

                if (session.guestSessionId) {
                    setSessionId(session.guestSessionId);
                }

                const recoveredCart = buildRecoveredCart(session);
                const checkoutStore = useCheckoutStore.getState();
                checkoutStore.reset();

                const hasAddress = !!(
                    session.address.fullName ||
                    session.address.phone ||
                    session.address.city ||
                    session.address.area ||
                    session.address.addressLine
                );
                const hasShippingMethod = !!(session.shippingMethod?.id || session.shippingMethodId);
                const recoveredPaymentMethodCode = session.paymentMethod?.code ?? session.paymentMethodCode ?? null;
                const hasPaymentMethod = !!recoveredPaymentMethodCode;
                const requiresManualWalletReentry = recoveredPaymentMethodCode === 'MANUALWALLET';

                let inferredStep: 'shipping' | 'payment' | 'review' = 'shipping';
                if (hasAddress && hasShippingMethod && hasPaymentMethod && !requiresManualWalletReentry) {
                    inferredStep = 'review';
                } else if (hasAddress && hasShippingMethod) {
                    inferredStep = 'payment';
                }

                useCheckoutStore.setState({
                    checkoutSessionId: null,
                    checkoutSessionCartId: null,
                    step: inferredStep,
                    address: {
                        fullName: session.address.fullName || '',
                        phone: session.address.phone || '',
                        city: session.address.city || '',
                        area: session.address.area || '',
                        addressLine: session.address.addressLine || '',
                        country: session.address.country || undefined,
                        notes: session.notes || '',
                    },
                    shippingMethodId: session.shippingMethod?.id ?? session.shippingMethodId ?? null,
                    paymentMethod: recoveredPaymentMethodCode,
                    selectedPaymentMethodCode: recoveredPaymentMethodCode,
                    manualWalletType: null,
                    manualWalletPhoneNumber: '',
                    manualWalletProofFileKey: null,
                    manualWalletUploadToken: null,
                    error: null,
                    errorCode: null,
                    orderResult: null,
                    autosaveStatus: 'idle',
                    autosaveError: null,
                    lastAutosavedAt: null,
                });

                // Only set error if the session requires user review or cart changed
                if (session.requiresReview || session.cartChangedDuringRecovery) {
                    checkoutStore.setError(
                        session.recoveryMessage || 'Review the latest cart, stock, and pricing before placing the order.',
                        session.cartChangedDuringRecovery ? 'RECOVERY_CART_CHANGED' : 'RECOVERY_REVIEW_REQUIRED',
                    );
                }
                // Note: session.recoveryMessage may contain informational text about successful
                // revalidation. We don't treat this as an error unless requiresReview or
                // cartChangedDuringRecovery flags are set.

                useCartStore.getState().restoreRecoveredCart(recoveredCart);
                navigate('/checkout', { replace: true });
            } catch (error: any) {
                console.error('[RecoverCheckout] Recovery failed:', error);

                const code = error?.response?.data?.code as string | undefined;
                setErrorInfo((code && ERROR_MESSAGES[code]) || ERROR_MESSAGES.DEFAULT);
                setStatus('error');
            }
        };

        void recover();
    }, [navigate, token]);

    if (status === 'error') {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gray-50">
                <div className="text-center p-8 max-w-md">
                    <div className="text-4xl font-bold mb-4 text-gray-700">{errorInfo.emoji}</div>
                    <h1 className="text-2xl font-bold text-gray-900 mb-2">
                        {errorInfo.title}
                    </h1>
                    <p className="text-gray-600 mb-6">
                        {errorInfo.message}
                    </p>
                    <a
                        href={errorInfo.ctaHref}
                        className="inline-block px-6 py-3 bg-indigo-600 text-white rounded-lg font-medium hover:bg-indigo-700 transition-colors"
                    >
                        {errorInfo.ctaLabel}
                    </a>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50">
            <div className="text-center">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600 mx-auto mb-4" />
                <p className="text-gray-600">Restoring your checkout...</p>
            </div>
        </div>
    );
}
