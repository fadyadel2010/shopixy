import {
    AnalyticsEventType,
    buildAnalyticsEvent,
    markSessionActivity,
    sendAnalyticsEvent,
} from '@/analytics';

export function emitCheckoutSelectionInvalidated(
    selectionType: 'shipping' | 'payment',
    reason: string,
    selectionValue?: string | null
): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.CheckoutSelectionInvalidated, {
            extra: {
                selectionType,
                reason,
                selectionValue: selectionValue ?? null,
            },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block checkout recovery.
    }
}
