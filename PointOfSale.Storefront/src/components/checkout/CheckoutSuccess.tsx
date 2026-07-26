/**
 * Checkout Success Component
 * 
 * Success screen shown after successful order placement.
 * Displays order number, next steps, and order timeline.
 * 
 * Phase 7.7: Checkout UI Implementation
 * Phase 7.7.1: UX Polish - Enhanced copy, timeline preview, track order CTA
 * Phase 10.5: i18n Support - Arabic translations
 * Purchase analytics are emitted server-side after authoritative payment confirmation.
 */

import React, { useEffect } from 'react';
import { useTranslation } from '@/i18n';
import { formatMoney } from '@/utils';
import type { OrderResult } from '@/types/checkout';

interface CheckoutSuccessProps {
    order: OrderResult;
    onContinueShopping: () => void;
}

/**
 * Order Timeline Component
 * Visual preview of order journey
 */
const OrderTimeline: React.FC = () => {
    const { t } = useTranslation();

    const steps = [
        { label: t('checkoutSuccess.timeline.orderPlaced'), icon: '1', status: 'completed' as const },
        { label: t('checkoutSuccess.timeline.preparing'), icon: '2', status: 'upcoming' as const },
        { label: t('checkoutSuccess.timeline.shipped'), icon: '3', status: 'upcoming' as const },
        { label: t('checkoutSuccess.timeline.delivered'), icon: '4', status: 'upcoming' as const },
    ];

    return (
        <div className="flex items-center justify-between max-w-sm mx-auto">
            {steps.map((step, index) => (
                <React.Fragment key={step.label}>
                    <div className="flex flex-col items-center">
                        <div
                            className={`
                                w-10 h-10 rounded-full flex items-center justify-center text-lg
                                ${step.status === 'completed'
                                    ? 'bg-green-100 text-green-600'
                                    : 'bg-muted/10 text-muted'
                                }
                            `}
                        >
                            {step.icon}
                        </div>
                        <span
                            className={`
                                text-xs mt-1
                                ${step.status === 'completed' ? 'text-green-700 font-medium' : 'text-muted'}
                            `}
                        >
                            {step.label}
                        </span>
                    </div>
                    {index < steps.length - 1 && (
                        <div
                            className={`
                                flex-1 h-0.5 mx-1 mt-[-16px]
                                ${step.status === 'completed' ? 'bg-green-300' : 'bg-muted/20'}
                            `}
                        />
                    )}
                </React.Fragment>
            ))}
        </div>
    );
};

/**
 * Checkout Success Component
 */
export const CheckoutSuccess: React.FC<CheckoutSuccessProps> = ({
    order,
    onContinueShopping,
}) => {
    const { t } = useTranslation();

    // Focus management for accessibility
    useEffect(() => {
        const heading = document.getElementById('success-heading');
        heading?.focus();
    }, []);

    // Normalize payment method for comparison (handle both uppercase and lowercase)
    const isCod = order.paymentMethod?.toUpperCase() === 'COD';

    // Get localized payment method name
    const getPaymentMethodName = () => {
        const method = order.paymentMethod?.toUpperCase();
        if (method === 'COD') {
            return t('checkoutSuccess.paymentMethods.cod');
        } else if (method === 'STRIPE') {
            return t('checkoutSuccess.paymentMethods.stripe');
        } else if (method === 'PAYMOB') {
            return t('checkoutSuccess.paymentMethods.paymob');
        } else if (method === 'FAWRY') {
            return 'Fawry';
        }
        return t('checkoutSuccess.paymentMethods.online');
    };

    const formattedTotal = formatMoney(order.total, order.currency);

    return (
        <div className="text-center py-8 px-4">
            {/* Success Icon with animation */}
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
                            strokeWidth={2}
                            d="M5 13l4 4L19 7"
                        />
                    </svg>
                </div>
            </div>

            {/* Heading - Friendly, calm tone */}
            <h1
                id="success-heading"
                className="text-2xl md:text-3xl font-bold text-text mb-2"
                tabIndex={-1}
            >
                {t('checkoutSuccess.title')}
            </h1>

            <p className="text-muted mb-8 max-w-md mx-auto">
                {t('checkoutSuccess.subtitle')}
            </p>

            {/* Order Details Card */}
            <div className="max-w-md mx-auto bg-muted/5 border border-muted/10 rounded-xl p-6 mb-8">
                <div className="space-y-4">
                    {/* Order Number */}
                    <div>
                        <p className="text-sm text-muted mb-1">
                            {t('checkoutSuccess.orderNumber')}
                        </p>
                        <p className="text-lg font-bold text-text font-mono">
                            #{order.orderNumber}
                        </p>
                    </div>

                    <hr className="border-muted/10" />

                    {/* Order Total */}
                    <div>
                        <p className="text-sm text-muted mb-1">
                            {t('checkoutSuccess.totalAmount')}
                        </p>
                        <p className="text-2xl font-bold text-text">
                            {formattedTotal}
                        </p>
                    </div>

                    {/* Payment Method */}
                    <div>
                        <p className="text-sm text-muted mb-1">
                            {t('checkoutSuccess.paymentMethod')}
                        </p>
                        <p className="text-text font-medium">
                            {getPaymentMethodName()}
                        </p>
                    </div>
                </div>
            </div>

            {/* COD Notice - Friendly tone */}
            {isCod && (
                <div className="max-w-md mx-auto mb-8 p-4 bg-amber-50 border border-amber-200 rounded-lg text-left rtl:text-right">
                    <div className="flex items-start gap-3">
                        <span className="text-xl" aria-hidden="true">$</span>
                        <div>
                            <p className="text-sm font-medium text-amber-800">
                                {t('checkoutSuccess.codNotice.title')}
                            </p>
                            <p className="text-sm text-amber-700 mt-1">
                                {t('checkoutSuccess.codNotice.description', { amount: formattedTotal })}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Fawry Reference Code Display */}
            {(order.paymentMethod?.toUpperCase() === 'FAWRY' && order.referenceCode) && (
                <div className="max-w-md mx-auto mb-8 p-6 bg-yellow-50 border-2 border-yellow-400 rounded-xl text-center animate-pulse-once">
                    <p className="text-lg font-bold text-yellow-800 mb-3">
                        Fawry Reference Code / كود فوري
                    </p>
                    <div className="text-4xl font-mono font-bold tracking-widest text-black bg-white py-4 px-6 rounded-lg inline-block border border-gray-200 shadow-sm mb-3">
                        {order.referenceCode}
                    </div>
                    {order.expiresAtUtc && (
                        <p className="text-sm text-yellow-700 font-medium">
                            Expires: {new Date(order.expiresAtUtc).toLocaleString()}
                        </p>
                    )}
                    <p className="text-xs text-yellow-600 mt-2">
                        Use this code to pay at any Fawry machine.
                    </p>
                </div>
            )}

            {/* Manual Wallet - Payment Under Review */}
            {(order.paymentMethod?.toUpperCase() === 'MANUALWALLET') && (
                <div className="max-w-md mx-auto mb-8 p-6 bg-blue-50 border-2 border-blue-400 rounded-xl text-center">
                    <div className="w-16 h-16 mx-auto bg-blue-100 rounded-full flex items-center justify-center mb-4 text-3xl">
                        i
                    </div>
                    <h3 className="text-lg font-bold text-blue-900 mb-2">
                        Payment Under Review
                    </h3>
                    <p className="text-sm text-blue-800 mb-4">
                        We have received your payment proof. Your order will be confirmed shortly after verification.
                    </p>
                    <div className="text-xs bg-white/50 p-2 rounded text-blue-700">
                        Method: {order.walletType}<br />
                        Merchant Number: {order.merchantWalletNumber}
                    </div>
                </div>
            )}

            {/* Order Timeline Preview */}
            <div className="max-w-md mx-auto mb-8">
                <h2 className="text-sm font-medium text-muted mb-4">
                    {t('checkoutSuccess.orderProgress')}
                </h2>
                <OrderTimeline />
            </div>

            {/* What's Next - Enhanced copy */}
            <div className="max-w-md mx-auto mb-8 p-4 bg-blue-50/50 border border-blue-100 rounded-xl text-left rtl:text-right">
                <h2 className="text-sm font-semibold text-blue-800 mb-3">
                    {t('checkoutSuccess.whatsNext.title')}
                </h2>
                <div className="space-y-2 text-sm text-blue-700">
                    <div className="flex items-start gap-2">
                        <span className="text-blue-500 mt-0.5 rtl:order-last" aria-hidden="true">-</span>
                        <span>{t('checkoutSuccess.whatsNext.step1')}</span>
                    </div>
                    <div className="flex items-start gap-2">
                        <span className="text-blue-500 mt-0.5 rtl:order-last" aria-hidden="true">-</span>
                        <span>{t('checkoutSuccess.whatsNext.step2')}</span>
                    </div>
                    <div className="flex items-start gap-2">
                        <span className="text-blue-500 mt-0.5 rtl:order-last" aria-hidden="true">-</span>
                        <span>{t('checkoutSuccess.whatsNext.step3')}</span>
                    </div>
                </div>
            </div>

            {/* Action Buttons - Now with Track Order */}
            <div className="flex flex-col gap-3 justify-center max-w-md mx-auto">
                {/* Primary: Continue Shopping */}
                <button
                    onClick={onContinueShopping}
                    className="
                        w-full py-3 px-6
                        rounded-xl font-medium
                        bg-primary text-white
                        hover:bg-primary/90
                        transition-colors
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                    "
                >
                    {t('checkoutSuccess.continueShopping')}
                </button>

                {/* Secondary Row */}
                <div className="flex gap-3">
                    {/* Track Order - Links to placeholder */}
                    <a
                        href={`/orders/${order.orderId}/track`}
                        className="
                            flex-1 py-3 px-6
                            rounded-xl font-medium
                            border border-primary/30 text-primary
                            hover:bg-primary/5
                            transition-colors
                            text-center
                            focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        "
                    >
                        {t('checkoutSuccess.trackOrder')}
                    </a>

                    {/* Back to Home */}
                    <a
                        href="/"
                        className="
                            flex-1 py-3 px-6
                            rounded-xl font-medium
                            border border-muted/20 text-text
                            hover:bg-muted/10
                            transition-colors
                            text-center
                            focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        "
                    >
                        {t('checkoutSuccess.home')}
                    </a>
                </div>
            </div>

            {/* Footer note - Reassuring */}
            <p className="mt-8 text-xs text-muted">
                {t('checkoutSuccess.supportNote')}
            </p>
        </div>
    );
};
