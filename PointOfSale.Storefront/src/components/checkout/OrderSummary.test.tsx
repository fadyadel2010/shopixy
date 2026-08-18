import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const mockState = vi.hoisted(() => ({
    cart: null as CartDto | null,
    checkout: {
        shippingMethods: [] as Array<{ id: string; name: string }>,
        shippingMethodId: null as string | null,
        pricing: null as null | {
            subtotal: number;
            shipping: number;
            tax: number;
            discount: number;
            total: number;
            couponDiscount: number;
            promotionDiscount: number;
            cartPromotionDiscount: number;
            paymentFee: number;
            taxRate: number;
            taxSource: string;
            pricesRefreshed: boolean;
        },
        pricingLoading: false,
        pricingError: null as string | null,
        hasRequestedPricing: false,
        refreshPricingPreview: vi.fn(),
    },
}));

vi.mock('@/features/checkout/checkoutSelectionTelemetry', () => ({
    emitCheckoutSelectionInvalidated: vi.fn(),
}));

vi.mock('@/features/cart/cartTelemetry', () => ({
    emitCartStateMismatchDetected: vi.fn(),
}));

vi.mock('@/features/auth/authTelemetry', () => ({
    emitAuthStateDegraded: vi.fn(),
    emitAuthDegradedRecovery: vi.fn(),
    emitCheckoutStateLeakPrevented: vi.fn(),
}));

vi.mock('@/components/cart', () => ({
    CouponInput: () => null,
}));

vi.mock('@/store', () => ({
    useCartStore: (selector: (state: { cart: CartDto | null }) => unknown) => selector({ cart: mockState.cart }),
    selectCartItems: (state: { cart: CartDto | null }) => state.cart?.items ?? [],
    selectCartCurrency: (state: { cart: CartDto | null }) => state.cart?.currency ?? '',
    selectAppliedCoupon: (state: { cart: CartDto | null }) => state.cart?.appliedCoupon ?? null,
}));

vi.mock('@/store/checkoutStore', () => ({
    useCheckoutStore: (selector: (state: typeof mockState.checkout) => unknown) => selector(mockState.checkout),
    selectSelectedShippingMethod: (state: typeof mockState.checkout) =>
        state.shippingMethods.find((method) => method.id === state.shippingMethodId) ?? null,
    selectPricingPreview: (state: typeof mockState.checkout) => state.pricing,
    selectPricingLoading: (state: typeof mockState.checkout) => state.pricingLoading,
    selectPricingError: (state: typeof mockState.checkout) => state.pricingError,
    selectHasRequestedPricing: (state: typeof mockState.checkout) => state.hasRequestedPricing,
}));

import type { CartDto } from '@/types';
import { OrderSummary } from './OrderSummary';

function makeCart(items: CartDto['items']): CartDto {
    const subtotal = items.reduce((sum, item) => sum + item.lineTotal, 0);

    return {
        cartId: 'cart-summary',
        tenantId: 'tenant-summary',
        ownerType: 'session',
        ownerId: 'session-summary',
        items,
        itemCount: items.reduce((sum, item) => sum + item.quantity, 0),
        subtotal,
        discountTotal: 0,
        taxTotal: 0,
        grandTotal: subtotal,
        currency: 'USD',
        lastUpdatedUtc: '2026-08-12T00:00:00.000Z',
        expiresAtUtc: '2026-08-13T00:00:00.000Z',
        appliedCouponCode: null,
        appliedCoupon: null,
    };
}

function setSummaryState(cart: CartDto): void {
    mockState.cart = cart;
    mockState.checkout = {
        ...mockState.checkout,
        pricing: {
            subtotal: cart.subtotal,
            shipping: 0,
            tax: 0,
            discount: 0,
            total: cart.grandTotal,
            couponDiscount: 0,
            promotionDiscount: 0,
            cartPromotionDiscount: 0,
            paymentFee: 0,
            taxRate: 0,
            taxSource: 'none',
            pricesRefreshed: true,
        },
        pricingLoading: false,
        pricingError: null,
        hasRequestedPricing: true,
        shippingMethodId: null,
        shippingMethods: [],
    };
}

describe('checkout order summary rendering', () => {
    beforeEach(() => {
        setSummaryState(makeCart([]));
    });

    it('renders authoritative product identity, image, variant, SKU, quantity, unit price, and line total', () => {
        setSummaryState(makeCart([
            {
                productId: 'product-1',
                variantId: 'variant-red-xl',
                productName: 'Authoritative Jacket',
                sku: 'JACKET-RED-XL',
                unitPrice: 100,
                quantity: 2,
                lineTotal: 200,
                imageUrl: 'https://cdn.shopixy.test/products/jacket-red-xl.jpg',
                variantAttributes: [
                    { name: 'Color', value: 'Red' },
                    { name: 'Size', value: 'XL' },
                ],
            },
        ]));

        const markup = renderToStaticMarkup(<OrderSummary collapsible={false} />);

        expect(markup).toContain('Authoritative Jacket');
        expect(markup).toContain('https://cdn.shopixy.test/products/jacket-red-xl.jpg');
        expect(markup).toContain('Red / XL');
        expect(markup).toContain('SKU: JACKET-RED-XL');
        expect(markup).toContain('Qty: 2');
        expect(markup).toContain('$100.00');
        expect(markup).toContain('$200.00');
    });

    it('keeps variant lines distinct and updates when the authoritative cart changes', () => {
        setSummaryState(makeCart([
            {
                productId: 'product-1',
                variantId: 'variant-red',
                productName: 'Variant Product',
                sku: 'SKU-RED',
                unitPrice: 40,
                quantity: 1,
                lineTotal: 40,
                imageUrl: 'https://cdn.shopixy.test/red.jpg',
                variantAttributes: [{ name: 'Color', value: 'Red' }],
            },
            {
                productId: 'product-1',
                variantId: 'variant-blue',
                productName: 'Variant Product',
                sku: 'SKU-BLUE',
                unitPrice: 45,
                quantity: 2,
                lineTotal: 90,
                imageUrl: 'https://cdn.shopixy.test/blue.jpg',
                variantAttributes: [{ name: 'Color', value: 'Blue' }],
            },
        ]));

        const initialMarkup = renderToStaticMarkup(<OrderSummary collapsible={false} />);
        expect(initialMarkup).toContain('SKU: SKU-RED');
        expect(initialMarkup).toContain('SKU: SKU-BLUE');
        expect(initialMarkup).toContain('Qty: 2');

        setSummaryState(makeCart([
            {
                productId: 'product-1',
                variantId: 'variant-blue',
                productName: 'Variant Product',
                sku: 'SKU-BLUE',
                unitPrice: 45,
                quantity: 3,
                lineTotal: 135,
                imageUrl: 'https://cdn.shopixy.test/blue.jpg',
                variantAttributes: [{ name: 'Color', value: 'Blue' }],
            },
        ]));

        const refreshedMarkup = renderToStaticMarkup(<OrderSummary collapsible={false} />);
        expect(refreshedMarkup).not.toContain('SKU: SKU-RED');
        expect(refreshedMarkup).toContain('SKU: SKU-BLUE');
        expect(refreshedMarkup).toContain('Qty: 3');
        expect(refreshedMarkup).toContain('$135.00');
    });

    it('does not render editable price controls in the summary', () => {
        setSummaryState(makeCart([
            {
                productId: 'product-1',
                productName: 'Simple Product',
                sku: 'SIMPLE-SKU',
                unitPrice: 15,
                quantity: 1,
                lineTotal: 15,
            },
        ]));

        const markup = renderToStaticMarkup(<OrderSummary collapsible={false} />);

        expect(markup).not.toContain('type="number"');
        expect(markup).not.toContain('name="unitPrice"');
        expect(markup).not.toContain('name="lineTotal"');
        expect(markup).toContain('$15.00');
    });

    it('uses the safe image fallback and readable missing-name label when presentation data is unavailable', () => {
        setSummaryState(makeCart([
            {
                productId: 'product-no-image',
                productName: '',
                sku: 'NO-IMAGE',
                unitPrice: 12,
                quantity: 1,
                lineTotal: 12,
            },
        ]));

        const markup = renderToStaticMarkup(<OrderSummary collapsible={false} />);

        expect(markup).toContain('Product unavailable');
        expect(markup).toContain('data:image/svg+xml');
        expect(markup).toContain('SKU: NO-IMAGE');
    });
});
