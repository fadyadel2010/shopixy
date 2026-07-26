import { beforeEach, describe, expect, it, vi } from 'vitest'

const postMock = vi.hoisted(() => vi.fn())
const getMock = vi.hoisted(() => vi.fn())
const putMock = vi.hoisted(() => vi.fn())
const requestUseMock = vi.hoisted(() => vi.fn())
const responseUseMock = vi.hoisted(() => vi.fn())

vi.mock('axios', () => ({
    default: {
        create: vi.fn(() => ({
            post: postMock,
            get: getMock,
            put: putMock,
            interceptors: {
                request: { use: requestUseMock },
                response: { use: responseUseMock },
            },
        })),
        post: postMock,
    },
}))

describe('affiliate frontend session authority', () => {
    beforeEach(() => {
        localStorage.clear()
        postMock.mockReset()
        getMock.mockReset()
        putMock.mockReset()
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

    it('purges stale local affiliate identity when backend session restore fails', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID, 'stale-account')
        localStorage.setItem(affiliateAuthApi.STORAGE_KEYS.EMAIL, 'stale@example.com')
        getMock.mockRejectedValueOnce({ response: { status: 401 } })

        await expect(affiliateAuthApi.restoreSession()).rejects.toMatchObject({
            response: { status: 401 },
        })

        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.ACCOUNT_ID)).toBeNull()
        expect(localStorage.getItem(affiliateAuthApi.STORAGE_KEYS.EMAIL)).toBeNull()
        expect(affiliateAuthApi.isAuthenticated()).toBe(false)
    })

    it('restores portal actor authority with no-store profile reads', async () => {
        const { affiliateAuthApi } = await import('./affiliateAuthApi')
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
        })
    })
})
