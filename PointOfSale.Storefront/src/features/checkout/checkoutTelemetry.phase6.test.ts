import { expect, it, vi } from 'vitest';
const analytics = vi.hoisted(() => ({ build: vi.fn(() => ({ eventId: 'p6-token-scrub' })), queue: vi.fn(), send: vi.fn() }));
vi.mock('@/analytics', () => ({ AnalyticsEventType: { TrackingTokenRemovedFromUrl: 'TrackingTokenRemovedFromUrl' }, buildAnalyticsEvent: analytics.build, enqueueAnalyticsEvent: analytics.queue, sendAnalyticsEvent: analytics.send, markSessionActivity: vi.fn() }));
import { emitTrackingTokenRemovedFromUrl } from './checkoutTelemetry';
it('buffers early token-removal telemetry without an eager pre-CSRF request or token payload', () => {
    emitTrackingTokenRemovedFromUrl('query');
    expect(analytics.build).toHaveBeenCalledWith('TrackingTokenRemovedFromUrl', { extra: { source: 'query' } });
    expect(analytics.queue).toHaveBeenCalledExactlyOnceWith({ eventId: 'p6-token-scrub' });
    expect(analytics.send).not.toHaveBeenCalled();
});
