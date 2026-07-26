import { beforeEach, describe, expect, it, vi } from 'vitest';

const {
    clearCart,
    resetCheckoutState,
    clearPersistedCheckoutStateStorage,
    readActiveCheckoutOrderContext,
    clearActiveCheckoutOrderContextIfMatches,
    emitCartUnexpectedClearBlocked,
    emitCheckoutFinalizeFailure,
} = vi.hoisted(() => ({
    clearCart: vi.fn(),
    resetCheckoutState: vi.fn(),
    clearPersistedCheckoutStateStorage: vi.fn(),
    readActiveCheckoutOrderContext: vi.fn(),
    clearActiveCheckoutOrderContextIfMatches: vi.fn(),
    emitCartUnexpectedClearBlocked: vi.fn(),
    emitCheckoutFinalizeFailure: vi.fn(),
}));

vi.mock('@/store/cartStore', () => ({
    useCartStore: {
        getState: () => ({
            clearCart,
        }),
    },
}));

vi.mock('@/store/checkoutStore', () => ({
    useCheckoutStore: {
        getState: () => ({
            reset: resetCheckoutState,
        }),
    },
    clearPersistedCheckoutStateStorage,
}));

vi.mock('@/features/checkout/activeCheckoutOrderContext', () => ({
    readActiveCheckoutOrderContext,
    clearActiveCheckoutOrderContextIfMatches,
}));

vi.mock('@/features/checkout/checkoutTelemetry', () => ({
    emitCartUnexpectedClearBlocked,
    emitCheckoutFinalizeFailure,
}));

import { finalizeSuccessfulCheckout } from '@/features/checkout/checkoutCompletion';

describe('finalizeSuccessfulCheckout', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it('blocks cart clearing when active checkout context is missing', async () => {
        readActiveCheckoutOrderContext.mockReturnValue(null);

        await expect(finalizeSuccessfulCheckout('order-1')).resolves.toBeUndefined();

        expect(clearCart).not.toHaveBeenCalled();
        expect(resetCheckoutState).not.toHaveBeenCalled();
        expect(clearPersistedCheckoutStateStorage).not.toHaveBeenCalled();
        expect(clearActiveCheckoutOrderContextIfMatches).toHaveBeenCalledWith('order-1');
        expect(emitCartUnexpectedClearBlocked).toHaveBeenCalledWith('order-1', 'missing_active_order_context');
        expect(emitCheckoutFinalizeFailure).not.toHaveBeenCalled();
    });

    it('keeps cleanup non-blocking when cart clearing fails', async () => {
        readActiveCheckoutOrderContext.mockReturnValue({
            orderId: 'order-1',
            orderNumber: 'SO-1',
            createdAt: new Date().toISOString(),
        });
        clearCart.mockResolvedValue(false);

        await expect(finalizeSuccessfulCheckout('order-1')).resolves.toBeUndefined();

        expect(resetCheckoutState).toHaveBeenCalledOnce();
        expect(clearPersistedCheckoutStateStorage).toHaveBeenCalledOnce();
        expect(clearActiveCheckoutOrderContextIfMatches).toHaveBeenCalledWith('order-1');
        expect(emitCartUnexpectedClearBlocked).not.toHaveBeenCalled();
        expect(emitCheckoutFinalizeFailure).toHaveBeenCalledWith('order-1', 'clear_cart_failed');
    });

    it('stays silent when cleanup succeeds with matching order context', async () => {
        readActiveCheckoutOrderContext.mockReturnValue({
            orderId: 'order-1',
            orderNumber: 'SO-1',
            createdAt: new Date().toISOString(),
        });
        clearCart.mockResolvedValue(true);

        await expect(finalizeSuccessfulCheckout('order-1')).resolves.toBeUndefined();

        expect(clearCart).toHaveBeenCalledOnce();
        expect(resetCheckoutState).toHaveBeenCalledOnce();
        expect(clearPersistedCheckoutStateStorage).toHaveBeenCalledOnce();
        expect(clearActiveCheckoutOrderContextIfMatches).toHaveBeenCalledWith('order-1');
        expect(emitCartUnexpectedClearBlocked).not.toHaveBeenCalled();
        expect(emitCheckoutFinalizeFailure).not.toHaveBeenCalled();
    });

    it('blocks unrelated cart clearing when active order context has a different order ID', async () => {
        readActiveCheckoutOrderContext.mockReturnValue({
            orderId: 'order-2',
            orderNumber: 'SO-2',
            createdAt: new Date().toISOString(),
        });

        await expect(finalizeSuccessfulCheckout('order-1')).resolves.toBeUndefined();

        expect(clearCart).not.toHaveBeenCalled();
        expect(resetCheckoutState).not.toHaveBeenCalled();
        expect(clearPersistedCheckoutStateStorage).not.toHaveBeenCalled();
        expect(clearActiveCheckoutOrderContextIfMatches).toHaveBeenCalledWith('order-1');
        expect(emitCartUnexpectedClearBlocked).toHaveBeenCalledWith('order-1', 'mismatched_active_order_context');
        expect(emitCheckoutFinalizeFailure).not.toHaveBeenCalled();
    });
});
