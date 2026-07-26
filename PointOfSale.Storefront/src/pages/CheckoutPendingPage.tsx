import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useCategories } from '@/hooks';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';
import { useTranslation } from '@/i18n';
import { paymentStatusApi } from '@/services/api/paymentStatusApi';
import {
    type CanonicalPaymentStatusResponse,
    resolvePendingCheckoutAction,
} from '@/features/checkout/paymentFlow';
import {
    clearActiveCheckoutOrderContextIfMatches,
    readCheckoutTokenForOrder,
} from '@/features/checkout/activeCheckoutOrderContext';
import { finalizeSuccessfulCheckout } from '@/features/checkout/checkoutCompletion';
import { emitGuestPaymentRecoverySuccess } from '@/features/checkout/checkoutTelemetry';

const POLL_INTERVAL_MS = 3000;
const MAX_POLL_ATTEMPTS = 20;

export const CheckoutPendingPage: React.FC = () => {
    const { t } = useTranslation();
    const navigate = useAffiliateNavigate();
    const { categories, isLoading: categoriesLoading } = useCategories();
    const [searchParams] = useSearchParams();

    const orderId = (searchParams.get('orderId') || searchParams.get('order') || '').trim();

    const [paymentStatus, setPaymentStatus] = useState<CanonicalPaymentStatusResponse | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [isTimedOut, setIsTimedOut] = useState(false);
    const [hasFailed, setHasFailed] = useState(false);
    const [requiresManualReview, setRequiresManualReview] = useState(false);
    const [lastError, setLastError] = useState<string | null>(null);
    const [pollAttempt, setPollAttempt] = useState(0);

    const attemptRef = useRef(0);
    const redirectedRef = useRef(false);
    const navigateRef = useRef(navigate);
    const tRef = useRef(t);
    const abortControllerRef = useRef<AbortController | null>(null);
    const guestRecoveryTelemetrySentRef = useRef(false);

    // Keep refs up to date
    useEffect(() => {
        navigateRef.current = navigate;
        tRef.current = t;
    }, [navigate, t]);

    // Cleanup: abort any in-flight requests on unmount
    useEffect(() => {
        return () => {
            abortControllerRef.current?.abort();
            abortControllerRef.current = null;
        };
    }, []);

    const displayOrderNumber = useMemo(() => {
        return paymentStatus?.orderNumber || t('checkout.pending.orderUnavailable');
    }, [paymentStatus?.orderNumber, t]);

    const trackingHref = useMemo(() => {
        return `/orders/${orderId}/track`;
    }, [orderId]);

    const fetchPaymentStatus = useCallback(async (currentOrderId: string) => {
        if (!currentOrderId || redirectedRef.current) {
            setIsLoading(false);
            return;
        }

        // Abort any previous request
        abortControllerRef.current?.abort();
        const controller = new AbortController();
        abortControllerRef.current = controller;

        try {
            const checkoutToken = readCheckoutTokenForOrder(currentOrderId);
            const response = await paymentStatusApi.getCanonicalPaymentStatus(currentOrderId, {
                signal: controller.signal,
                checkoutToken,
            });
            
            // Ignore stale responses - verify we haven't been aborted or redirected
            if (controller.signal.aborted || redirectedRef.current) {
                return;
            }

            setPaymentStatus(response);
            setLastError(null);
            setIsLoading(false);

            if (checkoutToken && !guestRecoveryTelemetrySentRef.current) {
                guestRecoveryTelemetrySentRef.current = true;
                emitGuestPaymentRecoverySuccess(currentOrderId, 'volatile_checkout_token');
            }

            const nextAction = resolvePendingCheckoutAction(currentOrderId, response);
            if (nextAction.kind === 'success') {
                redirectedRef.current = true;
                navigateRef.current(nextAction.href, { replace: true });
                void finalizeSuccessfulCheckout(currentOrderId);
                return;
            } else if (nextAction.kind === 'manual_review') {
                setHasFailed(false);
                setRequiresManualReview(true);
                setIsTimedOut(false);
                return;
            } else if (nextAction.kind === 'failed') {
                clearActiveCheckoutOrderContextIfMatches(currentOrderId);
                setHasFailed(true);
                setRequiresManualReview(false);
                setIsTimedOut(false);
                return;
            } else {
                setHasFailed(false);
                setRequiresManualReview(false);
                setIsTimedOut(false);
            }
        } catch (error) {
            // Ignore abort errors - they're expected when orderId changes
            if (controller.signal.aborted || (error instanceof Error && error.name === 'AbortError')) {
                return;
            }
            console.error('[CheckoutPendingPage] Failed to load payment status:', error);
            setIsLoading(false);
            setLastError(tRef.current('checkout.pending.pollingError'));
            // Note: Errors intentionally count toward MAX_POLL_ATTEMPTS to prevent infinite polling
            // when the API is consistently failing. attemptRef.current is incremented below.
        }

        if (attemptRef.current + 1 >= MAX_POLL_ATTEMPTS) {
            setIsTimedOut(true);
            return;
        }

        attemptRef.current += 1;
        setPollAttempt(attemptRef.current);
    }, []);

    // Reset telemetry guard when actual orderId changes (not display number)
    useEffect(() => {
        guestRecoveryTelemetrySentRef.current = false;
    }, [orderId]);

    // Reset polling state when order identity changes
    useEffect(() => {
        // Abort any in-flight request for the previous orderId
        abortControllerRef.current?.abort();
        
        attemptRef.current = 0;
        redirectedRef.current = false;
        setPollAttempt(0);
        setPaymentStatus(null);
        setIsLoading(true);
        setIsTimedOut(false);
        setHasFailed(false);
        setRequiresManualReview(false);
        setLastError(null);
    }, [orderId]);

    // Initial fetch when order identity changes
    useEffect(() => {
        if (orderId) {
            void fetchPaymentStatus(orderId);
        }
    }, [orderId, fetchPaymentStatus]);

    useEffect(() => {
        if (!orderId || redirectedRef.current || isTimedOut || hasFailed || requiresManualReview || pollAttempt === 0) {
            return;
        }

        const timeoutId = window.setTimeout(() => {
            void fetchPaymentStatus(orderId);
        }, POLL_INTERVAL_MS);

        return () => {
            window.clearTimeout(timeoutId);
        };
    }, [fetchPaymentStatus, hasFailed, isTimedOut, orderId, pollAttempt, requiresManualReview]);

    const handleManualRefresh = useCallback(() => {
        attemptRef.current = 0;
        redirectedRef.current = false;
        setPollAttempt(0);
        setIsTimedOut(false);
        setHasFailed(false);
        setRequiresManualReview(false);
        setLastError(null);
        setIsLoading(true);
        if (orderId) {
            void fetchPaymentStatus(orderId);
        }
    }, [fetchPaymentStatus, orderId]);

    const statusTone = requiresManualReview
        ? 'border-orange-200 bg-orange-50'
        : hasFailed
            ? 'border-red-200 bg-red-50'
            : 'border-amber-200 bg-amber-50';

    const statusIcon = requiresManualReview ? (
        <svg className="h-10 w-10 text-orange-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v4m0 4h.01M10.29 3.86l-7.5 13A1 1 0 0 0 3.67 18h16.66a1 1 0 0 0 .87-1.5l-7.5-13a1 1 0 0 0-1.74 0Z" />
        </svg>
    ) : hasFailed ? (
        <svg className="h-10 w-10 text-red-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18 18 6M6 6l12 12" />
        </svg>
    ) : (
        <svg className="h-10 w-10 animate-spin text-amber-600" fill="none" viewBox="0 0 24 24">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 0 1 8-8V0C5.373 0 0 5.373 0 12h4Z" />
        </svg>
    );

    if (!orderId) {
        return (
            <Layout categories={categories} isLoading={categoriesLoading}>
                <div className="container mx-auto px-4 py-8 max-w-2xl">
                    <div className="rounded-2xl border border-red-200 bg-red-50 p-6 text-center">
                        <h1 className="text-2xl font-bold text-red-900">
                            {t('checkout.pending.missingOrderTitle')}
                        </h1>
                        <p className="mt-3 text-sm text-red-700">
                            {t('checkout.pending.missingOrderDescription')}
                        </p>
                        <div className="mt-6">
                            <AffiliateLink
                                href="/checkout"
                                className="inline-flex items-center justify-center rounded-xl bg-primary px-5 py-3 font-medium text-white hover:bg-primary/90"
                            >
                                {t('checkout.pending.returnToCheckout')}
                            </AffiliateLink>
                        </div>
                    </div>
                </div>
            </Layout>
        );
    }

    return (
        <Layout categories={categories} isLoading={categoriesLoading}>
            <div className="container mx-auto px-4 py-8 max-w-3xl">
                <div className="rounded-3xl border border-muted/10 bg-white p-6 shadow-sm md:p-8">
                    <div className="mx-auto max-w-2xl text-center">
                        <div className={`mx-auto flex h-20 w-20 items-center justify-center rounded-full ${
                            requiresManualReview ? 'bg-orange-100' : hasFailed ? 'bg-red-100' : 'bg-amber-100'
                        }`}>
                            {statusIcon}
                        </div>

                        <h1 className="mt-6 text-2xl font-bold text-text md:text-3xl">
                            {requiresManualReview
                                ? t('checkout.pending.reviewTitle')
                                : hasFailed
                                ? t('checkout.pending.failedTitle')
                                : t('checkout.pending.title')}
                        </h1>

                        <p className="mt-3 text-muted">
                            {requiresManualReview
                                ? t('checkout.pending.reviewDescription')
                                : hasFailed
                                ? t('checkout.pending.failedDescription')
                                : t('checkout.pending.description')}
                        </p>

                        <div className="mt-6 rounded-2xl border border-primary/10 bg-primary/5 p-4">
                            <p className="text-sm text-muted">{t('checkout.pending.orderNumber')}</p>
                            <p className="mt-1 font-mono text-lg font-semibold text-text">
                                {displayOrderNumber}
                            </p>
                            {paymentStatus && !hasFailed ? (
                                <p className="mt-2 text-xs text-muted">
                                    {t('checkout.pending.backendStatus')}: {paymentStatus.status}
                                </p>
                            ) : null}
                        </div>

                        {!hasFailed ? (
                            <div className={`mt-6 rounded-2xl border p-5 text-left ${statusTone}`}>
                                <p className="font-medium text-amber-900">
                                    {requiresManualReview
                                        ? t('checkout.pending.reviewStatusTitle')
                                        : isTimedOut
                                        ? t('checkout.pending.timeoutTitle')
                                        : t('checkout.pending.waitingTitle')}
                                </p>
                                <p className="mt-2 text-sm text-amber-800">
                                    {requiresManualReview
                                        ? t('checkout.pending.reviewStatusDescription')
                                        : isTimedOut
                                        ? t('checkout.pending.timeoutDescription')
                                        : t('checkout.pending.waitingDescription')}
                                </p>
                                {lastError ? (
                                    <p className="mt-3 text-sm text-amber-900">
                                        {lastError}
                                    </p>
                                ) : null}
                            </div>
                        ) : null}

                        {isLoading && !hasFailed && !requiresManualReview ? (
                            <p className="mt-4 text-sm text-muted">
                                {t('checkout.pending.loading')}
                            </p>
                        ) : null}

                        <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:justify-center">
                            {hasFailed ? (
                                <>
                                    <button
                                        type="button"
                                        onClick={() => {
                                            clearActiveCheckoutOrderContextIfMatches(orderId);
                                            navigate('/checkout');
                                        }}
                                        className="inline-flex items-center justify-center rounded-xl bg-primary px-5 py-3 font-medium text-white hover:bg-primary/90"
                                    >
                                        {t('checkout.pending.returnToCheckout')}
                                    </button>

                                    <button
                                        type="button"
                                        onClick={handleManualRefresh}
                                        className="inline-flex items-center justify-center rounded-xl border border-muted/20 px-5 py-3 font-medium text-text hover:bg-muted/10"
                                    >
                                        {t('checkout.pending.refreshStatus')}
                                    </button>

                                    <AffiliateLink
                                        href={trackingHref}
                                        className="inline-flex items-center justify-center rounded-xl border border-primary/30 px-5 py-3 font-medium text-primary hover:bg-primary/5"
                                    >
                                        {t('checkout.pending.trackOrder')}
                                    </AffiliateLink>
                                </>
                            ) : requiresManualReview ? (
                                <>
                                    <AffiliateLink
                                        href={trackingHref}
                                        className="inline-flex items-center justify-center rounded-xl border border-primary/30 px-5 py-3 font-medium text-primary hover:bg-primary/5"
                                    >
                                        {t('checkout.pending.trackOrder')}
                                    </AffiliateLink>

                                    <button
                                        type="button"
                                        onClick={handleManualRefresh}
                                        className="inline-flex items-center justify-center rounded-xl border border-muted/20 px-5 py-3 font-medium text-text hover:bg-muted/10"
                                    >
                                        {t('checkout.pending.refreshStatus')}
                                    </button>

                                    <AffiliateLink
                                        href="/products"
                                        className="inline-flex items-center justify-center rounded-xl bg-primary px-5 py-3 font-medium text-white hover:bg-primary/90"
                                    >
                                        {t('checkout.pending.continueShopping')}
                                    </AffiliateLink>
                                </>
                            ) : (
                                <>
                                    <AffiliateLink
                                        href={trackingHref}
                                        className="inline-flex items-center justify-center rounded-xl border border-primary/30 px-5 py-3 font-medium text-primary hover:bg-primary/5"
                                    >
                                        {t('checkout.pending.trackOrder')}
                                    </AffiliateLink>

                                    <button
                                        type="button"
                                        onClick={handleManualRefresh}
                                        className="inline-flex items-center justify-center rounded-xl border border-muted/20 px-5 py-3 font-medium text-text hover:bg-muted/10"
                                    >
                                        {t('checkout.pending.refreshStatus')}
                                    </button>

                                    <AffiliateLink
                                        href="/products"
                                        className="inline-flex items-center justify-center rounded-xl bg-primary px-5 py-3 font-medium text-white hover:bg-primary/90"
                                    >
                                        {t('checkout.pending.continueShopping')}
                                    </AffiliateLink>
                                </>
                            )}
                        </div>
                    </div>
                </div>
            </div>
        </Layout>
    );
};

export default CheckoutPendingPage;
