import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiGetMock = vi.hoisted(() => vi.fn())

vi.mock('../services/api', () => ({
    default: {
        get: apiGetMock,
    },
}))

function createDeferred<T = unknown>() {
    let resolve!: (value: T) => void
    let reject!: (reason?: unknown) => void

    const promise = new Promise<T>((res, rej) => {
        resolve = res
        reject = rej
    })

    return { promise, resolve, reject }
}

describe('affiliate readiness read authority', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
    })

    it('uses no-store headers when reading backend readiness authority', async () => {
        apiGetMock.mockResolvedValueOnce({
            data: {
                isReady: true,
                storefrontLive: true,
                hasPaymentMethod: true,
                hasShippingMethod: true,
                missingRequirements: [],
            },
        })

        const { useAffiliateReadiness } = await import('./useAffiliateReadiness')
        const { result } = renderHook(() => useAffiliateReadiness())

        await waitFor(() => expect(result.current.loading).toBe(false))

        expect(apiGetMock).toHaveBeenCalledWith(
            '/admin/affiliate/readiness',
            expect.objectContaining({
                headers: expect.objectContaining({
                    'Cache-Control': 'no-store',
                    Pragma: 'no-cache',
                }),
            }),
        )
        expect(result.current.isReady).toBe(true)
    })

    it('does not let a stale readiness response overwrite a newer backend read', async () => {
        const firstRead = createDeferred<{ data: { isReady: boolean } }>()
        const secondRead = createDeferred<{ data: { isReady: boolean } }>()

        apiGetMock
            .mockReturnValueOnce(firstRead.promise)
            .mockReturnValueOnce(secondRead.promise)

        const { useAffiliateReadiness } = await import('./useAffiliateReadiness')
        const { result } = renderHook(() => useAffiliateReadiness())

        await act(async () => {
            void result.current.refetch({ showLoading: false })
            secondRead.resolve({ data: { isReady: true } })
            await Promise.resolve()
        })

        await waitFor(() => expect(result.current.isReady).toBe(true))

        await act(async () => {
            firstRead.resolve({ data: { isReady: false } })
            await Promise.resolve()
        })

        expect(result.current.isReady).toBe(true)
    })
})
