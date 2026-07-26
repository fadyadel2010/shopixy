import { beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.hoisted(() => vi.fn())
const postMock = vi.hoisted(() => vi.fn())
const deleteMock = vi.hoisted(() => vi.fn())

vi.mock('./affiliateAuthApi', () => ({
    affiliateAxios: {
        get: getMock,
        post: postMock,
        delete: deleteMock,
    },
}))

describe('affiliate portal read authority', () => {
    beforeEach(() => {
        getMock.mockReset()
        postMock.mockReset()
        deleteMock.mockReset()
    })

    it('uses no-store hints for profile and dashboard actor reads', async () => {
        const { affiliateApi } = await import('./affiliateApi')
        getMock.mockResolvedValue({ data: {} })

        await affiliateApi.getProfile()
        await affiliateApi.getDashboard()
        await affiliateApi.getAnalyticsSummary()

        const expectedConfig = {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        }

        expect(getMock).toHaveBeenCalledWith('/affiliate/profile', expectedConfig)
        expect(getMock).toHaveBeenCalledWith('/affiliate/dashboard', expectedConfig)
        expect(getMock).toHaveBeenCalledWith('/affiliate/analytics/summary', {
            ...expectedConfig,
            params: { range: 30 },
        })
    })

    it('uses no-store hints for promotion link generation and reads', async () => {
        const { affiliateApi } = await import('./affiliateApi')
        getMock.mockResolvedValue({ data: [] })
        postMock.mockResolvedValue({ data: {} })
        deleteMock.mockResolvedValue({ data: {} })

        await affiliateApi.createPromotionLink('tenant-1', 'product-1')
        await affiliateApi.listPromotionLinks()
        await affiliateApi.deactivatePromotionLink('link-1')

        const expectedConfig = {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        }

        expect(postMock).toHaveBeenCalledWith(
            '/affiliate/promotion-links',
            { tenantId: 'tenant-1', productId: 'product-1' },
            expectedConfig,
        )
        expect(getMock).toHaveBeenCalledWith('/affiliate/promotion-links', expectedConfig)
        expect(deleteMock).toHaveBeenCalledWith('/affiliate/promotion-links/link-1', expectedConfig)
    })

    it('keeps withdrawal requests backend-bound and sends no financial authority fields', async () => {
        const { affiliateApi } = await import('./affiliateApi')
        getMock.mockResolvedValue({ data: {} })
        postMock.mockResolvedValue({ data: {} })

        await affiliateApi.getPayouts(2, 10)
        await affiliateApi.requestPayout(125)

        const expectedConfig = {
            headers: {
                'Cache-Control': 'no-store',
                Pragma: 'no-cache',
            },
        }

        expect(getMock).toHaveBeenCalledWith('/affiliate/payouts', {
            ...expectedConfig,
            params: { page: 2, pageSize: 10 },
        })
        expect(postMock).toHaveBeenCalledWith(
            '/affiliate/payouts/request',
            { amount: 125 },
            expectedConfig,
        )
    })
})
