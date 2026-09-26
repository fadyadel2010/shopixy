import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    authStore: {
        checkAuth: vi.fn().mockResolvedValue(undefined),
        forceLogout: vi.fn(),
        loadProviders: vi.fn().mockResolvedValue(undefined),
        logout: vi.fn(),
        refreshAuth: vi.fn().mockResolvedValue('unauthenticated'),
        setUnauthenticated: vi.fn(),
    },
    setAuthInterceptor: vi.fn(),
}));

vi.mock('../store/authStore', () => ({
    useAuthStore: {
        getState: () => mocks.authStore,
    },
}));

vi.mock('@/services/api/httpClient', () => ({
    setAuthInterceptor: mocks.setAuthInterceptor,
}));

import { initializeAuth, resetAuthInit } from './authBootstrap';

describe('initializeAuth', () => {
    beforeEach(() => {
        resetAuthInit();
        vi.clearAllMocks();
    });

    it('uses neutral guest state without auth requests during a signed theme preview', async () => {
        vi.stubGlobal('window', { location: { search: '?themePreviewToken=signed-token' } });

        await initializeAuth();

        expect(mocks.authStore.setUnauthenticated).toHaveBeenCalledOnce();
        expect(mocks.authStore.checkAuth).not.toHaveBeenCalled();
        expect(mocks.authStore.loadProviders).not.toHaveBeenCalled();
        expect(mocks.setAuthInterceptor).not.toHaveBeenCalled();
    });

    it('initializes customer auth outside theme preview mode', async () => {
        vi.stubGlobal('window', { location: { search: '' } });

        await initializeAuth();

        expect(mocks.setAuthInterceptor).toHaveBeenCalledOnce();
        expect(mocks.authStore.checkAuth).toHaveBeenCalledOnce();
        expect(mocks.authStore.loadProviders).toHaveBeenCalledOnce();
        expect(mocks.authStore.setUnauthenticated).not.toHaveBeenCalled();
    });
});
