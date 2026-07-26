/**
 * Checkout Page
 * 
 * Multi-step checkout flow:
 * 1. Shipping (Address + Shipping Method)
 * 2. Payment (Payment Method Selection)
 * 3. Review & Place Order
 * 
 * Single route: /checkout
 * Steps controlled via internal state, NOT route changes.
 * 
 * Phase 7.7: Checkout UI Implementation
 * Phase 7.7.1: UX Polish - Step transitions, state cleanup, accessibility
 * Phase 10.1: Multi-language support
 * Phase 5A: Analytics tracking (beginCheckout)
 */

import React, { useEffect, useCallback, useRef } from 'react';
import { Layout } from '@/components/layout';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import {
    CheckoutStepper,
    OrderSummary,
    ShippingStep,
    PaymentStep,
    ReviewStep,
    CheckoutSkeleton,
    CheckoutEmptyState,
    StripePaymentForm,
    PaymobPaymentForm,
    AffiliateGuestCheckoutBanner,

} from '@/components/checkout';

import { useCategories } from '@/hooks';
import { useBuildAffiliateUrl } from '@/hooks/useAffiliateNavigation';
import { useTranslation } from '@/i18n';
import { useAnalytics } from '@/analytics';
import {
    useCartStore,
    selectCart,
    selectIsCartEmpty,
    selectIsCartInitialized,
    selectIsCartLoading,
    selectCartSubtotal,
} from '@/store';
import {
    useCheckoutStore,
    type CheckoutAutosaveStatus,
    selectCheckoutStep,
    selectOrderResult,
    selectIsSubmitting,
    selectCheckoutAutosaveStatus,
    selectCheckoutAutosaveError,
    selectCheckoutLastAutosavedAt,
    selectCheckoutError,
    selectErrorCode,
    selectShippingMethodId,
    selectSelectedPaymentMethodCode,
    selectShippingAddress,
} from '@/store/checkoutStore';
import type { CheckoutStep, OrderResult } from '@/types/checkout';

/**
 * Check if user prefers reduced motion
 */
const prefersReducedMotion = (): boolean => {
    if (typeof window === 'undefined') return false;
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
};

/**
 * Smooth scroll to element with reduced motion support
 */
const smoothScrollTo = (element: HTMLElement | null, offset = 0) => {
    if (!element) return;

    const top = element.getBoundingClientRect().top + window.scrollY - offset;

    if (prefersReducedMotion()) {
        window.scrollTo({ top });
    } else {
        window.scrollTo({ top, behavior: 'smooth' });
    }
};

const buildPendingCheckoutUrl = (order: OrderResult): string => {
    const params = new URLSearchParams({
        orderId: order.orderId,
    });

    return `/checkout/pending?${params.toString()}`;
};

const formatAutosaveTime = (value: string | null): string | null => {
    if (!value) {
        return null;
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return null;
    }

    return new Intl.DateTimeFormat(undefined, {
        hour: 'numeric',
        minute: '2-digit',
    }).format(date);
};

interface CheckoutAutosaveBannerProps {
    status: CheckoutAutosaveStatus;
    error: string | null;
    lastSavedAt: string | null;
    onRetry: () => Promise<void>;
}

const CheckoutAutosaveBanner: React.FC<CheckoutAutosaveBannerProps> = ({
    status,
    error,
    lastSavedAt,
    onRetry,
}) => {
    if (status === 'idle') {
        return null;
    }

    const savedAtLabel = formatAutosaveTime(lastSavedAt);

    if (status === 'error') {
        return (
            <div className="mb-6 rounded-2xl border border-red-200 bg-red-50 px-4 py-3" role="alert">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                        <p className="font-semibold text-red-900">Checkout changes are not saved</p>
                        <p className="mt-1 text-sm text-red-700">
                            {error || "We couldn't save your checkout changes. Retry to keep this checkout recoverable."}
                        </p>
                    </div>
                    <button
                        onClick={() => void onRetry()}
                        className="inline-flex items-center justify-center rounded-xl bg-red-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-red-700"
                    >
                        Retry save
                    </button>
                </div>
            </div>
        );
    }

    if (status === 'saving') {
        return (
            <div className="mb-6 rounded-2xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm text-sky-800" role="status">
                Saving checkout details...
            </div>
        );
    }

    if (status === 'unsaved') {
        return (
            <div className="mb-6 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800" role="status">
                Unsaved changes. We will keep trying to save this checkout.
            </div>
        );
    }

    return (
        <div className="mb-6 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800" role="status">
            {savedAtLabel
                ? `Checkout details saved at ${savedAtLabel}.`
                : 'Checkout details saved.'}
        </div>
    );
};

/**
 * Checkout Page Component
 */
export const CheckoutPage: React.FC = () => {
    const { t } = useTranslation();
    const buildAffiliateUrl = useBuildAffiliateUrl();

    // Refs
    const stepContainerRef = useRef<HTMLDivElement>(null);
    const previousStepRef = useRef<CheckoutStep>('shipping');
    const checkoutTrackedRef = useRef(false);

    // PHASE 5A: Analytics for tracking
    const analytics = useAnalytics();

    // Categories for layout
    const { categories, isLoading: categoriesLoading } = useCategories();

    // Cart state
    const isCartEmpty = useCartStore(selectIsCartEmpty);
    const isCartInitialized = useCartStore(selectIsCartInitialized);
    const isCartLoading = useCartStore(selectIsCartLoading);
    const cart = useCartStore(selectCart);
    const cartTotal = useCartStore(selectCartSubtotal);
    const fetchCart = useCartStore((state) => state.fetchCart);

    // Checkout state
    const currentStep = useCheckoutStore(selectCheckoutStep);
    const orderResult = useCheckoutStore(selectOrderResult);
    const isSubmitting = useCheckoutStore(selectIsSubmitting);
    const autosaveStatus = useCheckoutStore(selectCheckoutAutosaveStatus);
    const autosaveError = useCheckoutStore(selectCheckoutAutosaveError);
    const lastAutosavedAt = useCheckoutStore(selectCheckoutLastAutosavedAt);
    const checkoutError = useCheckoutStore(selectCheckoutError);
    const checkoutErrorCode = useCheckoutStore(selectErrorCode);
    const shippingMethodId = useCheckoutStore(selectShippingMethodId);
    const selectedPaymentMethodCode = useCheckoutStore(selectSelectedPaymentMethodCode);
    const shippingAddress = useCheckoutStore(selectShippingAddress);

    // Checkout actions
    const goToStep = useCheckoutStore((state) => state.goToStep);
    const goToNextStep = useCheckoutStore((state) => state.goToNextStep);
    const goToPreviousStep = useCheckoutStore((state) => state.goToPreviousStep);
    const submitCheckout = useCheckoutStore((state) => state.submitCheckout);
    const fetchShippingMethods = useCheckoutStore((state) => state.fetchShippingMethods);
    const fetchPaymentMethods = useCheckoutStore((state) => state.fetchPaymentMethods);
    const refreshPricingPreview = useCheckoutStore((state) => state.refreshPricingPreview);
    const retryAutosaveCheckoutSession = useCheckoutStore((state) => state.retryAutosaveCheckoutSession);
    const resetCheckoutError = useCheckoutStore((state) => state.resetError);

    // Fetch cart and payment methods on mount
    // This ensures coupon state and payment options are current
    useEffect(() => {
        // Always fetch cart when entering checkout to ensure freshness
        fetchCart();
        fetchShippingMethods();
        // Fetch payment methods from POS Admin (Phase 10.4.D2)
        fetchPaymentMethods();
    }, [fetchCart, fetchShippingMethods, fetchPaymentMethods]);

    useEffect(() => {
        if (!cart?.cartId) {
            return;
        }

        void refreshPricingPreview();
    }, [
        cart?.cartId,
        cart?.lastUpdatedUtc,
        shippingMethodId,
        selectedPaymentMethodCode,
        shippingAddress?.country,
        shippingAddress?.geography,
        refreshPricingPreview
    ]);

    // PHASE 5A: Track beginCheckout ONCE when cart is loaded (CRITICAL for funnel)
    useEffect(() => {
        if (isCartInitialized && !isCartEmpty && !checkoutTrackedRef.current && cartTotal > 0) {
            analytics.beginCheckout(cartTotal);
            checkoutTrackedRef.current = true;
            console.log('[Analytics] BeginCheckout tracked, cartTotal:', cartTotal);

            // Phase: Abandoned Checkout — create/resume session (fire-and-forget)
            useCheckoutStore.getState().initCheckoutSession();
        }
    }, [isCartInitialized, isCartEmpty, cartTotal, analytics]);

    // Step transition effects - scroll to top and highlight
    useEffect(() => {
        if (previousStepRef.current !== currentStep) {
            // Scroll to step container
            smoothScrollTo(stepContainerRef.current, 120);

            // Add brief highlight animation (via CSS class)
            const stepHeading = stepContainerRef.current?.querySelector('h2, section > h2');
            if (stepHeading && !prefersReducedMotion()) {
                stepHeading.classList.add('checkout-step-highlight');
                setTimeout(() => {
                    stepHeading.classList.remove('checkout-step-highlight');
                }, 600);
            }

            // Focus management for accessibility
            setTimeout(() => {
                const heading = stepContainerRef.current?.querySelector('h2');
                if (heading) {
                    (heading as HTMLElement).focus();
                }
            }, 100);

            previousStepRef.current = currentStep;
        }
    }, [currentStep]);

    // Every post-checkout path must go through backend-confirmed pending state
    // unless the user still needs to complete an inline payment form first.
    useEffect(() => {
        if (!orderResult) {
            return;
        }

        const hasStripeInline = orderResult.stripeClientSecret && orderResult.stripePublishableKey;
        const hasPaymobInline = orderResult.paymobIframeUrl;
        const requiresPendingRoute = !hasStripeInline && !hasPaymobInline;

        if (requiresPendingRoute) {
            window.location.replace(buildAffiliateUrl(buildPendingCheckoutUrl(orderResult)));
        }
    }, [buildAffiliateUrl, orderResult]);

    // Handle step navigation with accessibility
    const handleStepClick = useCallback((step: CheckoutStep) => {
        if (!isSubmitting) {
            goToStep(step);
        }
    }, [goToStep, isSubmitting]);

    // Handle next step with smooth transition
    const handleNextStep = useCallback(() => {
        goToNextStep();
    }, [goToNextStep]);

    // Handle previous step with smooth transition
    const handlePreviousStep = useCallback(() => {
        goToPreviousStep();
    }, [goToPreviousStep]);

    // Handle place order
    const handlePlaceOrder = useCallback(async () => {
        const success = await submitCheckout();
        if (success) {
            // Scroll to top for success view
            smoothScrollTo(document.body);
        }
    }, [submitCheckout]);

    // Determine what to render
    const isLoading = !isCartInitialized || isCartLoading || categoriesLoading;

    // Render loading skeleton
    if (isLoading) {
        return (
            <Layout categories={categories} isLoading>
                <div className="container mx-auto px-4 py-6 md:py-10">
                    <CheckoutSkeleton />
                </div>
            </Layout>
        );
    }

    // Render success screen or payment form based on payment method
    // PHASE 10.4.D5: Paymob Checkout Flow Enforcement
    // ARCHITECTURAL RULE: Order must NOT be marked as success until payment is confirmed via webhook
    if (orderResult) {
        // Localization-first approach for promotion warnings:
        // 1. Try to use a localization key if backend provides promotionWarningCode
        // 2. Use backend message only as a fallback for backward compatibility
        // 3. Fall back to generic localized message
        const promotionWarningMessage = (() => {
            if (!orderResult.promotionCalculationFailed) {
                return null;
            }

            // Priority 1: Use localization code if provided
            if (orderResult.promotionWarningCode) {
                const localizedKey = `checkout.promotionWarnings.${orderResult.promotionWarningCode}`;
                const translated = t(localizedKey, orderResult.promotionWarningParams || {});
                // Only use if translation exists (doesn't return the key itself)
                if (translated !== localizedKey) {
                    return translated;
                }
            }

            // Priority 2: Fall back to backend message for backward compatibility
            if (orderResult.promotionWarningMessage) {
                return orderResult.promotionWarningMessage;
            }

            // Priority 3: Fall back to generic localized message
            return t('checkout.promotionCalculationFailed');
        })();

        const promotionWarning = promotionWarningMessage ? (
            <div
                className="mb-6 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm font-medium text-amber-900"
                role="alert"
            >
                {promotionWarningMessage}
            </div>
        ) : null;

        // =========================================================
        // STRIPE: Show Stripe payment form (client secret present)
        // =========================================================
        if (orderResult.stripeClientSecret && orderResult.stripePublishableKey) {
            console.log('[Checkout] Showing Stripe payment form | orderId:', orderResult.orderId);
            const affiliateAwarePendingUrl = buildAffiliateUrl(buildPendingCheckoutUrl(orderResult));
            return (
                <Layout categories={categories}>
                    <div className="container mx-auto px-4 py-6 md:py-10">
                        <div className="max-w-2xl mx-auto">
                            {promotionWarning}
                            <h1 className="text-2xl font-bold mb-6 text-center">{t('checkout.completePayment')}</h1>
                            <StripePaymentForm
                                clientSecret={orderResult.stripeClientSecret}
                                publishableKey={orderResult.stripePublishableKey}
                                orderId={orderResult.orderId}
                                orderNumber={orderResult.orderNumber}
                                amount={orderResult.total}
                                currency={orderResult.currency}
                                returnUrl={`${window.location.origin}${affiliateAwarePendingUrl}`}
                                onPaymentPending={() => {
                                    window.location.href = affiliateAwarePendingUrl;
                                }}
                                onPaymentError={(err) => console.error('[Checkout] Stripe payment error:', err)}
                                onCancel={() => {
                                    // Reload to show pending state
                                    window.location.reload();
                                }}
                            />
                        </div>
                    </div>
                </Layout>
            );
        }

        // =========================================================
        // PAYMOB: Show Paymob payment form (iframe URL present)
        // CRITICAL: Do NOT redirect to success - wait for payment confirmation
        // =========================================================
        if (orderResult.paymobIframeUrl) {
            console.log('[Checkout] Redirecting to Paymob | orderId:', orderResult.orderId, '| iframeUrl:', orderResult.paymobIframeUrl);
            return (
                <Layout categories={categories}>
                    <div className="container mx-auto px-4 py-6 md:py-10">
                        <div className="max-w-2xl mx-auto">
                            {promotionWarning}
                            <h1 className="text-2xl font-bold mb-6 text-center">{t('checkout.completePayment')}</h1>
                            <PaymobPaymentForm
                                iframeUrl={orderResult.paymobIframeUrl}
                                orderId={orderResult.orderId}
                                orderNumber={orderResult.orderNumber}
                                amount={orderResult.total}
                                currency={orderResult.currency}
                                onPending={() => {
                                    console.log('[Checkout] Waiting for Paymob backend confirmation | orderId:', orderResult.orderId);
                                    window.location.href = buildAffiliateUrl(buildPendingCheckoutUrl(orderResult));
                                }}
                                onError={(error) => {
                                    if (error) {
                                        console.error('[Checkout] Paymob payment error:', error);
                                    }
                                }}
                            />
                        </div>
                    </div>
                </Layout>
            );
        }
        return (
            <Layout categories={categories}>
                <div className="container mx-auto px-4 py-6 md:py-10">
                    {promotionWarning}
                    <div className="mx-auto max-w-2xl rounded-2xl border border-blue-200 bg-blue-50 p-6 text-center">
                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-blue-100">
                            <svg className="h-7 w-7 animate-spin text-blue-600" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 0 1 8-8V0C5.373 0 0 5.373 0 12h4Z" />
                            </svg>
                        </div>
                        <h1 className="mt-4 text-2xl font-bold text-text">
                            {t('checkout.pending.redirectingTitle')}
                        </h1>
                        <p className="mt-2 text-muted">
                            {t('checkout.pending.redirectingDescription')}
                        </p>
                    </div>
                </div>
            </Layout>
        );
    }


    // Render empty cart state
    if (isCartEmpty) {
        return (
            <Layout categories={categories}>
                <div className="container mx-auto px-4 py-6 md:py-10">
                    <CheckoutEmptyState />
                </div>
            </Layout>
        );
    }

    // Render step content
    const renderStep = () => {
        switch (currentStep) {
            case 'shipping':
                return <ShippingStep onContinue={handleNextStep} />;
            case 'payment':
                return <PaymentStep onContinue={handleNextStep} onBack={handlePreviousStep} />;
            case 'review':
                return <ReviewStep onPlaceOrder={handlePlaceOrder} onBack={handlePreviousStep} />;
            default:
                return null;
        }
    };

    return (
        <Layout categories={categories}>
            <div className="container mx-auto px-4 py-6 md:py-10">
                {/* Page Header */}
                <div className="mb-6">
                    {/* Breadcrumb */}
                    <nav className="flex items-center gap-2 text-sm mb-4" aria-label="Breadcrumb">
                        <AffiliateLink href="/" className="text-muted hover:text-primary transition-colors">
                            {t('common.home')}
                        </AffiliateLink>
                        <svg className="w-4 h-4 text-muted/50 rtl-flip" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                        </svg>
                        <AffiliateLink href="/cart" className="text-muted hover:text-primary transition-colors">
                            {t('cart.title')}
                        </AffiliateLink>
                        <svg className="w-4 h-4 text-muted/50 rtl-flip" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                        </svg>
                        <span className="text-text font-medium">{t('checkout.title')}</span>
                    </nav>

                    {/* Title */}
                    <h1 className="text-2xl md:text-3xl font-bold text-text">{t('checkout.title')}</h1>
                </div>

                {/* AF-13B: Guest Checkout Banner (affiliate mode only) */}
                <AffiliateGuestCheckoutBanner />

                {/* Stepper */}
                <CheckoutStepper
                    currentStep={currentStep}
                    onStepClick={handleStepClick}
                    disabled={isSubmitting}
                />

                <CheckoutAutosaveBanner
                    status={autosaveStatus}
                    error={autosaveError}
                    lastSavedAt={lastAutosavedAt}
                    onRetry={retryAutosaveCheckoutSession}
                />

                {checkoutError && currentStep !== 'review' && (
                    <div
                        className={`mb-6 rounded-2xl px-4 py-3 ${
                            checkoutErrorCode?.startsWith('RECOVERY_') || checkoutErrorCode?.startsWith('MANUAL_WALLET_')
                                ? 'border border-amber-200 bg-amber-50 text-amber-800'
                                : 'border border-red-200 bg-red-50 text-red-800'
                        }`}
                        role="alert"
                    >
                        <div className="flex items-start justify-between gap-4">
                            <p className="text-sm font-medium">{checkoutError}</p>
                            <button
                                type="button"
                                onClick={resetCheckoutError}
                                className="text-sm underline hover:no-underline"
                            >
                                {t('common.dismiss')}
                            </button>
                        </div>
                    </div>
                )}

                {/* Main Content */}
                <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 lg:gap-8">
                    {/* Step Content */}
                    <div
                        ref={stepContainerRef}
                        className="lg:col-span-2"
                    >
                        {renderStep()}
                    </div>

                    {/* Order Summary */}
                    <div className="lg:col-span-1">
                        <div className="lg:sticky lg:top-24">
                            <OrderSummary />
                        </div>
                    </div>
                </div>
            </div>
        </Layout>
    );
};
