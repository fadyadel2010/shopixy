import { describe, expect, it } from 'vitest';
import {
    requireCanonicalPaymentStatusForOrder,
    resolveBackendPaymentScreen,
    resolvePaymobPollingErrorOutcome,
    resolveStripeClientOutcome,
} from '@/features/checkout/paymentFlow';

describe('payment flow resolution', () => {
    it('keeps Stripe processing states in pending until backend confirmation arrives', () => {
        expect(resolveStripeClientOutcome('processing')).toBe('pending');
        expect(resolveStripeClientOutcome('succeeded')).toBe('pending');
    });

    it('keeps Paymob polling failures in pending instead of assuming success', () => {
        expect(resolvePaymobPollingErrorOutcome()).toBe('pending');
    });

    it('only treats backend PAID as success', () => {
        expect(resolveBackendPaymentScreen('PAID')).toBe('success');
        expect(resolveBackendPaymentScreen('PENDING')).toBe('pending');
        expect(resolveBackendPaymentScreen('PROCESSING')).toBe('pending');
    });

    it('shows failure when backend confirms FAILED', () => {
        expect(resolveBackendPaymentScreen('FAILED')).toBe('failed');
    });

    it('rejects fabricated backend lifecycle vocabulary before presentation', () => {
        expect(() => requireCanonicalPaymentStatusForOrder('order-1', {
            orderId: 'order-1',
            orderNumber: 'ORD-1',
            status: 'PAID',
            orderStatus: 'Completed',
            paymentStatus: 'Fabricated',
            canRetryPayment: false,
            totalAmount: 10,
            currency: 'EGP',
        })).toThrow(/valid payment lifecycle/i);
    });

    it('rejects canonical success that contradicts current backend payment status', () => {
        expect(() => requireCanonicalPaymentStatusForOrder('order-1', {
            orderId: 'order-1',
            orderNumber: 'ORD-1',
            status: 'PAID',
            orderStatus: 'Completed',
            paymentStatus: 'Failed',
            paymentIntentStatus: 'Failed',
            canRetryPayment: false,
            totalAmount: 10,
            currency: 'EGP',
        })).toThrow(/contradicted current payment lifecycle/i);
    });
});
