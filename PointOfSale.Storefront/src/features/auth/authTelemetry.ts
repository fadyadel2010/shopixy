import {
    AnalyticsEventType,
    buildAnalyticsEvent,
    markSessionActivity,
    sendAnalyticsEvent,
} from '@/analytics';

export function emitAuthStateDegraded(
    source: string,
    reason: string,
    hasCustomerContext: boolean
): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.AuthStateDegraded, {
            extra: {
                source,
                reason,
                hasCustomerContext,
            },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block auth recovery.
    }
}

export function emitAuthDegradedRecovery(source: string, outcome: 'recovered' | 'failed'): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.AuthDegradedRecovery, {
            extra: {
                source,
                outcome,
            },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block auth recovery.
    }
}

export function emitCheckoutStateLeakPrevented(trigger: string): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.CheckoutStateLeakPrevented, {
            extra: { trigger },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block auth transitions.
    }
}
