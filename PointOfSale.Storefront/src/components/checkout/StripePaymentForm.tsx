/**
 * Stripe Payment Form Component
 * 
 * PHASE 10.4.D4: Stripe Integration (Global, Intent-Based, Production-Grade)
 * 
 * This component handles Stripe Payment Element integration.
 * 
 * ARCHITECTURAL RULES:
 * - Order is created BEFORE payment
 * - Uses clientSecret from backend
 * - Stripe.js handles all card input
 * - Webhook confirms final payment status
 * - NO pricing logic in this component
 */

import React, { useState, useEffect, useCallback } from 'react';
import { loadStripe } from '@stripe/stripe-js/pure';
import type { Stripe } from '@stripe/stripe-js';
import {
    Elements,
    PaymentElement,
    useStripe,
    useElements,
} from '@stripe/react-stripe-js';
import { useTranslation } from '@/i18n';
import { resolveStripeClientOutcome } from '@/features/checkout/paymentFlow';

// ============================================================================
// Types
// ============================================================================

interface StripePaymentFormProps {
    clientSecret: string;
    publishableKey: string;
    orderId: string;
    orderNumber: string;
    amount: number;
    currency: string;
    returnUrl: string;
    onPaymentPending: () => void;
    onPaymentError: (error: string) => void;
    onCancel: () => void;
}

interface PaymentFormInnerProps {
    orderNumber: string;
    amount: number;
    currency: string;
    returnUrl: string;
    onPaymentPending: () => void;
    onPaymentError: (error: string) => void;
    onCancel: () => void;
    isRtl: boolean;
}

// ============================================================================
// Stripe Promise Cache
// ============================================================================

const stripePromiseCache = new Map<string, Promise<Stripe | null>>();

const getStripePromise = (publishableKey: string) => {
    if (!stripePromiseCache.has(publishableKey)) {
        stripePromiseCache.set(publishableKey, loadStripe(publishableKey));
    }
    return stripePromiseCache.get(publishableKey)!;
};

// ============================================================================
// Payment Form Inner Component (uses Stripe hooks)
// ============================================================================

const PaymentFormInner: React.FC<PaymentFormInnerProps> = ({
    orderNumber,
    amount,
    currency,
    returnUrl,
    onPaymentPending,
    onPaymentError,
    onCancel,
    isRtl,
}) => {
    const { t } = useTranslation();
    const stripe = useStripe();
    const elements = useElements();

    const [isProcessing, setIsProcessing] = useState(false);
    const [errorMessage, setErrorMessage] = useState<string | null>(null);

    // PHASE 10.4.Y: Currency is REQUIRED - must come from order (which got it from MongoDB)
    // NO fallbacks allowed - if currency is missing, throw error to prevent wrong currency charges
    if (!currency || currency.trim() === '') {
        throw new Error('Currency is required for Stripe payment. This indicates a critical checkout error.');
    }

    // Format amount for display using the validated currency
    const formattedAmount = new Intl.NumberFormat(isRtl ? 'ar-SA' : 'en-US', {
        style: 'currency',
        currency: currency,
    }).format(amount);

    // Handle form submission
    const handleSubmit = useCallback(async (event: React.FormEvent) => {
        event.preventDefault();

        if (!stripe || !elements) {
            // Stripe.js hasn't loaded yet
            return;
        }

        // Prevent double submission
        if (isProcessing) {
            return;
        }

        setIsProcessing(true);
        setErrorMessage(null);

        try {
            // Confirm the payment with the Payment Element
            const { error, paymentIntent } = await stripe.confirmPayment({
                elements,
                confirmParams: {
                    // Return URL routes to the pending confirmation screen (affiliate-aware).
                    return_url: returnUrl,
                },
                redirect: 'if_required',
            });

            if (error) {
                // Handle specific error types
                let errorMsg = t('checkout.payment.stripe.failed');

                switch (error.code) {
                    case 'card_declined':
                        errorMsg = t('checkout.payment.stripe.errors.cardDeclined');
                        break;
                    case 'expired_card':
                        errorMsg = t('checkout.payment.stripe.errors.expiredCard');
                        break;
                    case 'incorrect_cvc':
                        errorMsg = t('checkout.payment.stripe.errors.incorrectCvc');
                        break;
                    case 'processing_error':
                        errorMsg = t('checkout.payment.stripe.errors.processingError');
                        break;
                    case 'insufficient_funds':
                        errorMsg = t('checkout.payment.stripe.errors.insufficientFunds');
                        break;
                    default:
                        errorMsg = error.message || t('checkout.payment.stripe.errors.genericDecline');
                }

                setErrorMessage(errorMsg);
                onPaymentError(errorMsg);
                setIsProcessing(false);
                return;
            }

            if (resolveStripeClientOutcome(paymentIntent?.status) === 'pending') {
                if (paymentIntent?.status === 'requires_action') {
                    setErrorMessage(t('checkout.payment.stripe.authenticating'));
                }

                // Frontend never treats Stripe.js status as final success.
                onPaymentPending();
            }
        } catch (err) {
            console.error('Payment error:', err);
            const errorMsg = t('checkout.payment.stripe.errors.processingError');
            setErrorMessage(errorMsg);
            onPaymentError(errorMsg);
        } finally {
            setIsProcessing(false);
        }
    }, [stripe, elements, isProcessing, returnUrl, onPaymentError, onPaymentPending, t]);

    return (
        <form onSubmit={handleSubmit} className="space-y-6">
            {/* Order Summary Header */}
            <div className="bg-primary/5 rounded-xl p-4 border border-primary/20">
                <div className="flex justify-between items-center">
                    <span className="text-sm text-muted">
                        {t('order.orderNumber')}
                    </span>
                    <span className="font-mono font-medium text-text">
                        {orderNumber}
                    </span>
                </div>
                <div className="flex justify-between items-center mt-2">
                    <span className="text-sm text-muted">
                        {t('cart.total')}
                    </span>
                    <span className="text-lg font-semibold text-primary">
                        {formattedAmount}
                    </span>
                </div>
            </div>

            {/* Stripe Payment Element */}
            <div className="bg-white rounded-xl border border-muted/20 p-4">
                <PaymentElement
                    options={{
                        layout: 'tabs',
                        paymentMethodOrder: ['card'],
                    }}
                />
            </div>

            {/* Error Message */}
            {errorMessage && (
                <div className="p-4 bg-red-50 border border-red-200 rounded-xl text-red-700 text-sm flex items-start gap-2">
                    <svg className="w-5 h-5 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                    </svg>
                    <span>{errorMessage}</span>
                </div>
            )}

            {/* Security Notice */}
            <div className="flex items-center gap-2 text-sm text-muted">
                <svg className="w-4 h-4 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                </svg>
                <span>{t('checkout.payment.stripe.securePayment')}</span>
            </div>

            {/* Action Buttons */}
            <div className="flex flex-col sm:flex-row gap-3 pt-2">
                <button
                    type="button"
                    onClick={onCancel}
                    disabled={isProcessing}
                    className="
                        py-3 px-6
                        rounded-xl font-medium
                        border border-muted/20 text-text
                        hover:bg-muted/10
                        transition-colors
                        disabled:opacity-50 disabled:cursor-not-allowed
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        order-2 sm:order-1
                    "
                >
                    {t('checkout.backToPayment')}
                </button>

                <button
                    type="submit"
                    disabled={!stripe || isProcessing}
                    className={`
                        flex-1 py-3.5 px-6
                        rounded-xl font-medium
                        transition-all duration-200
                        focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        order-1 sm:order-2
                        ${!stripe || isProcessing
                            ? 'bg-muted/20 text-muted cursor-not-allowed'
                            : 'bg-primary text-white hover:bg-primary/90 shadow-md hover:shadow-lg'
                        }
                    `}
                >
                    {isProcessing ? (
                        <span className="flex items-center justify-center gap-2">
                            <svg className="w-5 h-5 animate-spin" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                            </svg>
                            {t('checkout.payment.stripe.processing')}
                        </span>
                    ) : (
                        <span className="flex items-center justify-center gap-2">
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                            </svg>
                            {t('checkout.payment.stripe.payNow')} - {formattedAmount}
                        </span>
                    )}
                </button>
            </div>
        </form>
    );
};

// ============================================================================
// Main Stripe Payment Form Component
// ============================================================================

export const StripePaymentForm: React.FC<StripePaymentFormProps> = ({
    clientSecret,
    publishableKey,
    orderNumber,
    amount,
    currency,
    returnUrl,
    onPaymentPending,
    onPaymentError,
    onCancel,
}) => {
    const { isRTL } = useTranslation();
    const [stripePromise, setStripePromise] = useState<Promise<Stripe | null> | null>(null);

    // Initialize Stripe with the publishable key
    useEffect(() => {
        if (publishableKey) {
            setStripePromise(getStripePromise(publishableKey));
        }
    }, [publishableKey]);

    // Don't render until we have both Stripe and clientSecret
    if (!stripePromise || !clientSecret) {
        return (
            <div className="flex items-center justify-center p-8">
                <div className="flex flex-col items-center gap-4">
                    <svg className="w-8 h-8 animate-spin text-primary" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                    <span className="text-muted">Loading payment form...</span>
                </div>
            </div>
        );
    }

    return (
        <Elements
            stripe={stripePromise}
            options={{
                clientSecret,
                appearance: {
                    theme: 'stripe',
                    variables: {
                        colorPrimary: '#6366f1',
                        colorBackground: '#ffffff',
                        colorText: '#1f2937',
                        colorDanger: '#ef4444',
                        fontFamily: 'system-ui, -apple-system, sans-serif',
                        spacingUnit: '4px',
                        borderRadius: '8px',
                    },
                    rules: {
                        '.Label': {
                            marginBottom: '8px',
                        },
                        '.Input': {
                            padding: '12px',
                        },
                    },
                },
                locale: isRTL ? 'ar' : 'en',
            }}
        >
            <PaymentFormInner
                orderNumber={orderNumber}
                amount={amount}
                currency={currency}
                returnUrl={returnUrl}
                onPaymentPending={onPaymentPending}
                onPaymentError={onPaymentError}
                onCancel={onCancel}
                isRtl={isRTL}
            />
        </Elements>
    );
};

export default StripePaymentForm;
