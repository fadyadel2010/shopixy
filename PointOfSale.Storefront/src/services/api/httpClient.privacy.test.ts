import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../bootstrap/index', () => ({
    isRequestContextReady: () => true,
    getRequestHeaders: () => ({ 'X-Tenant-Id': 'tenant-qa', 'X-Session-Id': 'session-qa' }),
}));
vi.mock('@/bootstrap/affiliateRefTracker', () => ({ getAffiliateRefCode: () => null, getConfirmedAffiliateRefCode: () => null, acceptAffiliateRefVerdict: vi.fn() }));
vi.mock('@/i18n', () => ({ useLanguageStore: { getState: () => ({ language: 'en' }) } }));
vi.mock('@/storefront/preview/themePreviewMode', () => ({ getThemePreviewToken: () => null }));
vi.mock('./csrf', () => ({ appendCsrfHeader: (headers: Record<string, string>) => headers }));

import { httpClient, setAuthInterceptor } from './httpClient';

const secret = 'SYNTHETIC_PRIVATE_RECOVERY_CREDENTIAL';
const url = `/api/storefront/checkout-session/recover/${secret}?previewToken=${secret}`;
// Derive the adapter contract through the maintained transport boundary rather
// than importing Axios from a consumer/test and bypassing its import policy.
type Adapter = Extract<NonNullable<Parameters<typeof httpClient.request>[0]['adapter']>, (...args: never[]) => unknown>;
type RequestConfig = Parameters<Adapter>[0];
type Response = Awaited<ReturnType<Adapter>>;
const response = (config: RequestConfig, status = 200): Response => ({ config, status, statusText: 'QA', headers: {}, data: { ok: true } });
const failure = (config: RequestConfig, status: number, message: string) => Object.assign(new Error(message), {
    name: 'AxiosError', isAxiosError: true, code: 'ERR_BAD_RESPONSE', config,
    response: { ...response(config, status), data: { code: 'UNAVAILABLE', message } },
});

describe('HTTP diagnostic privacy at recovery/auth boundaries', () => {
    let log: ReturnType<typeof vi.spyOn>;
    let errorLog: ReturnType<typeof vi.spyOn>;
    beforeEach(() => {
        log = vi.spyOn(console, 'log').mockImplementation(() => {});
        errorLog = vi.spyOn(console, 'error').mockImplementation(() => {});
    });
    afterEach(() => vi.restoreAllMocks());

    it('does not log a protected request/response URL while preserving the actual request', async () => {
        const adapter = vi.fn(async (config: RequestConfig) => response(config));
        await httpClient.get(url, { adapter });
        expect(adapter.mock.calls[0][0].url).toBe(url);
        expect(log).toHaveBeenCalled();
        expect(JSON.stringify(log.mock.calls)).not.toContain(secret);
    });

    it('does not log transport configuration or provider text, but preserves normalized UI errors', async () => {
        const adapter = async (config: RequestConfig) => {
            throw failure(config, 503, secret);
        };
        await expect(httpClient.get(url, { adapter })).rejects.toMatchObject({ normalized: { status: 503, message: secret } });
        expect(errorLog).toHaveBeenCalled();
        expect(JSON.stringify([...log.mock.calls, ...errorLog.mock.calls])).not.toContain(secret);
    });

    it('does not reveal the protected URL when retrying after an accepted refresh', async () => {
        setAuthInterceptor({ refreshToken: vi.fn().mockResolvedValue('authenticated'), forceLogout: vi.fn() });
        let attempts = 0;
        const adapter = async (config: RequestConfig) => {
            if (++attempts === 1) throw failure(config, 401, 'Unauthorized');
            return response(config);
        };
        await httpClient.get(url, { adapter });
        expect(attempts).toBe(2);
        expect(JSON.stringify([...log.mock.calls, ...errorLog.mock.calls])).not.toContain(secret);
    });
});
