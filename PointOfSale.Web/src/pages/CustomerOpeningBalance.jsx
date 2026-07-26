import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'

const CustomerOpeningBalance = () => {
    const { t } = useTranslation()
    const { settings } = useSettings()
    const { showSuccess, showError } = useToast()
    const [customers, setCustomers] = useState([])
    const [loading, setLoading] = useState(false)
    const [saving, setSaving] = useState(false)
    const [balances, setBalances] = useState({})

    useEffect(() => {
        loadCustomers()
    }, [])

    const loadCustomers = async () => {
        setLoading(true)
        try {
            const response = await api.get('/customers/opening-balances')
            setCustomers(response.data)

            // Initialize balances state with existing data
            const initialBalances = {}
            response.data.forEach(customer => {
                if (customer.hasOpeningBalance) {
                    initialBalances[customer.customerId] = {
                        amount: customer.openingBalanceAmount || 0,
                        balanceType: customer.balanceType || 'Debit',
                        date: customer.openingBalanceDate ? new Date(customer.openingBalanceDate).toISOString().split('T')[0] : new Date().toISOString().split('T')[0],
                        notes: '',
                        hasExisting: true
                    }
                } else {
                    initialBalances[customer.customerId] = {
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
            console.error('Failed to load customers', error)
            showError(t('openingBalance.failedToLoadCustomers'))
        } finally {
            setLoading(false)
        }
    }

    const handleBalanceChange = (customerId, field, value) => {
        setBalances(prev => ({
            ...prev,
            [customerId]: {
                ...prev[customerId],
                [field]: value
            }
        }))
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        // Prepare data - only include customers with amount > 0 and no existing balance
        const customerBalances = Object.entries(balances)
            .filter(([customerId, data]) => !data.hasExisting && parseFloat(data.amount) > 0)
            .map(([customerId, data]) => ({
                customerId,
                amount: parseFloat(data.amount),
                balanceType: data.balanceType,
                date: data.date,
                notes: data.notes
            }))

        if (customerBalances.length === 0) {
            showError(t('openingBalance.noBalancesToSave'))
            return
        }

        setSaving(true)
        try {
            await api.post('/customers/opening-balances', {
                customerBalances
            })

            showSuccess(t('openingBalance.balancesSaved', { count: customerBalances.length }))

            // Reload customers to get updated data
            await loadCustomers()
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
                {t('openingBalance.customersTitle')}
            </h1>

            <div className="bg-white rounded-lg shadow-md p-6">
                <div className="mb-4 bg-blue-50 border-l-4 border-blue-400 p-4">
                    <p className="text-sm text-blue-700">
                        <strong>{t('openingBalance.instructions')}</strong> {t('openingBalance.instructionsText')}
                    </p>
                </div>

                <form onSubmit={handleSubmit}>
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('openingBalance.customerName')}
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
                                {customers.map((customer) => {
                                    const balance = balances[customer.customerId] || {}
                                    const isDisabled = balance.hasExisting

                                    return (
                                        <tr key={customer.customerId} className={isDisabled ? 'bg-gray-50' : ''}>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                {customer.customerName}
                                                {isDisabled && (
                                                    <span className="ml-2 inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                                                        {t('openingBalance.alreadySet')}
                                                    </span>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                {customer.phone || '-'}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <select
                                                    value={balance.balanceType || 'Debit'}
                                                    onChange={(e) => handleBalanceChange(customer.customerId, 'balanceType', e.target.value)}
                                                    disabled={isDisabled}
                                                    className="block w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm disabled:bg-gray-100 disabled:cursor-not-allowed"
                                                >
                                                    <option value="Debit">{t('openingBalance.debit')}</option>
                                                    <option value="Credit">{t('openingBalance.credit')}</option>
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
                                                        onChange={(e) => handleBalanceChange(customer.customerId, 'amount', e.target.value)}
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
                                                    onChange={(e) => handleBalanceChange(customer.customerId, 'date', e.target.value)}
                                                    disabled={isDisabled}
                                                    className="focus:ring-blue-500 focus:border-blue-500 block w-full px-3 py-2 sm:text-sm border-gray-300 rounded-md disabled:bg-gray-100 disabled:cursor-not-allowed"
                                                />
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <input
                                                    type="text"
                                                    value={balance.notes || ''}
                                                    onChange={(e) => handleBalanceChange(customer.customerId, 'notes', e.target.value)}
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

                    {customers.length === 0 && (
                        <div className="text-center py-8 text-gray-500">
                            {t('openingBalance.noCustomersFound')}
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

export default CustomerOpeningBalance
