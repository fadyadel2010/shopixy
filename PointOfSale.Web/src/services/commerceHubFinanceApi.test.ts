import { describe, expect, it } from 'vitest'
import { buildStatementParams } from './commerceHubFinanceApi'

describe('commerceHubFinanceApi', () => {
    it('builds paginated CommerceHub statement params with date bounds and transaction filters', () => {
        const params = buildStatementParams({
            page: 3,
            pageSize: 50,
            fromDate: '2026-03-01',
            toDate: '2026-03-26',
            transactionTypes: ['Refund', 'Payout'],
        })

        expect(params.get('page')).toBe('3')
        expect(params.get('size')).toBe('50')
        expect(params.get('fromDate')).toBe('2026-03-01T00:00:00.000Z')
        expect(params.get('toDate')).toBe('2026-03-26T23:59:59.999Z')
        expect(params.getAll('transactionTypes')).toEqual(['Refund', 'Payout'])
    })

    it('keeps pagination defaults when optional filters are omitted', () => {
        const params = buildStatementParams({})

        expect(params.get('page')).toBe('1')
        expect(params.get('size')).toBe('25')
        expect(params.has('fromDate')).toBe(false)
        expect(params.has('toDate')).toBe(false)
        expect(params.getAll('transactionTypes')).toEqual([])
    })
})
