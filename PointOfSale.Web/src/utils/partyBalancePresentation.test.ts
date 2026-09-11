import { describe, expect, it } from 'vitest'
import { getAdjustmentEffectTranslationKey, getPartyBalanceColorClass } from './partyBalancePresentation'

describe('party balance presentation', () => {
    it('marks balances owed to suppliers as adverse', () => {
        expect(getPartyBalanceColorClass('Supplier', 'Credit')).toBe('text-red-600')
        expect(getPartyBalanceColorClass('Supplier', 'Debit')).toBe('text-green-600')
    })

    it('keeps customer balance severity aligned with receivables', () => {
        expect(getPartyBalanceColorClass('Customer', 'Debit')).toBe('text-red-600')
        expect(getPartyBalanceColorClass('Customer', 'Credit')).toBe('text-green-600')
    })

    it('describes supplier adjustments using supplier ledger semantics', () => {
        expect(getAdjustmentEffectTranslationKey('Supplier', 'CreditNote')).toBe('accounts.increasesAmountOwed')
        expect(getAdjustmentEffectTranslationKey('Supplier', 'DebitNote')).toBe('accounts.reducesAmountOwed')
        expect(getAdjustmentEffectTranslationKey('Supplier', 'CreditAdjustment')).toBe('accounts.increasesAmountOwed')
        expect(getAdjustmentEffectTranslationKey('Supplier', 'DebitAdjustment')).toBe('accounts.reducesAmountOwed')
    })
})
