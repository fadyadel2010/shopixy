import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AxiosAdapter, AxiosResponse } from 'axios';

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
import { httpClient, isAuthEndpoint } from '@/services/api/httpClient';
import { useCheckoutStore } from '@/store/checkoutStore';
import type { CartDto } from '@/types';

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

function makeCartResponse(sessionId: string, tenantId: string): CartDto {
    return {
        cartId: `cart:${sessionId}`,
        tenantId,
        ownerType: 'session',
        ownerId: sessionId,
        items: [],
        itemCount: 0,
        subtotal: 0,
        discountTotal: 0,
        taxTotal: 0,
        grandTotal: 0,
        currency: 'USD',
        lastUpdatedUtc: '2026-07-31T00:00:00.000Z',
        expiresAtUtc: '2026-08-01T00:00:00.000Z',
        appliedCouponCode: null,
        appliedCoupon: null,
    };
}

function createCartEchoAdapter(captured: string[]): AxiosAdapter {
    return async (config) => {
        const sessionId = String(config.headers?.['X-Session-Id'] ?? '');
        const tenantId = String(config.headers?.['X-Tenant-Id'] ?? '');
        captured.push(`${config.method?.toUpperCase()} ${config.url} ${tenantId} ${sessionId}`);

        const response: AxiosResponse<CartDto> = {
            data: makeCartResponse(sessionId, tenantId),
            status: 200,
            statusText: 'OK',
            headers: {},
            config,
        };

        return response;
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

    it('keeps the same guest identity from POST add item to GET cart', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const capturedRequests: string[] = [];
        const adapter = createCartEchoAdapter(capturedRequests);

        const addResponse = await httpClient.post<CartDto>(
            '/api/storefront/cart/items',
            { productId: '44444444-4444-4444-8444-444444444444', quantity: 1 },
            { adapter }
        );
        const getResponse = await httpClient.get<CartDto>(
            '/api/storefront/cart',
            { adapter }
        );

        expect(addResponse.data.cartId).toBe(`cart:${SESSION_A}`);
        expect(getResponse.data.cartId).toBe(addResponse.data.cartId);
        expect(addResponse.data.ownerId).toBe(SESSION_A);
        expect(getResponse.data.ownerId).toBe(addResponse.data.ownerId);
        expect(capturedRequests).toEqual([
            `POST /api/storefront/cart/items ${TENANT_A} ${SESSION_A}`,
            `GET /api/storefront/cart ${TENANT_A} ${SESSION_A}`,
        ]);
    });

    it('keeps storefront auth probes out of forced logout interception', () => {
        expect(isAuthEndpoint('/api/storefront/auth/me')).toBe(true);
        expect(isAuthEndpoint('/api/storefront/auth/refresh')).toBe(true);
        expect(isAuthEndpoint('/api/storefront/cart')).toBe(false);
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
