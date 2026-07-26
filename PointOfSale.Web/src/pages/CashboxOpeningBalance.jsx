import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

const CashboxOpeningBalance = () => {
    const { t } = useTranslation()
    const { settings, formatCurrency } = useSettings()
    const { dir, isRTL, locale } = useRTL()
    const { showSuccess, showError } = useToast()
    const [cashboxes, setCashboxes] = useState([])
    const [selectedCashboxId, setSelectedCashboxId] = useState('')
    const [existingOpeningBalance, setExistingOpeningBalance] = useState(null)
    const [form, setForm] = useState({
        amount: '',
        date: new Date().toISOString().split('T')[0],
        notes: ''
    })
    const [loading, setLoading] = useState(false)
    const [checking, setChecking] = useState(false)

    useEffect(() => {
        loadCashboxes()
    }, [])

    useEffect(() => {
        if (selectedCashboxId) {
            checkOpeningBalance(selectedCashboxId)
        } else {
            setExistingOpeningBalance(null)
        }
    }, [selectedCashboxId])

    const loadCashboxes = async () => {
        try {
            const response = await api.get('/cashbox')
            const items = response.data.items || response.data
            setCashboxes(items)
            if (items.length > 0) {
                setSelectedCashboxId(items[0].id)
            }
        } catch (error) {
            console.error('Failed to load cashboxes', error)
            showError(t('openingBalance.failedToLoad'))
        }
    }

    const checkOpeningBalance = async (id) => {
        setChecking(true)
        try {
            const response = await api.get(`/cashbox/${id}/opening-balance`)
            setExistingOpeningBalance(response.data)
        } catch (error) {
            if (error.response && error.response.status === 404) {
                setExistingOpeningBalance(null)
            } else {
                console.error('Error checking opening balance', error)
            }
        } finally {
            setChecking(false)
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (!selectedCashboxId) return

        setLoading(true)
        try {
            await api.post(`/cashbox/${selectedCashboxId}/opening-balance`, {
                amount: parseFloat(form.amount),
                date: form.date,
                notes: form.notes
            })
            showSuccess(t('openingBalance.success'))
            checkOpeningBalance(selectedCashboxId)
        } catch (error) {
            showError(error.response?.data?.message || t('openingBalance.failed'))
        } finally {
            setLoading(false)
        }
    }

    return (
        <div dir={dir} className="p-6">
            <h1 className="text-2xl font-bold text-gray-900 mb-6">{t('openingBalance.title')}</h1>

            <div className="bg-white rounded-lg shadow-md p-6">
                <div className="mb-6">
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                        {t('openingBalance.selectCashRegister')}
                    </label>
                    <select
                        value={selectedCashboxId}
                        onChange={(e) => setSelectedCashboxId(e.target.value)}
                        className="w-full md:w-1/2 px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                    >
                        {cashboxes.map(box => (
                            <option key={box.id} value={box.id}>
                                {box.name} ({box.warehouseName || 'System-wide'})
                            </option>
                        ))}
                    </select>
                </div>

                {checking ? (
                    <div className="text-center py-4">{t('common.loading')}</div>
                ) : existingOpeningBalance ? (
                    <div className="bg-yellow-50 border-l-4 border-yellow-400 p-4 mb-4">
                        <div className="flex">
                            <div className="flex-shrink-0">
                                <svg className="h-5 w-5 text-yellow-400" viewBox="0 0 20 20" fill="currentColor">
                                    <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                                </svg>
                            </div>
                            <div className={isRTL ? 'mr-3' : 'ml-3'}>
                                <p className="text-sm text-yellow-700">
                                    {t('openingBalance.alreadyDefined')}
                                </p>
                                <div className="mt-2 text-sm text-yellow-700">
                                    <p>{t('openingBalance.amount')}: <strong>{formatCurrency(existingOpeningBalance.amount)}</strong></p>
                                    <p>{t('openingBalance.date')}: {formatDate(existingOpeningBalance.date, locale)}</p>
                                </div>
                            </div>
                        </div>
                    </div>
                ) : (
                    <form onSubmit={handleSubmit} className="space-y-6">
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                {t('openingBalance.currentAvailableCash')}
                            </label>
                            <div className="relative rounded-md shadow-sm md:w-1/2">
                                <div className={`absolute inset-y-0 flex items-center pointer-events-none ${isRTL ? 'right-0 pr-3' : 'left-0 pl-3'}`}>
                                    <span className="text-gray-500 sm:text-sm">{settings?.currencySymbol || '$'}</span>
                                </div>
                                <input
                                    type="number"
                                    step="0.01"
                                    min="0"
                                    required
                                    value={form.amount}
                                    onChange={(e) => setForm({ ...form, amount: e.target.value })}
                                    className={`focus:ring-blue-500 focus:border-blue-500 block w-full sm:text-sm border-gray-300 rounded-md py-2 border ${isRTL ? 'pr-7 pl-12' : 'pl-7 pr-12'}`}
                                    placeholder="0.00"
                                />
                            </div>
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                {t('openingBalance.dateOfOpeningBalance')}
                            </label>
                            <input
                                type="date"
                                required
                                value={form.date}
                                onChange={(e) => setForm({ ...form, date: e.target.value })}
                                className="focus:ring-blue-500 focus:border-blue-500 block w-full md:w-1/2 sm:text-sm border-gray-300 rounded-md py-2 border px-3"
                            />
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-2">
                                {t('openingBalance.optionalNotes')}
                            </label>
                            <textarea
                                rows={3}
                                value={form.notes}
                                onChange={(e) => setForm({ ...form, notes: e.target.value })}
                                className="focus:ring-blue-500 focus:border-blue-500 block w-full sm:text-sm border-gray-300 rounded-md py-2 border px-3"
                                placeholder={t('openingBalance.enterNotes')}
                            />
                        </div>

                        <div className="flex justify-end">
                            <button
                                type="submit"
                                disabled={loading}
                                className={`inline-flex justify-center py-2 px-4 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 ${loading ? 'opacity-50 cursor-not-allowed' : ''
                                    }`}
                            >
                                {loading ? t('openingBalance.saving') : t('openingBalance.saveOpeningBalance')}
                            </button>
                        </div>
                    </form>
                )}
            </div>
        </div>
    )
}

export default CashboxOpeningBalance
