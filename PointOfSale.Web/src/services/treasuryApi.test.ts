import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getTreasuryAccounts } from './treasuryApi'

const apiMock = vi.hoisted(() => ({ get: vi.fn() }))

vi.mock('./api', () => ({ default: apiMock }))

describe('treasuryApi account catalog', () => {
    beforeEach(() => vi.clearAllMocks())

    it('forwards cancellation and returns the tenant-scoped account response unchanged', async () => {
        const signal = new AbortController().signal
        const rows = [{ accountCode: 'CUSTOM-1', accountType: 'Cashbox' }]
        apiMock.get.mockResolvedValue({ data: rows })

        await expect(getTreasuryAccounts({ signal })).resolves.toBe(rows)
        expect(apiMock.get).toHaveBeenCalledWith('/treasury/accounts', { signal })
    })
})
