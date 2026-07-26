import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useToast } from '../components/Toast'

export default function CashboxTransactionModal({ cashboxId, type, onClose, onSuccess }) {
    const { t } = useTranslation()
    const [amount, setAmount] = useState('')
    const [description, setDescription] = useState('')
    const [paymentMethod, setPaymentMethod] = useState('Cash')
    const [notes, setNotes] = useState('')
    const [loading, setLoading] = useState(false)

    const { showSuccess, showError } = useToast()

    const getTitle = () => {
        switch (type) {
            case 'cash-in':
                return t('cashbox.cashInDeposit')
            case 'cash-out':
                return t('cashbox.cashOutWithdrawal')
            case 'expense':
                return t('cashbox.recordExpense')
            default:
                return t('cashbox.transaction')
        }
    }

    const getEndpoint = () => {
        switch (type) {
            case 'cash-in':
                return `/cashbox/${cashboxId}/cash-in`
            case 'cash-out':
                return `/cashbox/${cashboxId}/cash-out`
            case 'expense':
                return `/cashbox/${cashboxId}/expense`
            default:
                return ''
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        if (!amount || parseFloat(amount) <= 0) {
            showError(t('cashbox.validAmountError'))
            return
        }

        if (!description.trim()) {
            showError(t('cashbox.descriptionError'))
            return
        }

        setLoading(true)
        try {
            const data = {
                amount: parseFloat(amount),
                description: description.trim(),
                paymentMethod: type === 'expense' ? paymentMethod : 'Cash',
                notes: notes.trim() || null
            }

            await api.post(getEndpoint(), data)
            showSuccess(`${getTitle()} ${t('cashbox.recordedSuccessfully')}`)
            onSuccess()
        } catch (error) {
            console.error('Error recording transaction:', error)
            showError(error.response?.data?.message || t('cashbox.failedToRecord'))
        } finally {
            setLoading(false)
        }
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl w-full max-w-md">
                <div className="px-6 py-4 border-b border-gray-200">
                    <h2 className="text-xl font-semibold text-gray-800">{getTitle()}</h2>
                </div>

                <form onSubmit={handleSubmit} className="p-6 space-y-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('cashbox.amountRequired')}
                        </label>
                        <input
                            type="number"
                            step="0.01"
                            min="0.01"
                            value={amount}
                            onChange={(e) => setAmount(e.target.value)}
                            className="input w-full"
                            placeholder={t('cashbox.amountPlaceholder')}
                            required
                            autoFocus
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('cashbox.descriptionRequired')}
                        </label>
                        <input
                            type="text"
                            value={description}
                            onChange={(e) => setDescription(e.target.value)}
                            className="input w-full"
                            placeholder={type === 'expense' ? t('cashbox.descriptionPlaceholderExpense') : t('cashbox.descriptionPlaceholderDeposit')}
                            required
                        />
                    </div>

                    {type === 'expense' && (
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('cashbox.paymentMethodRequired')}
                            </label>
                            <select
                                value={paymentMethod}
                                onChange={(e) => setPaymentMethod(e.target.value)}
                                className="input w-full"
                                required
                            >
                                <option value="Cash">{t('purchases.cash')}</option>
                                <option value="Card">{t('purchases.card')}</option>
                                <option value="BankTransfer">{t('purchases.bankTransfer')}</option>
                            </select>
                        </div>
                    )}

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('cashbox.notesOptional')}
                        </label>
                        <textarea
                            value={notes}
                            onChange={(e) => setNotes(e.target.value)}
                            className="input w-full"
                            rows="3"
                            placeholder={t('cashbox.notesPlaceholder')}
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
                            {loading ? t('cashbox.recording') : t('cashbox.recordTransaction')}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    )
}
