import { useState, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api, { getSafeApiErrorMessage, isRequestTimeout } from '../services/api'
import AsyncEntitySelect from './AsyncEntitySelect'
import ConfirmModal from './ConfirmModal'
import {
    roundCurrency,
    clampAmount,
    calculateLineSubtotal,
    calculateRemainingSubtotalFromItems,
    parseStrictAmountInput,
} from '../utils/financialMath'
import { buildIdempotencyHeaders, stableStringify } from '../utils/requestSafety'
import { searchSalesRepresentatives } from '../services/entityLookup'

const toStrictAmount = (value, { allowZero = false } = {}) => {
    const parsed = parseStrictAmountInput(value, { allowZero })
    return parsed.isValid ? parsed.value : Number.NaN
}

// Helper for non-currency numeric values (quantity, tax rates) that need more precision
const toStrictNumber = (value, { allowZero = false, maxDecimalPlaces = 10 } = {}) => {
    const parsed = parseStrictAmountInput(value, { allowZero, maxDecimalPlaces })
    return parsed.isValid ? parsed.value : Number.NaN
}

const buildRequestId = (prefix) => `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
const MIXED_PAYMENT_TOLERANCE = 0.01

export const getSaleSubmissionErrorMessage = (error, t) => {
    if (isRequestTimeout(error)) {
        return t(
            'paymentModal.validation.requestTimedOutBeforeRetry',
            'Payment request timed out. Check sales history before trying again.',
        )
    }

    return getSafeApiErrorMessage(error, t('paymentModal.failedToProcess'))
}

const isActiveSaleSubmissionConflict = (error) => {
    if (error?.response?.status !== 409) {
        return false
    }

    // Check for machine-readable conflict code first
    const conflictCode = error.response?.data?.conflictCode
    if (conflictCode) {
        // Only treat SALE_IN_PROGRESS as an active submission conflict
        // Other conflict codes (if any) should return false
        return conflictCode === 'SALE_IN_PROGRESS'
    }

    // Fall back to legacy message text parsing for backward compatibility
    const message = String(error.response?.data?.message || error.message || '').toLowerCase()
    return message.includes('already being processed') ||
        message.includes('same payment attempt id') ||
        message.includes('same idempotency key')
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

const buildPaymentBreakdown = ({ paymentMethod, safeTotal, change, mixedPayment, cashValue, t }) => {
    if (paymentMethod === 'Cash') {
        return [
            {
                key: 'cash',
                label: t('paymentModal.cashReceived'),
                amount: Math.max(0, roundCurrency(cashValue)),
            },
            {
                key: 'change',
                label: t('paymentModal.change'),
                amount: change,
            },
        ]
    }

    if (paymentMethod === 'Card') {
        return [{
            key: 'card',
            label: t('paymentModal.cardCharged'),
            amount: safeTotal,
        }]
    }

    return [
        {
            key: 'cash',
            label: t('paymentModal.cashPortion'),
            amount: mixedPayment.normalizedCashAmount,
        },
        {
            key: 'card',
            label: t('paymentModal.cardPortion'),
            amount: mixedPayment.normalizedCardAmount,
        },
    ]
}

export default function PaymentModal({
    total,
    items,
    customer,
    discountType,
    discountValue,
    warehouseId,
    paymentAttemptId,
    posCartId,
    onRevalidateStock,
    onClose,
    onComplete,
    onError,
    editingSaleId,
    editingMutationVersion,
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const { formatCurrency } = useSettings()
    const [paymentMethod, setPaymentMethod] = useState('Cash')
    const [cashAmount, setCashAmount] = useState('')
    const [loading, setLoading] = useState(false)
    const [isRevalidating, setIsRevalidating] = useState(false)
    const [selectedSalesRep, setSelectedSalesRep] = useState(null)
    const [pendingConfirmation, setPendingConfirmation] = useState(null)
    const submissionLockRef = useRef(false)
    const confirmationLockRef = useRef(false)
    const lastSubmissionRef = useRef(null)
    const [ambiguousSubmissionError, setAmbiguousSubmissionError] = useState('')

    useEffect(() => {
        const safeTotal = Math.max(0, roundCurrency(total))
        setPaymentMethod('Cash')
        setCashAmount(safeTotal.toFixed(2))
        setLoading(false)
        setSelectedSalesRep(null)
        setPendingConfirmation(null)
        setAmbiguousSubmissionError('')
        submissionLockRef.current = false
        confirmationLockRef.current = false
        lastSubmissionRef.current = null
    }, [total])

    const mixedPayment = getMixedPaymentState(total, cashAmount)

    useEffect(() => {
        const handleKeyDown = (event) => {
            if (event.key !== 'Escape') {
                return
            }

            event.preventDefault()
            if (pendingConfirmation || loading || isRevalidating) {
                return
            }

            onClose()
        }

        window.addEventListener('keydown', handleKeyDown)
        return () => window.removeEventListener('keydown', handleKeyDown)
    }, [loading, onClose, pendingConfirmation, isRevalidating])

    const safeTotal = mixedPayment.totalDue
    const maxFixedDiscount = calculateRemainingSubtotalFromItems(items)
    const parsedCashInput = parseAmountInput(cashAmount)
    const cashValue = parsedCashInput.isValid
        ? roundCurrency(parsedCashInput.value)
        : null
    const change = paymentMethod === 'Cash'
        ? Math.max(0, roundCurrency((cashValue ?? 0) - safeTotal))
        : 0
    const livePaymentSource = {
        paymentMethod,
        safeTotal,
        change,
        mixedPayment,
        cashValue,
    }
    const paymentSource = pendingConfirmation ?? livePaymentSource
    const paymentBreakdown = buildPaymentBreakdown({
        paymentMethod: paymentSource.paymentMethod,
        safeTotal: paymentSource.safeTotal,
        change: paymentSource.change,
        mixedPayment: paymentSource.mixedPayment,
        cashValue: paymentSource.cashValue,
        t,
    })
    const isBusy = loading || isRevalidating || Boolean(pendingConfirmation)

    const getValidationMessage = () => {
        const parsedDiscountValue = parseStrictAmountInput(discountValue, { allowZero: true })
        const safeDiscountValue = parsedDiscountValue.isValid ? roundCurrency(parsedDiscountValue.value) : Number.NaN

        if (!warehouseId) {
            return t('paymentModal.validation.warehouseRequired')
        }

        if (!Array.isArray(items) || items.length === 0) {
            return t('paymentModal.validation.itemsRequired')
        }

        if (safeTotal <= 0) {
            return t('paymentModal.validation.totalMustBePositive')
        }

        if (!Number.isFinite(safeDiscountValue)) {
            return t('paymentModal.validation.discountOutOfRange')
        }

        if (discountType === 'percentage' && (safeDiscountValue < 0 || safeDiscountValue > 100)) {
            return t('paymentModal.validation.discountOutOfRange')
        }

        if (discountType === 'fixed' && (safeDiscountValue < 0 || safeDiscountValue > maxFixedDiscount)) {
            return t('paymentModal.validation.discountOutOfRange')
        }

        for (const item of items) {
            const quantity = toStrictNumber(item.quantity)
            const unitPrice = toStrictAmount(item.unitPrice, { allowZero: true })
            const discountAmount = toStrictAmount(item.discountAmount ?? 0, { allowZero: true })
            const taxRate = toStrictNumber(item.taxRate ?? 0, { allowZero: true })
            const lineSubtotal = calculateLineSubtotal(quantity, unitPrice)

            if (!item.productId) {
                return t('paymentModal.validation.invalidProductReference')
            }

            if (!Number.isFinite(quantity) || quantity <= 0) {
                return t('paymentModal.validation.invalidItemQuantity', { product: item.name })
            }

            if (!Number.isFinite(unitPrice) || unitPrice < 0) {
                return t('paymentModal.validation.invalidItemPrice', { product: item.name })
            }

            if (!Number.isFinite(discountAmount) || discountAmount < 0 || discountAmount > lineSubtotal) {
                return t('paymentModal.validation.invalidItemDiscount', { product: item.name })
            }

            if (!Number.isFinite(taxRate) || taxRate < 0) {
                return t('paymentModal.validation.invalidItemTax', { product: item.name })
            }
        }

        if (paymentMethod === 'Cash' && (!parsedCashInput.isValid || cashValue == null || cashValue < safeTotal)) {
            return t('paymentModal.validation.cashAmountTooLow')
        }

        if (paymentMethod === 'Mixed') {
            if (
                mixedPayment.hasInvalidCashAmount
                || mixedPayment.hasNegativeCashAmount
                || mixedPayment.hasZeroCashAmount
                || mixedPayment.hasCashAmountAtOrAboveTotal
                || mixedPayment.difference > MIXED_PAYMENT_TOLERANCE
            ) {
                return t('paymentModal.validation.invalidMixedPaymentTotal')
            }
        }

        return ''
    }

    const validationMessage = getValidationMessage()

    const buildSalePayload = () => {
        if (validationMessage) {
            return { error: validationMessage }
        }

        const normalizedItems = items.map(item => ({
            productId: item.productId,
            productVariantId: item.variantId || null,
            unitId: item.unitId || null,
            quantity: Number(item.quantity),
            unitPrice: roundCurrency(item.unitPrice),
            discountAmount: roundCurrency(item.discountAmount ?? 0),
            taxRate: Number(item.taxRate ?? 0),
        }))

        let paidAmount = safeTotal
        let nextCashAmount = 0
        let nextCardAmount = 0

        if (paymentMethod === 'Cash') {
            paidAmount = roundCurrency(cashValue)
            nextCashAmount = paidAmount
        } else if (paymentMethod === 'Card') {
            nextCardAmount = safeTotal
        } else {
            if (
                mixedPayment.hasInvalidCashAmount
                || mixedPayment.hasNegativeCashAmount
                || mixedPayment.hasZeroCashAmount
                || mixedPayment.hasCashAmountAtOrAboveTotal
                || mixedPayment.difference > MIXED_PAYMENT_TOLERANCE
            ) {
                return { error: t('paymentModal.validation.invalidMixedPaymentTotal') }
            }

            nextCashAmount = mixedPayment.normalizedCashAmount
            nextCardAmount = mixedPayment.normalizedCardAmount
            paidAmount = mixedPayment.totalPaid
        }

        return {
            payload: {
                customerId: customer?.id || null,
                warehouseId,
                salesRepresentativeId: selectedSalesRep?.id || null,
                expectedMutationVersion: editingSaleId && Number.isInteger(editingMutationVersion)
                    ? editingMutationVersion
                    : null,
                items: normalizedItems,
                discountPercentage: discountType === 'percentage' ? clampAmount(roundCurrency(discountValue), 0, 100) : 0,
                discountAmount: discountType === 'fixed'
                    ? clampAmount(roundCurrency(discountValue), 0, maxFixedDiscount)
                    : 0,
                paymentMethod,
                paidAmount,
                cashAmount: paymentMethod === 'Mixed' ? mixedPayment.normalizedCashAmount : nextCashAmount,
                cardAmount: paymentMethod === 'Mixed' ? mixedPayment.normalizedCardAmount : nextCardAmount,
            },
            ...livePaymentSource,
        }
    }

    const handleClose = () => {
        if (!loading && !pendingConfirmation && !isRevalidating) {
            onClose()
        }
    }

    const submitPayment = async (submission) => {
        if (submissionLockRef.current) {
            return false
        }

        const fingerprint = stableStringify(submission.payload)
        const idempotencyKey = lastSubmissionRef.current?.fingerprint === fingerprint
            ? lastSubmissionRef.current.key
            : buildRequestId(editingSaleId ? 'pos-sale-update' : 'pos-sale-create')

        lastSubmissionRef.current = { key: idempotencyKey, fingerprint }
        submissionLockRef.current = true
        setLoading(true)
        setAmbiguousSubmissionError('')

        try {
            let saleResponse

            try {
                saleResponse = editingSaleId
                    ? await api.put(`/Sales/${editingSaleId}`, submission.payload, {
                        headers: buildIdempotencyHeaders(idempotencyKey, paymentAttemptId, posCartId),
                    })
                    : await api.post('/Sales', submission.payload, {
                        headers: buildIdempotencyHeaders(idempotencyKey, paymentAttemptId, posCartId),
                    })
            } catch (error) {
                console.error('Sale submission error:', error)

                const isActiveConflict = isActiveSaleSubmissionConflict(error)
                if (error.response && !isActiveConflict) {
                    lastSubmissionRef.current = null
                }

                const message = getSaleSubmissionErrorMessage(error, t)
                if (isRequestTimeout(error) || isActiveConflict) {
                    setAmbiguousSubmissionError(message)
                }
                onError(message)
                return false
            }

            const completeSale = (completionData = {}) => {
                onComplete({
                    ...saleResponse.data,
                    attemptId: paymentAttemptId,
                    receipt: null,
                    receiptWarning: null,
                    ...completionData,
                })
            }

            completeSale()

            try {
                const invoiceId = saleResponse.data?.invoiceId
                if (!invoiceId) {
                    completeSale({
                        receipt: null,
                        receiptWarning: t('paymentModal.validation.receiptUnavailable'),
                    })
                    return true
                }

                const receiptResponse = await api.get(`/Sales/receipt/${invoiceId}`)

                completeSale({
                    receipt: receiptResponse.data,
                })
            } catch (error) {
                console.error('Receipt fetch error:', error)
                completeSale({
                    receipt: null,
                    receiptWarning: error.response?.data?.message || error.message || t('paymentModal.validation.receiptUnavailable'),
                })
            }

            return true
        } finally {
            setLoading(false)
            submissionLockRef.current = false
        }
    }

    const handleConfirmPayment = async () => {
        if (!pendingConfirmation || loading || isRevalidating || confirmationLockRef.current) {
            return
        }

        confirmationLockRef.current = true
        try {
            if (typeof onRevalidateStock === 'function') {
                setIsRevalidating(true)
                try {
                    let hasFreshStock

                    try {
                        hasFreshStock = await onRevalidateStock()
                    } catch (error) {
                        console.error('Stock revalidation error:', error)
                        onError(t('paymentModal.validation.stockRevalidationFailed'))
                        return
                    }

                    if (!hasFreshStock) {
                        return
                    }
                } finally {
                    setIsRevalidating(false)
                }
            }

            const success = await submitPayment(pendingConfirmation)
            if (success) {
                setPendingConfirmation(null)
            }
        } finally {
            confirmationLockRef.current = false
        }
    }

    const handleSubmit = (event) => {
        event.preventDefault()

        if (isBusy || ambiguousSubmissionError) {
            return
        }

        const submission = buildSalePayload()
        if (submission.error) {
            onError(submission.error)
            return
        }

        setPendingConfirmation(submission)
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50" dir={isRTL ? 'rtl' : 'ltr'}>
            <div className="mx-4 w-full max-w-md rounded-lg bg-white shadow-xl">
                <div className="p-6">
                    <div className={`mb-6 flex items-center justify-between ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <h2 className={`text-xl font-bold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {t('paymentModal.title')}
                        </h2>
                        {ambiguousSubmissionError ? null : (
                            <button
                                type="button"
                                onClick={handleClose}
                                disabled={isBusy}
                                aria-label={t('common.close')}
                                className="text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                <svg className="h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                </svg>
                            </button>
                        )}
                    </div>

                    {customer && (
                        <div className="mb-4 rounded-lg bg-blue-50 p-3">
                            <p className={`text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('pos.customer')}</p>
                            <p className={`font-semibold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>{customer.name}</p>
                        </div>
                    )}

                    <div className="mb-6 rounded-lg bg-gradient-to-r from-blue-50 to-blue-100 p-4">
                        <p className={`mb-1 text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('paymentModal.totalAmount')}</p>
                        <p className={`text-3xl font-bold text-blue-600 ${isRTL ? 'text-right' : 'text-left'}`}>{formatCurrency(paymentSource.safeTotal)}</p>
                    </div>

                    <form onSubmit={handleSubmit} className="space-y-4">
                        <div>
                            <label className={`mb-2 block text-sm font-medium text-gray-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('paymentModal.salesRepOptional')}
                            </label>
                            <AsyncEntitySelect
                                value={selectedSalesRep}
                                onChange={setSelectedSalesRep}
                                loadOptions={({ search, page, pageSize }) => searchSalesRepresentatives({
                                    search,
                                    page,
                                    pageSize,
                                    isActive: true,
                                })}
                                placeholder={t('paymentModal.salesRepPlaceholder')}
                                emptyText={t('paymentModal.noSalesRepsFound')}
                                disabled={isBusy}
                                renderOption={(rep) => (
                                    <div>
                                        <div className="font-medium text-gray-900">{rep.name}</div>
                                        <div className="text-sm text-gray-500">{rep.phone || t('common.none')}</div>
                                    </div>
                                )}
                            />
                        </div>

                        <div>
                            <label className={`mb-2 block text-sm font-medium text-gray-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('paymentModal.paymentMethod')}
                            </label>
                            <div className="grid grid-cols-3 gap-2">
                                {[
                                    { value: 'Cash', label: t('paymentModal.cashButton') },
                                    { value: 'Card', label: t('paymentModal.cardButton') },
                                    { value: 'Mixed', label: t('paymentModal.mixedButton') },
                                ].map(option => (
                                    <button
                                        key={option.value}
                                        type="button"
                                        onClick={() => setPaymentMethod(option.value)}
                                        disabled={isBusy}
                                        className={`rounded-lg border-2 px-4 py-3 font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${paymentSource.paymentMethod === option.value
                                            ? 'border-blue-500 bg-blue-50 text-blue-700'
                                            : 'border-gray-300 text-gray-700 hover:border-gray-400'
                                            }`}
                                    >
                                        {option.label}
                                    </button>
                                ))}
                            </div>
                        </div>

                        {(paymentSource.paymentMethod === 'Cash' || paymentSource.paymentMethod === 'Mixed') && (
                            <div>
                                <label
                                    htmlFor="payment-cash-amount"
                                    className={`mb-2 block text-sm font-medium text-gray-700 ${isRTL ? 'text-right' : 'text-left'}`}
                                >
                                    {t('paymentModal.cashAmount')}
                                </label>
                                <input
                                    id="payment-cash-amount"
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    max={paymentSource.paymentMethod === 'Mixed' ? paymentSource.safeTotal : undefined}
                                    value={cashAmount}
                                    onChange={(event) => setCashAmount(event.target.value)}
                                    aria-invalid={Boolean(validationMessage)}
                                    aria-describedby={validationMessage ? 'payment-validation-message' : undefined}
                                    className={`w-full rounded-lg border border-gray-300 px-4 py-3 text-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                                    placeholder={t('common.amountPlaceholder')}
                                    autoFocus
                                    disabled={isBusy}
                                />
                                {paymentSource.paymentMethod === 'Cash' && paymentSource.change > 0 && (
                                    <p className={`mt-2 text-sm font-medium text-green-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                                        {t('paymentModal.change')}: {formatCurrency(paymentSource.change)}
                                    </p>
                                )}
                            </div>
                        )}

                        {paymentSource.paymentMethod === 'Mixed' && (
                            <div>
                                <label className={`mb-2 block text-sm font-medium text-gray-700 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    {t('paymentModal.cardAmount')}
                                </label>
                                <input
                                    type="number"
                                    step="0.01"
                                    value={paymentSource.mixedPayment.normalizedCardAmount.toFixed(2)}
                                    readOnly
                                    className={`w-full rounded-lg border border-gray-300 bg-gray-50 px-4 py-3 text-lg ${isRTL ? 'text-right' : 'text-left'}`}
                                />
                            </div>
                        )}

                        {validationMessage ? (
                            <p
                                id="payment-validation-message"
                                role="alert"
                                className={`rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 ${isRTL ? 'text-right' : 'text-left'}`}
                            >
                                {validationMessage}
                            </p>
                        ) : null}

                        <div className={`flex gap-3 pt-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                            <button
                                type="button"
                                onClick={handleClose}
                                disabled={isBusy}
                                className="flex-1 rounded-lg border border-gray-300 px-4 py-3 font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                type="submit"
                                disabled={isBusy || Boolean(validationMessage)}
                                className="flex-1 rounded-lg bg-blue-600 px-4 py-3 font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-gray-400"
                            >
                                {loading ? t('paymentModal.processing') : t('paymentModal.completeSale')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
            {pendingConfirmation ? (
                <ConfirmModal
                    title={t('paymentModal.confirmPaymentTitle')}
                    description={t('paymentModal.confirmPaymentMessage')}
                    confirmLabel={t('paymentModal.confirmPayment')}
                    cancelLabel={t('common.cancel')}
                    onConfirm={handleConfirmPayment}
                    onCancel={() => {
                        setPendingConfirmation(null)
                    }}
                    loading={loading || isRevalidating}
                    confirmDisabled={Boolean(ambiguousSubmissionError && loading)}
                    cancelDisabled={Boolean(ambiguousSubmissionError)}
                    disableEscapeKeyDown={Boolean(ambiguousSubmissionError)}
                    hideCloseButton={Boolean(ambiguousSubmissionError)}
                >
                    <div className="space-y-4">
                        {ambiguousSubmissionError ? (
                            <p
                                role="alert"
                                className={`rounded-lg bg-amber-50 px-3 py-2 text-sm font-medium text-amber-800 ${isRTL ? 'text-right' : 'text-left'}`}
                            >
                                {ambiguousSubmissionError}
                            </p>
                        ) : null}
                        <div className="rounded-lg bg-blue-50 px-4 py-3">
                            <p className={`text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('paymentModal.totalAmount')}</p>
                            <p className={`text-2xl font-bold text-blue-600 ${isRTL ? 'text-right' : 'text-left'}`}>{formatCurrency(paymentSource.safeTotal)}</p>
                        </div>

                        <div className="rounded-lg border border-gray-200">
                            {paymentBreakdown.map((entry) => (
                                <div
                                    key={entry.key}
                                    className={`flex items-center justify-between px-4 py-3 text-sm text-gray-700 ${isRTL ? 'flex-row-reverse' : ''}`}
                                >
                                    <span>{entry.label}</span>
                                    <span className="font-semibold">{formatCurrency(entry.amount)}</span>
                                </div>
                            ))}
                        </div>
                    </div>
                </ConfirmModal>
            ) : null}
        </div>
    )
}
