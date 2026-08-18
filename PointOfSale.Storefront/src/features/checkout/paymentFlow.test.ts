import { describe, expect, it } from 'vitest';
import {
    isPaymentStatusAccessDeniedFailure,
    requireCanonicalPaymentStatusForOrder,
    resolvePendingCheckoutAction,
    type CanonicalPaymentStatusResponse,
} from './paymentFlow';

const orderId = 'c481589a-1a46-4c3e-a8e6-33bf8c44dbc3';

function status(overrides: Partial<CanonicalPaymentStatusResponse> = {}): CanonicalPaymentStatusResponse {
    return {
        orderId,
        orderNumber: 'ORD-83B3-2026-01005',
        status: 'PENDING',
        orderStatus: 'Pending',
        paymentStatus: 'Pending',
        paymentIntentStatus: 'Pending',
        paymentMethod: 'COD',
        canRetryPayment: false,
        totalAmount: 410,
        currency: 'EGP',
        ...overrides,
    };
}

describe('canonical payment flow', () => {
    it('requires the backend status response to prove the requested order identity', () => {
        const response = requireCanonicalPaymentStatusForOrder(orderId, status());

        expect(response.orderId).toBe(orderId);
        expect(response.orderNumber).toBe('ORD-83B3-2026-01005');
    });

    it('rejects status for a different order instead of reconciling stale route state', () => {
        expect(() => requireCanonicalPaymentStatusForOrder(
            orderId,
            status({ orderId: '04ca9737-00c8-4daf-bd5c-6b34ba0d8515' })
        )).toThrow(/requested payment identity/i);
    });

    it('keeps fresh COD checkout in pending until backend payment lifecycle reaches a terminal state', () => {
        const response = requireCanonicalPaymentStatusForOrder(orderId, status());

        expect(resolvePendingCheckoutAction(orderId, response)).toEqual({ kind: 'pending' });
    });

    it('routes completed COD status to confirmation only after backend proves paid state', () => {
        const response = requireCanonicalPaymentStatusForOrder(orderId, status({
            status: 'PAID',
            paymentStatus: 'CashOnDelivery',
            paymentIntentStatus: undefined,
        }));

        expect(resolvePendingCheckoutAction(orderId, response)).toEqual({
            kind: 'success',
            href: `/orders/${orderId}/confirmation?orderNumber=ORD-83B3-2026-01005&paymentMethod=COD&totalAmount=410&currency=EGP`,
        });
    });

    it('treats access denial as terminal for pending-page polling', () => {
        expect(isPaymentStatusAccessDeniedFailure({ status: 403, code: 'ORDER_ACCESS_DENIED' })).toBe(true);
        expect(isPaymentStatusAccessDeniedFailure({ status: 503, code: 'SERVICE_UNAVAILABLE' })).toBe(false);
    });

    it('accepts retry only when canonical failed Paymob status authorizes it', () => {
        const response = requireCanonicalPaymentStatusForOrder(orderId, status({
            status: 'FAILED',
            paymentStatus: 'Failed',
            paymentIntentStatus: 'Failed',
            paymentMethod: 'PAYMOB',
            canRetryPayment: true,
        }));

        expect(response.canRetryPayment).toBe(true);
        expect(resolvePendingCheckoutAction(orderId, response)).toEqual({ kind: 'failed' });
    });

    it('rejects retry authority on a pending response', () => {
        expect(() => requireCanonicalPaymentStatusForOrder(orderId, status({
            paymentMethod: 'PAYMOB',
            canRetryPayment: true,
        }))).toThrow(/retry authority/i);
    });
});
