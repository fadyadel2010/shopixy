export type CanonicalPaymentState = 'PENDING' | 'PROCESSING' | 'MANUAL_REVIEW' | 'FAILED' | 'PAID';
export type PaymentResolutionScreen = 'pending' | 'manual_review' | 'failed' | 'success';

const CANONICAL_PAYMENT_STATES = new Set<CanonicalPaymentState>([
    'PENDING',
    'PROCESSING',
    'MANUAL_REVIEW',
    'FAILED',
    'PAID',
]);
const ORDER_PAYMENT_STATUSES = new Set([
    'Pending',
    'Authorized',
    'Collected',
    'Paid',
    'PartiallyPaid',
    'Failed',
    'Refunded',
    'PartiallyRefunded',
    'AwaitingVerification',
    'Rejected',
    'Settled',
    'CashOnDelivery',
    'Expired',
    'AmountMismatch',
]);
const PAYMENT_INTENT_STATUSES = new Set([
    'Pending',
    'Authorized',
    'Paid',
    'Failed',
    'Cancelled',
    'Canceled',
    'Expired',
    'Skipped',
    'AwaitingVerification',
    'Rejected',
    'AmountMismatch',
    'Suspicious',
]);

export interface CanonicalPaymentStatusResponse {
    orderId: string;
    orderNumber: string;
    status: CanonicalPaymentState;
    orderStatus: string;
    paymentStatus: string;
    paymentIntentStatus?: string;
    paymentMethod?: string;
    totalAmount: number;
    currency: string;
}

/**
 * A route value is only a backend lookup candidate. Payment identity becomes
 * presentable only when the current backend response proves the exact target.
 */
export function requireCanonicalPaymentStatusForOrder(
    requestedOrderId: string,
    response: CanonicalPaymentStatusResponse,
): CanonicalPaymentStatusResponse {
    const requested = requestedOrderId.trim();
    const orderId = typeof response.orderId === 'string' ? response.orderId.trim() : '';
    const orderNumber = typeof response.orderNumber === 'string' ? response.orderNumber.trim() : '';

    if (!requested || !orderId || !orderNumber || orderId !== requested) {
        throw new Error('Backend payment status did not prove the requested payment identity.');
    }

    if (
        !CANONICAL_PAYMENT_STATES.has(response.status) ||
        typeof response.paymentStatus !== 'string' ||
        !ORDER_PAYMENT_STATUSES.has(response.paymentStatus) ||
        (response.paymentIntentStatus != null && !PAYMENT_INTENT_STATUSES.has(response.paymentIntentStatus)) ||
        typeof response.totalAmount !== 'number' ||
        !Number.isFinite(response.totalAmount) ||
        response.totalAmount < 0 ||
        typeof response.currency !== 'string' ||
        !/^[A-Z]{3}$/.test(response.currency)
    ) {
        throw new Error('Backend payment status did not prove valid payment lifecycle evidence.');
    }

    if (
        response.status === 'PAID' &&
        !['Collected', 'Paid', 'Settled', 'CashOnDelivery', 'Refunded', 'PartiallyRefunded'].includes(response.paymentStatus)
    ) {
        throw new Error('Backend payment status contradicted current payment lifecycle evidence.');
    }

    if (
        response.status === 'FAILED' &&
        !['Failed', 'Rejected', 'Expired', 'AmountMismatch'].includes(response.paymentStatus)
    ) {
        throw new Error('Backend payment status contradicted current payment lifecycle evidence.');
    }

    if (
        response.status === 'MANUAL_REVIEW' &&
        !['AwaitingVerification', 'AmountMismatch', 'Rejected'].includes(response.paymentStatus)
    ) {
        throw new Error('Backend payment status contradicted current manual-review evidence.');
    }

    return { ...response, orderId, orderNumber };
}

export type PendingCheckoutAction =
    | { kind: 'pending' }
    | { kind: 'manual_review' }
    | { kind: 'failed' }
    | { kind: 'success'; href: string };

export function resolveBackendPaymentScreen(status: CanonicalPaymentState): PaymentResolutionScreen {
    switch (status) {
        case 'PAID':
            return 'success';
        case 'MANUAL_REVIEW':
            return 'manual_review';
        case 'FAILED':
            return 'failed';
        default:
            return 'pending';
    }
}

export function buildOrderConfirmationSearch(
    status?: CanonicalPaymentStatusResponse,
    _fallbackOrderNumber?: string | null
): string {
    const params = new URLSearchParams();

    // Never promote URL/mutation fallback data into payment identity.
    const orderNumber = status?.orderNumber || '';
    if (orderNumber) {
        params.set('orderNumber', orderNumber);
    }

    if (status?.paymentMethod) {
        params.set('paymentMethod', status.paymentMethod);
    }

    if (typeof status?.totalAmount === 'number') {
        params.set('totalAmount', status.totalAmount.toString());
    }

    if (status?.currency) {
        params.set('currency', status.currency);
    }

    const search = params.toString();
    return search ? `?${search}` : '';
}

export function buildOrderConfirmationPath(
    orderId: string,
    status?: CanonicalPaymentStatusResponse,
    fallbackOrderNumber?: string | null
): string {
    const authoritativeOrderId = status?.orderId === orderId ? status.orderId : null;
    if (!authoritativeOrderId) {
        throw new Error('Cannot navigate using unverified payment identity.');
    }

    return `/orders/${authoritativeOrderId}/confirmation${buildOrderConfirmationSearch(status, fallbackOrderNumber)}`;
}

export function resolvePendingCheckoutAction(
    orderId: string,
    status: CanonicalPaymentStatusResponse,
    _fallbackOrderNumber?: string | null
): PendingCheckoutAction {
    const screen = resolveBackendPaymentScreen(status.status);

    if (screen === 'success') {
        return {
            kind: 'success',
            href: buildOrderConfirmationPath(orderId, status),
        };
    }

    if (screen === 'failed') {
        return { kind: 'failed' };
    }

    if (screen === 'manual_review') {
        return { kind: 'manual_review' };
    }

    return { kind: 'pending' };
}

/**
 * Resolves Stripe client-side payment status to a screen state.
 * 
 * **INTENTIONAL STUB**: This function always returns 'pending' regardless of the Stripe status.
 * 
 * **Design Rationale:**
 * - The frontend MUST NEVER treat client-side payment status as final
 * - Only the backend webhook confirmation is authoritative for payment success
 * - Stripe.js statuses like 'succeeded' or 'processing' are NOT sufficient to show success
 * - Server-side finalization via webhook is required before showing success screen
 * 
 * **Architecture:**
 * - Client receives Stripe status → always shows pending screen
 * - Backend webhook processes payment → updates order status
 * - Frontend polls backend via `/api/orders/{orderId}/payment-status`
 * - Only backend canonical status determines final screen (success/failed)
 * 
 * **Related:**
 * - Used in: `StripePaymentForm.tsx` (after confirmPayment)
 * - Tested in: `paymentFlow.test.ts` (verifies 'processing' and 'succeeded' both return 'pending')
 * - Backend authority: `CanonicalPaymentStatusResolver.cs`
 * 
 * @param _stripeStatus - Stripe PaymentIntent status (intentionally ignored)
 * @returns Always 'pending' - user must wait for backend confirmation
 */
export function resolveStripeClientOutcome(_stripeStatus?: string | null): PaymentResolutionScreen {
    return 'pending';
}

/**
 * Resolves Paymob polling error to a screen state.
 * 
 * **INTENTIONAL STUB**: This function always returns 'pending' on polling errors.
 * 
 * **Design Rationale:**
 * - If frontend polling fails, we CANNOT assume payment failed
 * - Payment may have succeeded but polling timed out or network failed
 * - Showing 'failed' screen would be incorrect if payment actually succeeded
 * - User should retry checking status or contact support
 * 
 * **Architecture:**
 * - Paymob iframe completes → frontend polls backend for status
 * - If polling fails (network error, timeout) → show pending screen
 * - Backend webhook may still process payment successfully
 * - User can refresh or check order status later
 * 
 * **Related:**
 * - Used in: Paymob payment flow (when backend polling fails)
 * - Tested in: `paymentFlow.test.ts` (verifies polling errors return 'pending')
 * - Backend authority: `CanonicalPaymentStatusResolver.cs`
 * 
 * @returns Always 'pending' - polling failure does not mean payment failed
 */
export function resolvePaymobPollingErrorOutcome(): PaymentResolutionScreen {
    return 'pending';
}
