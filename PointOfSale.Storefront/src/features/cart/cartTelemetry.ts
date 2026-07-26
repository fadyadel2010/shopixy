import {
    AnalyticsEventType,
    buildAnalyticsEvent,
    markSessionActivity,
    sendAnalyticsEvent,
} from '@/analytics';

export function emitCartStateMismatchDetected(surface: string, reason: string): void {
    try {
        markSessionActivity();

        const event = buildAnalyticsEvent(AnalyticsEventType.CartStateMismatchDetected, {
            extra: {
                surface,
                reason,
            },
        });

        void sendAnalyticsEvent(event).catch(() => {
            // Best-effort telemetry only.
        });
    } catch {
        // Telemetry must never block cart rendering.
    }
}
