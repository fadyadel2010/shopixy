/**
 * Paymob Payment Form Component
 *
 * The frontend never treats the Paymob return as final success.
 * After returning from Paymob, the user is moved to the pending screen
 * where backend payment status polling decides success or failure.
 */

import React, { useEffect, useState } from 'react';
import { useTranslation } from '@/i18n';

interface PaymobPaymentFormProps {
    iframeUrl: string;
    orderId: string;
    orderNumber: string;
    amount: number;
    currency: string;
    onPending: () => void;
    onError: (error: string | null) => void;
}

function parsePaymobRedirectParams(): {
    isReturn: boolean;
} {
    const urlParams = new URLSearchParams(window.location.search);
    const hasPaymobParams = urlParams.has('success') || urlParams.has('error_occured');

    return {
        isReturn: hasPaymobParams,
    };
}

export const PaymobPaymentForm: React.FC<PaymobPaymentFormProps> = ({
    iframeUrl,
    orderId,
    orderNumber,
    amount,
    currency,
    onPending,
    onError,
}) => {
    const { t, isRTL } = useTranslation();
    const [isLoading, setIsLoading] = useState(false);
    const [returnedFromPaymob, setReturnedFromPaymob] = useState(false);
    const [paymentError, setPaymentError] = useState<string | null>(null);
    const [formattedAmount, setFormattedAmount] = useState<string>('');

    // Validate currency and format amount in useEffect to avoid render-time side effects
    useEffect(() => {
        try {
            // Currency is REQUIRED - must come from order (which got it from MongoDB)
            // NO fallbacks allowed - if currency is missing or invalid, capture error for inline display
            if (!currency || currency.trim() === '') {
                throw new Error('Currency is required for Paymob payment. This indicates a critical checkout error.');
            }

            // Format amount for display using the validated currency
            // This will throw if currency code is invalid (e.g., "INVALID_CODE")
            const formatted = new Intl.NumberFormat(isRTL ? 'ar-EG' : 'en-US', {
                style: 'currency',
                currency: currency,
            }).format(amount);
            
            setFormattedAmount(formatted);
            
            // Clear any previous errors when currency becomes valid
            // Use functional updater to deduplicate parent notifications
            setPaymentError(prev => {
                if (prev === null) return prev;
                onError(null);
                return null;
            });
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : 'Invalid currency configuration';
            
            // Set local error state for inline display and notify parent only if error changed
            // Use functional updater to deduplicate parent notifications
            setPaymentError(prev => {
                if (prev === errorMessage) return prev;
                onError(errorMessage);
                return errorMessage;
            });
            setFormattedAmount('');
        }
    }, [currency, amount, isRTL, onError]);

    const handlePayWithPaymob = () => {
        if (!iframeUrl) {
            const message = t('checkout.pending.paymobUnavailable');
            onError(message);
            return;
        }

        setIsLoading(true);
        window.location.href = iframeUrl;
    };

    useEffect(() => {
        const paymobParams = parsePaymobRedirectParams();
        if (!paymobParams.isReturn) {
            return;
        }

        // Provider redirect values are evidence only. They never identify a
        // payment; the pending route re-enters backend status authority.
        window.history.replaceState({}, '', window.location.pathname);

        setReturnedFromPaymob(true);
        onPending();
    }, [onPending, orderId]);

    // Render inline error fallback if currency validation failed
    if (paymentError) {
        return (
            <div className="paymob-payment-form space-y-6">
                <div className="rounded-xl border border-red-200 bg-red-50 p-4">
                    <div className="flex items-center gap-3">
                        <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-red-500">
                            <svg className="h-6 w-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                            </svg>
                        </div>
                        <div>
                            <h3 className="font-semibold text-red-900">
                                {t('checkout.paymentError') || 'Payment Configuration Error'}
                            </h3>
                            <p className="text-sm text-red-700">
                                {paymentError}
                            </p>
                        </div>
                    </div>
                </div>
                <p className="text-center text-sm text-muted">
                    {t('checkout.contactSupport') || 'Please contact support if this issue persists.'}
                </p>
            </div>
        );
    }

    return (
        <div className="paymob-payment-form space-y-6">
            <div className="rounded-xl border border-emerald-200 bg-gradient-to-br from-emerald-50 to-teal-50 p-4">
                <div className="mb-3 flex items-center gap-3">
                    <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-emerald-500">
                        <svg className="h-6 w-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 0 0 3-3V8a3 3 0 0 0-3-3H6a3 3 0 0 0-3 3v8a3 3 0 0 0 3 3Z" />
                        </svg>
                    </div>
                    <div>
                        <h3 className="font-semibold text-emerald-900">
                            {t('checkout.paymobPayment') || 'Paymob Payment'}
                        </h3>
                        <p className="text-sm text-emerald-700">
                            {t('checkout.paymobDescription') || 'Pay securely with Paymob'}
                        </p>
                    </div>
                </div>

                <div className="flex items-center justify-between border-t border-emerald-200 pt-3">
                    <span className="text-emerald-800">
                        {t('checkout.orderNumber') || 'Order'}: {orderNumber}
                    </span>
                    <span className="text-lg font-bold text-emerald-900">
                        {formattedAmount}
                    </span>
                </div>
            </div>

            {returnedFromPaymob ? (
                <div className="rounded-xl border border-blue-200 bg-blue-50 p-4">
                    <div className="flex items-center gap-3">
                        <div className="h-5 w-5 animate-spin rounded-full border-2 border-blue-500 border-t-transparent" />
                        <div>
                            <p className="font-medium text-blue-800">
                                {t('checkout.pending.redirectingTitle')}
                            </p>
                            <p className="text-sm text-blue-600">
                                {t('checkout.pending.redirectingDescription')}
                            </p>
                        </div>
                    </div>
                </div>
            ) : (
                <button
                    onClick={handlePayWithPaymob}
                    disabled={isLoading || !iframeUrl}
                    className={`
                        w-full rounded-xl px-6 py-4 text-lg font-semibold transition-all duration-200
                        ${isLoading || !iframeUrl
                            ? 'cursor-not-allowed bg-gray-300 text-gray-500'
                            : 'bg-gradient-to-r from-emerald-500 to-teal-500 text-white shadow-lg hover:from-emerald-600 hover:to-teal-600 hover:shadow-xl'
                        }
                    `}
                >
                    {isLoading ? (t('common.loading') || 'Loading...') : (t('checkout.payWithPaymob') || 'Pay with Paymob')}
                </button>
            )}

            <div className="flex items-center justify-center gap-2 text-sm text-muted">
                <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2Zm10-10V7a4 4 0 0 0-8 0v4h8Z" />
                </svg>
                <span>{t('checkout.securePayment') || 'Your payment is secured by Paymob'}</span>
            </div>
        </div>
    );
};

export default PaymobPaymentForm;
