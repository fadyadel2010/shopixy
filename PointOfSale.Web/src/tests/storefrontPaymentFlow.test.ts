import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { httpGet } = vi.hoisted(() => ({
    httpGet: vi.fn(),
}));

vi.mock('@/services/api/httpClient', () => ({
    httpClient: {
        get: httpGet,
    },
}));

import { resolvePendingCheckoutAction } from '../../../PointOfSale.Storefront/src/features/checkout/paymentFlow';
import { paymentStatusApi } from '@/services/api/paymentStatusApi';
import {
    clearActiveCheckoutOrderContext,
    persistActiveCheckoutOrderContext,
    readActiveCheckoutOrderContext,
    readCheckoutTokenForOrder,
} from '@/features/checkout/activeCheckoutOrderContext';

describe('storefront payment flow hardening', () => {
    const baseStatus = {
        orderId: 'order-123',
        orderNumber: 'SO-1001',
        orderStatus: 'Confirmed',
        paymentStatus: 'Paid',
        totalAmount: 249.99,
        currency: 'EGP',
        canRetryPayment: false,
    } as const;

    beforeEach(() => {
        vi.clearAllMocks();

        const sessionStorage = createStorageMock();
        vi.stubGlobal('window', {
            sessionStorage,
        });
    });

    afterEach(() => {
        clearActiveCheckoutOrderContext();
        vi.unstubAllGlobals();
    });

    it('routes backend-paid orders to the confirmation page', () => {
        const result = resolvePendingCheckoutAction('order-123', {
            ...baseStatus,
            status: 'PAID',
            paymentMethod: 'STRIPE',
        });

        expect(result.kind).toBe('success');

        if (result.kind !== 'success') {
            throw new Error('Expected success action');
        }

        const url = new URL(`https://example.test${result.href}`);
        expect(url.pathname).toBe('/orders/order-123/confirmation');
        expect(url.searchParams.get('orderNumber')).toBe('SO-1001');
        expect(url.searchParams.get('paymentMethod')).toBe('STRIPE');
        expect(url.searchParams.get('totalAmount')).toBe('249.99');
        expect(url.searchParams.get('currency')).toBe('EGP');
    });

    it('keeps non-final backend states on the pending screen', () => {
        const result = resolvePendingCheckoutAction('order-123', {
            ...baseStatus,
            status: 'PROCESSING',
            paymentMethod: 'PAYMOB',
        });

        expect(result).toEqual({ kind: 'pending' });
    });

    it('returns failed action for FAILED status', () => {
        const result = resolvePendingCheckoutAction('order-123', {
            ...baseStatus,
            status: 'FAILED',
            paymentMethod: 'STRIPE',
        });

        expect(result).toEqual({ kind: 'failed' });
    });

    it('does not persist backend-issued guest tokens in browser storage', () => {
        persistActiveCheckoutOrderContext({
            orderId: 'order-123',
            orderNumber: 'SO-1001',
            checkoutToken: 'guest-token-123',
        });

        expect(readActiveCheckoutOrderContext()).toBeNull();
        expect(readCheckoutTokenForOrder('order-123')).toBeNull();
        expect(readCheckoutTokenForOrder('other-order')).toBeNull();
        expect(window.sessionStorage.length).toBe(0);
    });

    it('sends X-Checkout-Token when recovering canonical payment status', async () => {
        httpGet.mockResolvedValue({
            data: {
                ...baseStatus,
                status: 'PROCESSING',
                paymentMethod: 'PAYMOB',
                canRetryPayment: false,
            },
        });

        await paymentStatusApi.getCanonicalPaymentStatus('order-123', {
            checkoutToken: 'guest-token-123',
        });

        expect(httpGet).toHaveBeenCalledWith(
            '/api/storefront/payments/status/order-123',
            {
                signal: undefined,
                headers: {
                    'X-Checkout-Token': 'guest-token-123',
                },
            },
        );
    });

    it('does NOT send X-Checkout-Token when checkoutToken is null', async () => {
        httpGet.mockResolvedValue({
            data: {
                ...baseStatus,
                status: 'PROCESSING',
                paymentMethod: 'PAYMOB',
                canRetryPayment: false,
            },
        });

        await paymentStatusApi.getCanonicalPaymentStatus('order-123', {
            checkoutToken: null,
        });

        expect(httpGet).toHaveBeenCalledWith(
            '/api/storefront/payments/status/order-123',
            {
                signal: undefined,
                headers: undefined,
            },
        );
    });

    it('does NOT send X-Checkout-Token when checkoutToken is empty string', async () => {
        httpGet.mockResolvedValue({
            data: {
                ...baseStatus,
                status: 'PROCESSING',
                paymentMethod: 'PAYMOB',
                canRetryPayment: false,
            },
        });

        await paymentStatusApi.getCanonicalPaymentStatus('order-123', {
            checkoutToken: '',
        });

        expect(httpGet).toHaveBeenCalledWith(
            '/api/storefront/payments/status/order-123',
            {
                signal: undefined,
                headers: undefined,
            },
        );
    });
});

function createStorageMock(): Storage {
    const store = new Map<string, string>();

    return {
        get length() {
            return store.size;
        },
        clear: () => {
            store.clear();
        },
        getItem: (key: string) => store.get(key) ?? null,
        key: (index: number) => Array.from(store.keys())[index] ?? null,
        removeItem: (key: string) => {
            store.delete(key);
        },
        setItem: (key: string, value: string) => {
            store.set(key, value);
        },
    };
}
