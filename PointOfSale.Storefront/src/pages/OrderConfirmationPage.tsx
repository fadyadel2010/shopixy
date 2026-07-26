/**
 * Order Confirmation Page
 *
 * This route is hardened so it only shows the success experience when
 * backend payment status is PAID. All other states render pending,
 * manual-review, or failure UI without treating the order as completed.
 */

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';
import { useOrder } from '@/hooks/useOrder';
import { formatMoney } from '@/utils';
import { paymentStatusApi } from '@/services/api/paymentStatusApi';
import {
    resolveBackendPaymentScreen,
    type CanonicalPaymentStatusResponse,
} from '@/features/checkout/paymentFlow';
import {
    clearActiveCheckoutOrderContextIfMatches,
    readCheckoutTokenForOrder,
} from '@/features/checkout/activeCheckoutOrderContext';
import { finalizeSuccessfulCheckout } from '@/features/checkout/checkoutCompletion';
import { emitGuestPaymentRecoverySuccess } from '@/features/checkout/checkoutTelemetry';
import {
    OrderStatusTimeline,
    OrderSummaryCard,
    OrderItemsList,
    ShippingAddressCard,
    OrderErrorDisplay,
    OrderPageSkeleton,
} from '@/components/order';

interface PaymentStatePanelProps {
    orderId: string;
    orderNumber: string;
    title: string;
    description: string;
    accentClasses: string;
    icon: React.ReactNode;
    primaryLabel: string;
    onPrimaryClick: () => void;
}

function buildOrderTrackingHref(orderId: string): string {
    return `/orders/${orderId}/track`;
}

const PaymentStatePanel: React.FC<PaymentStatePanelProps> = ({
    orderId,
    orderNumber,
    title,
    description,
    accentClasses,
    icon,
    primaryLabel,
    onPrimaryClick,
}) => {
    const navigate = useAffiliateNavigate();

    return (
        <div className="min-h-screen bg-background">
            <div className="container mx-auto max-w-2xl px-4 py-8">
                <div className={`rounded-3xl border p-8 text-center shadow-sm ${accentClasses}`}>
                    <div className="mx-auto flex h-20 w-20 items-center justify-center rounded-full bg-white/80">
                        {icon}
                    </div>

                    <h1 className="mt-6 text-2xl font-bold text-text md:text-3xl">
                        {title}
                    </h1>

                    <p className="mt-3 text-muted">
                        {description}
                    </p>

                    <div className="mt-6 rounded-2xl border border-white/60 bg-white/70 p-4">
                        <p className="text-sm text-muted">Order Number</p>
                        <p className="mt-1 font-mono text-lg font-semibold text-text">
                            {orderNumber || orderId}
                        </p>
                    </div>

                    <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
                        <button
                            type="button"
                            onClick={onPrimaryClick}
                            className="rounded-xl bg-primary px-5 py-3 font-medium text-white hover:bg-primary/90"
                        >
                            {primaryLabel}
                        </button>

                        <button
                            type="button"
                            onClick={() => navigate(buildOrderTrackingHref(orderId))}
                            className="rounded-xl border border-primary/30 px-5 py-3 font-medium text-primary hover:bg-primary/5"
                        >
                            Track Order
                        </button>

                        <button
                            type="button"
                            onClick={() => navigate('/products')}
                            className="rounded-xl border border-muted/20 px-5 py-3 font-medium text-text hover:bg-muted/10"
                        >
                            Continue Shopping
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

interface FallbackSuccessStateProps {
    orderId: string;
    orderNumber: string;
    totalAmount?: number;
    currency?: string;
}

const FallbackSuccessState: React.FC<FallbackSuccessStateProps> = ({
    orderId,
    orderNumber,
    totalAmount,
    currency,
}) => {
    const navigate = useAffiliateNavigate();

    return (
        <div className="min-h-screen bg-background">
            <div className="container mx-auto max-w-2xl px-4 py-8">
                <div className="rounded-3xl border border-green-200 bg-green-50 p-8 text-center shadow-sm">
                    <div className="mx-auto flex h-20 w-20 items-center justify-center rounded-full bg-white/80">
                        <svg className="h-10 w-10 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2.5} d="M5 13l4 4L19 7" />
                        </svg>
                    </div>

                    <h1 className="mt-6 text-2xl font-bold text-text md:text-3xl">
                        Payment confirmed
                    </h1>

                    <p className="mt-3 text-muted">
                        Your payment has been confirmed by the backend and your order is ready for the next fulfillment step.
                    </p>

                    <div className="mt-6 rounded-2xl border border-white/60 bg-white/80 p-5">
                        <p className="text-sm text-muted">Order Number</p>
                        <p className="mt-1 font-mono text-lg font-semibold text-text">{orderNumber || orderId}</p>
                        {typeof totalAmount === 'number' && currency ? (
                            <p className="mt-3 text-sm text-text">
                                Total paid: <span className="font-semibold">{formatMoney(totalAmount, currency)}</span>
                            </p>
                        ) : null}
                    </div>

                    <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
                        <button
                            type="button"
                            onClick={() => navigate(buildOrderTrackingHref(orderId))}
                            className="rounded-xl bg-primary px-5 py-3 font-medium text-white hover:bg-primary/90"
                        >
                            Track Order
                        </button>
                        <button
                            type="button"
                            onClick={() => navigate('/products')}
                            className="rounded-xl border border-muted/20 px-5 py-3 font-medium text-text hover:bg-muted/10"
                        >
                            Continue Shopping
                        </button>
                    </div>
                </div>
            </div>
        </div>
    );
};

export const OrderConfirmationPage: React.FC = () => {
    const { orderId } = useParams<{ orderId: string }>();
    const [searchParams] = useSearchParams();
    const navigate = useAffiliateNavigate();
    const { order, timeline, isLoading: orderLoading, error: orderError, refetch } = useOrder(orderId);

    const [paymentStatus, setPaymentStatus] = useState<CanonicalPaymentStatusResponse | null>(null);
    const [paymentStatusLoading, setPaymentStatusLoading] = useState(true);
    const [paymentStatusError, setPaymentStatusError] = useState<Error | null>(null);
    const guestRecoveryTelemetrySentRef = useRef(false);

    const fallbackOrderNumber = searchParams.get('orderNumber') || '';
    const fallbackTotalAmountRaw = searchParams.get('totalAmount');
    const fallbackCurrency = searchParams.get('currency') || '';

    // Safely parse fallbackTotalAmount to avoid NaN rendering
    const fallbackTotalAmount = useMemo(() => {
        if (!fallbackTotalAmountRaw) return undefined;
        const parsed = Number(fallbackTotalAmountRaw);
        return Number.isFinite(parsed) ? parsed : undefined;
    }, [fallbackTotalAmountRaw]);

    const fetchPaymentStatus = useCallback(async () => {
        if (!orderId) {
            setPaymentStatusLoading(false);
            setPaymentStatusError(new Error('Missing order ID'));
            return;
        }

        try {
            const checkoutToken = readCheckoutTokenForOrder(orderId);
            const response = await paymentStatusApi.getCanonicalPaymentStatus(orderId, {
                checkoutToken,
            });
            setPaymentStatus(response);
            setPaymentStatusError(null);

            if (checkoutToken && !guestRecoveryTelemetrySentRef.current) {
                guestRecoveryTelemetrySentRef.current = true;
                emitGuestPaymentRecoverySuccess(orderId, 'volatile_checkout_token');
            }
        } catch (error) {
            console.error('[OrderConfirmationPage] Failed to load payment status:', error);
            setPaymentStatusError(error instanceof Error ? error : new Error('Failed to load payment status'));
        } finally {
            setPaymentStatusLoading(false);
        }
    }, [orderId]);

    useEffect(() => {
        guestRecoveryTelemetrySentRef.current = false;
    }, [orderId]);

    useEffect(() => {
        void fetchPaymentStatus();
    }, [fetchPaymentStatus]);

    useEffect(() => {
        if (!orderId || paymentStatusLoading || paymentStatusError || paymentStatus?.status !== 'PAID') {
            return;
        }

        void finalizeSuccessfulCheckout(orderId);
    }, [orderId, paymentStatusLoading, paymentStatusError, paymentStatus?.status]);

    useEffect(() => {
        if (!orderId || paymentStatus?.status !== 'FAILED') {
            return;
        }

        clearActiveCheckoutOrderContextIfMatches(orderId);
    }, [orderId, paymentStatus?.status]);

    useEffect(() => {
        // Only focus when both payment status and order are loaded, and payment is confirmed
        if (paymentStatusLoading || orderLoading || paymentStatusError || paymentStatus?.status !== 'PAID') {
            return;
        }

        const attemptFocus = (retryCount = 0) => {
            const heading = document.getElementById('confirmation-heading');
            if (heading) {
                heading.focus();
            } else if (retryCount < 2) {
                // Retry with requestAnimationFrame to catch the success UI mount
                requestAnimationFrame(() => attemptFocus(retryCount + 1));
            }
        };

        attemptFocus();
    }, [paymentStatusLoading, orderLoading, paymentStatusError, paymentStatus?.status]);

    const displayOrderNumber = useMemo(() => {
        return paymentStatus?.orderNumber || order?.orderNumber || fallbackOrderNumber || orderId || '';
    }, [fallbackOrderNumber, order?.orderNumber, orderId, paymentStatus?.orderNumber]);

    if (paymentStatusLoading) {
        return <OrderPageSkeleton />;
    }

    if (paymentStatusError) {
        return (
            <div className="min-h-screen bg-background">
                <div className="container mx-auto px-4 py-8">
                    <OrderErrorDisplay
                        error={{
                            code: 'UNKNOWN_ERROR',
                            message: paymentStatusError.message,
                            retryable: true,
                        }}
                        onRetry={() => {
                            setPaymentStatusLoading(true);
                            void fetchPaymentStatus();
                        }}
                        onGoHome={() => navigate('/')}
                    />
                </div>
            </div>
        );
    }

    const screen = paymentStatus ? resolveBackendPaymentScreen(paymentStatus.status) : 'pending';

    if (screen === 'pending') {
        return (
            <PaymentStatePanel
                orderId={orderId || ''}
                orderNumber={displayOrderNumber}
                title="Payment is still processing"
                description="We have the order, but backend payment confirmation is not final yet. Check back shortly or keep tracking the order."
                accentClasses="border-amber-200 bg-amber-50"
                icon={<svg className="h-10 w-10 animate-spin text-amber-600" fill="none" viewBox="0 0 24 24"><circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" /><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 0 1 8-8V0C5.373 0 0 5.373 0 12h4Z" /></svg>}
                primaryLabel="Refresh status"
                onPrimaryClick={() => {
                    setPaymentStatusLoading(true);
                    void fetchPaymentStatus();
                }}
            />
        );
    }

    if (screen === 'manual_review') {
        return (
            <PaymentStatePanel
                orderId={orderId || ''}
                orderNumber={displayOrderNumber}
                title="Payment requires review"
                description="POS quarantined this payment because provider cash may already exist but cannot yet be recognized safely. Do not retry payment from checkout; refresh later or keep tracking the order."
                accentClasses="border-orange-200 bg-orange-50"
                icon={<svg className="h-10 w-10 text-orange-600" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v4m0 4h.01M10.29 3.86l-7.5 13A1 1 0 0 0 3.67 18h16.66a1 1 0 0 0 .87-1.5l-7.5-13a1 1 0 0 0-1.74 0Z" /></svg>}
                primaryLabel="Refresh status"
                onPrimaryClick={() => {
                    setPaymentStatusLoading(true);
                    void fetchPaymentStatus();
                }}
            />
        );
    }

    if (screen === 'failed') {
        return (
            <PaymentStatePanel
                orderId={orderId || ''}
                orderNumber={displayOrderNumber}
                title="Payment failed"
                description="Payment failed - retry or change method from checkout. Your cart is still available there."
                accentClasses="border-red-200 bg-red-50"
                icon={<svg className="h-10 w-10 text-red-600" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18 18 6M6 6l12 12" /></svg>}
                primaryLabel="Return to checkout"
                onPrimaryClick={() => {
                    clearActiveCheckoutOrderContextIfMatches(orderId || '');
                    navigate('/checkout');
                }}
            />
        );
    }

    if (orderLoading) {
        return <OrderPageSkeleton />;
    }

    if (orderError || !order) {
        return (
            <FallbackSuccessState
                orderId={orderId || ''}
                orderNumber={displayOrderNumber}
                totalAmount={paymentStatus?.totalAmount ?? fallbackTotalAmount}
                currency={paymentStatus?.currency || fallbackCurrency || undefined}
            />
        );
    }

    const trackingOrderId = orderId || order.orderId;

    return (
        <div className="min-h-screen bg-background">
            <div className="container mx-auto px-4 py-8 max-w-4xl">
                <header className="text-center mb-8">
                    <div className="mb-6 animate-scale-in">
                        <div className="w-20 h-20 mx-auto rounded-full bg-green-100 flex items-center justify-center">
                            <svg
                                className="w-10 h-10 text-green-600"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2.5}
                                    d="M5 13l4 4L19 7"
                                />
                            </svg>
                        </div>
                    </div>

                    <h1
                        id="confirmation-heading"
                        className="text-2xl md:text-3xl font-bold text-text mb-2"
                        tabIndex={-1}
                    >
                        Thank you for your order!
                    </h1>

                    <p className="text-muted max-w-md mx-auto">
                        Backend payment confirmation is complete, and the order is now safe to move through fulfillment.
                    </p>

                    <div className="mt-6 inline-flex items-center gap-2 bg-primary/10 text-primary px-4 py-2 rounded-full">
                        <span className="text-sm">Order</span>
                        <span className="font-bold font-mono">#{order.orderNumber}</span>
                    </div>
                </header>

                {order.paymentMethod === 'cod' && order.paymentStatus === 'pending' && (
                    <div className="mb-8 p-4 bg-amber-50 border border-amber-200 rounded-xl">
                        <div className="flex items-start gap-3">
                            <span className="text-2xl">Cash</span>
                            <div>
                                <p className="font-medium text-amber-800">
                                    Pay on Delivery
                                </p>
                                <p className="text-sm text-amber-700 mt-1">
                                    Please have {formatMoney(order.total, order.currency)} ready when your order arrives.
                                </p>
                            </div>
                        </div>
                    </div>
                )}

                <section className="mb-8 p-6 bg-muted/5 border border-muted/10 rounded-xl">
                    <h2 className="text-sm font-semibold text-muted mb-6 text-center">
                        Order Progress
                    </h2>
                    <OrderStatusTimeline steps={timeline} />
                </section>

                <section className="mb-8 p-5 bg-blue-50/50 border border-blue-100 rounded-xl">
                    <h2 className="text-sm font-semibold text-blue-800 mb-3">
                        What happens next?
                    </h2>
                    <div className="space-y-2 text-sm text-blue-700">
                        <div className="flex items-start gap-2">
                            <span className="text-blue-400 mt-0.5">-&gt;</span>
                            <span>You&apos;ll receive a confirmation email shortly with all the details</span>
                        </div>
                        <div className="flex items-start gap-2">
                            <span className="text-blue-400 mt-0.5">-&gt;</span>
                            <span>We&apos;ll send you a tracking link once your order ships</span>
                        </div>
                        <div className="flex items-start gap-2">
                            <span className="text-blue-400 mt-0.5">-&gt;</span>
                            <span>Your fulfillment flow can now continue safely because payment is confirmed</span>
                        </div>
                    </div>
                </section>

                <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
                    <OrderSummaryCard order={order} />
                    <ShippingAddressCard address={order.shippingAddress} />
                </div>

                <OrderItemsList items={order.items} currency={order.currency} />

                <div className="mt-8 flex flex-col sm:flex-row gap-3 justify-center">
                    <button
                        onClick={() => navigate('/products')}
                        className="py-3 px-8 rounded-xl font-medium bg-primary text-white hover:bg-primary/90 transition-colors"
                    >
                        Continue Shopping
                    </button>

                    <button
                        onClick={() => navigate(buildOrderTrackingHref(trackingOrderId))}
                        className="py-3 px-8 rounded-xl font-medium border border-primary/30 text-primary hover:bg-primary/5 transition-colors"
                    >
                        Track Order
                    </button>

                    <button
                        onClick={() => void refetch()}
                        className="py-3 px-8 rounded-xl font-medium border border-muted/20 text-text hover:bg-muted/10 transition-colors"
                    >
                        Refresh Order
                    </button>
                </div>
            </div>
        </div>
    );
};
