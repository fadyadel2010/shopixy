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
    setResolvedTenant,
    setSessionId,
} from '@/bootstrap';
import { sendAnalyticsEvent } from '@/analytics/AnalyticsClient';
import { AnalyticsEventType, type AnalyticsEvent } from '@/analytics/eventTypes';
import { httpClient, isAuthEndpoint } from '@/services/api/httpClient';
import { useCheckoutStore, validateAddress, validateCustomCheckoutFields } from '@/store/checkoutStore';
import type { CheckoutFieldDefinition } from '@/types/checkout';
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

function createCheckoutDependencyAdapter(dataByUrl: Record<string, unknown>): AxiosAdapter {
    return async (config) => {
        const response: AxiosResponse<unknown> = {
            data: dataByUrl[String(config.url)],
            status: 200,
            statusText: 'OK',
            headers: {},
            config,
        };

        return response;
    };
}

function createNetworkFailureAdapter(): AxiosAdapter {
    return async (config) => {
        throw Object.assign(new Error('Network Error'), {
            config,
            isAxiosError: true,
            toJSON: () => ({}),
        });
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
        shippingMethods: [],
        shippingMethodsStatus: 'IDLE',
        shippingMethodsError: null,
        paymentMethods: [],
        paymentMethodsStatus: 'IDLE',
        paymentMethodsError: null,
        selectedPaymentMethodCode: null,
        paymentMethod: null,
        paymentSelectionStatus: 'none',
        stalePaymentSelection: null,
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

    it('classifies active checkout shipping methods as available', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
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

    it('classifies zero checkout shipping methods as empty instead of load failed', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
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

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('EMPTY');
        expect(useCheckoutStore.getState().shippingMethods).toEqual([]);
        expect(useCheckoutStore.getState().shippingMethodsError).toBeNull();
    });

    it('classifies non-applicable checkout shipping methods separately from empty configuration', async () => {
        setResolvedTenant(TENANT_A, { overwrite: true });
        setSessionId(SESSION_A);
        const adapter = createCheckoutDependencyAdapter({
            '/api/storefront/shipping-methods': {
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
            '/api/storefront/shipping-methods': { status: 'AVAILABLE', shippingMethods: [{ id: 'bad' }] },
        });
        vi.spyOn(httpClient, 'get').mockImplementation((url, config) => httpClient.request({ ...config, url, method: 'get', adapter }) as any);

        await useCheckoutStore.getState().fetchShippingMethods();

        expect(useCheckoutStore.getState().shippingMethodsStatus).toBe('ERROR');
        expect(useCheckoutStore.getState().shippingMethodsError).toMatch(/missing tenantId|could not be loaded/i);
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
