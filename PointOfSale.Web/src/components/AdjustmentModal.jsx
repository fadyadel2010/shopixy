import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { getAdjustmentEffectTranslationKey } from '../utils/partyBalancePresentation'

const AdjustmentModal = ({ isOpen, onClose, onSubmit, partyName, partyType, loading }) => {
    const { t } = useTranslation()
    const [formData, setFormData] = useState({
        adjustmentType: 'CreditNote',
        amount: '',
        notes: '',
    })

    if (!isOpen) return null

    const handleSubmit = (e) => {
        e.preventDefault()
        if (loading) {
            return
        }

        onSubmit({
            adjustmentType: formData.adjustmentType,
            amount: parseFloat(formData.amount),
            notes: formData.notes,
        })
    }

    const adjustmentTypes = [
        { value: 'CreditNote', label: t('accounts.creditNoteLabel'), description: t(getAdjustmentEffectTranslationKey(partyType, 'CreditNote')) },
        { value: 'DebitNote', label: t('accounts.debitNoteLabel'), description: t(getAdjustmentEffectTranslationKey(partyType, 'DebitNote')) },
        { value: 'CreditAdjustment', label: t('accounts.creditAdjustmentLabel'), description: t(getAdjustmentEffectTranslationKey(partyType, 'CreditAdjustment')) },
        { value: 'DebitAdjustment', label: t('accounts.debitAdjustmentLabel'), description: t(getAdjustmentEffectTranslationKey(partyType, 'DebitAdjustment')) },
    ]

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
            <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
                <div className="p-6">
                    <div className="flex justify-between items-center mb-6">
                        <h2 className="text-xl font-bold text-gray-900">{t('accounts.createAdjustment')}</h2>
                        <button
                            type="button"
                            aria-label="Close"
                            onClick={() => {
                                if (!loading) {
                                    onClose()
                                }
                            }}
                            disabled={loading}
                            className="text-gray-400 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Party Info */}
                    <div className="mb-6 p-4 bg-gray-50 rounded-lg">
                        <p className="text-sm text-gray-600 mb-1">{partyType === 'Customer' ? t('accounts.customer') : t('accounts.supplier')}</p>
                        <p className="text-lg font-semibold text-gray-900">{partyName}</p>
                    </div>

                    <form onSubmit={handleSubmit} className="space-y-4">
                        {/* Adjustment Type */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                {t('accounts.adjustmentTypeRequired')}
                            </label>
                            <select
                                required
                                value={formData.adjustmentType}
                                onChange={(e) => setFormData({ ...formData, adjustmentType: e.target.value })}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                disabled={loading}
                            >
                                {adjustmentTypes.map((type) => (
                                    <option key={type.value} value={type.value}>
                                        {type.label}
                                    </option>
                                ))}
                            </select>
                            <p className="text-xs text-gray-500 mt-1">
                                {adjustmentTypes.find((t) => t.value === formData.adjustmentType)?.description}
                            </p>
                        </div>

                        {/* Amount */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                {t('cashbox.amountRequired')}
                            </label>
                            <input
                                type="number"
                                step="0.01"
                                required
                                value={formData.amount}
                                onChange={(e) => setFormData({ ...formData, amount: e.target.value })}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                placeholder={t('cashbox.amountPlaceholder')}
                                disabled={loading}
                            />
                        </div>

                        {/* Notes */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                {t('accounts.notesRequired')}
                            </label>
                            <textarea
                                required
                                value={formData.notes}
                                onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                                rows={3}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                                placeholder={t('accounts.reasonForAdjustment')}
                                disabled={loading}
                            />
                        </div>

                        {/* Buttons */}
                        <div className="flex gap-3 pt-4">
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
                                {loading ? t('accounts.creating') : t('accounts.createAdjustmentButton')}
                            </button>
                        </div>
                    </form>
                </div>
            </div>
        </div>
    )
}

export default AdjustmentModal
