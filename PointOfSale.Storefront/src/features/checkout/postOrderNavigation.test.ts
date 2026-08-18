import { describe, expect, it } from 'vitest';
import {
    buildPendingCheckoutUrl,
    readPendingCheckoutRouteEvidence,
} from './postOrderNavigation';

describe('post-order checkout navigation contract', () => {
    it('builds the pending route from the authoritative checkout response identity', () => {
        const url = buildPendingCheckoutUrl({
            orderId: 'c481589a-1a46-4c3e-a8e6-33bf8c44dbc3',
            orderNumber: 'ORD-83B3-2026-01005',
        });

        expect(url).toBe('/checkout/pending?orderId=c481589a-1a46-4c3e-a8e6-33bf8c44dbc3&orderNumber=ORD-83B3-2026-01005');
    });

    it('rejects incomplete checkout success evidence instead of navigating with stale state', () => {
        expect(() => buildPendingCheckoutUrl({
            orderId: 'c481589a-1a46-4c3e-a8e6-33bf8c44dbc3',
            orderNumber: '',
        })).toThrow(/canonical post-order identity/i);
    });

    it('reads current pending route evidence without consulting persisted browser state', () => {
        const evidence = readPendingCheckoutRouteEvidence(new URLSearchParams({
            orderId: 'c481589a-1a46-4c3e-a8e6-33bf8c44dbc3',
            orderNumber: 'ORD-83B3-2026-01005',
        }));

        expect(evidence).toEqual({
            orderId: 'c481589a-1a46-4c3e-a8e6-33bf8c44dbc3',
            orderNumber: 'ORD-83B3-2026-01005',
        });
    });

    it('keeps the legacy order query alias as lookup-only compatibility', () => {
        const evidence = readPendingCheckoutRouteEvidence(new URLSearchParams({
            order: '04ca9737-00c8-4daf-bd5c-6b34ba0d8515',
        }));

        expect(evidence).toEqual({
            orderId: '04ca9737-00c8-4daf-bd5c-6b34ba0d8515',
            orderNumber: null,
        });
    });
});
