import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'

const postMock = vi.hoisted(() => vi.fn())
const getMock = vi.hoisted(() => vi.fn())
const putMock = vi.hoisted(() => vi.fn())
const requestUseMock = vi.hoisted(() => vi.fn())
const responseUseMock = vi.hoisted(() => vi.fn())
const instanceMock = vi.hoisted(() => vi.fn())

vi.mock('axios', () => ({
    default: {
        create: vi.fn(() => {
            return Object.assign(instanceMock, { post: postMock, get: getMock, put: putMock, interceptors: {
                request: { use: requestUseMock },
                response: { use: responseUseMock },
            } })
        }),
        post: postMock,
    },
}))

describe('affiliate frontend session authority', () => {
    beforeEach(() => {
        localStorage.clear()
        postMock.mockReset()
        getMock.mockReset()
        putMock.mockReset()
        instanceMock.mockReset()
    })

    it('does not cache registration response as authenticated affiliate authority', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        postMock.mockResolvedValueOnce({
            data: {
                affiliateAccountId: 'account-1',
                email: 'affiliate@example.com',
                displayName: 'Affiliate',
                referralCode: 'AFF123',
                emailVerified: false,
            },
        })

        await affiliateAuthApi.register({
            email: 'affiliate@example.com',
            password: 'Password1!',
            displayName: 'Affiliate',
        })

        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBeNull()
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.EMAIL)).toBeNull()
    })

    it('clears stale profile cache before storing a backend-proven login profile', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID, 'stale-account')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.REFERRAL_CODE, 'STALE')
        postMock.mockResolvedValueOnce({
            data: {
                affiliateAccountId: 'account-2',
                email: 'fresh@example.com',
                displayName: 'Fresh Affiliate',
            },
        })

        await affiliateAuthApi.login({
            email: 'fresh@example.com',
            password: 'Password1!',
        })

        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBe('account-2')
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.REFERRAL_CODE)).toBeNull()
    })

    it('preserves presentation cache when a restore fails without a terminal auth response', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID, 'stale-account')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.EMAIL, 'stale@example.com')
        getMock.mockRejectedValueOnce({ response: { status: 503 } })

        await expect(affiliateAuthApi.restoreSession()).rejects.toMatchObject({
            response: { status: 503 },
        })

        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBe('stale-account')
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.EMAIL)).toBe('stale@example.com')
    })

    it('restores portal actor authority with no-store caller-owned profile reads', async () => {
        const { affiliateAuthApi, AFFILIATE_AUTH_FAILURE_POLICY } = await import('./affiliateAuthApi')
        getMock.mockResolvedValueOnce({
            data: {
                affiliateAccountId: 'account-3',
                email: 'fresh@example.com',
                displayName: 'Fresh Affiliate',
            },
        })

        await affiliateAuthApi.restoreSession()

        expect(getMock).toHaveBeenCalledWith('/affiliate/profile', {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
            affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE,
        })
    })

    it('coalesces concurrent restore callers into one semantic profile request', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        let resolveProfile: (value: { data: { affiliateAccountId: string } }) => void = () => undefined
        getMock.mockReturnValueOnce(new Promise<{ data: { affiliateAccountId: string } }>((resolve) => {
            resolveProfile = resolve
        }))

        const first = affiliateAuthApi.restoreSession()
        const second = affiliateAuthApi.restoreSession()
        resolveProfile({ data: { affiliateAccountId: 'account-shared' } })

        await expect(Promise.all([first, second])).resolves.toHaveLength(2)
        expect(getMock).toHaveBeenCalledTimes(1)
    })

    it('exposes the complete public identity contract', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        postMock.mockResolvedValue({ data: { message: 'ok' } })

        await affiliateAuthApi.verifyEmail('verify-token')
        await affiliateAuthApi.resendVerification('affiliate@example.com')
        await affiliateAuthApi.forgotPassword('affiliate@example.com')
        await affiliateAuthApi.resetPassword('reset-token', 'Password1!')

        expect(postMock).toHaveBeenNthCalledWith(1, '/affiliate/auth/verify-email', { token: 'verify-token' })
        expect(postMock).toHaveBeenNthCalledWith(2, '/affiliate/auth/resend-verification', { email: 'affiliate@example.com' })
        expect(postMock).toHaveBeenNthCalledWith(3, '/affiliate/auth/forgot-password', { email: 'affiliate@example.com' })
        expect(postMock).toHaveBeenNthCalledWith(4, '/affiliate/auth/reset-password', { token: 'reset-token', newPassword: 'Password1!' })
    })

    it('accepts only local affiliate portal return URLs', async () => {
        const { resolveAffiliatePortalReturnUrl } = await import('./affiliateAuthApi')

        expect(resolveAffiliatePortalReturnUrl('/affiliate/portal?tab=discover&search=Phone')).toBe(
            '/affiliate/portal?tab=discover&search=Phone',
        )
        expect(resolveAffiliatePortalReturnUrl('https://attacker.test/affiliate/portal')).toBe('/affiliate/portal')
        expect(resolveAffiliatePortalReturnUrl('//attacker.test/affiliate/portal')).toBe('/affiliate/portal')
        expect(resolveAffiliatePortalReturnUrl('/affiliate/register')).toBe('/affiliate/portal')
    })
})

describe('affiliate interceptor auth-failure policy', () => {
    let navigations: string[]
    let setAffiliateLoginNavigator: (navigator: (path: string) => void) => void
    let defaultNavigator: (path: string) => void

    const getResponseRejectionHandler = () => {
        const registration = responseUseMock.mock.calls[0]
        expect(registration).toBeDefined()
        return registration[1] as (error: unknown) => Promise<unknown>
    }

    const makeAuthError = (url: string, extraConfig: Record<string, unknown> = {}) => ({
        config: { url, ...extraConfig },
        response: { status: 401 },
    })

    beforeAll(async () => {
        const module = await import('./affiliateAuthApi')
        setAffiliateLoginNavigator = module.setAffiliateLoginNavigator
        defaultNavigator = (path: string) => {
            window.location.href = path
        }
    })

    beforeEach(async () => {
        localStorage.clear()
        postMock.mockReset()
        getMock.mockReset()
        putMock.mockReset()
        instanceMock.mockReset()
        navigations = []

        const { setAffiliateLoginNavigator: setNavigator } = await import('./affiliateAuthApi')
        setNavigator((path: string) => {
            navigations.push(path)
        })
    })

    afterEach(() => {
        setAffiliateLoginNavigator(defaultNavigator)
    })

    it('attaches caller-owned passive policy to the restoreSession profile read', async () => {
        const { affiliateAuthApi, AFFILIATE_AUTH_FAILURE_POLICY } = await import('./affiliateAuthApi')
        getMock.mockRejectedValueOnce(new Error('session unavailable'))

        await affiliateAuthApi.restoreSession().catch(() => undefined)

        expect(getMock).toHaveBeenCalledWith(
            '/affiliate/profile',
            expect.objectContaining({
                affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE,
            }),
        )
    })

    it('replays the passive profile read once after a successful refresh without navigating', async () => {
        const { affiliateAuthApi, AFFILIATE_AUTH_FAILURE_POLICY } = await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()
        postMock.mockResolvedValueOnce({ data: { affiliateAccountId: 'account-refreshed' } })
        instanceMock.mockResolvedValueOnce({
            data: {
                affiliateAccountId: 'account-refreshed',
                email: 'fresh@example.com',
                displayName: 'Fresh Affiliate',
            },
        })

        const response = await onError(
            makeAuthError(
                '/affiliate/profile',
                { affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE },
            ),
        )

        expect(response).toMatchObject({ data: { affiliateAccountId: 'account-refreshed' } })
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBe('account-refreshed')
        expect(postMock).toHaveBeenCalledTimes(1)
        expect(instanceMock).toHaveBeenCalledWith(
            expect.objectContaining({ url: '/affiliate/profile' }),
        )
        expect(navigations).toEqual([])
    })

    it('clears presentation cache and rejects a failed passive profile restore without navigating', async () => {
        const { affiliateAuthApi, AFFILIATE_AUTH_FAILURE_POLICY } = await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID, 'stale-account')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.EMAIL, 'stale@example.com')
        postMock.mockRejectedValueOnce(new Error('refresh failed'))

        const failure = makeAuthError(
            '/affiliate/profile',
            { affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE },
        )

        await expect(onError(failure)).rejects.toBe(failure)

        expect(postMock).toHaveBeenCalledTimes(1)
        expect(navigations).toEqual([])
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBeNull()
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.EMAIL)).toBeNull()
    })

    it('navigates to affiliate login exactly once for concurrent terminal 401s on protected requests', async () => {
        await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()
        localStorage.setItem('affiliate_accountId', 'stale-account')
        postMock.mockRejectedValue(new Error('refresh failed'))

        const first = onError(makeAuthError('/affiliate/orders'))
        const second = onError(makeAuthError('/affiliate/payouts'))
        const outcomes = await Promise.allSettled([first, second])

        expect(outcomes.every((outcome) => outcome.status === 'rejected')).toBe(true)
        expect(navigations).toEqual(['/affiliate/login'])
        expect(localStorage.getItem('affiliate_accountId')).toBeNull()
    })

    it('settles a replayed passive profile 401 without another refresh or navigation', async () => {
        const { affiliateAuthApi, AFFILIATE_AUTH_FAILURE_POLICY } = await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID, 'stale-account')

        const replayFailure = makeAuthError(
            '/affiliate/profile',
            {
                _retry: true,
                affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE,
            },
        )

        await expect(onError(replayFailure)).rejects.toBe(replayFailure)

        expect(postMock).not.toHaveBeenCalled()
        expect(navigations).toEqual([])
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBeNull()
    })

    it('terminates a replayed protected 401 without another refresh', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID, 'revoked-account')

        const replayFailure = makeAuthError('/affiliate/orders', { _retry: true })

        await expect(onError(replayFailure)).rejects.toBe(replayFailure)

        expect(postMock).not.toHaveBeenCalled()
        expect(navigations).toEqual(['/affiliate/login'])
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBeNull()
    })

    it('never recursively intercepts affiliate auth endpoint failures', async () => {
        await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()

        const failure = makeAuthError('/affiliate/auth/login')

        await expect(onError(failure)).rejects.toBe(failure)

        expect(postMock).not.toHaveBeenCalled()
        expect(navigations).toEqual([])
    })

    it('does not suppress non-401 failures on caller-owned reads', async () => {
        const { AFFILIATE_AUTH_FAILURE_POLICY } = await import('./affiliateAuthApi')
        const onError = getResponseRejectionHandler()

        const failure = {
            config: {
                url: '/affiliate/profile',
                affiliateAuthFailurePolicy: AFFILIATE_AUTH_FAILURE_POLICY.CALLER_OWNED_PASSIVE,
            },
            response: { status: 500 },
        }

        await expect(onError(failure)).rejects.toBe(failure)

        expect(postMock).not.toHaveBeenCalled()
        expect(navigations).toEqual([])
    })
})
