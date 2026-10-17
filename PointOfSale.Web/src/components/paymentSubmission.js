import { getSafeApiErrorMessage, isRequestTimeout } from '../services/api'
import { roundCurrency, clampAmount, parseStrictAmountInput } from '../utils/financialMath'

export const getSaleSubmissionErrorMessage = (error, t) => {
    if (isRequestTimeout(error)) {
        return t(
            'paymentModal.validation.requestTimedOutBeforeRetry',
            'Payment request timed out. Check sales history before trying again.',
        )
    }
    return getSafeApiErrorMessage(error, t('paymentModal.failedToProcess'))
}

export const parseAmountInput = (value) => {
    return parseStrictAmountInput(value, { allowZero: true, allowNegative: false, maxDecimalPlaces: 2 })
}

export const getMixedPaymentState = (total, cashAmount) => {
    const totalDue = Math.max(0, roundCurrency(total))
    const parsedCashAmount = parseAmountInput(cashAmount)
    const hasNegativeCashAmount = parsedCashAmount.reason === 'negative'
    const hasZeroCashAmount = parsedCashAmount.isValid && parsedCashAmount.value <= 0
    const hasCashAmountAtOrAboveTotal = parsedCashAmount.isValid && roundCurrency(parsedCashAmount.value) >= totalDue
    const normalizedCashAmount = parsedCashAmount.isValid
        ? clampAmount(roundCurrency(parsedCashAmount.value), 0, totalDue)
        : 0
    const normalizedCardAmount = Math.max(0, roundCurrency(totalDue - normalizedCashAmount))
    const totalPaid = roundCurrency(normalizedCashAmount + normalizedCardAmount)

    return {
        totalDue,
        normalizedCashAmount,
        normalizedCardAmount,
        totalPaid,
        difference: Math.abs(totalPaid - totalDue),
        hasInvalidCashAmount: !parsedCashAmount.isValid,
        hasNegativeCashAmount,
        hasZeroCashAmount,
        hasCashAmountAtOrAboveTotal,
        shouldClampCashAmount: false,
    }
}
