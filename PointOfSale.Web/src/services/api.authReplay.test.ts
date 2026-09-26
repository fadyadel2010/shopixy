import axios from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import api, { canReplayAfterAuthRefresh, requestPosSessionRefresh } from './api'
import { useAuthStore } from '../store/authStore'

const authenticatedUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
    username: 'cashier',
    firstName: 'POS',
    lastName: 'User',
    role: 'Cashier',
    effectivePermissions: ['dashboard.view'],
    navigationCapabilities: [],
    vendorLifecycleAuthority: {
        vendorId: '22222222-2222-4222-8222-222222222222',
        status: 'Active',
        debtStatus: 'Current',
        tenantActive: true,
        marketplaceApproved: true,
        kycVerified: true,
        isDebtBlocked: false,
        blocksCatalog: false,
        blocksOrders: false,
        blocksStorefront: false,
        blocksAnalytics: false,
        blocksPayments: false,
        blocksPayouts: false,
        blocksLogin: false,
    },
}

const unauthorized = () => Promise.reject({ response: { status: 401 } })

describe('API authentication refresh replay policy', () => {
    beforeEach(() => {
        useAuthStore.setState({ user: null, isAuthenticated: false, hasHydrated: true })
    })

    afterEach(() => {
        vi.restoreAllMocks()
    })

    it('replays only idempotent reads after refresh', () => {
        expect(canReplayAfterAuthRefresh({ method: 'get' })).toBe(true)
        expect(canReplayAfterAuthRefresh({ method: 'HEAD' })).toBe(true)
        expect(canReplayAfterAuthRefresh({ method: 'post' })).toBe(false)
        expect(canReplayAfterAuthRefresh({ method: 'put' })).toBe(false)
        expect(canReplayAfterAuthRefresh({ method: 'patch' })).toBe(false)
        expect(canReplayAfterAuthRefresh({ method: 'delete' })).toBe(false)
    })

    it('single-flights concurrent refresh attempts and returns one authoritative session', async () => {
        const get = vi.spyOn(axios, 'get')
            .mockImplementationOnce(unauthorized)
            .mockResolvedValueOnce({ data: { user: authenticatedUser } })
        const post = vi.spyOn(axios, 'post').mockResolvedValue({ data: {} })

        const [first, second] = await Promise.all([
            requestPosSessionRefresh(),
            requestPosSessionRefresh(),
        ])

        expect(first).toMatchObject(authenticatedUser)
        expect(second).toMatchObject(authenticatedUser)
        expect(post).toHaveBeenCalledOnce()
        expect(get).toHaveBeenCalledTimes(2)
    })

    it('refreshes and retries one idempotent request after a 401', async () => {
        vi.spyOn(axios, 'get')
            .mockImplementationOnce(unauthorized)
            .mockResolvedValueOnce({ data: { user: authenticatedUser } })
        vi.spyOn(axios, 'post').mockResolvedValue({ data: {} })
        const adapter = vi.fn(async (config) => ({
            data: { ok: true },
            status: 200,
            statusText: 'OK',
            headers: {},
            config,
        }))
        const responseHandlers = (api.interceptors.response as unknown as {
            handlers: Array<{ rejected: (error: unknown) => Promise<unknown> }>
        }).handlers
        const rejectResponse = responseHandlers.at(-1)?.rejected

        const response = await rejectResponse?.({
            config: {
                url: '/orders',
                method: 'get',
                headers: {},
                adapter,
            },
            response: { status: 401 },
        }) as { data: { ok: boolean } }

        expect(response.data.ok).toBe(true)
        expect(adapter).toHaveBeenCalledOnce()
        expect(useAuthStore.getState().isAuthenticated).toBe(true)
    })

    it('does not erase a proven session when refresh fails because the network is unavailable', async () => {
        useAuthStore.getState().login(authenticatedUser)
        vi.spyOn(axios, 'get').mockImplementationOnce(unauthorized)
        vi.spyOn(axios, 'post').mockRejectedValue(Object.assign(new Error('offline'), { code: 'ERR_NETWORK' }))
        const responseHandlers = (api.interceptors.response as unknown as {
            handlers: Array<{ rejected: (error: unknown) => Promise<unknown> }>
        }).handlers
        const rejectResponse = responseHandlers.at(-1)?.rejected

        await expect(rejectResponse?.({
            config: { url: '/orders', method: 'get', headers: {} },
            response: { status: 401 },
        })).rejects.toMatchObject({ code: 'ERR_NETWORK' })

        expect(useAuthStore.getState().isAuthenticated).toBe(true)
    })
})
