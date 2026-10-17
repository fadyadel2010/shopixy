import type { OrderStatus, OrderTimelineEvent, TimelineStep } from '@/types/order';

/** Preserve every admitted event, including repeated statuses and identical observations. */
export function buildOrderEventTimeline(events: readonly OrderTimelineEvent[]): TimelineStep[] {
    const occurrences = new Map<string, number>();
    return events.map<TimelineStep>(event => {
        let id: OrderStatus = 'pending';
        let icon = '📅';
        if (event.type === 'OrderCreated') { id = 'pending'; icon = '📝'; }
        else if (event.type === 'MarkedAsPaid') { id = 'confirmed'; icon = '💲'; }
        else if (event.type === 'Shipped') { id = 'shipped'; icon = '🚚'; }
        else if (event.type === 'Delivered') { id = 'delivered'; icon = '🏠'; }
        else if (event.type === 'Cancelled') { id = 'cancelled'; icon = '❌'; }
        const identity = JSON.stringify([event.type, event.at, event.description ?? null]);
        const occurrence = occurrences.get(identity) ?? 0;
        occurrences.set(identity, occurrence + 1);
        return {
            id, eventKey: `${identity}:${occurrence}`, icon,
            label: event.description || event.type,
            status: 'completed', timestamp: event.at, description: event.description,
        };
    });
}
