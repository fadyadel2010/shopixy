import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())

vi.mock('./api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
    },
}))

describe('affiliate configuration admin read API authority', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiGetMock.mockResolvedValue({ data: {} })
        apiPostMock.mockResolvedValue({ data: {} })
    })

    it('sends no-store headers on affiliate configuration read endpoints', async () => {
        const { default: adminAffiliateApi, AFFILIATE_ADMIN_READ_HEADERS } = await import('./adminAffiliateApi')

        await adminAffiliateApi.listAffiliates(1, 20, 'Active', 'ref')
        await adminAffiliateApi.getAffiliateDetail('affiliate-1')
        await adminAffiliateApi.getAffiliateOrders('affiliate-1', 2, 20, 'Earned')
        await adminAffiliateApi.getAffiliateEarnings('affiliate-1')
        await adminAffiliateApi.getAffiliateWithdrawals('affiliate-1', 'Pending', 1, 20)
        await adminAffiliateApi.listPayoutRequests('Approved', 1, 20)

        for (const call of apiGetMock.mock.calls) {
            expect(call[1]?.headers).toMatchObject(AFFILIATE_ADMIN_READ_HEADERS)
        }
    })

    it('sends no-store headers on product affiliate configuration reads', async () => {
        const { default: adminProductAffiliateApi } = await import('./adminProductAffiliateApi')

        await adminProductAffiliateApi.getSettings('product-1')
        await adminProductAffiliateApi.getCategoryTree()

        expect(apiGetMock).toHaveBeenCalledWith(
            '/admin/products/product-1/affiliate',
            expect.objectContaining({
                headers: expect.objectContaining({ 'Cache-Control': 'no-store' }),
            }),
        )
        expect(apiGetMock).toHaveBeenCalledWith(
            '/admin/affiliate/categories/tree',
            expect.objectContaining({
                headers: expect.objectContaining({ Pragma: 'no-cache' }),
            }),
        )
    })

    it('serializes product affiliate mutations as candidate intent without authority fields', async () => {
        const { default: adminProductAffiliateApi } = await import('./adminProductAffiliateApi')

        await adminProductAffiliateApi.enableWithCategory('product-1', 0.125, 'category-a', null)
        await adminProductAffiliateApi.disable('product-1')

        expect(apiPostMock).toHaveBeenCalledWith(
            '/admin/products/product-1/affiliate/enable-with-category',
            {
                commissionRate: 0.125,
                categoryId: 'category-a',
                maxCommissionPerItem: null,
            },
        )
        expect(apiPostMock.mock.calls[0][1]).not.toHaveProperty('tenantId')
        expect(apiPostMock.mock.calls[0][1]).not.toHaveProperty('storeId')
        expect(apiPostMock.mock.calls[0][1]).not.toHaveProperty('ownerId')
        expect(apiPostMock.mock.calls[0][1]).not.toHaveProperty('createdBy')
        expect(apiPostMock.mock.calls[0][1]).not.toHaveProperty('updatedBy')
        expect(apiPostMock.mock.calls[0][1]).not.toHaveProperty('version')
        expect(apiPostMock).toHaveBeenCalledWith('/admin/products/product-1/affiliate/disable')
    })

    it('serializes payout approval and rejection as request-id notes only', async () => {
        const { default: adminAffiliateApi } = await import('./adminAffiliateApi')

        await adminAffiliateApi.approvePayoutRequest('request-1', 'approved after review')
        await adminAffiliateApi.rejectPayoutRequest('request-1', 'wallet changed')

        expect(apiPostMock).toHaveBeenCalledWith(
            '/admin/affiliates/payouts/request-1/approve',
            { adminNotes: 'approved after review' },
        )
        expect(apiPostMock).toHaveBeenCalledWith(
            '/admin/affiliates/payouts/request-1/reject',
            { adminNotes: 'wallet changed' },
        )

        for (const [, payload] of apiPostMock.mock.calls.slice(-2)) {
            expect(payload).not.toHaveProperty('amount')
            expect(payload).not.toHaveProperty('approvedAmount')
            expect(payload).not.toHaveProperty('wallet')
            expect(payload).not.toHaveProperty('walletNumber')
            expect(payload).not.toHaveProperty('affiliateAccountId')
            expect(payload).not.toHaveProperty('storeTenantId')
            expect(payload).not.toHaveProperty('currency')
            expect(payload).not.toHaveProperty('status')
            expect(payload).not.toHaveProperty('approvedBy')
            expect(payload).not.toHaveProperty('rejectedBy')
        }
    })

    it('serializes payout payment execution as request-id proof only', async () => {
        const { default: adminAffiliateApi } = await import('./adminAffiliateApi')

        await adminAffiliateApi.markPayoutPaid('request-1', 'BANK-REF-123', 'transfer completed')

        expect(apiPostMock).toHaveBeenCalledWith(
            '/admin/affiliates/payouts/request-1/mark-paid',
            { paymentReference: 'BANK-REF-123', adminNotes: 'transfer completed' },
        )

        const payload = apiPostMock.mock.calls[apiPostMock.mock.calls.length - 1]?.[1]
        expect(payload).not.toHaveProperty('amount')
        expect(payload).not.toHaveProperty('approvedAmount')
        expect(payload).not.toHaveProperty('requestedAmount')
        expect(payload).not.toHaveProperty('wallet')
        expect(payload).not.toHaveProperty('walletNumber')
        expect(payload).not.toHaveProperty('affiliateAccountId')
        expect(payload).not.toHaveProperty('storeTenantId')
        expect(payload).not.toHaveProperty('currency')
        expect(payload).not.toHaveProperty('status')
        expect(payload).not.toHaveProperty('paid')
        expect(payload).not.toHaveProperty('paidAtUtc')
        expect(payload).not.toHaveProperty('paidBy')
        expect(payload).not.toHaveProperty('settlementId')
        expect(payload).not.toHaveProperty('ledgerId')
    })
})
