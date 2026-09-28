import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    login: vi.fn(),
    getMe: vi.fn(),
    refreshToken: vi.fn(),
    normalizeAuthError: vi.fn((error: unknown) => error),
    setCustomerId: vi.fn(),
    checkoutReset: vi.fn(),
    mergeCart: vi.fn().mockResolvedValue({ items: [], ownerType: 'customer' }),
}));

vi.mock('../api/authApi', () => ({
    login: mocks.login,
    register: vi.fn(),
    logout: vi.fn(),
    getMe: mocks.getMe,
    refreshToken: mocks.refreshToken,
    googleLogin: vi.fn(),
    facebookLogin: vi.fn(),
    getProviders: vi.fn().mockResolvedValue({ google: false, facebook: false }),
    normalizeAuthError: mocks.normalizeAuthError,
}));

vi.mock('@/bootstrap', () => ({
    clearSession: vi.fn(),
    setCustomerId: mocks.setCustomerId,
}));

vi.mock('@/storefront/preview/themePreviewMode', () => ({
    isThemePreviewMode: () => false,
}));

vi.mock('@/features/auth/authTelemetry', () => ({
    emitAuthDegradedRecovery: vi.fn(),
    emitAuthStateDegraded: vi.fn(),
    emitCheckoutStateLeakPrevented: vi.fn(),
}));

vi.mock('@/store/checkoutStore', () => ({
    useCheckoutStore: { getState: () => ({ reset: mocks.checkoutReset }) },
}));

vi.mock('@/store/cartStore', () => ({
    useCartStore: {
        setState: vi.fn(),
        getState: () => ({ fetchCart: vi.fn() }),
    },
}));

vi.mock('@/services/api/storefrontApi', () => ({
    mergeCart: mocks.mergeCart,
}));

import { useAuthStore } from './authStore';
import type { AuthResponse, OnlineCustomer } from '../types';

const originalCustomer: OnlineCustomer = {
    id: '11111111-1111-4111-8111-111111111111',
    email: 'original@example.test',
    displayName: 'Original',
    provider: 'Local',
    emailVerified: true,
    createdAtUtc: '2026-09-28T00:00:00Z',
};

const loginCustomer: OnlineCustomer = {
    ...originalCustomer,
    id: '22222222-2222-4222-8222-222222222222',
    email: 'login@example.test',
    displayName: 'Login',
};

function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((accept, decline) => {
        resolve = accept;
        reject = decline;
    });
    return { promise, resolve, reject };
}

describe('storefront auth operation ordering', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        useAuthStore.setState({
            status: 'unknown',
            customer: null,
            isLoading: false,
            error: null,
            availableProviders: null,
        });
    });

    it('does not let an older bootstrap response overwrite a newer login', async () => {
        const bootstrap = deferred<OnlineCustomer>();
        mocks.getMe.mockReturnValueOnce(bootstrap.promise);
        mocks.login.mockResolvedValueOnce({ customer: loginCustomer, expiresAt: '2026-09-28T01:00:00Z' } satisfies AuthResponse);

        const bootstrapPromise = useAuthStore.getState().checkAuth();
        await useAuthStore.getState().login({ email: loginCustomer.email, password: 'not-recorded' });
        bootstrap.resolve(originalCustomer);
        await bootstrapPromise;

        expect(useAuthStore.getState().status).toBe('authenticated');
        expect(useAuthStore.getState().customer?.id).toBe(loginCustomer.id);
        expect(mocks.setCustomerId).toHaveBeenCalledWith(loginCustomer.id);
        expect(mocks.setCustomerId).not.toHaveBeenCalledWith(originalCustomer.id);
    });

    it('does not let an older failed bootstrap clear a newer login', async () => {
        const bootstrap = deferred<OnlineCustomer>();
        mocks.getMe.mockReturnValueOnce(bootstrap.promise);
        mocks.login.mockResolvedValueOnce({ customer: loginCustomer, expiresAt: '2026-09-28T01:00:00Z' } satisfies AuthResponse);

        const bootstrapPromise = useAuthStore.getState().checkAuth();
        await useAuthStore.getState().login({ email: loginCustomer.email, password: 'not-recorded' });
        bootstrap.reject(new Error('Unauthorized'));
        await bootstrapPromise;

        expect(useAuthStore.getState().status).toBe('authenticated');
        expect(useAuthStore.getState().customer?.id).toBe(loginCustomer.id);
    });
});
