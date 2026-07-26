import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import { soaApi } from '../services/soaApi'
import { useToast } from '../components/Toast'
import api from '../services/api'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'

const StatementOfAccount = () => {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError, showWarning } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const [partyType, setPartyType] = useState('Customer')
    const [parties, setParties] = useState([])
    const [selectedParty, setSelectedParty] = useState(null)
    const [startDate, setStartDate] = useState('')
    const [endDate, setEndDate] = useState('')
    const [statement, setStatement] = useState(null)
    const [loading, setLoading] = useState(false)
    const [page, setPage] = useState(1)
    const pageSize = 50

    // Load parties when party type changes
    useEffect(() => {
        loadParties()
    }, [partyType])

    const loadParties = async () => {
        try {
            const endpoint = partyType === 'Customer' ? '/Customers' : '/Suppliers'
            const response = await api.get(endpoint)
            setParties(response.data.items || response.data)
            setSelectedParty(null)
            setStatement(null)
        } catch (error) {
            showError(t('accounts.failedToLoadParties'))
        }
    }

    const loadStatement = async () => {
        if (!selectedParty) {
            showWarning(t('accounts.selectPartyWarning'))
            return
        }

        setLoading(true)
        try {
            const filters = {
                partyType,
                partyId: selectedParty,
                startDate: startDate || null,
                endDate: endDate || null,
                page,
                pageSize,
            }

            const data = await soaApi.getStatement(filters)
            setStatement(data)
        } catch (error) {
            showError(t('accounts.failedToLoadStatement'))
            console.error(error)
        } finally {
            setLoading(false)
        }
    }

    const getTransactionTypeLabel = (type) => {
        const labels = {
            Sale: t('accounts.sale'),
            SalesReturn: t('accounts.salesReturn'),
            Purchase: t('accounts.purchase'),
            PurchaseReturn: t('accounts.purchaseReturn'),
            Payment: t('accounts.payment'),
            PaymentReceived: t('accounts.paymentReceived'),
            PaymentMade: t('accounts.paymentMade'),
            CreditNote: t('accounts.creditNote'),
            DebitNote: t('accounts.debitNote'),
            CreditAdjustment: t('accounts.creditAdjustment'),
            DebitAdjustment: t('accounts.debitAdjustment'),
            OpeningBalance: t('cashbox.openingBalanceType'),
        }
        return labels[type] || type
    }

    const getBalanceColor = (balanceType) => {
        if (balanceType === 'Debit' || balanceType === t('accounts.debit')) return 'text-red-600'
        if (balanceType === 'Credit' || balanceType === t('accounts.credit')) return 'text-green-600'
        return 'text-gray-600'
    }

    return (
        <div dir={dir} className="space-y-6">
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('accounts.statementOfAccount')}</h1>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-lg shadow p-6">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                    {/* Party Type */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('accounts.partyType')}
                        </label>
                        <select
                            value={partyType}
                            onChange={(e) => setPartyType(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="Customer">{t('accounts.customer')}</option>
                            <option value="Supplier">{t('accounts.supplier')}</option>
                        </select>
                    </div>

                    {/* Party Selection */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('accounts.selectParty')} {partyType === 'Customer' ? t('accounts.customer') : t('accounts.supplier')}
                        </label>
                        <select
                            value={selectedParty || ''}
                            onChange={(e) => setSelectedParty(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{partyType === 'Customer' ? t('accounts.allCustomers') : t('accounts.allSuppliers')}</option>
                            {parties.map((party) => (
                                <option key={party.id} value={party.id}>
                                    {party.name}
                                </option>
                            ))}
                        </select>
                    </div>

                    {/* Start Date */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('cashbox.startDate')}
                        </label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* End Date */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('cashbox.endDate')}
                        </label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Load Button */}
                    <div className="flex items-end">
                        <button
                            onClick={loadStatement}
                            disabled={loading}
                            className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400"
                        >
                            {loading ? t('common.loading') : t('accounts.loadStatement')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Summary */}
            {statement && (
                <div className="bg-white rounded-lg shadow p-6">
                    <div className="mb-4">
                        <h2 className="text-xl font-semibold text-gray-900">
                            {statement.partyName || (partyType === 'Customer' ? t('accounts.allCustomers') : t('accounts.allSuppliers'))}
                        </h2>
                        <p className="text-sm text-gray-500">
                            {partyType === 'Customer' ? t('accounts.customerStatement') : t('accounts.supplierStatement')}
                        </p>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                        <div className="bg-gray-50 p-4 rounded-lg">
                            <p className="text-sm text-gray-600 mb-1">{t('accounts.openingBalance')}</p>
                            <p className="text-2xl font-bold text-gray-900">
                                {formatCurrency(statement.summary.openingBalance)}
                            </p>
                        </div>

                        <div className="bg-red-50 p-4 rounded-lg">
                            <p className="text-sm text-gray-600 mb-1">{t('accounts.totalDebit')}</p>
                            <p className="text-2xl font-bold text-red-600">
                                {formatCurrency(statement.summary.totalDebit)}
                            </p>
                        </div>

                        <div className="bg-green-50 p-4 rounded-lg">
                            <p className="text-sm text-gray-600 mb-1">{t('accounts.totalCredit')}</p>
                            <p className="text-2xl font-bold text-green-600">
                                {formatCurrency(statement.summary.totalCredit)}
                            </p>
                        </div>

                        <div className="bg-blue-50 p-4 rounded-lg">
                            <p className="text-sm text-gray-600 mb-1">{t('accounts.closingBalance')}</p>
                            <p className={`text-2xl font-bold ${getBalanceColor(statement.summary.balanceType)}`}>
                                {formatCurrency(statement.summary.closingBalance)}
                            </p>
                            <p className="text-xs text-gray-500 mt-1">
                                {statement.summary.balanceType === 'Debit' ? t('accounts.debit') : t('accounts.credit')}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Transactions Table */}
            {statement && statement.transactions.length > 0 && (
                <div className="bg-white rounded-lg shadow overflow-hidden">
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('common.date')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('cashbox.type')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('accounts.documentNumber')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('accounts.debit')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('accounts.credit')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('accounts.balance')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('cashbox.notes')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {statement.transactions.map((transaction) => (
                                    <tr key={transaction.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                            {formatDateTime(transaction.transactionDate, locale)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                            <span className="px-2 py-1 text-xs font-medium rounded-full bg-blue-100 text-blue-800">
                                                {getTransactionTypeLabel(transaction.transactionType)}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                            {transaction.documentNumber}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-red-600 font-medium">
                                            {transaction.debitAmount > 0 ? formatCurrency(transaction.debitAmount) : '-'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-green-600 font-medium">
                                            {transaction.creditAmount > 0 ? formatCurrency(transaction.creditAmount) : '-'}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-bold text-gray-900">
                                            {formatCurrency(transaction.runningBalance)}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-500">
                                            {transaction.notes || '-'}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>

                    {/* Pagination */}
                    {statement.totalPages > 1 && (
                        <div className="bg-gray-50 px-6 py-4 flex items-center justify-between border-t border-gray-200">
                            <div className="text-sm text-gray-700">
                                {t('accounts.showingPage')} {statement.page} {t('cashbox.of')} {statement.totalPages} ({statement.totalCount} {t('accounts.totalTransactions')})
                            </div>
                            <div className="flex gap-2">
                                <button
                                    onClick={() => setPage(page - 1)}
                                    disabled={page === 1}
                                    className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                                >
                                    {t('cashbox.previous')}
                                </button>
                                <button
                                    onClick={() => setPage(page + 1)}
                                    disabled={page === statement.totalPages}
                                    className="px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                                >
                                    {t('cashbox.next')}
                                </button>
                            </div>
                        </div>
                    )}
                </div>
            )}

            {/* No Data */}
            {statement && statement.transactions.length === 0 && (
                <div className="bg-white rounded-lg shadow p-12 text-center">
                    <p className="text-gray-500">{t('accounts.noTransactionsFound')}</p>
                </div>
            )}
        </div>
    )
}

export default StatementOfAccount
