import { describe, expect, it } from 'vitest';
import { resolveCartFetchFailure } from '../../../PointOfSale.Storefront/src/store/cartStateResolver';

const emptyCart = {
    cartId: '',
    tenantId: 'tenant-1',
    ownerType: 'session',
    ownerId: 'session-1',
    items: [],
    itemCount: 0,
    subtotal: 0,
    discountTotal: 0,
    taxTotal: 0,
    grandTotal: 0,
    currency: 'EGP',
    lastUpdatedUtc: '2026-04-17T00:00:00.000Z',
    expiresAtUtc: '2026-04-18T00:00:00.000Z',
    appliedCouponCode: null,
    appliedCoupon: null,
};

const previousCart = {
    ...emptyCart,
    cartId: 'cart-1',
    items: [
        {
            productId: 'product-1',
            productName: 'Coffee Beans',
            quantity: 2,
            unitPrice: 120,
            lineTotal: 240,
        },
    ],
    itemCount: 2,
    subtotal: 240,
    grandTotal: 240,
};

describe('resolveCartFetchFailure', () => {
    it('preserves the last valid cart when syncing fails', () => {
        const resolution = resolveCartFetchFailure(previousCart, emptyCart, {
            message: 'Network Error',
        });

        expect(resolution.nextCart).toBe(previousCart);
        expect(resolution.preserveLastValidCart).toBe(true);
        expect(resolution.message).toContain('last saved cart');
    });

    it('clears the cart only when the backend says it no longer exists', () => {
        const resolution = resolveCartFetchFailure(previousCart, emptyCart, {
            response: {
                status: 404,
                data: {
                    code: 'CART_NOT_FOUND',
                },
            },
        });

        expect(resolution.nextCart).toBe(emptyCart);
        expect(resolution.preserveLastValidCart).toBe(false);
        expect(resolution.message).toContain('no longer available');
    });
});
