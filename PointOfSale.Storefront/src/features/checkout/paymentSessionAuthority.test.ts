import { describe, expect, it } from 'vitest';
import type { CheckoutResponse } from '@/types/checkout';
import {
    requirePaymobRetrySessionAuthority,
    requireStorefrontPaymentSessionAuthority,
} from './paymentSessionAuthority';

const cartId = '34c3f180-acde-4c07-a721-05db16f724c3';

function createManualWalletResponse(overrides: Partial<CheckoutResponse> = {}): CheckoutResponse {
    return {
        orderId: '8bf8f86a-4a83-4520-a9bf-cdf6af7af0d7',
        orderNumber: 'ORD-20260813-0001',
        status: 'Pending',
        cartId,
        paymentIntentId: '16577ed9-8b4c-43a4-a486-58372494805e',
        total: 400,
        totalAmount: 400,
        currency: 'EGP',
        paymentStatus: 'pending',
        paymentMethodCode: 'MANUALWALLET',
        createdAt: '2026-08-13T19:44:00Z',
        isManualWallet: true,
        merchantWalletNumber: '01000000000',
        walletType: 'VODAFONECASH',
        walletInstructions: 'Transfer the order total and keep the proof for review.',
        ...overrides,
    };
}

function createPaymobResponse(overrides: Partial<CheckoutResponse> = {}): CheckoutResponse {
    return {
        orderId: '8bf8f86a-4a83-4520-a9bf-cdf6af7af0d7',
        orderNumber: 'ORD-20260814-0001',
        status: 'Pending',
        cartId,
        paymentIntentId: '3e9c8b38-7590-4d02-8df5-c9937d18b8c1',
        total: 410,
        totalAmount: 410,
        currency: 'EGP',
        checkoutToken: 'checkout-token',
        paymentStatus: 'pending',
        paymentMethodCode: 'PAYMOB',
        createdAt: '2026-08-14T19:44:00Z',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        ...overrides,
    };
}

describe('requireStorefrontPaymentSessionAuthority Manual Wallet responses', () => {
    it.each([
        ['omitted', undefined],
        ['null', null],
        ['empty', ''],
        ['whitespace', '   '],
    ])('accepts valid Manual Wallet evidence when the Fawry reference is %s', (_label, referenceCode) => {
        const response = createManualWalletResponse({ referenceCode } as Partial<CheckoutResponse>);

        expect(requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId)).toBe(response);
    });

    it('rejects a non-empty Fawry artifact on a Manual Wallet response', () => {
        const response = createManualWalletResponse({ referenceCode: 'FWY-123456' });

        expect(() => requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId))
            .toThrow('Backend checkout response did not prove a complete Fawry payment-session handoff.');
    });

    it('accepts valid Manual Wallet session evidence without merchant presentation fields', () => {
        const response = createManualWalletResponse({
            merchantWalletNumber: undefined,
            walletInstructions: undefined,
        });

        expect(requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId)).toBe(response);
    });

    it.each([
        ['payment intent', { paymentIntentId: undefined }],
        ['manual wallet marker', { isManualWallet: false }],
        ['wallet type', { walletType: '' }],
    ])('still requires server-generated pending Manual Wallet authority: %s', (_label, overrides) => {
        const response = createManualWalletResponse(overrides as Partial<CheckoutResponse>);

        expect(() => requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId))
            .toThrow('Backend checkout response did not prove pending ManualWallet payment-session evidence.');
    });

    it('accepts a server-proven terminal Manual Wallet replay without reusable payment-session artifacts', () => {
        const response = createManualWalletResponse({
            status: 'Confirmed',
            paymentIntentId: null,
            paymentStatus: 'Collected',
            isManualWallet: false,
            merchantWalletNumber: null,
            walletType: null,
            walletInstructions: null,
        } as unknown as Partial<CheckoutResponse>);

        expect(requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId)).toBe(response);
    });

    it.each([
        ['checkout token', { checkoutToken: 'reusable-token' }],
        ['expiration', { expiresAtUtc: '2099-01-01T00:00:00Z' }],
        ['redirect', { redirectUrl: 'https://payments.example.test/replay' }],
        ['payment reference', { paymentReference: 'PAY-STALE-REPLAY' }],
    ])('rejects terminal Manual Wallet replay with a reusable %s artifact', (_label, artifact) => {
        const response = createManualWalletResponse({
            status: 'Confirmed',
            paymentIntentId: null,
            paymentStatus: 'Collected',
            isManualWallet: false,
            merchantWalletNumber: null,
            walletType: null,
            walletInstructions: null,
            ...artifact,
        } as Partial<CheckoutResponse>);

        expect(() => requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId))
            .toThrow('Terminal Manual Wallet replay unexpectedly included reusable payment-session artifacts.');
    });

    it('rejects a checkout response whose backend payment method does not match the selected method', () => {
        const response = createManualWalletResponse({ paymentMethodCode: 'COD' });

        expect(() => requireStorefrontPaymentSessionAuthority(response, 'MANUALWALLET', cartId))
            .toThrow('Backend checkout response did not prove valid payment-session presentation evidence.');
    });
});

describe('requireStorefrontPaymentSessionAuthority online provider responses', () => {
    it('accepts a complete Paymob Unified Checkout handoff', () => {
        const response = createPaymobResponse({
            paymobIframeUrl: 'https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test&clientSecret=cs_test',
            paymobPaymentKey: 'redacted-session-marker',
        });

        expect(requireStorefrontPaymentSessionAuthority(response, 'PAYMOB', cartId)).toBe(response);
    });

    it('accepts Paymob when the checkout token is transferred to its HttpOnly cookie', () => {
        const response = createPaymobResponse({
            checkoutToken: null,
            paymobIframeUrl: 'https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test&clientSecret=cs_test',
        } as unknown as Partial<CheckoutResponse>);

        expect(requireStorefrontPaymentSessionAuthority(response, 'PAYMOB', cartId)).toBe(response);
    });

    it('rejects a non-terminal Paymob response without a Unified Checkout handoff', () => {
        const response = createPaymobResponse({
            paymobIframeUrl: undefined,
            paymobPaymentKey: undefined,
        });

        expect(() => requireStorefrontPaymentSessionAuthority(response, 'PAYMOB', cartId))
            .toThrow('Backend checkout response did not prove an online payment handoff for the selected provider.');
    });

    it.each([
        ['expired session', {
            expiresAtUtc: '2020-01-01T00:00:00Z',
            paymobIframeUrl: 'https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test&clientSecret=cs_test',
        }],
        ['wrong host', {
            paymobIframeUrl: 'https://payments.example.test/unifiedcheckout/?publicKey=pk_test&clientSecret=cs_test',
        }],
        ['missing client secret', {
            paymobIframeUrl: 'https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test',
        }],
    ])('rejects an unsafe Paymob handoff: %s', (_label, overrides) => {
        const response = createPaymobResponse(overrides as Partial<CheckoutResponse>);

        expect(() => requireStorefrontPaymentSessionAuthority(response, 'PAYMOB', cartId))
            .toThrow('Backend checkout response did not prove a complete Paymob handoff for the selected provider.');
    });

    it('accepts a terminal Paymob replay only after reusable provider artifacts are scrubbed', () => {
        const response = createPaymobResponse({
            status: 'Confirmed',
            paymentStatus: 'Collected',
            paymentIntentId: null,
            checkoutToken: null,
            expiresAtUtc: null,
            paymobIframeUrl: null,
            paymobPaymentKey: null,
        } as unknown as Partial<CheckoutResponse>);

        expect(requireStorefrontPaymentSessionAuthority(response, 'PAYMOB', cartId)).toBe(response);
    });
});

describe('Paymob retry handoff authority', () => {
    const response = {
        orderId: '8bf8f86a-4a83-4520-a9bf-cdf6af7af0d7',
        orderNumber: 'ORD-P9-RETRY-1',
        paymentIntentId: 'a9358fbe-4aac-4f73-bf01-a38539fd7d34',
        intentionId: '588000001',
        iframeUrl: 'https://accept.paymob.com/unifiedcheckout/?publicKey=pk_test&clientSecret=cs_fresh',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        amount: 410,
        currency: 'EGP',
    };

    it('accepts an order-bound fresh Unified Checkout retry handoff', () => {
        expect(requirePaymobRetrySessionAuthority(response.orderId, 410, 'EGP', response)).toEqual(response);
    });

    it.each([
        ['different order', { orderId: '6d35cafe-0bf6-41aa-bac1-b706ef9311fc' }],
        ['different amount', { amount: 411 }],
        ['expired handoff', { expiresAtUtc: '2020-01-01T00:00:00Z' }],
        ['unsafe URL', { iframeUrl: 'https://example.test/unifiedcheckout/?publicKey=pk&clientSecret=cs' }],
    ])('rejects a retry response with %s', (_label, override) => {
        expect(() => requirePaymobRetrySessionAuthority(
            response.orderId,
            410,
            'EGP',
            { ...response, ...override },
        )).toThrow(/fresh Paymob payment-session handoff/i);
    });
});
