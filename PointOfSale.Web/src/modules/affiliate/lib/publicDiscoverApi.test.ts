import { beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.hoisted(() => vi.fn())

vi.mock('axios', () => ({
    default: {
        get: getMock,
    },
}))

describe('AF-39 anonymous public discovery clients', () => {
    beforeEach(() => {
        getMock.mockReset()
        getMock.mockResolvedValue({ data: {} })
    })

    it('targets the anonymous public-products route with public-safe defaults and no auth material', async () => {
        const { discoverPublicProducts } = await import('./publicDiscoverApi')

        await discoverPublicProducts({ page: 2, pageSize: 20, categorySlug: 'electronics', search: 'nokia' })

        expect(getMock).toHaveBeenCalledTimes(1)
        const [url, config] = getMock.mock.calls[0]

        expect(url).toBe('/api/affiliate/discover/public-products')
        expect(config.params).toEqual({
            page: 2,
            pageSize: 20,
            categorySlug: 'electronics',
            search: 'nokia',
            sort: 'newest',
        })
        // The bare axios package carries no interceptors; the config itself must never grow an Authorization header.
        expect(config).not.toHaveProperty('headers')
        expect(config.signal).toBeUndefined()
    })

    it('never sends commission ordering through the anonymous boundary', async () => {
        const { discoverPublicProducts, PUBLIC_SORT_OPTIONS, DEFAULT_PUBLIC_SORT, isValidPublicSort } =
            await import('./publicDiscoverApi')

        const optionValues = PUBLIC_SORT_OPTIONS.map(o => o.value)
        expect(optionValues).not.toContain('commission_desc')
        expect(optionValues).not.toContain('commission_asc')
        expect(optionValues).not.toContain('ranking')

        expect(isValidPublicSort('commission_desc')).toBe(false)
        expect(isValidPublicSort('price_asc')).toBe(true)

        // A tampered or legacy commission sort value collapses to the anonymous default.
        await discoverPublicProducts({ sort: 'commission_desc' })

        expect(getMock.mock.calls[0][1].params.sort).toBe(DEFAULT_PUBLIC_SORT)
        expect(DEFAULT_PUBLIC_SORT).toBe('newest')
    })

    it('resolves the canonical anonymous detail route with an encoded slug', async () => {
        const { discoverPublicProductDetail } = await import('./publicDiscoverApi')

        await discoverPublicProductDetail('11111111-1111-1111-1111-111111111111', { signal: 'signal-token' })

        const [url, config] = getMock.mock.calls[0]
        expect(url).toBe('/api/affiliate/discover/public-products/11111111-1111-1111-1111-111111111111')
        expect(config).toEqual({ signal: 'signal-token', timeout: 15_000 })
    })

    it('propagates a live 401 contract without retrying or attaching an Affiliate Authorization header', async () => {
        const { discoverPublicProducts, discoverPublicProductDetail } = await import('./publicDiscoverApi')

        const unauthorized = Object.assign(new Error('Request failed with status code 401'), {
            response: { status: 401 },
        })

        getMock.mockRejectedValue(unauthorized)

        await expect(discoverPublicProducts({ page: 1 })).rejects.toMatchObject({ response: { status: 401 } })
        await expect(discoverPublicProductDetail('11111111-1111-1111-1111-111111111111')).rejects.toMatchObject({ response: { status: 401 } })

        // One attempt per call: the client never retries with credentials against the anonymous route.
        expect(getMock).toHaveBeenCalledTimes(2)
        for (const [, config] of getMock.mock.calls) {
            expect(config?.headers?.Authorization).toBeUndefined()
        }
    })
})
