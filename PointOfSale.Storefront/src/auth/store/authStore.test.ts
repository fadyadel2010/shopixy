import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    login: vi.fn(),
    register: vi.fn(),
    getMe: vi.fn(),
    refreshToken: vi.fn(),
    normalizeAuthError: vi.fn((error: unknown) => error),
    setCustomerId: vi.fn(),
    checkoutReset: vi.fn(),
    mergeCart: vi.fn().mockResolvedValue({ items: [], ownerType: 'customer' }),
    cartSetState: vi.fn(),
    fetchCart: vi.fn(),
    getProviders: vi.fn(),
    googleLogin: vi.fn(),
    facebookLogin: vi.fn(),
}));

vi.mock('../api/authApi', () => ({
    login: mocks.login,
    register: mocks.register,
    logout: vi.fn(),
    getMe: mocks.getMe,
    refreshToken: mocks.refreshToken,
    googleLogin: mocks.googleLogin,
    facebookLogin: mocks.facebookLogin,
    getProviders: mocks.getProviders,
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
        setState: mocks.cartSetState,
        getState: () => ({ fetchCart: mocks.fetchCart }),
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
    it('keeps registration unauthenticated and returns verification dispatch evidence', async () => {
        const response = { customer: loginCustomer, expiresAt: '2099-01-01T00:00:00Z', verificationEmailSent: false };
        mocks.register.mockResolvedValueOnce(response);
        expect(await useAuthStore.getState().register({ email: loginCustomer.email, displayName: 'QA', password: 'Synthetic1!' })).toEqual(response);
        expect(useAuthStore.getState().status).toBe('unauthenticated');
        expect(useAuthStore.getState().customer).toBeNull();
    });

    it('does not report a stale successful registration after a newer auth transition', async () => {
        const pending = deferred<AuthResponse>();
        mocks.register.mockReturnValueOnce(pending.promise);
        const registration = useAuthStore.getState().register({ email: loginCustomer.email, displayName: 'QA', password: 'Synthetic1!' });
        const rejected = expect(registration).rejects.toMatchObject({ name: 'AbortError' });
        useAuthStore.getState().setUnauthenticated();
        pending.resolve({ customer: loginCustomer, expiresAt: '2099-01-01T00:00:00Z' });
        await rejected;
        expect(useAuthStore.getState().error).toBeNull();
        expect(useAuthStore.getState().status).toBe('unauthenticated');
    });

    it('does not let an old registration failure overwrite a newer identity', async () => {
        const pending = deferred<AuthResponse>();
        mocks.register.mockReturnValueOnce(pending.promise);
        const registration = useAuthStore.getState().register({ email: loginCustomer.email, displayName: 'QA', password: 'Synthetic1!' });
        const rejected = expect(registration).rejects.toThrow('Synthetic old failure');
        mocks.login.mockResolvedValueOnce({ customer: originalCustomer, expiresAt: '2099-01-01T00:00:00Z' });
        await useAuthStore.getState().login({ email: originalCustomer.email, password: 'Synthetic1!' });
        pending.reject(new Error('Synthetic old failure'));
        await rejected;
        expect(useAuthStore.getState().customer?.id).toBe(originalCustomer.id);
        expect(useAuthStore.getState().error).toBeNull();
    });
    it('rejects a late old provider read after a newer authoritative read', async () => {
        const first = deferred<{ google: boolean; facebook: boolean }>();
        mocks.getProviders.mockReturnValueOnce(first.promise).mockResolvedValueOnce({ google: false, facebook: false });
        const old = useAuthStore.getState().loadProviders(); await useAuthStore.getState().loadProviders();
        first.resolve({ google: true, facebook: true }); await old;
        expect(useAuthStore.getState().availableProviders).toEqual({ google: false, facebook: false });
    });

    it('does not turn provider catalog failure into a successful disabled catalog', async () => {
        mocks.getProviders.mockRejectedValueOnce(new Error('unavailable'));
        await useAuthStore.getState().loadProviders();
        expect(useAuthStore.getState().providersError).toBe('PROVIDERS_UNAVAILABLE');
        mocks.getProviders.mockResolvedValueOnce({ google: false, facebook: false });
        await useAuthStore.getState().loadProviders();
        expect(useAuthStore.getState().providersError).toBeNull();
    });

    it('does not authenticate a cancelled social completion and forwards browser correlation state', async () => {
        const pending = deferred<AuthResponse>(); mocks.googleLogin.mockReturnValueOnce(pending.promise);
        const controller = new AbortController();
        const login = useAuthStore.getState().googleLogin('synthetic-credential', 'synthetic-state', controller.signal);
        controller.abort(); useAuthStore.getState().setUnauthenticated();
        pending.resolve({ customer: loginCustomer, expiresAt: new Date().toISOString() }); expect(await login).toBe(false);
        expect(mocks.googleLogin).toHaveBeenCalledWith('synthetic-credential', 'synthetic-state', controller.signal);
        expect(useAuthStore.getState().status).toBe('unauthenticated');
    });

    it('releases global loading when a social transport resolves after cancellation', async () => {
        const pending = deferred<AuthResponse>(); mocks.facebookLogin.mockReturnValueOnce(pending.promise);
        const controller = new AbortController();
        const login = useAuthStore.getState().facebookLogin('synthetic', 'state', controller.signal);
        controller.abort(); pending.resolve({ customer: loginCustomer, expiresAt: new Date().toISOString() }); expect(await login).toBe(false);
        expect(useAuthStore.getState().isLoading).toBe(false);
        expect(useAuthStore.getState().status).not.toBe('authenticated');
    });

    it('reports an accepted social completion separately from a stale or cancelled response', async () => {
        mocks.googleLogin.mockResolvedValueOnce({ customer: loginCustomer, expiresAt: new Date().toISOString() });
        expect(await useAuthStore.getState().googleLogin('synthetic', 'state')).toBe(true);
        expect(useAuthStore.getState().status).toBe('authenticated');
    });
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

    it('admits an accepted login merge instead of leaving the cart unconfirmed', async () => {
        useAuthStore.getState().setAuthenticated({ customer: loginCustomer, expiresAt: '2099-01-01T00:00:00Z' });
        await vi.waitFor(() => expect(mocks.cartSetState).toHaveBeenCalledWith(expect.objectContaining({
            requiresRefresh: false, isInitialized: true, error: null,
        })));
    });

    it('keeps the cart unconfirmed if both the login merge and its refresh fail', async () => {
        mocks.mergeCart.mockRejectedValueOnce(new Error('Controlled merge failure'));
        mocks.fetchCart.mockRejectedValueOnce(new Error('Controlled refresh failure'));
        useAuthStore.getState().setAuthenticated({ customer: loginCustomer, expiresAt: '2099-01-01T00:00:00Z' });
        await vi.waitFor(() => expect(mocks.cartSetState).toHaveBeenCalledWith(expect.objectContaining({
            requiresRefresh: true, error: expect.any(String),
        })));
        expect(mocks.cartSetState).not.toHaveBeenCalledWith(expect.objectContaining({ requiresRefresh: false }));
    });
});
