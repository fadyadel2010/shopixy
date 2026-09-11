export const getPartyBalanceColorClass = (partyType, balanceType) => {
    if (balanceType === 'Zero') return 'text-gray-600'

    const isAmountOwed = partyType === 'Supplier'
        ? balanceType === 'Credit'
        : balanceType === 'Debit'

    return isAmountOwed ? 'text-red-600' : 'text-green-600'
}

export const getAdjustmentEffectTranslationKey = (partyType, adjustmentType) => {
    const isDebit = adjustmentType === 'DebitNote' || adjustmentType === 'DebitAdjustment'
    const increasesAmountOwed = partyType === 'Supplier' ? !isDebit : isDebit

    return increasesAmountOwed ? 'accounts.increasesAmountOwed' : 'accounts.reducesAmountOwed'
}
