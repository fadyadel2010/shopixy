import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
    addCartItem,
    applyCoupon,
    applyOrderDraft,
    clearCart,
    fetchCart,
    removeCartItem,
    removeCartItemByVariant,
    removeCoupon,
    updateCartItem,
    updateCartItemByVariant,
} from '@/services/api';
import type { CartDto } from '@/types';
import { selectCartIsConfirmed, useCartStore } from './cartStore';

vi.mock('@/features/cart/cartTelemetry', () => ({
    emitCartStateMismatchDetected: vi.fn(),
}));

vi.mock('@/services/api', () => ({
    fetchCart: vi.fn(),
    addCartItem: vi.fn(),
    updateCartItem: vi.fn(),
    removeCartItem: vi.fn(),
    updateCartItemByVariant: vi.fn(),
    removeCartItemByVariant: vi.fn(),
    clearCart: vi.fn(),
    applyCoupon: vi.fn(),
    removeCoupon: vi.fn(),
    applyOrderDraft: vi.fn(),
}));

const addCartItemMock = vi.mocked(addCartItem);
const fetchCartMock = vi.mocked(fetchCart);

const resetCartStore = () => {
    useCartStore.setState({
        cart: null,
        isLoading: false,
        error: null,
        updatingItemId: null,
        isInitialized: false,
        requiresRefresh: true,
        couponError: null,
        isApplyingCoupon: false,
        hydrationSource: null,
        skipNextFetch: false,
        recoveryMessage: null,
        cartMergeInProgress: false,
    });
};

const makeCart = ({
    cartId = 'cart-1',
    productId = 'product-1',
    variantId,
    quantity = 1,
}: {
    cartId?: string;
    productId?: string;
    variantId?: string;
    quantity?: number;
}): CartDto => {
    const unitPrice = 25;
    const lineTotal = unitPrice * quantity;

    return {
        cartId,
        tenantId: 'tenant-1',
        ownerType: 'session',
        ownerId: 'guest-session-1',
        items: [
            {
                productId,
                variantId,
                productName: 'Runtime Product',
                sku: variantId ? 'SKU-VARIANT' : 'SKU-SIMPLE',
                unitPrice,
                quantity,
                lineTotal,
                variantAttributes: variantId ? [{ name: 'Color', value: 'Black' }] : [],
            },
        ],
        itemCount: quantity,
        subtotal: lineTotal,
        discountTotal: 0,
        taxTotal: 0,
        grandTotal: lineTotal,
        currency: 'USD',
        lastUpdatedUtc: '2026-07-30T00:00:00.000Z',
        expiresAtUtc: '2026-07-31T00:00:00.000Z',
        appliedCouponCode: null,
        appliedCoupon: null,
    };
};

describe('cart persistence regression', () => {
    beforeEach(() => {
        vi.mocked(updateCartItem).mockReset();
        vi.mocked(removeCartItem).mockReset();
        vi.mocked(updateCartItemByVariant).mockReset();
        vi.mocked(removeCartItemByVariant).mockReset();
        vi.mocked(clearCart).mockReset();
        vi.mocked(applyCoupon).mockReset();
        vi.mocked(removeCoupon).mockReset();
        vi.mocked(applyOrderDraft).mockReset();
        addCartItemMock.mockReset();
        fetchCartMock.mockReset();
        resetCartStore();
    });

    it('keeps Product Details Add To Cart visible when the Cart page opens immediately', async () => {
        const addedCart = makeCart({ productId: 'product-pdp', quantity: 1 });
        addCartItemMock.mockResolvedValueOnce(addedCart);

        const success = await useCartStore.getState().addItem('product-pdp', 1);

        expect(success).toBe(true);
        expect(addCartItemMock).toHaveBeenCalledWith({
            productId: 'product-pdp',
            quantity: 1,
            variantId: undefined,
        });
        expect(useCartStore.getState().cart?.items).toHaveLength(1);
        expect(useCartStore.getState().cart?.items[0].productId).toBe('product-pdp');
        expect(useCartStore.getState().isInitialized).toBe(true);
        expect(fetchCartMock).not.toHaveBeenCalled();
    });

    it('sends the selected variant from Product Details and preserves the returned variant cart row', async () => {
        const addedCart = makeCart({
            productId: 'product-variant',
            variantId: 'variant-black',
            quantity: 2,
        });
        addCartItemMock.mockResolvedValueOnce(addedCart);

        const success = await useCartStore.getState().addItem('product-variant', 2, 'variant-black');

        expect(success).toBe(true);
        expect(addCartItemMock).toHaveBeenCalledWith({
            productId: 'product-variant',
            quantity: 2,
            variantId: 'variant-black',
        });
        expect(useCartStore.getState().cart?.items[0].variantId).toBe('variant-black');
        expect(useCartStore.getState().cart?.itemCount).toBe(2);
        expect(useCartStore.getState().isInitialized).toBe(true);
    });

    it('keeps listing quick-add behavior on the same canonical add pipeline', async () => {
        const addedCart = makeCart({
            productId: 'product-listing',
            variantId: 'variant-listing-default',
        });
        addedCart.items[0].sku = 'SKU-SIMPLE';
        addedCart.items[0].variantAttributes = [];
        addCartItemMock.mockResolvedValueOnce(addedCart);

        const success = await useCartStore.getState().addItem('product-listing', 1);

        expect(success).toBe(true);
        expect(addCartItemMock).toHaveBeenCalledWith({
            productId: 'product-listing',
            quantity: 1,
            variantId: undefined,
        });
        expect(useCartStore.getState().cart?.items[0].variantId).toBe('variant-listing-default');
        expect(useCartStore.getState().cart?.items[0].sku).toBe('SKU-SIMPLE');
    });

    it('mutates a canonical simple-product line through variant-scoped endpoints', async () => {
        const simpleCart = makeCart({
            productId: 'product-simple',
            variantId: 'variant-simple-default',
        });
        const updatedCart = makeCart({
            productId: 'product-simple',
            variantId: 'variant-simple-default',
            quantity: 2,
        });
        useCartStore.setState({ cart: simpleCart, isInitialized: true });
        vi.mocked(updateCartItemByVariant).mockResolvedValueOnce(updatedCart);
        vi.mocked(removeCartItemByVariant).mockResolvedValueOnce({
            ...updatedCart,
            items: [],
            itemCount: 0,
            subtotal: 0,
            grandTotal: 0,
        });

        const updated = await useCartStore.getState().updateQuantity(
            'product-simple',
            2,
            'variant-simple-default'
        );
        const removed = await useCartStore.getState().removeItem(
            'product-simple',
            'variant-simple-default'
        );

        expect(updated).toBe(true);
        expect(removed).toBe(true);
        expect(updateCartItemByVariant).toHaveBeenCalledWith(
            'variant-simple-default',
            { quantity: 2 }
        );
        expect(removeCartItemByVariant).toHaveBeenCalledWith('variant-simple-default');
        expect(updateCartItem).not.toHaveBeenCalled();
        expect(removeCartItem).not.toHaveBeenCalled();
    });

    it('loads a persisted guest cart for browser refresh or direct Cart URL entry', async () => {
        const persistedCart = makeCart({
            cartId: 'persisted-cart',
            productId: 'product-refresh',
            quantity: 3,
        });
        fetchCartMock.mockResolvedValueOnce(persistedCart);

        await useCartStore.getState().fetchCart();

        expect(fetchCartMock).toHaveBeenCalledTimes(1);
        expect(useCartStore.getState().isInitialized).toBe(true);
        expect(useCartStore.getState().cart?.cartId).toBe('persisted-cart');
        expect(useCartStore.getState().cart?.items[0].productId).toBe('product-refresh');
        expect(useCartStore.getState().cart?.itemCount).toBe(3);
    });

    it.each([404, 400, 401, 403, 422])('discards a previous cart when the backend rejects it with %s', async (status) => {
        useCartStore.setState({ cart: makeCart({}), requiresRefresh: false });
        fetchCartMock.mockRejectedValueOnce({ response: { status, data: {} } });
        await useCartStore.getState().fetchCart();
        expect(useCartStore.getState().cart?.items).toEqual([]);
        expect(selectCartIsConfirmed(useCartStore.getState())).toBe(false);
    });

    it.each([new Error('Network Error'), { response: { status: 503, data: { message: 'unavailable' } } }])('retains an explicitly unconfirmed snapshot on transient failure, including after dismissal', async (error) => {
        const previous = makeCart({});
        useCartStore.setState({ cart: previous, requiresRefresh: false });
        fetchCartMock.mockRejectedValueOnce(error);
        await useCartStore.getState().fetchCart();
        expect(useCartStore.getState().cart).toBe(previous);
        expect(useCartStore.getState().error).toContain('unconfirmed');
        useCartStore.getState().clearError();
        expect(selectCartIsConfirmed(useCartStore.getState())).toBe(false);

        const fresh = makeCart({ quantity: 3 });
        fetchCartMock.mockResolvedValueOnce(fresh);
        await useCartStore.getState().fetchCart();
        expect(useCartStore.getState().cart).toBe(fresh);
        expect(selectCartIsConfirmed(useCartStore.getState())).toBe(true);
    });

    it('does not invent cart contents on missing state or admit inconsistent server data', async () => {
        fetchCartMock.mockRejectedValueOnce(new Error('Network Error'));
        await useCartStore.getState().fetchCart();
        expect(useCartStore.getState().cart?.items).toEqual([]);
        expect(selectCartIsConfirmed(useCartStore.getState())).toBe(false);

        fetchCartMock.mockResolvedValueOnce({ ...makeCart({}), itemCount: 999 });
        await useCartStore.getState().fetchCart();
        useCartStore.getState().clearError();
        expect(useCartStore.getState().cart?.items).toEqual([]);
        expect(selectCartIsConfirmed(useCartStore.getState())).toBe(false);
    });

    it('coalesces concurrent hydration callers and resolves both from one authoritative fetch', async () => {
        const persistedCart = makeCart({ cartId: 'coalesced-cart' });
        let resolveFetch!: (cart: CartDto) => void;
        fetchCartMock.mockReturnValueOnce(new Promise<CartDto>((resolve) => {
            resolveFetch = resolve;
        }));

        const first = useCartStore.getState().fetchCart();
        const second = useCartStore.getState().fetchCart();

        expect(fetchCartMock).toHaveBeenCalledTimes(1);
        expect(useCartStore.getState().isLoading).toBe(true);

        resolveFetch(persistedCart);
        await Promise.all([first, second]);

        expect(useCartStore.getState().cart?.cartId).toBe('coalesced-cart');
        expect(useCartStore.getState().isLoading).toBe(false);
    });
});
