import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { httpClient } from '@/services/api';
import type { CartDto } from '@/types';
import type { CheckoutFieldDefinition, ShippingAddress } from '@/types/checkout';
import { requireStorefrontPaymentSessionAuthority } from '@/features/checkout/paymentSessionAuthority';
import { clearResolvedTenant, setResolvedTenant } from '@/bootstrap/tenantResolver';
import { useCartStore } from './cartStore';
import { hydrateCheckoutStateStorage, useCheckoutStore, validateAddress } from './checkoutStore';

vi.mock('@/features/checkout/activeCheckoutOrderContext', () => ({
    persistActiveCheckoutOrderContext: vi.fn(),
}));

vi.mock('@/features/checkout/paymentSessionAuthority', () => ({
    requireStorefrontPaymentSessionAuthority: vi.fn(),
}));

vi.mock('@/features/checkout/checkoutSelectionTelemetry', () => ({
    emitCheckoutSelectionInvalidated: vi.fn(),
}));

vi.mock('@/features/cart/cartTelemetry', () => ({
    emitCartStateMismatchDetected: vi.fn(),
}));

vi.mock('@/features/auth/authTelemetry', () => ({
    emitAuthDegradedRecovery: vi.fn(),
    emitAuthStateDegraded: vi.fn(),
    emitCheckoutStateLeakPrevented: vi.fn(),
}));

const cartId = '10000000-0000-0000-0000-000000000001';
const shippingMethodA = '20000000-0000-0000-0000-000000000001';
const shippingMethodB = '20000000-0000-0000-0000-000000000002';

function createMemoryStorage(): Storage {
    const values = new Map<string, string>();

    return {
        get length() {
            return values.size;
        },
        clear: () => values.clear(),
        getItem: (key) => values.get(key) ?? null,
        key: (index) => [...values.keys()][index] ?? null,
        removeItem: (key) => {
            values.delete(key);
        },
        setItem: (key, value) => {
            values.set(key, value);
        },
    };
}

const address: ShippingAddress = {
    fullName: '',
    phone: 'invalid retained phone',
    city: '',
    area: '',
    addressLine: '',
};

function systemField(systemFieldKey: string): CheckoutFieldDefinition {
    return {
        id: `30000000-0000-0000-0000-${systemFieldKey === 'phone' ? '000000000001' : '000000000002'}`,
        fieldKind: 'SYSTEM',
        fieldType: systemFieldKey === 'phone' ? 'PHONE' : 'TEXT',
        systemFieldKey,
        label: systemFieldKey,
        isEnabled: true,
        isRequired: false,
        displayOrder: 1,
        configuration: { schemaVersion: 1 },
    };
}

function cart(): CartDto {
    return {
        cartId,
        tenantId: '40000000-0000-0000-0000-000000000001',
        ownerType: 'session',
        ownerId: 'guest-session',
        items: [],
        itemCount: 0,
        subtotal: 0,
        discountTotal: 0,
        taxTotal: 0,
        grandTotal: 0,
        currency: 'EGP',
        lastUpdatedUtc: '2026-08-13T00:00:00.000Z',
        expiresAtUtc: '2026-08-14T00:00:00.000Z',
        appliedCouponCode: null,
        appliedCoupon: null,
    };
}

describe('checkout state authority regression', () => {
    beforeEach(() => {
        useCartStore.setState({ cart: cart(), isLoading: false, isInitialized: true });
        useCheckoutStore.getState().reset();
        useCheckoutStore.setState({
            address,
            checkoutFields: [],
            checkoutFieldsStatus: 'EMPTY',
            checkoutFieldsTotalCount: 1,
            shippingMethodsStatus: 'AVAILABLE',
            shippingMethodsLoading: false,
            shippingSelectionLoading: false,
            shippingMethodId: shippingMethodA,
            shippingSelectionStatus: 'valid',
            checkoutSessionId: '50000000-0000-0000-0000-000000000001',
            checkoutSessionCartId: cartId,
            manualWalletUploadToken: 'upload-token',
            warehouseResolutionId: '60000000-0000-4000-8000-000000000001',
            warehouseResolutionVersion: 1,
            warehouseResolutionStatus: 'RESOLVED',
            warehouseResolutionExpiresAtUtc: '2099-01-01T00:00:00.000Z',
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
        useCheckoutStore.getState().reset();
        clearResolvedTenant();
        vi.unstubAllGlobals();
    });

    it('does not fall back to legacy requirements when configured fields are all hidden or disabled', () => {
        expect(validateAddress(address, [])).toEqual({});
        expect(useCheckoutStore.getState().isStepValid('shipping')).toBe(true);
    });

    it('ignores retained hidden phone data but validates the phone when the field is visible', () => {
        expect(validateAddress(address, [])).toEqual({});
        expect(validateAddress(address, [systemField('phone')])).toEqual({
            phone: 'Please enter a valid phone number',
        });
    });

    it('blocks navigation without a current resolved warehouse authority or while an upload is active', () => {
        useCheckoutStore.setState({ warehouseResolutionId: null });
        expect(useCheckoutStore.getState().isStepValid('shipping')).toBe(false);

        useCheckoutStore.setState({
            warehouseResolutionId: '60000000-0000-4000-8000-000000000001',
            attachmentUploadsInProgress: 1,
        });
        expect(useCheckoutStore.getState().isStepValid('shipping')).toBe(false);
    });

    it('prunes cart-scoped attachments when recovered checkout data belongs to another cart', async () => {
        const attachmentFieldId = '70000000-0000-4000-8000-000000000001';
        const textFieldId = '70000000-0000-4000-8000-000000000002';
        useCheckoutStore.setState({
            checkoutDataCartId: '10000000-0000-0000-0000-000000000099',
            checkoutSessionId: null,
            checkoutSessionCartId: null,
            customFieldValues: {
                [attachmentFieldId]: JSON.stringify([{ attachmentId: 'attachment-from-old-cart' }]),
                [textFieldId]: 'Keep this customer value',
            },
        });
        vi.spyOn(httpClient, 'post').mockResolvedValue({
            data: {
                sessionId: '50000000-0000-0000-0000-000000000002',
                manualWalletUploadToken: 'new-upload-token',
            },
        });

        await useCheckoutStore.getState().initCheckoutSession();

        const state = useCheckoutStore.getState();
        expect(state.checkoutDataCartId).toBe(cartId);
        expect(state.customFieldValues[attachmentFieldId]).toBeUndefined();
        expect(state.customFieldValues[textFieldId]).toBe('Keep this customer value');
    });

    it('hydrates tenant-scoped address and confirmed attachments after tenant resolution', async () => {
        const tenantId = '40000000-0000-4000-8000-000000000001';
        const attachmentFieldId = '70000000-0000-4000-8000-000000000001';
        const sessionStorage = createMemoryStorage();
        const localStorage = createMemoryStorage();
        vi.stubGlobal('window', { sessionStorage, localStorage });
        setResolvedTenant(tenantId, { overwrite: true });
        sessionStorage.setItem(`checkout-sensitive-session:${tenantId}`, JSON.stringify({
            cartId,
            address: {
                fullName: 'Reloaded Customer',
                phone: '+201000000000',
                city: 'Cairo',
                area: 'Nasr City',
                addressLine: '10 Test Street',
            },
            customFieldValues: {
                [attachmentFieldId]: JSON.stringify([{ attachmentId: 'confirmed-attachment' }]),
            },
        }));

        await hydrateCheckoutStateStorage();

        const state = useCheckoutStore.getState();
        expect(state.checkoutDataCartId).toBe(cartId);
        expect(state.address.fullName).toBe('Reloaded Customer');
        expect(state.address.addressLine).toBe('10 Test Street');
        expect(state.customFieldValues[attachmentFieldId]).toContain('confirmed-attachment');
    });

    it('rejects malformed pricing responses at the API boundary', async () => {
        vi.spyOn(httpClient, 'post').mockResolvedValue({
            data: {
                subtotal: -1,
                shipping: 0,
                tax: 0,
                discount: 0,
                total: 0,
                couponDiscount: 0,
                promotionDiscount: 0,
                cartPromotionDiscount: 0,
                paymentFee: 0,
                taxRate: 0,
                taxSource: 'STORE_CONFIG',
                pricesRefreshed: false,
            },
        });

        await useCheckoutStore.getState().refreshPricingPreview();

        expect(useCheckoutStore.getState().pricing).toBeNull();
        expect(useCheckoutStore.getState().pricingError).toBe('Unable to load the latest checkout pricing.');
    });

    it('coalesces identical in-flight pricing previews', async () => {
        let resolvePricing!: (value: unknown) => void;
        const post = vi.spyOn(httpClient, 'post').mockReturnValue(
            new Promise((resolve) => { resolvePricing = resolve; }) as never
        );

        const first = useCheckoutStore.getState().refreshPricingPreview();
        const second = useCheckoutStore.getState().refreshPricingPreview();
        expect(post).toHaveBeenCalledTimes(1);

        resolvePricing({
            data: {
                subtotal: 100,
                shipping: 10,
                tax: 0,
                discount: 0,
                total: 110,
                couponDiscount: 0,
                promotionDiscount: 0,
                cartPromotionDiscount: 0,
                paymentFee: 0,
                taxRate: 0,
                taxSource: 'STORE_CONFIG',
                pricesRefreshed: false,
            },
        });
        await Promise.all([first, second]);

        expect(useCheckoutStore.getState().pricing?.total).toBe(110);
    });

    it('suppresses Fawry from stale storefront payment-method responses', async () => {
        vi.spyOn(httpClient, 'get').mockResolvedValue({
            data: {
                status: 'AVAILABLE',
                paymentMethods: [
                    {
                        id: '90000000-0000-4000-8000-000000000001',
                        code: 'FAWRY',
                        displayName: 'Fawry Pay',
                        supportsOnline: true,
                        supportsOffline: false,
                        fee: 0,
                        feeType: 'fixed',
                        sortOrder: 1,
                        isAvailable: true,
                    },
                    {
                        id: '90000000-0000-4000-8000-000000000002',
                        code: 'COD',
                        displayName: 'Cash on Delivery',
                        supportsOnline: false,
                        supportsOffline: true,
                        fee: 0,
                        feeType: 'fixed',
                        sortOrder: 2,
                        isAvailable: true,
                    },
                ],
            },
        });

        await useCheckoutStore.getState().fetchPaymentMethods();

        const state = useCheckoutStore.getState();
        expect(state.paymentMethods.map((method) => method.code)).toEqual(['COD']);
        expect(state.selectedPaymentMethodCode).toBe('COD');
        expect(state.paymentMethodsStatus).toBe('AVAILABLE');
    });

    it('rejects direct stale Fawry selection with the stable authority code', () => {
        useCheckoutStore.getState().selectPaymentMethod('fawry');

        const state = useCheckoutStore.getState();
        expect(state.selectedPaymentMethodCode).toBeNull();
        expect(state.paymentSelectionStatus).toBe('stale');
        expect(state.errorCode).toBe('FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE');
        expect(state.error).toBe('checkout.errors.fawryUnavailable.message');
    });

    it('admits only the latest shipping selection response', async () => {
        const resolvers = new Map<string, (value: unknown) => void>();
        vi.spyOn(httpClient, 'post').mockImplementation((_url, payload) => {
            const methodId = (payload as { shippingMethodId: string }).shippingMethodId;
            return new Promise((resolve) => resolvers.set(methodId, resolve)) as never;
        });

        const first = useCheckoutStore.getState().setShippingMethod(shippingMethodA);
        const second = useCheckoutStore.getState().setShippingMethod(shippingMethodB);
        await vi.waitFor(() => expect(resolvers.size).toBe(2));

        resolvers.get(shippingMethodB)?.({
            data: {
                warehouseResolutionId: '60000000-0000-4000-8000-000000000002',
                resolutionVersion: 2,
                status: 'RESOLVED',
                expiresAtUtc: '2099-01-01T00:00:00.000Z',
            },
        });
        await second;

        resolvers.get(shippingMethodA)?.({
            data: {
                warehouseResolutionId: '60000000-0000-4000-8000-000000000003',
                resolutionVersion: 1,
                status: 'RESOLVED',
                expiresAtUtc: '2099-01-01T00:00:00.000Z',
            },
        });
        await first;

        const state = useCheckoutStore.getState();
        expect(state.shippingMethodId).toBe(shippingMethodB);
        expect(state.warehouseResolutionId).toBe('60000000-0000-4000-8000-000000000002');
        expect(state.warehouseResolutionVersion).toBe(2);
    });

    it('clears stale order results before validating a new checkout submission', async () => {
        const oldOrderId = '80000000-0000-4000-8000-000000000001';
        useCartStore.setState({
            fetchCart: vi.fn().mockResolvedValue(undefined) as never,
        });
        useCheckoutStore.setState({
            orderResult: {
                orderId: oldOrderId,
                orderNumber: 'ORD-OLD',
                total: 410,
                currency: 'EGP',
                paymentMethod: 'COD',
            },
            fetchShippingMethods: vi.fn().mockResolvedValue(undefined) as never,
            fetchPaymentMethods: vi.fn().mockResolvedValue(undefined) as never,
            refreshPricingPreview: vi.fn().mockResolvedValue(undefined) as never,
            fetchCheckoutFields: vi.fn().mockResolvedValue(undefined) as never,
        });

        const success = await useCheckoutStore.getState().submitCheckout();

        expect(success).toBe(false);
        expect(useCheckoutStore.getState().orderResult).toBeNull();
    });

    it('classifies post-200 checkout authority failures separately from backend checkout failures', async () => {
        const responseOrder = {
            orderId: '80000000-0000-4000-8000-000000000003',
            orderNumber: 'ORD-MW-AUTHORITY',
            totalAmount: 410,
            currency: 'EGP',
            cartId,
        };
        useCartStore.setState({
            cart: {
                ...cart(),
                items: [{ productId: 'product-1', variantId: 'variant-1', quantity: 1, unitPrice: 400, lineTotal: 400 }] as never,
                itemCount: 1,
                subtotal: 400,
                grandTotal: 400,
            },
            fetchCart: vi.fn().mockResolvedValue(undefined) as never,
        });
        useCheckoutStore.setState({
            address: {
                fullName: 'Manual Wallet Customer',
                phone: '01000000000',
                email: 'customer@example.com',
                city: 'Cairo',
                area: 'Nasr City',
                addressLine: 'C5.6 Street',
                country: 'EG',
            },
            checkoutFields: [],
            checkoutFieldsStatus: 'EMPTY',
            checkoutFieldsTotalCount: 0,
            shippingMethodId: shippingMethodA,
            shippingSelectionStatus: 'valid',
            checkoutSessionId: '50000000-0000-0000-0000-000000000001',
            checkoutSessionCartId: cartId,
            warehouseResolutionId: '60000000-0000-4000-8000-000000000001',
            warehouseResolutionVersion: 1,
            warehouseResolutionStatus: 'RESOLVED',
            warehouseResolutionExpiresAtUtc: '2099-01-01T00:00:00.000Z',
            paymentMethod: 'MANUALWALLET',
            paymentMethods: [{ code: 'MANUALWALLET', isAvailable: true }] as never,
            paymentMethodsStatus: 'AVAILABLE',
            paymentMethodsLoading: false,
            paymentSelectionStatus: 'valid',
            manualWalletType: 'VODAFONECASH',
            manualWalletPhoneNumber: '01000000000',
            manualWalletProofFileKey: 'tenants/40000000-0000-0000-0000-000000000001/manual-wallet/proof.png',
            manualWalletConfig: {
                walletNumber: '01000000000',
                walletTypes: [{ code: 'VODAFONECASH', name: 'Vodafone Cash' }],
                allowedMimeTypes: ['image/png'],
                allowedExtensions: ['.png'],
                maxFileBytes: 1048576,
            },
            pricing: {
                subtotal: 400,
                shipping: 10,
                tax: 0,
                discount: 0,
                total: 410,
                couponDiscount: 0,
                promotionDiscount: 0,
                cartPromotionDiscount: 0,
                paymentFee: 0,
                taxRate: 0,
                taxSource: 'STORE_CONFIG',
                pricesRefreshed: false,
            },
            fetchShippingMethods: vi.fn().mockResolvedValue(undefined) as never,
            fetchPaymentMethods: vi.fn().mockResolvedValue(undefined) as never,
            refreshPricingPreview: vi.fn().mockResolvedValue(undefined) as never,
            fetchCheckoutFields: vi.fn().mockResolvedValue(undefined) as never,
        });
        vi.spyOn(httpClient, 'post').mockResolvedValue({ data: responseOrder });
        vi.mocked(requireStorefrontPaymentSessionAuthority).mockImplementation(() => {
            throw new Error('Backend checkout response did not prove pending ManualWallet payment-session evidence.');
        });

        const success = await useCheckoutStore.getState().submitCheckout();

        expect(success).toBe(false);
        expect(useCheckoutStore.getState().errorCode).toBe('CHECKOUT_RESPONSE_AUTHORITY_FAILED');
        expect(useCheckoutStore.getState().error).toContain('could not be verified');
    });

    it.each(['COD', 'MANUALWALLET'] as const)(
        'clears the cart after an authoritative %s checkout response',
        async (paymentMethod) => {
        const responseOrder = {
            orderId: '80000000-0000-4000-8000-000000000002',
            orderNumber: `ORD-${paymentMethod}-FRESH`,
            totalAmount: 410,
            currency: 'EGP',
            paymentMethod,
            cartId,
        };
        const clearCart = vi.fn().mockResolvedValue(true);
        useCartStore.setState({
            cart: {
                ...cart(),
                items: [{ productId: 'product-1', variantId: 'variant-1', quantity: 1, unitPrice: 400, lineTotal: 400 }] as never,
                itemCount: 1,
                subtotal: 400,
                grandTotal: 400,
            },
            fetchCart: vi.fn().mockResolvedValue(undefined) as never,
            clearCart: clearCart as never,
        });
        useCheckoutStore.setState({
            address: {
                fullName: 'C5 Three Customer',
                phone: '01000000000',
                email: 'customer@example.com',
                city: 'Cairo',
                area: 'Nasr City',
                addressLine: 'C5.3 Street',
                country: 'EG',
            },
            checkoutFields: [],
            checkoutFieldsStatus: 'EMPTY',
            checkoutFieldsTotalCount: 0,
            shippingMethodId: shippingMethodA,
            shippingSelectionStatus: 'valid',
            checkoutSessionId: '50000000-0000-0000-0000-000000000001',
            checkoutSessionCartId: cartId,
            warehouseResolutionId: '60000000-0000-4000-8000-000000000001',
            warehouseResolutionVersion: 1,
            warehouseResolutionStatus: 'RESOLVED',
            warehouseResolutionExpiresAtUtc: '2099-01-01T00:00:00.000Z',
            paymentMethod,
            paymentMethods: [{ code: paymentMethod, isAvailable: true }] as never,
            paymentMethodsStatus: 'AVAILABLE',
            paymentMethodsLoading: false,
            paymentSelectionStatus: 'valid',
            manualWalletType: 'VODAFONECASH',
            manualWalletPhoneNumber: '01000000000',
            manualWalletProofFileKey: 'tenants/40000000-0000-0000-0000-000000000001/manual-wallet-proofs/proof.png',
            manualWalletConfig: {
                walletNumber: '01000000000',
                walletTypes: [{ code: 'VODAFONECASH', name: 'Vodafone Cash' }],
                allowedMimeTypes: ['image/png'],
                allowedExtensions: ['.png'],
                maxFileBytes: 1048576,
            },
            pricing: {
                subtotal: 400,
                shipping: 10,
                tax: 0,
                discount: 0,
                total: 410,
                couponDiscount: 0,
                promotionDiscount: 0,
                cartPromotionDiscount: 0,
                paymentFee: 0,
                taxRate: 0,
                taxSource: 'STORE_CONFIG',
                pricesRefreshed: false,
            },
            fetchShippingMethods: vi.fn().mockResolvedValue(undefined) as never,
            fetchPaymentMethods: vi.fn().mockResolvedValue(undefined) as never,
            refreshPricingPreview: vi.fn().mockResolvedValue(undefined) as never,
            fetchCheckoutFields: vi.fn().mockResolvedValue(undefined) as never,
        });
        vi.mocked(requireStorefrontPaymentSessionAuthority).mockReturnValue(responseOrder as never);
        vi.spyOn(httpClient, 'post').mockResolvedValue({ data: responseOrder });

        const success = await useCheckoutStore.getState().submitCheckout();

        expect(success).toBe(true);
        expect(useCheckoutStore.getState().orderResult?.orderId).toBe(responseOrder.orderId);
        expect(clearCart).toHaveBeenCalledTimes(1);
    },
    );
});
