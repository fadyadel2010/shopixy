import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiGetMock = vi.hoisted(() => vi.fn())
const apiPostMock = vi.hoisted(() => vi.fn())

vi.mock('./api', () => ({
    default: {
        get: apiGetMock,
        post: apiPostMock,
    },
}))

describe('CommerceHub vendor finance API routes', () => {
    beforeEach(() => {
        apiGetMock.mockReset()
        apiPostMock.mockReset()
        apiGetMock.mockResolvedValue({ data: {} })
        apiPostMock.mockResolvedValue({ data: {} })
    })

    it('uses the canonical finance dashboard route under the shared /api base URL', async () => {
        const { getCommerceHubFinanceDashboard } = await import('./commerceHubFinanceApi')

        await getCommerceHubFinanceDashboard()

        expect(apiGetMock).toHaveBeenCalledWith('/admin/commercehub/finance/dashboard')
    })

    it('does not duplicate the shared /api prefix for earnings and payout methods', async () => {
        const {
            createVendorPayoutMethod,
            getCommerceHubEarningsDashboard,
            getVendorPayoutMethods,
        } = await import('./commerceHubEarningsApi')
        const payload = {
            methodType: 'BankTransfer',
            destinationValue: 'masked-destination',
            isDefault: true,
        }

        await getCommerceHubEarningsDashboard()
        await getVendorPayoutMethods()
        await createVendorPayoutMethod(payload)

        expect(apiGetMock).toHaveBeenCalledWith('/commercehub/earnings')
        expect(apiGetMock).toHaveBeenCalledWith('/vendors/payout-methods')
        expect(apiPostMock).toHaveBeenCalledWith('/vendors/payout-methods', payload)
        for (const [path] of [...apiGetMock.mock.calls, ...apiPostMock.mock.calls]) {
            expect(path).not.toMatch(/^\/api\//)
        }
    })

    it('uses the canonical vendor debt repayment dashboard route', async () => {
        const { getCommerceHubPaymentsDashboard } = await import('./commerceHubPaymentsApi')

        await getCommerceHubPaymentsDashboard()

        expect(apiGetMock).toHaveBeenCalledWith('/admin/commercehub/payments')
    })
})
