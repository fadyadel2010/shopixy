/**
 * Order Tracking Page
 * 
 * Displays order status and tracking information.
 * Works on page refresh and direct deep links.
 * 
 * Route: /orders/:orderId/track
 * 
 * Phase 7.8: Order Confirmation & Tracking UI
 */

import React, { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { useAffiliateNavigate } from '@/hooks/useAffiliateNavigation';
import { useOrder, STATUS_CONFIG } from '@/hooks/useOrder';
import { useTranslation } from '@/i18n';
import {
    OrderStatusTimeline,
    OrderSummaryCard,
    OrderItemsList,
    ShippingAddressCard,
    OrderErrorDisplay,
    OrderPageSkeleton,
    ShipmentTracking,
} from '@/components/order';
import type { OrderRefundDto, ReturnStatus } from '@/types/order';
import {
    commerceHubCustomerRefundGuidance,
    type BilingualCopy,
    type CustomerRefundGuidanceEntry,
} from '@/content/commerceHubRefundGuidance';

// ============================================================================
// Status Header Component
// ============================================================================

interface StatusHeaderProps {
    status: string;
    statusText: string;
    orderNumber: string;
    estimatedDelivery?: string;
}

const StatusHeader: React.FC<StatusHeaderProps> = ({
    status,
    statusText,
    orderNumber,
    estimatedDelivery,
}) => {
    // Get status config
    const config = STATUS_CONFIG[status as keyof typeof STATUS_CONFIG];

    // Determine header style based on status
    const isDelivered = status === 'delivered';
    const isCancelled = status === 'cancelled';

    let headerBg = 'bg-primary/5 border-primary/20';
    let iconBg = 'bg-primary/10 text-primary';
    let textColor = 'text-primary';

    if (isDelivered) {
        headerBg = 'bg-green-50 border-green-200';
        iconBg = 'bg-green-100 text-green-600';
        textColor = 'text-green-700';
    } else if (isCancelled) {
        headerBg = 'bg-red-50 border-red-200';
        iconBg = 'bg-red-100 text-red-600';
        textColor = 'text-red-700';
    }

    return (
        <div className={`rounded-xl border p-6 ${headerBg}`}>
            <div className="flex items-center gap-4">
                {/* Status Icon */}
                <div className={`w-14 h-14 rounded-full flex items-center justify-center text-2xl ${iconBg}`}>
                    {isDelivered ? (
                        <svg className="w-8 h-8" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                        </svg>
                    ) : (
                        <span>{config?.icon || '📦'}</span>
                    )}
                </div>

                {/* Status Text */}
                <div className="flex-1">
                    <p className="text-sm text-muted mb-1">Order #{orderNumber}</p>
                    <h1
                        id="tracking-heading"
                        className={`text-xl font-bold ${textColor}`}
                        tabIndex={-1}
                    >
                        {config?.label || 'Processing'}
                    </h1>
                    <p className="text-sm text-muted mt-1">
                        {statusText}
                    </p>
                </div>

                {/* Estimated Delivery */}
                {estimatedDelivery && !isDelivered && !isCancelled && (
                    <div className="text-right hidden sm:block">
                        <p className="text-xs text-muted">Estimated Delivery</p>
                        <p className="text-sm font-medium text-text">
                            {new Date(estimatedDelivery).toLocaleDateString('en-US', {
                                month: 'short',
                                day: 'numeric',
                            })}
                        </p>
                    </div>
                )}
            </div>

            {/* Mobile Estimated Delivery */}
            {estimatedDelivery && !isDelivered && !isCancelled && (
                <div className="mt-4 pt-4 border-t border-current/10 sm:hidden">
                    <p className="text-xs text-muted">Estimated Delivery</p>
                    <p className="text-sm font-medium text-text">
                        {new Date(estimatedDelivery).toLocaleDateString('en-US', {
                            weekday: 'long',
                            month: 'long',
                            day: 'numeric',
                        })}
                    </p>
                </div>
            )}
        </div>
    );
};

const formatReturnStatusLabel = (status: ReturnStatus): string =>
    status.replace(/([a-z])([A-Z])/g, '$1 $2');

const formatReturnTriggerLabel = (value: string): string => {
    switch (value) {
        case 'OnPickup':
            return 'Unlock refund on pickup';
        case 'OnDelivery':
            return 'Unlock refund on delivery';
        case 'OnVendorConfirmation':
            return 'Unlock refund on vendor confirmation';
        default:
            return value;
    }
};

const formatDateTime = (value?: string | null): string => {
    if (!value) {
        return 'Pending';
    }

    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) {
        return 'Pending';
    }

    return parsed.toLocaleString();
};

const getBilingualTextOrder = (language: string, copy: BilingualCopy) => {
    const isArabicPrimary = language === 'ar';

    return {
        primary: isArabicPrimary ? copy.ar : copy.en,
        secondary: isArabicPrimary ? copy.en : copy.ar,
        primaryLanguage: isArabicPrimary ? 'ar' : 'en',
        secondaryLanguage: isArabicPrimary ? 'en' : 'ar',
    } as const;
};

const resolveCustomerRefundGuidance = (refund: OrderRefundDto): CustomerRefundGuidanceEntry => {
    const returnStatus = refund.returnRequest?.status;

    if (refund.status === 'Paid' || refund.status === 'Executed' || refund.status === 'Completed') {
        return commerceHubCustomerRefundGuidance.entries.Paid;
    }

    if (refund.status === 'AwaitingPayout' || refund.status === 'ReadyForExecution') {
        return commerceHubCustomerRefundGuidance.entries.AwaitingPayout;
    }

    if (returnStatus === 'PickupScheduled') {
        return commerceHubCustomerRefundGuidance.entries.PickupScheduled;
    }

    if (returnStatus && ['PickedUp', 'InTransit', 'DeliveredToVendor', 'Confirmed'].includes(returnStatus)) {
        return commerceHubCustomerRefundGuidance.entries.PickedUp;
    }

    if (refund.status === 'WaitingForReturn') {
        return commerceHubCustomerRefundGuidance.entries.WaitingForReturn;
    }

    if (refund.status === 'Approved' || refund.status === 'Processing') {
        return commerceHubCustomerRefundGuidance.entries.Approved;
    }

    if (refund.status === 'Rejected') {
        return commerceHubCustomerRefundGuidance.entries.Rejected;
    }

    if (refund.status === 'Failed') {
        return commerceHubCustomerRefundGuidance.entries.Failed;
    }

    if (refund.status === 'Cancelled') {
        return commerceHubCustomerRefundGuidance.entries.Cancelled;
    }

    return commerceHubCustomerRefundGuidance.entries.Requested;
};

const shouldShowRefundGuarantee = (refund: OrderRefundDto): boolean => {
    if (
        refund.status === 'Paid' ||
        refund.status === 'Executed' ||
        refund.status === 'Completed' ||
        refund.status === 'Rejected' ||
        refund.status === 'Cancelled' ||
        refund.status === 'Failed'
    ) {
        return false;
    }

    return refund.status === 'Approved'
        || refund.status === 'WaitingForReturn'
        || refund.status === 'AwaitingPayout'
        || refund.status === 'ReadyForExecution'
        || refund.status === 'Processing'
        || Boolean(refund.returnRequest);
};

const formatRefundStatusLabel = (status: OrderRefundDto['status']): string => {
    if (status === 'AwaitingPayout' || status === 'ReadyForExecution') {
        return 'Awaiting Payout';
    }

    if (status === 'Paid' || status === 'Executed' || status === 'Completed') {
        return 'Paid';
    }

    return status.replace(/([a-z])([A-Z])/g, '$1 $2');
};

const GuidanceIcon: React.FC<{ tone?: 'info' | 'success' }> = ({ tone = 'info' }) => (
    <svg
        className={`h-5 w-5 ${tone === 'success' ? 'text-emerald-600' : 'text-sky-600'}`}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        aria-hidden="true"
    >
        {tone === 'success' ? (
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
        ) : (
            <>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 8.25h.008v.008H12V8.25Z" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M11.25 11.25h.75v4.5h.75" />
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
            </>
        )}
    </svg>
);

const DisclosureChevron: React.FC<{ isOpen: boolean }> = ({ isOpen }) => (
    <svg
        className={`h-5 w-5 text-muted transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`}
        viewBox="0 0 20 20"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        aria-hidden="true"
    >
        <path strokeLinecap="round" strokeLinejoin="round" d="m5 7.5 5 5 5-5" />
    </svg>
);

const BilingualCopyBlock: React.FC<{
    copy: BilingualCopy;
    language: string;
    primaryClassName?: string;
    secondaryClassName?: string;
}> = ({
    copy,
    language,
    primaryClassName = 'text-sm font-medium text-text',
    secondaryClassName = 'mt-1 text-sm text-muted',
}) => {
    const orderedCopy = getBilingualTextOrder(language, copy);

    return (
        <div>
            <p className={primaryClassName} lang={orderedCopy.primaryLanguage} dir={orderedCopy.primaryLanguage === 'ar' ? 'rtl' : 'ltr'}>
                {orderedCopy.primary}
            </p>
            <p className={secondaryClassName} lang={orderedCopy.secondaryLanguage} dir={orderedCopy.secondaryLanguage === 'ar' ? 'rtl' : 'ltr'}>
                {orderedCopy.secondary}
            </p>
        </div>
    );
};

const RefundGuidanceCard: React.FC<{
    refund: OrderRefundDto;
    language: string;
    isRTL: boolean;
}> = ({ refund, language, isRTL }) => {
    const [isOpen, setIsOpen] = React.useState(true);
    const guidance = resolveCustomerRefundGuidance(refund);
    const labels = commerceHubCustomerRefundGuidance.labels;

    return (
        <div className="mt-4 overflow-hidden rounded-xl border border-sky-200 bg-sky-50/70">
            <button
                type="button"
                className={`flex w-full items-center justify-between gap-3 px-4 py-3 text-start ${isRTL ? 'flex-row-reverse text-right' : ''}`}
                onClick={() => setIsOpen((current) => !current)}
            >
                <div className={`flex items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <div className="flex h-10 w-10 items-center justify-center rounded-full bg-white/80 shadow-sm">
                        <GuidanceIcon />
                    </div>
                    <div>
                        <BilingualCopyBlock
                            copy={commerceHubCustomerRefundGuidance.sectionTitle}
                            language={language}
                            primaryClassName="text-sm font-semibold text-sky-950"
                            secondaryClassName="mt-1 text-xs text-sky-700"
                        />
                    </div>
                </div>
                <DisclosureChevron isOpen={isOpen} />
            </button>

            {isOpen ? (
                <div className="border-t border-sky-200/70 px-4 py-4">
                    <div className="mb-4">
                        <BilingualCopyBlock
                            copy={commerceHubCustomerRefundGuidance.sectionSubtitle}
                            language={language}
                            primaryClassName="text-sm text-sky-900"
                            secondaryClassName="mt-1 text-xs text-sky-700"
                        />
                    </div>

                    <div className="grid gap-3 md:grid-cols-3">
                        <div className="rounded-xl border border-white/70 bg-white px-4 py-3 shadow-sm">
                            <BilingualCopyBlock
                                copy={labels.now}
                                language={language}
                                primaryClassName="text-xs font-semibold uppercase tracking-wide text-sky-900"
                                secondaryClassName="mt-1 text-[11px] text-sky-700"
                            />
                            <div className="mt-3">
                                <BilingualCopyBlock copy={guidance.explanation} language={language} />
                            </div>
                        </div>

                        <div className="rounded-xl border border-white/70 bg-white px-4 py-3 shadow-sm">
                            <BilingualCopyBlock
                                copy={labels.next}
                                language={language}
                                primaryClassName="text-xs font-semibold uppercase tracking-wide text-sky-900"
                                secondaryClassName="mt-1 text-[11px] text-sky-700"
                            />
                            <div className="mt-3">
                                <BilingualCopyBlock copy={guidance.next} language={language} />
                            </div>
                        </div>

                        <div className="rounded-xl border border-white/70 bg-white px-4 py-3 shadow-sm">
                            <BilingualCopyBlock
                                copy={labels.action}
                                language={language}
                                primaryClassName="text-xs font-semibold uppercase tracking-wide text-sky-900"
                                secondaryClassName="mt-1 text-[11px] text-sky-700"
                            />
                            <div className="mt-3">
                                <BilingualCopyBlock copy={guidance.action} language={language} />
                            </div>
                        </div>
                    </div>

                    {shouldShowRefundGuarantee(refund) ? (
                        <div className={`mt-4 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 ${isRTL ? 'text-right' : ''}`}>
                            <div className={`flex items-start gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <div className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-full bg-white">
                                    <GuidanceIcon tone="success" />
                                </div>
                                <div className="flex-1">
                                    <BilingualCopyBlock
                                        copy={labels.guarantee}
                                        language={language}
                                        primaryClassName="text-sm font-semibold text-emerald-950"
                                        secondaryClassName="mt-1 text-xs text-emerald-700"
                                    />
                                    <div className="mt-2">
                                        <BilingualCopyBlock
                                            copy={commerceHubCustomerRefundGuidance.guaranteeMessage}
                                            language={language}
                                            primaryClassName="text-sm text-emerald-900"
                                            secondaryClassName="mt-1 text-xs text-emerald-700"
                                        />
                                    </div>
                                </div>
                            </div>
                        </div>
                    ) : null}
                </div>
            ) : null}
        </div>
    );
};

const getRefundBadgeClass = (status: string): string => {
    switch (status) {
        case 'Paid':
        case 'Executed':
        case 'Completed':
            return 'bg-emerald-50 text-emerald-700 border-emerald-200';
        case 'AwaitingPayout':
        case 'ReadyForExecution':
            return 'bg-yellow-50 text-yellow-700 border-yellow-200';
        case 'Approved':
        case 'WaitingForReturn':
        case 'Processing':
            return 'bg-amber-50 text-amber-700 border-amber-200';
        case 'Rejected':
        case 'Cancelled':
        case 'Failed':
            return 'bg-rose-50 text-rose-700 border-rose-200';
        default:
            return 'bg-slate-50 text-slate-700 border-slate-200';
    }
};

const getReturnBadgeClass = (status: ReturnStatus): string => {
    switch (status) {
        case 'Pending':
            return 'bg-slate-50 text-slate-700 border-slate-200';
        case 'PickupScheduled':
            return 'bg-sky-50 text-sky-700 border-sky-200';
        case 'PickedUp':
            return 'bg-amber-50 text-amber-700 border-amber-200';
        case 'InTransit':
            return 'bg-indigo-50 text-indigo-700 border-indigo-200';
        case 'DeliveredToVendor':
            return 'bg-emerald-50 text-emerald-700 border-emerald-200';
        case 'Confirmed':
            return 'bg-green-50 text-green-700 border-green-200';
        case 'Failed':
            return 'bg-rose-50 text-rose-700 border-rose-200';
        default:
            return 'bg-slate-50 text-slate-700 border-slate-200';
    }
};

const RefundsAndReturnsSection: React.FC<{ refunds: OrderRefundDto[] }> = ({ refunds }) => {
    const { language, isRTL } = useTranslation();

    if (refunds.length === 0) {
        return null;
    }

    return (
        <section className="mb-6 rounded-xl border border-muted/10 bg-muted/5 p-6">
            <div className="mb-4 flex items-center justify-between gap-3">
                <div>
                    <h2 className="text-lg font-semibold text-text">Refund and return status</h2>
                    <p className="mt-1 text-sm text-muted">
                        Refunds stay blocked until the configured return milestone is reached, then move into manual payout confirmation.
                    </p>
                </div>
                <div className="rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold text-primary">
                    {refunds.length} active {refunds.length === 1 ? 'refund' : 'refunds'}
                </div>
            </div>

            <div className="space-y-4">
                {refunds.map((refund) => {
                    const returnRequest = refund.returnRequest;

                    return (
                        <article key={refund.refundId} className="rounded-xl border border-muted/10 bg-background p-5 shadow-sm">
                            <div className="flex flex-wrap items-start justify-between gap-3">
                                <div>
                                    <h3 className="text-base font-semibold text-text">{refund.productName}</h3>
                                    <p className="mt-1 text-sm text-muted">
                                        {refund.variantName || 'Default variant'} • Qty {refund.quantity} • {refund.amount} {refund.currency}
                                    </p>
                                    <p className="mt-2 text-sm text-text">{refund.reason}</p>
                                </div>

                                <div className="flex flex-wrap gap-2">
                                    <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${getRefundBadgeClass(refund.status)}`}>
                                        Refund {formatRefundStatusLabel(refund.status)}
                                    </span>
                                    {returnRequest ? (
                                        <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${getReturnBadgeClass(returnRequest.status)}`}>
                                            {formatReturnStatusLabel(returnRequest.status)}
                                        </span>
                                    ) : null}
                                </div>
                            </div>

                            <RefundGuidanceCard refund={refund} language={language} isRTL={isRTL} />

                            {refund.payout ? (
                                <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                                    <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Refund Method</p>
                                        <p className="mt-2 text-sm font-medium text-text">{refund.payout.method.replace(/([a-z])([A-Z])/g, '$1 $2')}</p>
                                    </div>
                                    <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Destination</p>
                                        <p className="mt-2 text-sm font-medium text-text">{refund.payout.maskedAccountIdentifier}</p>
                                    </div>
                                    <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Transaction Reference</p>
                                        <p className="mt-2 text-sm font-medium text-text">{refund.payout.transactionReference || 'Pending'}</p>
                                    </div>
                                    <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                        <p className="text-xs font-semibold uppercase tracking-wide text-muted">Paid At</p>
                                        <p className="mt-2 text-sm font-medium text-text">{formatDateTime(refund.payout.paidAt)}</p>
                                    </div>
                                </div>
                            ) : null}

                            {returnRequest ? (
                                <>
                                    <div className="mt-4 rounded-xl border border-primary/10 bg-primary/5 px-4 py-3 text-sm text-text">
                                        <p className="font-semibold">{formatReturnTriggerLabel(returnRequest.refundExecutionTrigger)}</p>
                                        <p className="mt-1 text-muted">
                                            {returnRequest.isExecutionTriggerReached
                                                ? 'Refund release milestone reached.'
                                                : 'Refund release is still waiting on the return milestone.'}
                                        </p>
                                    </div>

                                    <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-5">
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Pickup</p>
                                            <p className="mt-2 text-sm font-medium text-text">{formatDateTime(returnRequest.pickupScheduledAt)}</p>
                                        </div>
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Picked Up</p>
                                            <p className="mt-2 text-sm font-medium text-text">{formatDateTime(returnRequest.pickedUpAt)}</p>
                                        </div>
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">In Transit</p>
                                            <p className="mt-2 text-sm font-medium text-text">{formatDateTime(returnRequest.inTransitAt)}</p>
                                        </div>
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Delivered</p>
                                            <p className="mt-2 text-sm font-medium text-text">{formatDateTime(returnRequest.deliveredAt)}</p>
                                        </div>
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Confirmed</p>
                                            <p className="mt-2 text-sm font-medium text-text">{formatDateTime(returnRequest.confirmedAt)}</p>
                                        </div>
                                    </div>

                                    <div className="mt-4 grid gap-3 md:grid-cols-3">
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Courier</p>
                                            <p className="mt-2 text-sm font-medium text-text">{returnRequest.courierName || 'Awaiting assignment'}</p>
                                        </div>
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Tracking Number</p>
                                            <p className="mt-2 text-sm font-medium text-text">{returnRequest.trackingNumber || 'Unavailable'}</p>
                                        </div>
                                        <div className="rounded-xl border border-muted/10 bg-muted/5 px-4 py-3">
                                            <p className="text-xs font-semibold uppercase tracking-wide text-muted">Latest Notes</p>
                                            <p className="mt-2 text-sm font-medium text-text">{returnRequest.notes || 'No operational notes yet'}</p>
                                        </div>
                                    </div>
                                </>
                            ) : (
                                <div className="mt-4 rounded-xl border border-muted/10 bg-muted/5 px-4 py-3 text-sm text-muted">
                                    Return workflow has not been created yet for this refund.
                                </div>
                            )}
                        </article>
                    );
                })}
            </div>
        </section>
    );
};

// ============================================================================
// Main Component
// ============================================================================

export const OrderTrackingPage: React.FC = () => {
    const { orderId } = useParams<{ orderId: string }>();
    const navigate = useAffiliateNavigate();
    const { order, status, timeline, isLoading, error, refetch } = useOrder(orderId);

    // Focus management for accessibility
    useEffect(() => {
        if (!isLoading && !error && order) {
            const heading = document.getElementById('tracking-heading');
            heading?.focus();
        }
    }, [isLoading, error, order]);

    // Loading state
    if (isLoading) {
        return <OrderPageSkeleton />;
    }

    // Error state
    if (error) {
        return (
            <div className="min-h-screen bg-background">
                <div className="container mx-auto px-4 py-8">
                    <OrderErrorDisplay
                        error={error}
                        onRetry={error.retryable ? refetch : undefined}
                        onGoHome={() => navigate('/')}
                    />
                </div>
            </div>
        );
    }

    // No order (shouldn't happen if no error)
    if (!order || !status) {
        return null;
    }

    return (
        <div className="min-h-screen bg-background">
            <div className="container mx-auto px-4 py-8 max-w-4xl">
                {/* Back Navigation */}
                <nav className="mb-6">
                    <button
                        onClick={() => navigate(-1)}
                        className="
                            inline-flex items-center gap-2 text-muted hover:text-text
                            transition-colors text-sm
                            focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary rounded
                        "
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                        </svg>
                        Back
                    </button>
                </nav>

                {/* Status Header */}
                <StatusHeader
                    status={order.status}
                    statusText={order.status === 'cancelled' && order.cancellationReason ? order.cancellationReason : status.statusText}
                    orderNumber={order.orderNumber}
                    estimatedDelivery={status.estimatedDelivery}
                />

                {/* Order Timeline */}
                <section
                    className="my-8 p-6 bg-muted/5 border border-muted/10 rounded-xl"
                    aria-labelledby="timeline-heading"
                >
                    <h2 id="timeline-heading" className="sr-only">Order Progress Timeline</h2>
                    <OrderStatusTimeline steps={timeline} />
                </section>

                {/* ============================================ */}
                {/* SHIPMENT TRACKING - Phase Enhancement        */}
                {/* Read-only display of shipment progress       */}
                {/* Uses order timeline data - CQRS compliant    */}
                {/* ============================================ */}
                <div className="mb-6">
                    <ShipmentTracking
                        trackingNumber={order.trackingNumber}
                        carrierName={order.carrierName}
                        trackingUrl={(order as any).trackingUrl}
                        timeline={(order as any).timeline}
                    />
                </div>

                {/* Order Details Grid */}
                <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
                    <OrderSummaryCard order={order} />
                    <ShippingAddressCard address={order.shippingAddress} />
                </div>

                {/* Order Items */}
                <OrderItemsList items={order.items} currency={order.currency} />

                <RefundsAndReturnsSection refunds={order.refunds || []} />

                {/* Action Buttons */}
                <div className="mt-8 flex flex-col sm:flex-row gap-3 justify-center">
                    <button
                        onClick={() => refetch()}
                        className="
                            py-3 px-8 rounded-xl font-medium
                            bg-muted/10 text-text
                            hover:bg-muted/20
                            transition-colors
                            focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                            inline-flex items-center justify-center gap-2
                        "
                    >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path
                                strokeLinecap="round"
                                strokeLinejoin="round"
                                strokeWidth={2}
                                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
                            />
                        </svg>
                        Refresh Status
                    </button>

                    <button
                        onClick={() => navigate('/products')}
                        className="
                            py-3 px-8 rounded-xl font-medium
                            bg-primary text-white
                            hover:bg-primary/90
                            transition-colors
                            focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2
                        "
                    >
                        Continue Shopping
                    </button>
                </div>

                {/* Help Section */}
                <div className="mt-8 p-5 bg-muted/5 border border-muted/10 rounded-xl">
                    <h2 className="font-semibold text-text mb-3">Need Help?</h2>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
                        <a
                            href="/faq"
                            className="flex items-center gap-2 text-muted hover:text-primary transition-colors"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                                />
                            </svg>
                            Frequently Asked Questions
                        </a>
                        <a
                            href="/contact"
                            className="flex items-center gap-2 text-muted hover:text-primary transition-colors"
                        >
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"
                                />
                            </svg>
                            Contact Support
                        </a>
                    </div>
                </div>
            </div>
        </div>
    );
};
