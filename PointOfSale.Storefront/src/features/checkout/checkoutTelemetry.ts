import {
    AnalyticsEventType,
    buildAnalyticsEvent,
    markSessionActivity,
    sendAnalyticsEvent,
    enqueueAnalyticsEvent,
} from '@/analytics';

/**
 * Allowed reasons for checkout finalization failures.
 * These values are normalized and sent as telemetry dimensions.
 */
export type CheckoutFinalizeFailureReason =
    | 'clear_cart_failed'
    | 'clear_cart_exception'
    | 'checkout_state_cleanup_failed';

/**
 * Allowed sources for guest payment recovery success.
 * These values are normalized and sent as telemetry dimensions.
 */
export type GuestPaymentRecoverySource =
    | 'volatile_checkout_token'
    | 'url_parameter'
    | 'local_storage';

export type TrackingTokenUrlSource = 'query' | 'fragment';

/**
 * Allowed reasons for blocking unexpected cart clears.
 * These values are normalized and sent as telemetry dimensions.
 */
export type CartClearBlockedReason =
    | 'mismatched_active_order_context'
    | 'missing_active_order_context';

export function emitCheckoutFinalizeFailure(
    orderId: string,
    reason: CheckoutFinalizeFailureReason
): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.CheckoutFinalizeFailure, {
            orderId,
            extra: { reason },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block checkout completion UX.
    }
}

export function emitGuestPaymentRecoverySuccess(
    orderId: string,
    source: GuestPaymentRecoverySource
): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.GuestPaymentRecoverySuccess, {
            orderId,
            extra: { source },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block payment recovery UX.
    }
}

export function emitCartUnexpectedClearBlocked(
    orderId: string,
    reason: CartClearBlockedReason
): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.CartUnexpectedClearBlocked, {
            orderId,
            extra: { reason },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block checkout completion UX.
    }
}

export function emitTrackingTokenRemovedFromUrl(source: TrackingTokenUrlSource): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.TrackingTokenRemovedFromUrl, {
            extra: { source },
        });

        // URL scrubbing can run before the CSRF cookie is established. Use the normal
        // buffered ingestion path instead of a permanently rejected eager request.
        enqueueAnalyticsEvent(event);
    } catch {
        // Telemetry must never block token removal.
    }
}
