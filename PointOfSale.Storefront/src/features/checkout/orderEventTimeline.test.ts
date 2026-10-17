import { describe, expect, it } from 'vitest';
import { buildOrderEventTimeline } from './orderEventTimeline';

describe('tracking timeline event identity', () => {
    it('preserves repeated lifecycle states without duplicate React keys', () => {
        const events = [
            { type: 'OrderCreated', at: '2026-10-14T00:00:00Z', description: 'Order placed' },
            { type: 'StatusChanged', at: '2026-10-14T00:00:01Z', description: 'Order synchronized' },
            { type: 'StatusChanged', at: '2026-10-14T00:00:01Z', description: 'Order synchronized' },
        ];
        const steps = buildOrderEventTimeline(events);
        expect(steps.map(step => step.id)).toEqual(['pending', 'pending', 'pending']);
        expect(steps.map(step => step.label)).toEqual(events.map(event => event.description));
        expect(new Set(steps.map(step => step.eventKey)).size).toBe(3);
        expect(buildOrderEventTimeline(events)).toEqual(steps);
    });
    it('keeps prior event identity when a newer event is prepended', () => {
        const event = { type: 'Shipped', at: '2026-10-14T00:00:01Z' };
        const prior = buildOrderEventTimeline([event]);
        const next = buildOrderEventTimeline([{ type: 'Delivered', at: '2026-10-14T00:00:02Z' }, event]);
        expect(next[1].eventKey).toBe(prior[0].eventKey);
        expect(next.map(step => step.id)).toEqual(['delivered', 'shipped']);
    });
});
