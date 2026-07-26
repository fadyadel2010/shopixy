import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'

const PaymentModal = ({ isOpen, onClose, onSubmit, partyName, currentBalance, balanceType, type, loading }) => {
    const { t } = useTranslation()
    const { isRTL, dir } = useRTL()
    const { formatCurrency } = useSettings()
    const [formData, setFormData] = useState({
        amount: '',
        paymentDate: new Date().toISOString().split('T')[0],
        paymentMethod: 'Cash',
        cashAmount: '',
        notes: '',
    })

    if (!isOpen) return null

    const handleSubmit = (e) => {
        e.preventDefault()
        if (loading) {
            return
        }

        const submitData = {
            amount: parseFloat(formData.amount),
            paymentDate: new Date(formData.paymentDate).toISOString(),
            paymentMethod: formData.paymentMethod,
            notes: formData.notes,
        }

        if (formData.paymentMethod === 'Mixed') {
            submitData.cashAmount = parseFloat(formData.cashAmount)
        }

        onSubmit(submitData)
    }



    const getBalanceColor = (balanceType) => {
        if (balanceType === 'Debit' || balanceType === t('accounts.debit')) return 'text-red-600'
        if (balanceType === 'Credit' || balanceType === t('accounts.credit')) return 'text-green-600'
        return 'text-gray-600'
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50" dir={dir}>
            <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
                <div className="p-6">
                    <div className={`flex justify-between items-center mb-6 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <h2 className={`text-xl font-bold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {type === 'receive' ? t('accounts.receivePayment') : t('accounts.makePayment')}
                        </h2>
                        <button
                            onClick={() => {
                                if (!loading) {
                                    onClose()
                                }
                            }}
                            disabled={loading}
                            className="text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Party Info */}
                    <div className="mb-6 p-4 bg-gray-50 rounded-lg">
                        <p className={`text-sm text-gray-600 mb-1 ${isRTL ? 'text-right' : 'text-left'}`}>
                            {type === 'receive' ? t('accounts.customer') : t('accounts.supplier')}
                        </p>
                        <p className={`text-lg font-semibold text-gray-900 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>{partyName}</p>
                        <div className={`flex justify-between items-center gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                            <span className="text-sm text-gray-600">{t('accounts.currentBalance')}:</span>
                            <span className={`text-lg font-bold ${getBalanceColor(balanceType)}`}>
                                {formatCurrency(currentBalance)} ({balanceType === 'Debit' ? t('accounts.debit') : t('accounts.credit')})
                            </span>
                        </div>
                    </div>

                    <form onSubmit={handleSubmit} className="space-y-4">
                        {/* Amount */}
                        <div>
                            <label className={`block text-sm font-medium text-gray-700 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('cashbox.amountRequired')}
                            </label>
                            <input
                                type="number"
                                step="0.01"
                                required
                                value={formData.amount}
                                onChange={(e) => setFormData({ ...formData, amount: e.target.value })}
                                className={`w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                                placeholder={t('cashbox.amountPlaceholder')}
                                disabled={loading}
                            />
                        </div>

                        {/* Payment Date */}
                        <div>
                            <label className={`block text-sm font-medium text-gray-700 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('accounts.paymentDateRequired')}
                            </label>
                            <input
                                type="date"
                                required
                                value={formData.paymentDate}
                                onChange={(e) => setFormData({ ...formData, paymentDate: e.target.value })}
                                className={`w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={loading}
                            />
                        </div>

                        {/* Payment Method */}
                        <div>
                            <label className={`block text-sm font-medium text-gray-700 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('accounts.paymentMethodRequired')}
                            </label>
                            <select
                                required
                                value={formData.paymentMethod}
                                onChange={(e) => setFormData({ ...formData, paymentMethod: e.target.value })}
                                className={`w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                                disabled={loading}
                            >
                                <option value="Cash">{t('accounts.cash')}</option>
                                <option value="Card">{t('accounts.card')}</option>
                                <option value="BankTransfer">{t('accounts.bankTransfer')}</option>
                                <option value="Mixed">{t('accounts.mixed')}</option>
                            </select>
                        </div>

                        {/* Cash Amount (for Mixed) */}
                        {formData.paymentMethod === 'Mixed' && (
                            <div>
                                <label className={`block text-sm font-medium text-gray-700 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    {t('accounts.cashAmountRequired')}
                                </label>
                                <input
                                    type="number"
                                    step="0.01"
                                    required
                                    value={formData.cashAmount}
                                    onChange={(e) => setFormData({ ...formData, cashAmount: e.target.value })}
                                    className={`w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                                    placeholder={t('cashbox.amountPlaceholder')}
                                    disabled={loading}
                                />
                                <p className={`text-xs text-gray-500 mt-1 ${isRTL ? 'text-right' : 'text-left'}`}>
                                    {t('accounts.cardAmount')} {formatCurrency((parseFloat(formData.amount) || 0) - (parseFloat(formData.cashAmount) || 0))}
                                </p>
                            </div>
                        )}

                        {/* Notes */}
                        <div>
                            <label className={`block text-sm font-medium text-gray-700 mb-2 ${isRTL ? 'text-right' : 'text-left'}`}>
                                {t('accounts.notes')}
                            </label>
                            <textarea
                                value={formData.notes}
                                onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                rows={3}
                                className={`w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'text-right' : 'text-left'}`}
                                placeholder={t('accounts.optionalNotes')}
                                disabled={loading}
                            />
                        </div>

                        {/* Buttons */}
                        <div className={`flex gap-3 pt-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                            <button
                                type="button"
                                onClick={onClose}
                                disabled={loading}
                                className="flex-1 px-4 py-2 border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                type="submit"
                                disabled={loading}
                                className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400"
                            >
                                {loading ? t('common.processing') : (type === 'receive' ? t('accounts.receivePayment') : t('accounts.makePayment'))}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </div>
    )
}

export default PaymentModal
