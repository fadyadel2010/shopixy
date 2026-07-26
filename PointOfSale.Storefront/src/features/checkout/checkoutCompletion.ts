import { useCartStore } from '@/store/cartStore';
import {
    clearPersistedCheckoutStateStorage,
    useCheckoutStore,
} from '@/store/checkoutStore';
import {
    clearActiveCheckoutOrderContextIfMatches,
    clearCheckoutTokenForOrder,
    readActiveCheckoutOrderContext,
} from './activeCheckoutOrderContext';
import {
    type CheckoutFinalizeFailureReason,
    emitCartUnexpectedClearBlocked,
    emitCheckoutFinalizeFailure,
} from './checkoutTelemetry';

export async function finalizeSuccessfulCheckout(orderId: string): Promise<void> {
    let failureReason: CheckoutFinalizeFailureReason | null = null;
    const activeOrder = readActiveCheckoutOrderContext();
    const hasMatchingActiveOrder = activeOrder?.orderId === orderId;

    if (!hasMatchingActiveOrder) {
        const reason = activeOrder
            ? 'mismatched_active_order_context'
            : 'missing_active_order_context';

        emitCartUnexpectedClearBlocked(orderId, reason);
        clearActiveCheckoutOrderContextIfMatches(orderId);
        return;
    }

    try {
        const cleared = await useCartStore.getState().clearCart();
        if (!cleared) {
            failureReason ??= 'clear_cart_failed';
        }
    } catch (error) {
        console.error('Failed to finalize checkout cleanup:', error);
        failureReason ??= 'clear_cart_exception';
    }

    try {
        useCheckoutStore.getState().reset();
        clearPersistedCheckoutStateStorage();
        clearActiveCheckoutOrderContextIfMatches(orderId);
        clearCheckoutTokenForOrder(orderId);
    } catch (error) {
        console.error('Failed to clear checkout state after successful payment:', error);
        failureReason ??= 'checkout_state_cleanup_failed';
    }

    if (failureReason) {
        emitCheckoutFinalizeFailure(orderId, failureReason);
    }
}
