import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useFormatting } from '../hooks/useFormatting'

export default function OpenCloseCashboxModal({ cashbox, mode, onClose, onSuccess }) {
    const { t } = useTranslation()
    const { dir, isRTL, formatCurrency, formatSignedCurrency } = useFormatting()
    const [amount, setAmount] = useState('')
    const [notes, setNotes] = useState('')
    const [loading, setLoading] = useState(false)
    const isSubmittingRef = useRef(false)
    const [showDenominations, setShowDenominations] = useState(false)
    const [denominations, setDenominations] = useState({
        hundreds: 0,
        fifties: 0,
        twenties: 0,
        tens: 0,
        fives: 0,
        ones: 0,
        coins: 0,
    })

    const { showSuccess, showError } = useToast()
    const isOpenMode = mode === 'open'

    const calculateTotal = () => (
        denominations.hundreds * 100
        + denominations.fifties * 50
        + denominations.twenties * 20
        + denominations.tens * 10
        + denominations.fives * 5
        + denominations.ones
        + Number.parseFloat(denominations.coins || 0)
    )

    const handleDenominationChange = (key, value) => {
        const numericValue = Number(value)
        let parsedValue = Number.isFinite(numericValue) && numericValue >= 0
            ? numericValue
            : 0

        // Note denominations must be whole numbers (you can't have fractional banknotes)
        if (key !== 'coins') {
            parsedValue = Math.floor(parsedValue)
        }

        const newDenominations = { ...denominations, [key]: parsedValue }
        setDenominations(newDenominations)
        setAmount(calculateDenominationTotal(newDenominations).toString())
    }

    const calculateDenominationTotal = (values) => (
        values.hundreds * 100
        + values.fifties * 50
        + values.twenties * 20
        + values.tens * 10
        + values.fives * 5
        + values.ones
        + Number.parseFloat(values.coins || 0)
    )

    const handleSubmit = async (event) => {
        event.preventDefault()

        if (isSubmittingRef.current) {
            return
        }

        const parsedAmount = Number.parseFloat(amount)

        if (!Number.isFinite(parsedAmount) || parsedAmount < 0) {
            showError(t('cashbox.validAmountError'))
            return
        }

        isSubmittingRef.current = true
        setLoading(true)
        try {
            if (isOpenMode) {
                await api.post(`/cashbox/${cashbox.id}/open`, {
                    openingBalance: parsedAmount,
                    notes: notes.trim() || null,
                })
                showSuccess(t('cashbox.openedSuccessfully'))
            } else {
                const difference = parsedAmount - cashbox.expectedBalance
                await api.post(`/cashbox/${cashbox.id}/close`, {
                    actualCash: parsedAmount,
                    notes: notes.trim() || null,
                })

                if (Math.abs(difference) > 0.01) {
                    showSuccess(t('cashbox.closedWithDifference', { difference: formatSignedCurrency(difference) }))
                } else {
                    showSuccess(t('cashbox.closedBalanced'))
                }
            }
            onSuccess()
        } catch (error) {
            console.error('Error:', error)
            showError(t(isOpenMode ? 'cashbox.failedToOpen' : 'cashbox.failedToClose'))
        } finally {
            isSubmittingRef.current = false
            setLoading(false)
        }
    }

    const difference = isOpenMode ? 0 : Number.parseFloat(amount || 0) - cashbox.expectedBalance
    const denominationValues = [100, 50, 20, 10, 5, 1]

    return (
        <div dir={dir} className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl w-full max-w-2xl">
                <div className="px-6 py-4 border-b border-gray-200">
                    <h2 className="text-xl font-semibold text-gray-800">
                        {t(isOpenMode ? 'cashbox.openCashbox' : 'cashbox.closeCashbox')}
                    </h2>
                </div>

                <form onSubmit={handleSubmit} className="p-6 space-y-4">
                    {!isOpenMode && (
                        <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 mb-4">
                            <div className="text-sm font-medium text-blue-800 mb-2">{t('cashbox.expectedBalance')}</div>
                            <div className="text-2xl font-bold text-blue-900">
                                {formatCurrency(cashbox.expectedBalance)}
                            </div>
                        </div>
                    )}

                    <div className="flex items-center justify-between mb-4">
                        <label className="block text-sm font-medium text-gray-700">
                            {t(isOpenMode ? 'cashbox.openingBalance' : 'cashbox.actualCashCount')} *
                        </label>
                        <button
                            type="button"
                            onClick={() => setShowDenominations((value) => !value)}
                            className="text-sm text-primary-600 hover:text-primary-700"
                        >
                            {t(showDenominations ? 'cashbox.hideDenominationBreakdown' : 'cashbox.showDenominationBreakdown')}
                        </button>
                    </div>

                    {showDenominations ? (
                        <div className="bg-gray-50 p-4 rounded-lg space-y-3">
                            <div className="grid grid-cols-2 gap-4">
                                {denominationValues.map((value) => {
                                    const key = {
                                        100: 'hundreds',
                                        50: 'fifties',
                                        20: 'twenties',
                                        10: 'tens',
                                        5: 'fives',
                                        1: 'ones',
                                    }[value]

                                    return (
                                        <div key={value}>
                                            <label className="block text-sm text-gray-600 mb-1">
                                                {t('cashbox.noteDenomination', { value })}
                                            </label>
                                            <input
                                                type="number"
                                                min="0"
                                                step="1"
                                                value={denominations[key]}
                                                onChange={(event) => handleDenominationChange(key, event.target.value)}
                                                className="input w-full"
                                            />
                                        </div>
                                    )
                                })}
                                <div className="col-span-2">
                                    <label className="block text-sm text-gray-600 mb-1">{t('cashbox.coinsLabel')}</label>
                                    <input
                                        type="number"
                                        step="0.01"
                                        min="0"
                                        value={denominations.coins}
                                        onChange={(event) => handleDenominationChange('coins', event.target.value)}
                                        className="input w-full"
                                    />
                                </div>
                            </div>
                            <div className="border-t border-gray-300 pt-3 mt-3">
                                <div className="flex justify-between items-center">
                                    <span className="text-sm font-medium text-gray-700">{t('cashbox.denominationTotal')}</span>
                                    <span className="text-xl font-bold text-gray-900">{formatCurrency(calculateTotal())}</span>
                                </div>
                            </div>
                        </div>
                    ) : (
                        <div>
                            <input
                                type="number"
                                step="0.01"
                                min="0"
                                value={amount}
                                onChange={(event) => setAmount(event.target.value)}
                                className="input w-full text-lg"
                                placeholder={t('common.amountPlaceholder')}
                                required
                                autoFocus
                            />
                        </div>
                    )}

                    {!isOpenMode && amount && (
                        <div className={`p-4 rounded-lg ${Math.abs(difference) < 0.01 ? 'bg-green-50 border border-green-200' : 'bg-yellow-50 border border-yellow-200'}`}>
                            <div className={`flex justify-between items-center ${isRTL ? 'flex-row-reverse' : ''}`}>
                                <span className="text-sm font-medium text-gray-700">{t('cashbox.differenceLabel')}</span>
                                <span className={`text-xl font-bold ${difference >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                    {formatSignedCurrency(difference)}
                                </span>
                            </div>
                            {Math.abs(difference) >= 0.01 && (
                                <p className="text-sm text-gray-600 mt-2">
                                    {t(difference > 0 ? 'cashbox.cashOver' : 'cashbox.cashShort')}
                                </p>
                            )}
                        </div>
                    )}

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('cashbox.notesLabel')}
                            {!isOpenMode && Math.abs(difference) >= 0.01 ? ` ${t('cashbox.notesRequiredInline')}` : ''}
                        </label>
                        <textarea
                            value={notes}
                            onChange={(event) => setNotes(event.target.value)}
                            className="input w-full"
                            rows="3"
                            placeholder={t(isOpenMode ? 'cashbox.openingNotesPlaceholder' : 'cashbox.closingNotesPlaceholder')}
                            required={!isOpenMode && Math.abs(difference) >= 0.01}
                        />
                    </div>

                    <div className="flex justify-end gap-3 pt-4">
                        <button
                            type="button"
                            onClick={onClose}
                            className="btn-secondary"
                            disabled={loading}
                        >
                            {t('common.cancel')}
                        </button>
                        <button
                            type="submit"
                            className="btn-primary"
                            disabled={loading}
                        >
                            {loading
                                ? t('common.processing')
                                : t(isOpenMode ? 'cashbox.openCashbox' : 'cashbox.closeCashbox')}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    )
}
