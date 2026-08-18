import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getCanonicalPaymentStatus, retryPaymobPayment } from './paymentStatusApi';
import { httpClient } from './httpClient';

vi.mock('./httpClient', () => ({
    httpClient: {
        get: vi.fn(),
        post: vi.fn(),
    },
}));

describe('paymentStatusApi', () => {
    beforeEach(() => {
        vi.mocked(httpClient.get).mockReset();
        vi.mocked(httpClient.post).mockReset();
    });

    it('uses the storefront-scoped payment status route so storefront auth cookies are sent', async () => {
        const orderId = '4ef04598-4aae-4bea-92ad-4aaef1479344';
        const signal = new AbortController().signal;

        vi.mocked(httpClient.get).mockResolvedValueOnce({
            data: {
                orderId,
                orderNumber: 'ORD-83B3-2026-01008',
                status: 'PENDING',
                orderStatus: 'Pending',
                paymentStatus: 'Pending',
                paymentMethod: 'COD',
                canRetryPayment: false,
                totalAmount: 410,
                currency: 'EGP',
            },
        });

        const response = await getCanonicalPaymentStatus(orderId, {
            signal,
            checkoutToken: 'guest-checkout-token',
        });

        expect(response.orderNumber).toBe('ORD-83B3-2026-01008');
        expect(httpClient.get).toHaveBeenCalledWith(
            '/api/storefront/payments/status/4ef04598-4aae-4bea-92ad-4aaef1479344',
            {
                signal,
                headers: {
                    'X-Checkout-Token': 'guest-checkout-token',
                },
            },
        );
    });

    it('posts only the authorized order identity and validates a fresh Paymob handoff', async () => {
        const orderId = '4ef04598-4aae-4bea-92ad-4aaef1479344';
        const status = {
            orderId,
            orderNumber: 'ORD-P9-RETRY-1',
            status: 'FAILED' as const,
            orderStatus: 'Pending',
            paymentStatus: 'Failed',
            paymentIntentStatus: 'Failed',
            paymentMethod: 'PAYMOB',
            canRetryPayment: true,
            totalAmount: 410,
            currency: 'EGP',
        };
        vi.mocked(httpClient.post).mockResolvedValueOnce({
            data: {
                orderId,
                orderNumber: status.orderNumber,
                paymentIntentId: 'd886e932-799d-478c-8932-002518c3db1b',
                intentionId: '588000001',
                iframeUrl: 'https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test&clientSecret=cs_fresh',
                expiresAtUtc: '2099-01-01T00:00:00Z',
                amount: 410,
                currency: 'EGP',
            },
        });

        const response = await retryPaymobPayment(status, { checkoutToken: 'guest-checkout-token' });

        expect(response.paymentIntentId).toBe('d886e932-799d-478c-8932-002518c3db1b');
        expect(httpClient.post).toHaveBeenCalledWith(
            `/api/storefront/payments/paymob/intent/${orderId}`,
            undefined,
            { signal: undefined, headers: { 'X-Checkout-Token': 'guest-checkout-token' } },
        );
    });

    it('does not post when canonical status denies retry', async () => {
        await expect(retryPaymobPayment({
            orderId: '4ef04598-4aae-4bea-92ad-4aaef1479344',
            orderNumber: 'ORD-P9-PENDING-1',
            status: 'PENDING',
            orderStatus: 'Pending',
            paymentStatus: 'Pending',
            paymentIntentStatus: 'Pending',
            paymentMethod: 'PAYMOB',
            canRetryPayment: false,
            totalAmount: 410,
            currency: 'EGP',
        })).rejects.toThrow(/did not authorize retry/i);

        expect(httpClient.post).not.toHaveBeenCalled();
    });
});
