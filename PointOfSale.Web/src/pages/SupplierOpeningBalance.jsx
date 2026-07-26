import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'

const SupplierOpeningBalance = () => {
    const { t } = useTranslation()
    const { settings } = useSettings()
    const { showSuccess, showError } = useToast()
    const [suppliers, setSuppliers] = useState([])
    const [loading, setLoading] = useState(false)
    const [saving, setSaving] = useState(false)
    const [balances, setBalances] = useState({})

    useEffect(() => {
        loadSuppliers()
    }, [])

    const loadSuppliers = async () => {
        setLoading(true)
        try {
            const response = await api.get('/suppliers/opening-balances')
            setSuppliers(response.data)

            // Initialize balances state with existing data
            const initialBalances = {}
            response.data.forEach(supplier => {
                if (supplier.hasOpeningBalance) {
                    initialBalances[supplier.supplierId] = {
                        amount: supplier.openingBalanceAmount || 0,
                        balanceType: supplier.balanceType || 'Debit',
                        date: supplier.openingBalanceDate ? new Date(supplier.openingBalanceDate).toISOString().split('T')[0] : new Date().toISOString().split('T')[0],
                        notes: '',
                        hasExisting: true
                    }
                } else {
                    initialBalances[supplier.supplierId] = {
                        amount: 0,
                        balanceType: 'Debit',
                        date: new Date().toISOString().split('T')[0],
                        notes: '',
                        hasExisting: false
                    }
                }
            })
            setBalances(initialBalances)
        } catch (error) {
            console.error('Failed to load suppliers', error)
            showError(t('openingBalance.failedToLoadSuppliers'))
        } finally {
            setLoading(false)
        }
    }

    const handleBalanceChange = (supplierId, field, value) => {
        setBalances(prev => ({
            ...prev,
            [supplierId]: {
                ...prev[supplierId],
                [field]: value
            }
        }))
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        // Prepare data - only include suppliers with amount > 0 and no existing balance
        const supplierBalances = Object.entries(balances)
            .filter(([supplierId, data]) => !data.hasExisting && parseFloat(data.amount) > 0)
            .map(([supplierId, data]) => ({
                supplierId,
                amount: parseFloat(data.amount),
                balanceType: data.balanceType,
                date: data.date,
                notes: data.notes
            }))

        if (supplierBalances.length === 0) {
            showError(t('openingBalance.noBalancesToSave'))
            return
        }

        setSaving(true)
        try {
            await api.post('/suppliers/opening-balances', {
                supplierBalances
            })

            showSuccess(t('openingBalance.balancesSaved', { count: supplierBalances.length }))

            // Reload suppliers to get updated data
            await loadSuppliers()
        } catch (error) {
            showError(error.response?.data?.message || t('openingBalance.failed'))
        } finally {
            setSaving(false)
        }
    }

    if (loading) {
        return (
            <div className="p-6">
                <div className="text-center py-8">{t('common.loading')}</div>
            </div>
        )
    }

    return (
        <div className="p-6">
            <h1 className="text-2xl font-bold text-gray-900 mb-6">
                {t('openingBalance.suppliersTitle')}
            </h1>

            <div className="bg-white rounded-lg shadow-md p-6">
                <div className="mb-4 bg-blue-50 border-l-4 border-blue-400 p-4">
                    <p className="text-sm text-blue-700">
                        <strong>{t('openingBalance.instructions')}</strong> {t('openingBalance.instructionsTextSuppliers')}
                    </p>
                </div>

                <form onSubmit={handleSubmit}>
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.supplierName')}
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.mobileCode')}
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.balanceType')}
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.openingBalanceAmount')}
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.openingBalanceDate')}
                                    </th>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.notes')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {suppliers.map((supplier) => {
                                    const balance = balances[supplier.supplierId] || {}
                                    const isDisabled = balance.hasExisting

                                    return (
                                        <tr key={supplier.supplierId} className={isDisabled ? 'bg-gray-50' : ''}>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                {supplier.supplierName}
                                                {isDisabled && (
                                                    <span className="ml-2 inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                                                        {t('openingBalance.alreadySet')}
                                                    </span>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                {supplier.phone || '-'}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <select
                                                    value={balance.balanceType || 'Debit'}
                                                    onChange={(e) => handleBalanceChange(supplier.supplierId, 'balanceType', e.target.value)}
                                                    disabled={isDisabled}
                                                    className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed"
                                                >
                                                    <option value="Debit">{t('openingBalance.debitSupplier')}</option>
                                                    <option value="Credit">{t('openingBalance.creditSupplier')}</option>
                                                </select>
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <div className="relative rounded-md shadow-sm">
                                                    <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                                                        <span className="text-gray-500 sm:text-sm">{settings?.currencySymbol || '$'}</span>
                                                    </div>
                                                    <input
                                                        type="number"
                                                        step="0.01"
                                                        min="0"
                                                        value={balance.amount || 0}
                                                        onChange={(e) => handleBalanceChange(supplier.supplierId, 'amount', e.target.value)}
                                                        disabled={isDisabled}
                                                        className="focus:ring-blue-500 focus:border-blue-500 block w-full pl-7 pr-3 py-2 sm:text-sm border-gray-300 rounded-md disabled:bg-gray-100 disabled:cursor-not-allowed"
                                                        placeholder="0.00"
                                                    />
                                                </div>
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <input
                                                    type="date"
                                                    value={balance.date || new Date().toISOString().split('T')[0]}
                                                    onChange={(e) => handleBalanceChange(supplier.supplierId, 'date', e.target.value)}
                                                    disabled={isDisabled}
                                                    className="focus:ring-blue-500 focus:border-blue-500 block w-full px-3 py-2 sm:text-sm border-gray-300 rounded-md disabled:bg-gray-100 disabled:cursor-not-allowed"
                                                />
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <input
                                                    type="text"
                                                    value={balance.notes || ''}
                                                    onChange={(e) => handleBalanceChange(supplier.supplierId, 'notes', e.target.value)}
                                                    disabled={isDisabled}
                                                    className="focus:ring-blue-500 focus:border-blue-500 block w-full px-3 py-2 sm:text-sm border-gray-300 rounded-md disabled:bg-gray-100 disabled:cursor-not-allowed"
                                                    placeholder={t('openingBalance.optionalNotes')}
                                                />
                                            </td>
                                        </tr>
                                    )
                                })}
                            </tbody>
                        </table>
                    </div>

                    {suppliers.length === 0 && (
                        <div className="text-center py-8 text-gray-500">
                            {t('openingBalance.noSuppliersFound')}
                        </div>
                    )}

                    <div className="mt-6 flex justify-end">
                        <button
                            type="submit"
                            disabled={saving}
                            className={`inline-flex justify-center py-2 px-6 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 ${saving ? 'opacity-50 cursor-not-allowed' : ''
                                }`}
                        >
                            {saving ? t('openingBalance.savingBalances') : t('openingBalance.saveOpeningBalances')}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    )
}

export default SupplierOpeningBalance
