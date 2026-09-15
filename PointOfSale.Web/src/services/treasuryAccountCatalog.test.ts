import { describe, expect, it } from 'vitest'
import {
    findTreasuryAccountByCode,
    getLocalizedTreasuryAccountName,
    isAvailableCashAccount,
    isClearingAccount,
    isTransferEligibleAccount,
    partitionTreasuryCatalog,
    sumTreasuryBalances,
} from './treasuryAccountCatalog'

const cashbox = { accountCode: '9001', name: 'Main Vault', nameAr: 'الخزينة الرئيسية', currentBalance: 100, accountType: 'Cashbox' }
const bank = { accountCode: '9002', name: 'Custom Bank', nameAr: '', currentBalance: 50.5, accountType: 'Bank' }
const clearing = { accountCode: '9003', name: 'Custom Clearing', nameAr: 'مقاصة مخصصة', currentBalance: 25, accountType: 'Clearing' }
const unknown = { accountCode: '1020', name: 'POS Visa Account', nameAr: '', currentBalance: 999, accountType: 'Asset' }

describe('treasuryAccountCatalog', () => {
    it('treats only exact Cashbox/Bank types as available cash', () => {
        expect(isAvailableCashAccount(cashbox)).toBe(true)
        expect(isAvailableCashAccount(bank)).toBe(true)
        expect(isAvailableCashAccount(clearing)).toBe(false)
        expect(isAvailableCashAccount(unknown)).toBe(false)
        expect(isAvailableCashAccount(null)).toBe(false)
        expect(isAvailableCashAccount({ accountType: 'cashbox' })).toBe(false)
    })

    it('treats only exact Clearing type as in-transit', () => {
        expect(isClearingAccount(clearing)).toBe(true)
        expect(isClearingAccount(cashbox)).toBe(false)
        expect(isClearingAccount(unknown)).toBe(false)
    })

    it('fails unknown types closed for transfers', () => {
        expect(isTransferEligibleAccount(cashbox)).toBe(true)
        expect(isTransferEligibleAccount(bank)).toBe(true)
        expect(isTransferEligibleAccount(clearing)).toBe(false)
        expect(isTransferEligibleAccount(unknown)).toBe(false)
        expect(isTransferEligibleAccount({ accountType: 'Mystery' })).toBe(false)
    })

    it('prefers nonblank Arabic names in Arabic and falls back to English', () => {
        expect(getLocalizedTreasuryAccountName(cashbox, 'ar')).toBe('الخزينة الرئيسية')
        expect(getLocalizedTreasuryAccountName(bank, 'ar-EG')).toBe('Custom Bank')
        expect(getLocalizedTreasuryAccountName(clearing, 'en')).toBe('Custom Clearing')
        expect(getLocalizedTreasuryAccountName(clearing, 'ar')).toBe('مقاصة مخصصة')
    })

    it('partitions arbitrary custom codes by accountType only', () => {
        const { available, clearing: transit, unavailable } = partitionTreasuryCatalog([cashbox, bank, clearing, unknown])
        expect(available.map(a => a.accountCode)).toEqual(['9001', '9002'])
        expect(transit.map(a => a.accountCode)).toEqual(['9003'])
        expect(unavailable.map(a => a.accountCode)).toEqual(['1020'])
        expect(sumTreasuryBalances(available)).toBeCloseTo(150.5)
        expect(sumTreasuryBalances(transit)).toBe(25)
    })

    it('never adds clearing or unknown rows to available cash', () => {
        const { available } = partitionTreasuryCatalog([clearing, unknown])
        expect(available).toEqual([])
        expect(sumTreasuryBalances(available)).toBe(0)
    })

    it('resolves route codes against the catalog without inventing identity', () => {
        expect(findTreasuryAccountByCode([cashbox, bank], '9002')).toBe(bank)
        expect(findTreasuryAccountByCode([cashbox, bank], '1010')).toBeNull()
        expect(findTreasuryAccountByCode([], '9001')).toBeNull()
    })
})
