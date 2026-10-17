import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { httpClient } from '@/services/api';
import type { CartDto } from '@/types';
import type { CheckoutResponse, PaymentMethod } from '@/types/checkout';
import { useCartStore } from './cartStore';
import { useCheckoutStore } from './checkoutStore';

vi.mock('@/features/checkout/activeCheckoutOrderContext', () => ({ persistActiveCheckoutOrderContext: vi.fn() }));
vi.mock('@/features/checkout/checkoutSelectionTelemetry', () => ({ emitCheckoutSelectionInvalidated: vi.fn() }));
vi.mock('@/features/cart/cartTelemetry', () => ({ emitCartStateMismatchDetected: vi.fn() }));
vi.mock('@/features/auth/authTelemetry', () => ({
    emitAuthDegradedRecovery: vi.fn(), emitAuthStateDegraded: vi.fn(), emitCheckoutStateLeakPrevented: vi.fn(),
}));

const cartId = '10000000-0000-4000-8000-000000000001';
const originalCartActions = { fetchCart: useCartStore.getState().fetchCart, clearCart: useCartStore.getState().clearCart };
const originalCheckoutActions = {
    fetchShippingMethods: useCheckoutStore.getState().fetchShippingMethods,
    fetchPaymentMethods: useCheckoutStore.getState().fetchPaymentMethods,
    refreshPricingPreview: useCheckoutStore.getState().refreshPricingPreview,
    fetchCheckoutFields: useCheckoutStore.getState().fetchCheckoutFields,
};
const originalCart: CartDto = {
    cartId, tenantId: '20000000-0000-4000-8000-000000000001', ownerType: 'session', ownerId: 'synthetic-session',
    items: [{ productId: 'product-1', variantId: 'variant-1', productName: 'Synthetic item', sku: 'TEST', quantity: 1, unitPrice: 100, lineTotal: 100 }],
    itemCount: 1, subtotal: 100, discountTotal: 0, taxTotal: 0, grandTotal: 100, currency: 'EGP',
    lastUpdatedUtc: '2026-10-15T00:00:00Z', expiresAtUtc: '2099-01-01T00:00:00Z', appliedCouponCode: null, appliedCoupon: null,
};

function response(method: PaymentMethod): CheckoutResponse {
    return {
        cartId, orderId: '30000000-0000-4000-8000-000000000001', orderNumber: 'CLOSEOUT-SYNTHETIC',
        totalAmount: 100, currency: 'EGP', status: 'Pending', paymentStatus: 'Pending', paymentMethodCode: method,
        paymentIntentId: '40000000-0000-4000-8000-000000000001', expiresAtUtc: '2099-01-01T00:00:00Z',
        ...(method === 'STRIPE' ? { stripeClientSecret: 'synthetic-secret', stripePublishableKey: 'synthetic-key' } : {}),
        ...(method === 'PAYMOB' ? { paymobIframeUrl: 'https://accept.paymob.com/unifiedcheckout?publicKey=synthetic&clientSecret=synthetic' } : {}),
        ...(method === 'MANUALWALLET' ? { isManualWallet: true, walletType: 'VODAFONECASH', paymentStatus: 'AwaitingVerification' } : {}),
    } as CheckoutResponse;
}

function prepare(method: PaymentMethod) {
    const clear = vi.spyOn(useCartStore.getState(), 'clearCart').mockResolvedValue(true);
    vi.spyOn(useCartStore.getState(), 'fetchCart').mockResolvedValue(undefined);
    useCartStore.setState({ cart: originalCart, requiresRefresh: false, error: null, isLoading: false, updatingItemId: null, isApplyingCoupon: false, cartMergeInProgress: false });
    useCheckoutStore.setState({
        address: { fullName: 'Synthetic', phone: '+201000000000', city: 'Cairo', area: 'Test', addressLine: 'Test street', country: 'EG' },
        checkoutFields: [], checkoutFieldsStatus: 'EMPTY', checkoutFieldsTotalCount: 0, checkoutFieldsLoading: false,
        shippingMethodId: '50000000-0000-4000-8000-000000000001', shippingSelectionStatus: 'valid',
        checkoutSessionId: '60000000-0000-4000-8000-000000000001', checkoutSessionCartId: cartId,
        warehouseResolutionId: '70000000-0000-4000-8000-000000000001', warehouseResolutionVersion: 1,
        warehouseResolutionStatus: 'RESOLVED', warehouseResolutionExpiresAtUtc: '2099-01-01T00:00:00Z',
        paymentMethod: method, paymentMethods: [{ code: method, isAvailable: true }] as never,
        paymentMethodsStatus: 'AVAILABLE', paymentMethodsLoading: false, paymentSelectionStatus: 'valid',
        manualWalletType: 'VODAFONECASH', manualWalletPhoneNumber: '+201000000000', manualWalletProofFileKey: 'synthetic-proof',
        manualWalletConfig: { walletTypes: [{ code: 'VODAFONECASH', name: 'Vodafone Cash' }] } as never,
        pricing: { subtotal: 100, shipping: 0, tax: 0, discount: 0, total: 100, couponDiscount: 0, promotionDiscount: 0, cartPromotionDiscount: 0, paymentFee: 0, taxRate: 0, taxSource: 'STORE_CONFIG', pricesRefreshed: false },
    });
    vi.spyOn(useCheckoutStore.getState(), 'fetchShippingMethods').mockResolvedValue(undefined);
    vi.spyOn(useCheckoutStore.getState(), 'fetchPaymentMethods').mockResolvedValue(undefined);
    vi.spyOn(useCheckoutStore.getState(), 'refreshPricingPreview').mockResolvedValue(undefined);
    vi.spyOn(useCheckoutStore.getState(), 'fetchCheckoutFields').mockResolvedValue(undefined);
    return { clear, post: vi.spyOn(httpClient, 'post') };
}

describe('checkout lifecycle closeout (real payment response admission)', () => {
    beforeEach(() => { vi.clearAllMocks(); useCheckoutStore.getState().reset(); });
    afterEach(() => {
        vi.restoreAllMocks();
        useCartStore.setState(originalCartActions);
        useCheckoutStore.setState(originalCheckoutActions);
        useCheckoutStore.getState().reset();
    });

    it.each(['COD', 'MANUALWALLET', 'STRIPE', 'PAYMOB'] as const)('clears only committed offline orders, never pending online %s', async method => {
        const { clear, post } = prepare(method);
        post.mockResolvedValue({ data: response(method) });
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(true);
        expect(useCheckoutStore.getState().orderResult?.orderId).toBe(response(method).orderId);
        expect(clear).toHaveBeenCalledTimes(method === 'COD' || method === 'MANUALWALLET' ? 1 : 0);
        if (method === 'STRIPE' || method === 'PAYMOB') expect(useCartStore.getState().cart).toBe(originalCart);
    });

    it.each(['COD', 'MANUALWALLET', 'STRIPE', 'PAYMOB'] as const)('keeps cart and checkout identity on failed %s submission, retrying the same request', async method => {
        const { clear, post } = prepare(method);
        post.mockRejectedValueOnce({ response: { status: 503, data: { message: 'Temporarily unavailable' } } })
            .mockResolvedValueOnce({ data: response(method) });
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(false);
        expect(clear).not.toHaveBeenCalled();
        expect(useCartStore.getState().cart).toBe(originalCart);
        expect(useCheckoutStore.getState().orderResult).toBeNull();
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(true);
        expect(post.mock.calls[1][1]).toEqual(post.mock.calls[0][1]);
        // Durable replay/non-duplication is separately verified by the actual
        // CheckoutRequestIdempotencyCoordinator SQLite tests, not this HTTP stub.
    });

    it.each(['COD', 'MANUALWALLET', 'STRIPE', 'PAYMOB'] as const)('does not clear or accept rejected response authority for %s', async method => {
        const { clear, post } = prepare(method);
        post.mockResolvedValue({ data: { ...response(method), cartId: 'foreign-cart' } });
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(false);
        expect(useCheckoutStore.getState().errorCode).toBe('CHECKOUT_RESPONSE_AUTHORITY_FAILED');
        expect(useCartStore.getState().cart).toBe(originalCart);
        expect(clear).not.toHaveBeenCalled();
    });

    it('coalesces same-tick submit attempts before any order response', async () => {
        const { post } = prepare('STRIPE');
        let finish!: (value: unknown) => void;
        post.mockReturnValue(new Promise(resolve => { finish = resolve; }) as never);
        const first = useCheckoutStore.getState().submitCheckout();
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(false);
        await vi.waitFor(() => expect(post).toHaveBeenCalledTimes(1));
        finish({ data: response('STRIPE') });
        expect(await first).toBe(true);
        expect(post).toHaveBeenCalledTimes(1);
    });

    it.each(['COD', 'MANUALWALLET'] as const)('does not turn committed %s into a retryable order failure when cleanup fails', async method => {
        const { clear, post } = prepare(method);
        clear.mockRejectedValue(new Error('Cart cleanup unavailable'));
        post.mockResolvedValue({ data: response(method) });
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(true);
        expect(useCheckoutStore.getState().orderResult?.orderId).toBe(response(method).orderId);
        expect(post).toHaveBeenCalledTimes(1);
    });

    it.each([true, false])('refuses direct checkout with an unconfirmed cart even when the error was dismissed (%s)', async dismissed => {
        const { clear, post } = prepare('COD');
        useCartStore.setState({ requiresRefresh: true, error: dismissed ? null : 'Refresh failed' });
        expect(await useCheckoutStore.getState().submitCheckout()).toBe(false);
        expect(useCheckoutStore.getState().errorCode).toBe('CART_REFRESH_REQUIRED');
        expect(post).not.toHaveBeenCalled();
        expect(clear).not.toHaveBeenCalled();
    });
});
