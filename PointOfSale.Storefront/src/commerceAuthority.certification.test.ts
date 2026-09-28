import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AxiosAdapter, AxiosResponse } from 'axios';
import { readFileSync } from 'node:fs';

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
    resolveTenant,
    setCanonicalTenant,
    setResolvedTenant,
    setSessionId,
} from '@/bootstrap';
import { sendAnalyticsEvent } from '@/analytics/AnalyticsClient';
import { AnalyticsEventType, type AnalyticsEvent } from '@/analytics/eventTypes';
import { httpClient, isAuthEndpoint, isRequestCanceled, normalizeError } from '@/services/api/httpClient';
import { useCartStore } from '@/store/cartStore';
import { useCheckoutStore, validateAddress, validateCustomCheckoutFields } from '@/store/checkoutStore';
import type { CheckoutFieldDefinition } from '@/types/checkout';
import type { CartDto } from '@/types';

const TENANT_A = '11111111-1111-4111-8111-111111111111';
const TENANT_B = '22222222-2222-4222-8222-222222222222';
const SESSION_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const SESSION_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const CART_A = '33333333-3333-4333-8333-333333333333';
const CART_B = '44444444-4444-4444-8444-444444444444';
const WAREHOUSE_RESOLUTION_ID = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';

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

function createCheckoutDependencyAdapter(dataByUrl: Record<string, unknown>): AxiosAdapter {
    const adapter: AxiosAdapter = async (config) => {
        const url = String(config.url);
        const legacyShippingPayload = dataByUrl['/api/storefront/shipping-methods'];
        const data = url === '/api/storefront/shipping-methods/preview' && legacyShippingPayload && typeof legacyShippingPayload === 'object'
            ? {
                ...legacyShippingPayload,
                status: 'PREVIEW',
                shippingMethodsStatus: (legacyShippingPayload as { status?: unknown }).status,
            }
            : url === '/api/storefront/shipping-methods/select'
            ? {
                warehouseResolutionId: WAREHOUSE_RESOLUTION_ID,
                resolutionVersion: 1,
                status: 'RESOLVED',
                expiresAtUtc: '2099-08-11T12:00:00.000Z',
            }
            : dataByUrl[url];
        const response: AxiosResponse<unknown> = {
            data,
            status: 200,
            statusText: 'OK',
            headers: {},
            config,
        };

        return response;
    };
    vi.spyOn(httpClient, 'post').mockImplementation((url, data, config) =>
        httpClient.request({ ...config, url, data, method: 'post', adapter }) as any);
    return adapter;
}

function createNetworkFailureAdapter(): AxiosAdapter {
    const adapter: AxiosAdapter = async (config) => {
        throw Object.assign(new Error('Network Error'), {
            config,
            isAxiosError: true,
            toJSON: () => ({}),
        });
    };
    vi.spyOn(httpClient, 'post').mockImplementation((url, data, config) =>
        httpClient.request({ ...config, url, data, method: 'post', adapter }) as any);
    return adapter;
}

function parseAdapterData(data: unknown): unknown {
    if (typeof data === 'string') {
        try {
            return JSON.parse(data);
        } catch {
            return data;
        }
    }

    return data;
}

function createDeferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((promiseResolve, promiseReject) => {
        resolve = promiseResolve;
        reject = promiseReject;
    });

    return { promise, resolve, reject };
}

function makeShippingPreviewResponse(methodId: string, name = 'Standard shipping', price = 20): unknown {
    return {
        tenantId: TENANT_A,
        status: 'PREVIEW',
        shippingMethodsStatus: 'AVAILABLE',
        shippingMethods: [
            {
                tenantId: TENANT_A,
                id: methodId,
                name,
                price,
                currency: 'USD',
                estimatedDays: 3,
            },
        ],
    };
}

function makeWarehouseResolutionResponse(id = WAREHOUSE_RESOLUTION_ID): unknown {
    return {
        warehouseResolutionId: id,
        resolutionVersion: 1,
        status: 'RESOLVED',
        expiresAtUtc: '2099-08-11T12:00:00.000Z',
    };
}

function makeAxiosResponse<T>(data: T, config: unknown = {}): AxiosResponse<T> {
    return {
        data,
        status: 200,
        statusText: 'OK',
        headers: {},
        config: config as AxiosResponse<T>['config'],
    };
}

function makeCanceledError(config?: unknown): Error & { code: string; name: string; isAxiosError: boolean; config?: unknown; toJSON: () => object } {
    return Object.assign(new Error('canceled'), {
        code: 'ERR_CANCELED',
        name: 'CanceledError',
        isAxiosError: true,
        config,
        toJSON: () => ({}),
    });
}

interface ControlledPreviewCall {
    data: unknown;
    signal?: {
        aborted?: boolean;
        addEventListener?: (type: 'abort', listener: () => void, options?: { once?: boolean }) => void;
    };
    deferred: ReturnType<typeof createDeferred<AxiosResponse<unknown>>>;
    aborted: boolean;
}

function createControlledShippingPostMock(options: { rejectOnAbort?: boolean } = {}) {
    const previewCalls: ControlledPreviewCall[] = [];
    const selectCalls: Array<{ data: unknown }> = [];
    const sessionCalls: Array<{ data: unknown }> = [];
    const rejectOnAbort = options.rejectOnAbort ?? true;

    vi.spyOn(httpClient, 'post').mockImplementation((url, data, config) => {
        const normalizedUrl = String(url);
        if (normalizedUrl === '/api/storefront/shipping-methods/preview') {
            const deferred = createDeferred<AxiosResponse<unknown>>();
            const call: ControlledPreviewCall = {
                data,
                signal: config?.signal,
                deferred,
                aborted: false,
            };
            call.signal?.addEventListener?.('abort', () => {
                call.aborted = true;
                if (rejectOnAbort) {
                    deferred.reject(makeCanceledError(config));
                }
            }, { once: true });
            previewCalls.push(call);
            return deferred.promise as any;
        }

        if (normalizedUrl === '/api/storefront/shipping-methods/select') {
            selectCalls.push({ data });
            return Promise.resolve(makeAxiosResponse(makeWarehouseResolutionResponse(), config)) as any;
        }

        if (normalizedUrl === '/api/storefront/checkout-session') {
            sessionCalls.push({ data });
            return Promise.resolve(makeAxiosResponse({ sessionId: SESSION_B, manualWalletUploadToken: `upload-token:${SESSION_B}` }, config)) as any;
        }

        return Promise.resolve(makeAxiosResponse({}, config)) as any;
    });

    return { previewCalls, selectCalls, sessionCalls };
}

async function flushMicrotasks(times = 3): Promise<void> {
    for (let index = 0; index < times; index += 1) {
        await Promise.resolve();
    }
}

function createStep2HShippingLifecycleAdapter(calls: Array<{ url: string; data: unknown }>, sessionId = SESSION_B): AxiosAdapter {
    const adapter: AxiosAdapter = async (config) => {
        const url = String(config.url);
        calls.push({ url, data: parseAdapterData(config.data) });

        const data = url === '/api/storefront/checkout-session'
            ? { sessionId, manualWalletUploadToken: `upload-token:${sessionId}` }
            : url === '/api/storefront/shipping-methods/preview'
            ? {
                tenantId: TENANT_A,
                status: 'PREVIEW',
                shippingMethodsStatus: 'AVAILABLE',
                shippingMethods: [
                    {
                        tenantId: TENANT_A,
                        id: '55555555-5555-4555-8555-555555555555',
                        name: 'Standard shipping',
                        price: 20,
                        currency: 'USD',
                        estimatedDays: 3,
                    },
                ],
            }
            : url === '/api/storefront/shipping-methods/select'
            ? {
                warehouseResolutionId: WAREHOUSE_RESOLUTION_ID,
                resolutionVersion: 1,
                status: 'RESOLVED',
                expiresAtUtc: '2099-08-11T12:00:00.000Z',
            }
            : {};

        return {
            data,
            status: 200,
            statusText: 'OK',
            headers: {},
            config,
        } as AxiosResponse<unknown>;
    };

    vi.spyOn(httpClient, 'post').mockImplementation((url, data, config) =>
        httpClient.request({ ...config, url, data, method: 'post', adapter }) as any);
    return adapter;
}

beforeEach(() => {
    vi.restoreAllMocks();
    vi.stubGlobal('localStorage', new MemoryStorage());
    vi.stubGlobal('sessionStorage', new MemoryStorage());
    vi.stubGlobal('window', {
        location: {
            hostname: 'store.example.test',
            search: '',
        },
    });
    vi.stubGlobal('document', {
        referrer: '',
        cookie: 'csrf_token=csrf-test-value',
    });
    vi.stubGlobal('navigator', {
        userAgent: 'Vitest',
    });
    vi.stubGlobal('fetch', vi.fn(async () => ({
        ok: true,
        status: 202,
        json: async () => ({ accepted: true, eventId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc' }),
    })));
    clearResolvedTenant();
    useCartStore.setState({
        cart: {
            ...makeCartResponse(SESSION_A, TENANT_A),
            cartId: CART_A,
        },
        isLoading: false,
        error: null,
        isInitialized: true,
        cartMergeInProgress: false,
    });
    useCheckoutStore.setState({
        shippingMethodId: null,
        shippingSelectionStatus: 'none',
        staleShippingSelection: null,
        shippingMethods: [],
        shippingMethodsLoading: false,
        shippingMethodsStatus: 'IDLE',
        shippingMethodsError: null,
        warehouseResolutionId: null,
        warehouseResolutionVersion: null,
        warehouseResolutionStatus: null,
        warehouseResolutionExpiresAtUtc: null,
        paymentMethods: [],
        paymentMethodsStatus: 'IDLE',
        paymentMethodsError: null,
        selectedPaymentMethodCode: null,
        paymentMethod: null,
        paymentSelectionStatus: 'none',
        stalePaymentSelection: null,
        checkoutSessionId: SESSION_A,
        checkoutSessionCartId: CART_A,
        manualWalletUploadToken: 'upload-token',
        checkoutFields: [],
        checkoutFieldsStatus: 'IDLE',
        checkoutFieldsError: null,
        checkoutFieldsTotalCount: 0,
        customFieldValues: {},
        touchedCustomFields: new Set(),
    });
});

const requiredPhoneField: CheckoutFieldDefinition = {
    id: '77777777-7777-4777-8777-777777777777',
    fieldKind: 'SYSTEM',
    fieldType: 'PHONE',
    systemFieldKey: 'phone',
    label: 'Mobile Number',
    helpText: null,
    isEnabled: true,
    isRequired: true,
    displayOrder: 10,
    configuration: { schemaVersion: 1, minLength: null, maxLength: null },
};

const customSelectField: CheckoutFieldDefinition = {
    id: '88888888-8888-4888-8888-888888888888',
    fieldKind: 'CUSTOM',
    fieldType: 'SELECT',
    systemFieldKey: null,
    label: 'T-Shirt Size',
    helpText: null,
    isEnabled: true,
    isRequired: true,
    displayOrder: 20,
    configuration: {
        schemaVersion: 1,
        options: [
            { value: 'large', label: 'Large' },
            { value: 'small', label: 'Small' },
        ],
    },
};

const customImageField: CheckoutFieldDefinition = {
    id: '99999999-9999-4999-8999-999999999999',
    fieldKind: 'CUSTOM',
    fieldType: 'IMAGE',
    systemFieldKey: null,
    label: 'Prescription Image',
    helpText: null,
    isEnabled: true,
    isRequired: true,
    displayOrder: 30,
    configuration: {
        schemaVersion: 1,
        allowedMimeTypes: ['image/jpeg', 'image/png'],
        maxFileSize: 5 * 1024 * 1024,
        maxFiles: 1,
    },
};

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

    it('resolves storefront tenant slugs from subdomains without treating them as GUIDs', () => {
        vi.stubGlobal('window', {
            location: {
                hostname: 'north-store.example.com',
                search: '',
            },
        });

        try {
            expect(resolveTenant()).toEqual({
                success: true,
                tenantId: 'north-store',
                source: 'subdomain',
            });
        } finally {
            vi.unstubAllGlobals();
        }
    });

    it('promotes the host candidate to canonical StoreConfig tenant authority', () => {
        setResolvedTenant('shopixy');

        setCanonicalTenant(TENANT_A);
        setSessionId(SESSION_A);

        expect(getRequestHeaders()).toMatchObject({
            'X-Tenant-Id': TENANT_A,
            'X-Session-Id': SESSION_A,
        });
        expect(() => setCanonicalTenant('shopixy')).toThrow(/canonical tenant ID/i);
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
                    'X-CSRF-Token': 'csrf-test-value',
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

    it('classifies active checkout shipping methods as available', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: TENANT_A,
                status: 'AVAILABLE',
                shippingMethods: [
                    {
                        tenantId: TENANT_A,
                        id: '55555555-5555-4555-8555-555555555555',
                        name: 'Standard shipping',
                        price: 20,
                        currency: 'USD',
                        estimatedDays: 3,
                    },
                ],
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('AVAILABLE');
        expect(useCheckoutStore.getState().shippingMethods).toHaveLength(1);
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('admits slug-resolved checkout shipping methods using server tenant authority', async () => {
        setResolvedTenant('shopixy-demo', { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: TENANT_A,
                status: 'AVAILABLE',
                shippingMethods: [
                    {
                        tenantId: TENANT_A,
                        id: '56565656-5656-4656-8656-565656565656',
                        name: 'Slug tenant shipping',
                        price: 25,
                        currency: 'USD',
                        estimatedDays: 4,
                    },
                ],
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(getRequestHeaders()['X-Tenant-Id']).toBe('shopixy-demo');
        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('AVAILABLE');
        expect(useCheckoutStore.getState().shippingMethods).toHaveLength(1);
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('admits subdomain-resolved slug checkout shipping methods using server tenant authority', async () => {
        setResolvedTenant('north-store', { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: TENANT_A,
                status: 'AVAILABLE',
                shippingMethods: [
                    {
                        tenantId: TENANT_A,
                        id: '57575757-5757-4757-8757-575757575757',
                        name: 'Subdomain tenant shipping',
                        price: 30,
                        currency: 'USD',
                        estimatedDays: 5,
                    },
                ],
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(getRequestHeaders()['X-Tenant-Id']).toBe('north-store');
        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('AVAILABLE');
        expect(useCheckoutStore.getState().shippingMethods).toHaveLength(1);
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('rejects foreign tenant evidence in checkout shipping methods', async () => {
        setResolvedTenant('shopixy-demo', { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: TENANT_A,
                status: 'AVAILABLE',
                shippingMethods: [
                    {
                        tenantId: TENANT_B,
                        id: '58585858-5858-4858-8858-585858585858',
                        name: 'Foreign tenant shipping',
                        price: 35,
                        currency: 'USD',
                        estimatedDays: 6,
                    },
                ],
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('ERROR');
        expect(useCheckoutStore.getState().shippingMethodsError).toMatch(/foreign tenant evidence|could not be loaded/i);
        expect(useCheckoutStore.getState().shippingMethods).toEqual([]);
    });

    it('rejects checkout shipping responses with missing tenant authority', async () => {
        setResolvedTenant('shopixy-demo', { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                status: 'EMPTY',
                shippingMethods: [],
                configuredCount: 0,
                activeCount: 0,
                applicableCount: 0,
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('ERROR');
        expect(useCheckoutStore.getState().shippingMethodsError).toMatch(/tenant scope|could not be loaded/i);
    });

    it('rejects checkout shipping responses with malformed tenant authority', async () => {
        setResolvedTenant('shopixy-demo', { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: 'shopixy-demo',
                status: 'EMPTY',
                shippingMethods: [],
                configuredCount: 0,
                activeCount: 0,
                applicableCount: 0,
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('ERROR');
        expect(useCheckoutStore.getState().shippingMethodsError).toMatch(/tenant scope|could not be loaded/i);
    });

    it('classifies zero checkout shipping methods as empty instead of load failed', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: TENANT_A,
                status: 'EMPTY',
                shippingMethods: [],
                configuredCount: 0,
                activeCount: 0,
                applicableCount: 0,
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('EMPTY');
        expect(useCheckoutStore.getState().shippingMethods).toEqual([]);
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('classifies non-applicable checkout shipping methods separately from empty configuration', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
                tenantId: TENANT_A,
                status: 'NOT_APPLICABLE',
                shippingMethods: [],
                configuredCount: 1,
                activeCount: 1,
                applicableCount: 0,
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('NOT_APPLICABLE');
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('classifies checkout shipping backend failures as unavailable', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createNetworkFailureAdapter();
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('UNAVAILABLE');
        expect(useCheckoutStore.getState().shippingMethodsError).toMatch(/temporarily unavailable/i);
    });

    it('classifies malformed checkout shipping responses as errors', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': { tenantId: TENANT_A, status: 'AVAILABLE', shippingMethods: [{ id: 'bad' }] },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('ERROR');
        expect(useCheckoutStore.getState().shippingMethodsError).toMatch(/missing tenantId|could not be loaded/i);
    });

    it('does not send shipping preview or surface terminal session errors before cart hydration', async () => {
        useCartStore.setState({ cart: null, isInitialized: false, isLoading: true });
        useCheckoutStore.setState({
            checkoutSessionId: null,
            checkoutSessionCartId: null,
            manualWalletUploadToken: null,
            shippingMethodsStatus: 'IDLE',
            shippingMethodsError: null,
        });
        const postSpy = vi.spyOn(httpClient, 'post');

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(postSpy).not.toHaveBeenCalled();
        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('IDLE');
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('creates checkout session after delayed cart hydration before previewing shipping methods', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        useCartStore.setState({ cart: null, isInitialized: false, isLoading: true });
        useCheckoutStore.setState({
            checkoutSessionId: null,
            checkoutSessionCartId: null,
            manualWalletUploadToken: null,
            shippingMethodsStatus: 'IDLE',
            shippingMethodsError: null,
        });
        const calls: Array<{ url: string; data: unknown }> = [];
        createStep2HShippingLifecycleAdapter(calls, SESSION_B);

        await useCheckoutStore.getState().fetchShippingMethods();
        useCartStore.setState({
            cart: { ...makeCartResponse(SESSION_A, TENANT_A), cartId: CART_B },
            isInitialized: true,
            isLoading: false,
        });
        await useCheckoutStore.getState().fetchShippingMethods();

        expect(calls.map(call => call.url)).toEqual([
            '/api/storefront/checkout-session',
            '/api/storefront/shipping-methods/preview',
            '/api/storefront/shipping-methods/select',
        ]);
        expect(calls[0].data).toMatchObject({ cartId: CART_B });
        expect(calls[1].data).toEqual({ checkoutSessionId: SESSION_B });
        expect(calls[1].data).not.toHaveProperty('warehouseId');
        expect(calls[1].data).not.toHaveProperty('cartId');
        expect(calls[1].data).not.toHaveProperty('tenantId');
        expect(useCheckoutStore.getState().checkoutSessionId).toBe(SESSION_B);
        expect(useCheckoutStore.getState().checkoutSessionCartId).toBe(CART_B);
        expect(useCheckoutStore.getState().warehouseResolutionId).toBe(WAREHOUSE_RESOLUTION_ID);
    });

    it('does not reuse a checkout session from a previous cart id', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        useCheckoutStore.setState({
            checkoutSessionId: SESSION_A,
            checkoutSessionCartId: CART_A,
            manualWalletUploadToken: 'upload-token:old-cart',
        });
        useCartStore.setState({
            cart: { ...makeCartResponse(SESSION_A, TENANT_A), cartId: CART_B },
            isInitialized: true,
            isLoading: false,
        });
        const calls: Array<{ url: string; data: unknown }> = [];
        createStep2HShippingLifecycleAdapter(calls, SESSION_B);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(calls.find(call => call.url === '/api/storefront/checkout-session')?.data)
            .toMatchObject({ cartId: CART_B });
        expect(calls.find(call => call.url === '/api/storefront/shipping-methods/preview')?.data)
            .toEqual({ checkoutSessionId: SESSION_B });
        expect(useCheckoutStore.getState().checkoutSessionId).toBe(SESSION_B);
        expect(useCheckoutStore.getState().checkoutSessionCartId).toBe(CART_B);
    });

    it('deduplicates concurrent checkout session initialization for the same cart', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        useCheckoutStore.setState({
            checkoutSessionId: null,
            checkoutSessionCartId: null,
            manualWalletUploadToken: null,
        });
        useCartStore.setState({
            cart: { ...makeCartResponse(SESSION_A, TENANT_A), cartId: CART_A },
            isInitialized: true,
            isLoading: false,
        });
        const calls: Array<{ url: string; data: unknown }> = [];
        createStep2HShippingLifecycleAdapter(calls, SESSION_B);

        await Promise.all([
            useCheckoutStore.getState().initCheckoutSession(),
            useCheckoutStore.getState().initCheckoutSession(),
        ]);

        expect(calls.filter(call => call.url === '/api/storefront/checkout-session')).toHaveLength(1);
        expect(useCheckoutStore.getState().checkoutSessionId).toBe(SESSION_B);
        expect(useCheckoutStore.getState().checkoutSessionCartId).toBe(CART_A);
    });

    it('keeps ShippingStep out of the automatic shipping preview lifecycle', () => {
        const source = readFileSync(new URL('./components/checkout/ShippingStep.tsx', import.meta.url), 'utf8');

        expect(source).not.toContain('Fetch shipping methods on mount');
        expect(source).not.toContain('useEffect(() => {\n        if (cartId');
        expect(source).not.toContain('void fetchShippingMethods();');
        expect(source).toContain('fetchShippingMethods({ force: true })');
    });

    it('deduplicates concurrent same-state shipping previews into one HTTP request', async () => {
        const { previewCalls, selectCalls } = createControlledShippingPostMock();

        const first = useCheckoutStore.getState().fetchShippingMethods();
        const second = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();

        expect(previewCalls).toHaveLength(1);
        expect(previewCalls[0].aborted).toBe(false);

        previewCalls[0].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('55555555-5555-4555-8555-555555555555'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));
        await Promise.all([first, second]);

        expect(previewCalls).toHaveLength(1);
        expect(previewCalls[0].aborted).toBe(false);
        expect(selectCalls).toHaveLength(1);
        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('AVAILABLE');
        expect(useCheckoutStore.getState().shippingMethods).toHaveLength(1);
        expect(useCheckoutStore.getState().warehouseResolutionId).toBe(WAREHOUSE_RESOLUTION_ID);
    });

    it('supersedes an in-flight shipping preview when cart state identity changes', async () => {
        const { previewCalls } = createControlledShippingPostMock();

        const first = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();
        expect(previewCalls).toHaveLength(1);

        useCartStore.setState({
            cart: {
                ...makeCartResponse(SESSION_A, TENANT_A),
                cartId: CART_A,
                lastUpdatedUtc: '2026-07-31T00:01:00.000Z',
            },
            isInitialized: true,
            isLoading: false,
        });

        const second = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();

        expect(previewCalls).toHaveLength(2);
        expect(previewCalls[0].aborted).toBe(true);
        expect(previewCalls[1].aborted).toBe(false);

        previewCalls[1].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('66666666-6666-4666-8666-666666666666', 'Updated shipping'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));

        await Promise.all([first, second]);

        expect(useCheckoutStore.getState().shippingMethods.map(method => method.id))
            .toEqual(['66666666-6666-4666-8666-666666666666']);
        expect(useCheckoutStore.getState().shippingMethodId).toBe('66666666-6666-4666-8666-666666666666');
    });

    it('prevents a late stale preview response from overwriting the latest request', async () => {
        const { previewCalls } = createControlledShippingPostMock({ rejectOnAbort: false });

        const first = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();

        useCartStore.setState({
            cart: {
                ...makeCartResponse(SESSION_A, TENANT_A),
                cartId: CART_A,
                lastUpdatedUtc: '2026-07-31T00:02:00.000Z',
            },
            isInitialized: true,
            isLoading: false,
        });

        const second = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();
        expect(previewCalls).toHaveLength(2);

        previewCalls[1].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('77777777-7777-4777-8777-777777777777', 'Latest shipping'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));
        await second;

        previewCalls[0].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('88888888-8888-4888-8888-888888888888', 'Stale shipping'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));
        await first;

        expect(useCheckoutStore.getState().shippingMethods.map(method => method.id))
            .toEqual(['77777777-7777-4777-8777-777777777777']);
        expect(useCheckoutStore.getState().shippingMethodId).toBe('77777777-7777-4777-8777-777777777777');
    });

    it('forces a fresh shipping preview for explicit retry on the same state', async () => {
        const { previewCalls } = createControlledShippingPostMock();

        const first = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();
        previewCalls[0].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('55555555-5555-4555-8555-555555555555'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));
        await first;

        const retry = useCheckoutStore.getState().fetchShippingMethods({ force: true });
        await flushMicrotasks();

        expect(previewCalls).toHaveLength(2);
        previewCalls[1].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('55555555-5555-4555-8555-555555555555'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));
        await retry;

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('AVAILABLE');
    });

    it('creates a new checkout session before preview when cart/session authority changes', async () => {
        useCheckoutStore.setState({
            checkoutSessionId: SESSION_A,
            checkoutSessionCartId: CART_A,
            manualWalletUploadToken: 'upload-token:old-cart',
        });
        useCartStore.setState({
            cart: { ...makeCartResponse(SESSION_A, TENANT_A), cartId: CART_B },
            isInitialized: true,
            isLoading: false,
        });
        const { previewCalls, sessionCalls } = createControlledShippingPostMock();

        const request = useCheckoutStore.getState().fetchShippingMethods();
        await flushMicrotasks();

        expect(sessionCalls).toHaveLength(1);
        expect(sessionCalls[0].data).toMatchObject({ cartId: CART_B });
        expect(previewCalls).toHaveLength(1);
        expect(previewCalls[0].data).toEqual({ checkoutSessionId: SESSION_B });

        previewCalls[0].deferred.resolve(makeAxiosResponse(
            makeShippingPreviewResponse('99999999-9999-4999-8999-999999999999'),
            { url: '/api/storefront/shipping-methods/preview' }
        ));
        await request;

        expect(useCheckoutStore.getState().checkoutSessionId).toBe(SESSION_B);
        expect(useCheckoutStore.getState().checkoutSessionCartId).toBe(CART_B);
    });

    it('uses forced shipping refresh during final checkout authoritative refresh', async () => {
        const originalCheckoutActions = {
            fetchShippingMethods: useCheckoutStore.getState().fetchShippingMethods,
            refreshPricingPreview: useCheckoutStore.getState().refreshPricingPreview,
            fetchCheckoutFields: useCheckoutStore.getState().fetchCheckoutFields,
        };
        const originalFetchCart = useCartStore.getState().fetchCart;
        const fetchShippingMethods = vi.fn(async () => {});

        try {
            useCheckoutStore.setState({
                fetchShippingMethods: fetchShippingMethods as any,
                refreshPricingPreview: vi.fn(async () => {}) as any,
                fetchCheckoutFields: vi.fn(async () => {}) as any,
                isSubmitting: false,
            });
            useCartStore.setState({
                fetchCart: vi.fn(async () => {}) as any,
                cartMergeInProgress: false,
                cart: { ...makeCartResponse(SESSION_A, TENANT_A), cartId: CART_A },
            });

            await useCheckoutStore.getState().submitCheckout();

            expect(fetchShippingMethods).toHaveBeenCalledWith({ force: true });
        } finally {
            useCheckoutStore.setState(originalCheckoutActions as any);
            useCartStore.setState({ fetchCart: originalFetchCart } as any);
        }
    });

    it('recognizes Axios cancellation without classifying it as network or retryable', () => {
        const canceled = makeCanceledError({ url: '/api/storefront/shipping-methods/preview' });
        const normalized = normalizeError(canceled as any);

        expect(isRequestCanceled(canceled)).toBe(true);
        expect(normalized.message).toBe('canceled');
        expect(normalized.isNetworkError).toBe(false);
        expect(normalized.retryable).toBe(false);
    });

    it('does not log intentional cancellation as a normal HTTP response error', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

        await expect(httpClient.request({
            url: '/api/storefront/shipping-methods/preview',
            method: 'post',
            adapter: async (config) => {
                throw makeCanceledError(config);
            },
        })).rejects.toMatchObject({ message: 'canceled' });

        expect(errorSpy).not.toHaveBeenCalledWith(
            '[HTTP] Response error:',
            expect.objectContaining({ message: 'canceled' })
        );
    });

    it('still logs and classifies real HTTP failures', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const failure = Object.assign(new Error('Server unavailable'), {
            response: {
                status: 503,
                data: { code: 'SERVICE_UNAVAILABLE', message: 'Service unavailable.' },
            },
            isAxiosError: true,
            toJSON: () => ({}),
        });

        await expect(httpClient.request({
            url: '/api/storefront/shipping-methods/preview',
            method: 'post',
            adapter: async () => {
                throw failure;
            },
        })).rejects.toMatchObject({ message: 'Server unavailable' });

        expect(errorSpy).toHaveBeenCalledWith(
            '[HTTP] Response error:',
            expect.objectContaining({
                status: 503,
                code: 'SERVICE_UNAVAILABLE',
                retryable: true,
            })
        );
    });

    it('surfaces real checkout session creation failures when cart context is valid', async () => {
        useCheckoutStore.setState({
            checkoutSessionId: null,
            checkoutSessionCartId: null,
            manualWalletUploadToken: null,
            shippingMethodsStatus: 'IDLE',
            shippingMethodsError: null,
        });
        useCartStore.setState({
            cart: { ...makeCartResponse(SESSION_A, TENANT_A), cartId: CART_A },
            isInitialized: true,
            isLoading: false,
        });
        vi.spyOn(httpClient, 'post').mockRejectedValue(Object.assign(new Error('Session service unavailable'), {
            response: {
                status: 503,
                data: {
                    code: 'SESSION_SERVICE_UNAVAILABLE',
                    message: 'Checkout sessions are temporarily unavailable.',
                },
            },
            isAxiosError: true,
            toJSON: () => ({}),
        }));

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).not.toBe('IDLE');
        expect(useCheckoutStore.getState().shippingMethodsError)
            .toMatch(/Checkout sessions are temporarily unavailable|Session service unavailable/i);
    });

    it('classifies active and empty checkout payment methods without collapsing empty into failure', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        let payload: unknown = {
            status: 'AVAILABLE',
            paymentMethods: [
                {
                    id: '66666666-6666-4666-8666-666666666666',
                    code: 'COD',
                    displayName: 'Cash on delivery',
                    supportsOnline: false,
                    supportsOffline: true,
                    fee: 0,
                    feeType: 'fixed',
                    sortOrder: 1,
                    isAvailable: true,
                    unavailableReasonCode: null,
                },
            ],
        };
        const adapter: AxiosAdapter = async (config) => ({
            data: payload,
            status: 200,
            statusText: 'OK',
            headers: {},
            config,
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchPaymentMethods();
        expect(useCheckoutStore.getState().paymentMethodsStatus).toBe('AVAILABLE');
        expect(useCheckoutStore.getState().paymentMethods).toHaveLength(1);

        payload = { status: 'EMPTY', paymentMethods: [], configuredCount: 0, enabledCount: 0, availableCount: 0 };
        useCheckoutStore.setState({ paymentMethods: [], selectedPaymentMethodCode: null, paymentMethod: null });

        await useCheckoutStore.getState().fetchPaymentMethods();
        expect(useCheckoutStore.getState().paymentMethodsStatus).toBe('EMPTY');
        expect(useCheckoutStore.getState().paymentMethodsError).toBeNull();
    });

    it('loads checkout field configuration and preserves server display order', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/checkout-fields': {
                status: 'AVAILABLE',
                fields: [customSelectField, requiredPhoneField],
                totalCount: 2,
                enabledCount: 2,
            },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchCheckoutFields();

        expect(
            useCheckoutStore.getState().checkoutFieldsStatus,
            useCheckoutStore.getState().checkoutFieldsError || undefined,
        ).toBe('AVAILABLE');
        expect(useCheckoutStore.getState().checkoutFields.map((field) => field.id)).toEqual([
            requiredPhoneField.id,
            customSelectField.id,
        ]);
    });

    it('treats empty checkout field configuration as compatibility mode', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/checkout-fields': { status: 'EMPTY', fields: [], totalCount: 0, enabledCount: 0 },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchCheckoutFields();

        expect(useCheckoutStore.getState().checkoutFieldsStatus).toBe('EMPTY');
        expect(useCheckoutStore.getState().checkoutFieldsError).toBeNull();
        expect(validateAddress({ fullName: '', phone: '', city: '', area: '', addressLine: '' }).fullName).toMatch(/required/i);
    });

    it('distinguishes configured-empty checkout fields from legacy compatibility mode', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/checkout-fields': { status: 'EMPTY', fields: [], totalCount: 10, enabledCount: 0 },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchCheckoutFields();

        expect(useCheckoutStore.getState().checkoutFieldsStatus).toBe('EMPTY');
        expect(useCheckoutStore.getState().checkoutFieldsTotalCount).toBe(10);
        expect(useCheckoutStore.getState().isStepValid('shipping')).toBe(false);
    });

    it('rejects contradictory checkout field response evidence', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/checkout-fields': { status: 'AVAILABLE', fields: [], totalCount: 0, enabledCount: 0 },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchCheckoutFields();

        expect(useCheckoutStore.getState().checkoutFieldsStatus).toBe('ERROR');
        expect(useCheckoutStore.getState().checkoutFields).toEqual([]);
    });

    it('applies system field required and optional validation to canonical address state', () => {
        const optionalEmail: CheckoutFieldDefinition = {
            ...requiredPhoneField,
            id: '99999999-9999-4999-8999-999999999999',
            fieldType: 'EMAIL',
            systemFieldKey: 'email',
            label: 'Email',
            isRequired: false,
            displayOrder: 11,
        };

        const errors = validateAddress(
            { fullName: '', phone: '', city: '', area: '', addressLine: '', email: '' },
            [requiredPhoneField, optionalEmail]
        );

        expect(errors.phone).toMatch(/mobile number is required/i);
        expect(errors.email).toBeUndefined();
    });

    it('matches backend phone digit-count and system length admission', () => {
        const constrainedPhone: CheckoutFieldDefinition = {
            ...requiredPhoneField,
            configuration: { schemaVersion: 1, minLength: 7, maxLength: 20 },
        };

        expect(validateAddress(
            { fullName: '', phone: '1234567', city: '', area: '', addressLine: '' },
            [constrainedPhone],
        ).phone).toBeUndefined();
        expect(validateAddress(
            { fullName: '', phone: '++++++++', city: '', area: '', addressLine: '' },
            [constrainedPhone],
        ).phone).toMatch(/valid/i);
        expect(validateAddress(
            { fullName: '', phone: '123456789012345678901', city: '', area: '', addressLine: '' },
            [constrainedPhone],
        ).phone).toBeDefined();
    });

    it('validates custom fields by field id and rejects invalid option values', () => {
        expect(validateCustomCheckoutFields([customSelectField], {})).toMatchObject({
            [customSelectField.id]: expect.stringMatching(/required/i),
        });

        expect(validateCustomCheckoutFields([customSelectField], { [customSelectField.id]: 'medium' })).toMatchObject({
            [customSelectField.id]: expect.stringMatching(/valid/i),
        });

        expect(validateCustomCheckoutFields([customSelectField], { [customSelectField.id]: 'large' })).toEqual({});
    });

    it('enforces backend absolute text bounds when no custom maximum is configured', () => {
        const textField: CheckoutFieldDefinition = {
            ...customSelectField,
            id: '77777777-7777-4777-8777-777777777777',
            fieldType: 'TEXT',
            isRequired: false,
            configuration: { schemaVersion: 1 },
        };

        expect(validateCustomCheckoutFields([textField], { [textField.id]: 'x'.repeat(1001) }))
            .toMatchObject({ [textField.id]: expect.stringMatching(/long/i) });
    });

    it('validates required upload checkout fields by attachment metadata', () => {
        expect(validateCustomCheckoutFields([customImageField], {})).toMatchObject({
            [customImageField.id]: expect.stringMatching(/required/i),
        });

        const attachmentValue = JSON.stringify([
            {
                attachmentId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
                fieldId: customImageField.id,
                fileName: 'safe.jpg',
                contentType: 'image/jpeg',
                sizeBytes: 1024,
                status: 'UPLOADED',
            },
        ]);

        expect(validateCustomCheckoutFields([customImageField], { [customImageField.id]: attachmentValue })).toEqual({});
    });

    it('keeps upload UI resource-specific and avoids base64 checkout submission', async () => {
        const source = await import('./components/checkout/ShippingStep');
        expect(source).toBeTruthy();
        const moduleText = readFileSync(new URL('./components/checkout/ShippingStep.tsx', import.meta.url), 'utf8');
        expect(moduleText).toContain("type=\"file\"");
        expect(moduleText).toContain('/api/storefront/checkout/attachments/upload-lease');
        expect(moduleText).toContain('/api/storefront/checkout/attachments/confirm');
        expect(moduleText).toContain('getCheckoutFieldLayoutClass');
        expect(moduleText).toContain("field.systemFieldKey === 'street_address'");
        expect(moduleText).toContain("field.fieldType === 'TEXTAREA'");
        expect(moduleText).not.toContain('base64');
    });
});
