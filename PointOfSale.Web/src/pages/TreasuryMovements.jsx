import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

export default function TreasuryMovements() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'

    // State for summary
    const [summary, setSummary] = useState(null)

    // State for transaction form
    const [transactionType, setTransactionType] = useState(1) // 1=Deposit, 2=Withdrawal
    const [paymentMethod, setPaymentMethod] = useState(1) // 1=Bank, 2=Visa, 3=Supplier, 4=Customer
    const [targetAccountId, setTargetAccountId] = useState('')
    const [amount, setAmount] = useState('')
    const [transactionDate, setTransactionDate] = useState(new Date().toISOString().split('T')[0])
    const [referenceNumber, setReferenceNumber] = useState('')
    const [notes, setNotes] = useState('')

    // State for accounts dropdown
    const [accounts, setAccounts] = useState([])

    // State for transactions list
    const [transactions, setTransactions] = useState([])
    const [loading, setLoading] = useState(false)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)

    // State for filters
    const [filterStartDate, setFilterStartDate] = useState('')
    const [filterEndDate, setFilterEndDate] = useState('')
    const [filterTransactionType, setFilterTransactionType] = useState('')
    const [filterPaymentMethod, setFilterPaymentMethod] = useState('')

    useEffect(() => {
        fetchSummary()
        fetchTransactions()
    }, [page, filterStartDate, filterEndDate, filterTransactionType, filterPaymentMethod])

    useEffect(() => {
        if (paymentMethod !== 2) { // Not Visa
            fetchAccounts()
        } else {
            setAccounts([])
            setTargetAccountId('')
        }
    }, [paymentMethod])

    const fetchSummary = async () => {
        try {
            const response = await api.get('/treasury/summary')
            setSummary(response.data)
        } catch (error) {
            console.error('Error fetching treasury summary:', error)
        }
    }

    const fetchTransactions = async () => {
        setLoading(true)
        try {
            const params = {
                page,
                pageSize: 50,
                startDate: filterStartDate || undefined,
                endDate: filterEndDate || undefined,
                transactionType: filterTransactionType || undefined,
                paymentMethod: filterPaymentMethod || undefined
            }
            const response = await api.get('/treasury/transactions', { params })
            setTransactions(response.data.items)
            setTotalPages(response.data.totalPages)
        } catch (error) {
            console.error('Error fetching transactions:', error)
            showError(t('treasury.failedToLoadTransactions'))
        } finally {
            setLoading(false)
        }
    }

    const fetchAccounts = async () => {
        try {
            const response = await api.get(`/treasury/accounts/${paymentMethod}`)
            setAccounts(response.data)
        } catch (error) {
            console.error('Error fetching accounts:', error)
            setAccounts([])
        }
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        if (!amount || parseFloat(amount) <= 0) {
            showError(t('treasury.invalidAmount'))
            return
        }

        if (paymentMethod !== 2 && !targetAccountId) {
            showError(t('treasury.selectTargetAccount'))
            return
        }

        try {
            const data = {
                transactionDate,
                transactionType,
                paymentMethod,
                targetAccountId: targetAccountId || null,
                amount: parseFloat(amount),
                referenceNumber: referenceNumber || null,
                notes: notes || null
            }

            await api.post('/treasury', data)
            showSuccess(t('treasury.transactionCreated'))

            // Reset form
            setAmount('')
            setReferenceNumber('')
            setNotes('')
            setTargetAccountId('')

            // Refresh data
            fetchSummary()
            fetchTransactions()
        } catch (error) {
            console.error('Error creating transaction:', error)
            showError(error.response?.data?.message || t('treasury.failedToCreateTransaction'))
        }
    }

    const clearFilters = () => {
        setFilterStartDate('')
        setFilterEndDate('')
        setFilterTransactionType('')
        setFilterPaymentMethod('')
        setPage(1)
    }

    const getPaymentMethodName = (method) => {
        switch (method) {
            case 1: return t('treasury.bank')
            case 2: return t('treasury.visa')
            case 3: return t('treasury.supplier')
            case 4: return t('treasury.customer')
            default: return t('common.unknown')
        }
    }

    return (
        <div dir={dir} className="h-full flex flex-col">
            {/* Header */}
            <div className="mb-6">
                <h1 className="text-2xl font-bold text-gray-800">{t('treasury.title')}</h1>
                <p className="text-gray-600 mt-1">{t('treasury.subtitle')}</p>
            </div>

            {/* Summary Cards */}
            <div className="grid grid-cols-1 md:grid-cols-5 gap-4 mb-6">
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('treasury.currentBalance')}</div>
                    <div className="text-3xl font-bold text-blue-600">
                        {formatCurrency(summary?.currentBalance || 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('treasury.depositsToday')}</div>
                    <div className="text-2xl font-bold text-green-600">
                        {formatCurrency(summary?.totalDepositsToday || 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('treasury.withdrawalsToday')}</div>
                    <div className="text-2xl font-bold text-red-600">
                        {formatCurrency(summary?.totalWithdrawalsToday || 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('treasury.netChangeToday')}</div>
                    <div className={`text-2xl font-bold ${summary?.netChangeToday >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                        {formatCurrency(summary?.netChangeToday || 0)}
                    </div>
                </div>
                <div className="bg-white p-6 rounded-lg shadow">
                    <div className="text-sm font-medium text-gray-500 mb-1">{t('treasury.transactionsToday')}</div>
                    <div className="text-2xl font-bold text-gray-800">
                        {summary?.transactionCountToday || 0}
                    </div>
                </div>
            </div>

            {/* Transaction Form */}
            <div className="bg-white p-6 rounded-lg shadow mb-6">
                <h2 className="text-lg font-semibold text-gray-800 mb-4">{t('treasury.newTransaction')}</h2>
                <form onSubmit={handleSubmit}>
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-4">
                        {/* Transaction Type */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('treasury.transactionType')}
                            </label>
                            <div className="flex gap-2">
                                <button
                                    type="button"
                                    onClick={() => setTransactionType(1)}
                                    className={`flex-1 py-2 px-4 rounded-lg font-medium transition-colors ${transactionType === 1
                                        ? 'bg-green-600 text-white'
                                        : 'bg-gray-200 text-gray-700 hover:bg-gray-300'
                                        }`}
                                >
                                    💵 {t('treasury.deposit')}
                                </button>
                                <button
                                    type="button"
                                    onClick={() => setTransactionType(2)}
                                    className={`flex-1 py-2 px-4 rounded-lg font-medium transition-colors ${transactionType === 2
                                        ? 'bg-red-600 text-white'
                                        : 'bg-gray-200 text-gray-700 hover:bg-gray-300'
                                        }`}
                                >
                                    💸 {t('treasury.withdrawal')}
                                </button>
                            </div>
                        </div>

                        {/* Payment Method */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('treasury.paymentMethod')}
                            </label>
                            <select
                                value={paymentMethod}
                                onChange={(e) => setPaymentMethod(parseInt(e.target.value))}
                                className="input w-full"
                            >
                                <option value={1}>{t('treasury.bank')}</option>
                                <option value={2}>{t('treasury.visa')}</option>
                                <option value={3}>{t('treasury.supplier')}</option>
                                <option value={4}>{t('treasury.customer')}</option>
                            </select>
                        </div>

                        {/* Target Account */}
                        {paymentMethod !== 2 && (
                            <div>
                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                    {t('treasury.targetAccount')}
                                </label>
                                <select
                                    value={targetAccountId}
                                    onChange={(e) => setTargetAccountId(e.target.value)}
                                    className="input w-full"
                                    required
                                >
                                    <option value="">{t('treasury.selectAccount')}</option>
                                    {accounts.map((account) => (
                                        <option key={account.id} value={account.id}>
                                            {account.name}
                                            {account.currentBalance !== null && account.currentBalance !== undefined && ` (${formatCurrency(account.currentBalance)})`}
                                        </option>
                                    ))}
                                </select>
                            </div>
                        )}

                        {/* Amount */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('treasury.amount')}
                            </label>
                            <input
                                type="number"
                                step="0.01"
                                value={amount}
                                onChange={(e) => setAmount(e.target.value)}
                                className="input w-full"
                                placeholder={t('common.amountPlaceholder')}
                                required
                            />
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-4">
                        {/* Transaction Date */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('treasury.transactionDate')}
                            </label>
                            <input
                                type="date"
                                value={transactionDate}
                                onChange={(e) => setTransactionDate(e.target.value)}
                                className="input w-full"
                                required
                            />
                        </div>

                        {/* Reference Number */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('treasury.referenceNumber')}
                            </label>
                            <input
                                type="text"
                                value={referenceNumber}
                                onChange={(e) => setReferenceNumber(e.target.value)}
                                className="input w-full"
                                placeholder={t('treasury.optional')}
                            />
                        </div>

                        {/* Notes */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('treasury.notes')}
                            </label>
                            <input
                                type="text"
                                value={notes}
                                onChange={(e) => setNotes(e.target.value)}
                                className="input w-full"
                                placeholder={t('treasury.optional')}
                            />
                        </div>
                    </div>

                    <div className="flex justify-end">
                        <button type="submit" className="btn-primary">
                            {t('treasury.createTransaction')}
                        </button>
                    </div>
                </form>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-lg shadow mb-6">
                <div className="grid grid-cols-1 md:grid-cols-5 gap-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.startDate')}
                        </label>
                        <input
                            type="date"
                            value={filterStartDate}
                            onChange={(e) => setFilterStartDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.endDate')}
                        </label>
                        <input
                            type="date"
                            value={filterEndDate}
                            onChange={(e) => setFilterEndDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.transactionType')}
                        </label>
                        <select
                            value={filterTransactionType}
                            onChange={(e) => setFilterTransactionType(e.target.value)}
                            className="input w-full"
                        >
                            <option value="">{t('treasury.all')}</option>
                            <option value="1">{t('treasury.deposit')}</option>
                            <option value="2">{t('treasury.withdrawal')}</option>
                        </select>
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('treasury.paymentMethod')}
                        </label>
                        <select
                            value={filterPaymentMethod}
                            onChange={(e) => setFilterPaymentMethod(e.target.value)}
                            className="input w-full"
                        >
                            <option value="">{t('treasury.all')}</option>
                            <option value="1">{t('treasury.bank')}</option>
                            <option value="2">{t('treasury.visa')}</option>
                            <option value="3">{t('treasury.supplier')}</option>
                            <option value="4">{t('treasury.customer')}</option>
                        </select>
                    </div>
                    <div className="flex items-end">
                        <button onClick={clearFilters} className="btn-secondary w-full">
                            {t('treasury.clearFilters')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Transactions Table */}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.transactionNumber')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.date')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.type')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.paymentMethod')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.targetAccount')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.amount')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('treasury.balanceAfter')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.reference')}
                                </th>
                                <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                    {t('treasury.notes')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading ? (
                                <tr>
                                    <td colSpan="9" className="px-6 py-4 text-center text-gray-500">
                                        {t('treasury.loading')}
                                    </td>
                                </tr>
                            ) : transactions.length === 0 ? (
                                <tr>
                                    <td colSpan="9" className="px-6 py-4 text-center text-gray-500">
                                        {t('treasury.noTransactions')}
                                    </td>
                                </tr>
                            ) : (
                                transactions.map((txn) => (
                                    <tr key={txn.id} className="hover:bg-gray-50">
                                        <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                            {txn.transactionNumber}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {formatDate(txn.transactionDate, locale)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm">
                                            <span className={`px-2 py-1 rounded-full text-xs font-medium ${txn.transactionType === 1
                                                ? 'bg-green-100 text-green-800'
                                                : 'bg-red-100 text-red-800'
                                                }`}>
                                                {txn.transactionType === 1 ? t('treasury.deposit') : t('treasury.withdrawal')}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                            {getPaymentMethodName(txn.paymentMethod)}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-900">
                                            {txn.targetAccountName || '-'}
                                        </td>
                                        <td className={`px-6 py-4 whitespace-nowrap text-sm font-medium text-right ${txn.transactionType === 1 ? 'text-green-600' : 'text-red-600'
                                            }`}>
                                            {txn.transactionType === 1 ? '+' : '-'}{formatCurrency(txn.amount)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right font-medium">
                                            {formatCurrency(txn.balanceAfter)}
                                        </td>
                                        <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                            {txn.referenceNumber || '-'}
                                        </td>
                                        <td className="px-6 py-4 text-sm text-gray-500">
                                            {txn.notes || '-'}
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                    <div className="flex-1 flex justify-between sm:hidden">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm"
                        >
                            {t('treasury.previous')}
                        </button>
                        <button
                            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                            disabled={page === totalPages}
                            className="btn-secondary text-sm"
                        >
                            {t('treasury.next')}
                        </button>
                    </div>
                    <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                        <div>
                            <p className="text-sm text-gray-700">
                                {t('treasury.page')} <span className="font-medium">{page}</span> {t('treasury.of')} <span className="font-medium">{totalPages}</span>
                            </p>
                        </div>
                        <div>
                            <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px">
                                <button
                                    onClick={() => setPage(p => Math.max(1, p - 1))}
                                    disabled={page === 1}
                                    className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:bg-gray-100"
                                >
                                    {t('treasury.previous')}
                                </button>
                                <button
                                    onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                                    disabled={page === totalPages}
                                    className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:bg-gray-100"
                                >
                                    {t('treasury.next')}
                                </button>
                            </nav>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    )
}
