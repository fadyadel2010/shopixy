import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/features/checkout/checkoutSelectionTelemetry', () => ({
    emitCheckoutSelectionInvalidated: vi.fn(),
}));

vi.mock('@/features/cart/cartTelemetry', () => ({
    emitCartStateMismatchDetected: vi.fn(),
}));

vi.mock('@/features/auth/authTelemetry', () => ({
    emitAuthStateDegraded: vi.fn(),
    emitAuthDegradedRecovery: vi.fn(),
    emitCheckoutStateLeakPrevented: vi.fn(),
}));

import {
    clearResolvedTenant,
    getRequestHeaders,
    getSessionId,
    getTenantScopedStorageKey,
    setResolvedTenant,
    setSessionId,
} from '@/bootstrap';
import { sendAnalyticsEvent } from '@/analytics/AnalyticsClient';
import { AnalyticsEventType, type AnalyticsEvent } from '@/analytics/eventTypes';
import { useCheckoutStore } from '@/store/checkoutStore';

const TENANT_A = '11111111-1111-4111-8111-111111111111';
const TENANT_B = '22222222-2222-4222-8222-222222222222';
const SESSION_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const SESSION_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

class MemoryStorage implements Storage {
    private readonly values = new Map<string, string>();

    get length(): number {
        return this.values.size;
    }

    clear(): void {
        this.values.clear();
    }

    getItem(key: string): string | null {
        return this.values.get(key) ?? null;
    }

    key(index: number): string | null {
        return Array.from(this.values.keys())[index] ?? null;
    }

    removeItem(key: string): void {
        this.values.delete(key);
    }

    setItem(key: string, value: string): void {
        this.values.set(key, value);
    }
}

function makeAnalyticsEvent(tenantId: string, sessionId: string): AnalyticsEvent {
    return {
        eventId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
        type: AnalyticsEventType.BeginCheckout,
        actor: { sessionId },
        context: {
            tenantId,
            pageUrl: 'https://store.example.test/checkout',
        },
    };
}

beforeEach(() => {
    vi.restoreAllMocks();
    vi.stubGlobal('localStorage', new MemoryStorage());
    vi.stubGlobal('sessionStorage', new MemoryStorage());
    vi.stubGlobal('fetch', vi.fn(async () => ({
        ok: true,
        status: 202,
        json: async () => ({ accepted: true, eventId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc' }),
    })));
    clearResolvedTenant();
    useCheckoutStore.setState({
        paymentMethods: [],
        selectedPaymentMethodCode: null,
        paymentMethod: null,
        paymentSelectionStatus: 'none',
        stalePaymentSelection: null,
    });
});

describe('commerce authority certification', () => {
    it('fails closed when tenant context is unknown', () => {
        expect(() => getRequestHeaders()).toThrow(/tenant not resolved/i);
        expect(() => getTenantScopedStorageKey('checkout-state')).toThrow(/tenant resolution/i);
    });

    it('adds tenant and session headers to commerce requests', () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);

        expect(getRequestHeaders()).toMatchObject({
            'X-Tenant-Id': TENANT_A,
            'X-Session-Id': SESSION_A,
        });
    });

    it('keeps storefront sessions tenant scoped in browser storage', () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        expect(getSessionId()).toBe(SESSION_A);

        setResolvedTenant(TENANT_B, { overwrite: true });
        setSessionId(SESSION_B);
        expect(getSessionId()).toBe(SESSION_B);

        expect(localStorage.getItem(`storefront_session_id:${TENANT_A}`)).toBe(SESSION_A);
        expect(localStorage.getItem(`storefront_session_id:${TENANT_B}`)).toBe(SESSION_B);
        expect(localStorage.getItem('storefront_session_id')).toBeNull();
    });

    it('dispatches analytics only with matching tenant and session context', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);

        await sendAnalyticsEvent(makeAnalyticsEvent(TENANT_A, SESSION_A));

        expect(fetch).toHaveBeenCalledWith(
            '/api/storefront/analytics/event',
            expect.objectContaining({
                headers: expect.objectContaining({
                    'X-Tenant-Id': TENANT_A,
                    'X-Session-Id': SESSION_A,
                }),
            })
        );

        await expect(sendAnalyticsEvent(makeAnalyticsEvent(TENANT_B, SESSION_A)))
            .rejects.toThrow(/tenant context/i);
    });

    it('rejects unknown client payment admission instead of marking it valid', () => {
        useCheckoutStore.setState({
            paymentMethods: [
                {
                    id: '33333333-3333-4333-8333-333333333333',
                    code: 'COD',
                    displayName: 'Cash on delivery',
                    supportsOnline: false,
                    supportsOffline: true,
                    minOrderAmount: undefined,
                    maxOrderAmount: undefined,
                    fee: 0,
                    feeType: 'fixed',
                    sortOrder: 1,
                    isAvailable: true,
                    unavailableReasonCode: null,
                },
            ],
        });

        useCheckoutStore.getState().selectPaymentMethod('WIRE_TRANSFER');

        expect(useCheckoutStore.getState().paymentMethod).toBeNull();
        expect(useCheckoutStore.getState().selectedPaymentMethodCode).toBeNull();
        expect(useCheckoutStore.getState().paymentSelectionStatus).toBe('stale');
    });
});
