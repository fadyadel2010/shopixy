import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useReactToPrint } from 'react-to-print'
import * as XLSX from 'xlsx'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

const BankStatement = () => {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const footerAlignClass = isRTL ? 'text-left' : 'text-right'
    const [filters, setFilters] = useState({
        bankId: '',
        bankName: '',
        fromDate: '',
        toDate: '',
        transactionType: 0,
        showZeroBalances: false,
        includeOpeningBalance: true
    })
    const [reportData, setReportData] = useState(null)
    const [loading, setLoading] = useState(false)
    const componentRef = useRef()

    // PERFORMANCE: Async bank loader instead of loading all with pageSize=1000
    const loadBanksAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/banks?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected bank for display
    const selectedBank = useMemo(() =>
        filters.bankId ? { id: filters.bankId, bankName: filters.bankName || '' } : null,
        [filters.bankId, filters.bankName]
    )

    const handleFilterChange = (e) => {
        const { name, value, type, checked } = e.target
        setFilters(prev => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value
        }))
    }

    const validateFilters = () => {
        if (!filters.bankId) {
            showError(t('banks.statement.validation.bankRequired'))
            return false
        }
        if (filters.fromDate && filters.toDate && new Date(filters.fromDate) > new Date(filters.toDate)) {
            showError(t('banks.statement.validation.invalidDateRange'))
            return false
        }
        return true
    }

    const fetchReport = async () => {
        if (!validateFilters()) return

        setLoading(true)
        try {
            const response = await api.post('/banks/statement', {
                bankId: filters.bankId,
                fromDate: filters.fromDate || null,
                toDate: filters.toDate || null,
                transactionType: parseInt(filters.transactionType),
                showZeroBalances: filters.showZeroBalances,
                includeOpeningBalance: filters.includeOpeningBalance
            })
            setReportData(response.data)
        } catch (error) {
            console.error('Error fetching bank statement:', error)
            showError(t('common.error'))
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: `Bank_Statement_${new Date().toISOString().slice(0, 10)}`,
    })

    const handleExportExcel = () => {
        if (!reportData?.items) return

        const dataToExport = reportData.items.map(item => ({
            [t('banks.statement.transactionDate')]: formatDate(item.transactionDate, locale),
            [t('banks.statement.transactionType')]: item.transactionType,
            [t('banks.statement.reference')]: item.referenceNumber,
            [t('banks.statement.description')]: item.description,
            [t('banks.statement.debit')]: item.debitAmount,
            [t('banks.statement.credit')]: item.creditAmount,
            [t('banks.statement.runningBalance')]: item.runningBalance
        }))

        const ws = XLSX.utils.json_to_sheet(dataToExport)
        const wb = XLSX.utils.book_new()
        XLSX.utils.book_append_sheet(wb, ws, "Bank Statement")
        XLSX.writeFile(wb, `Bank_Statement_${new Date().toISOString().slice(0, 10)}.xlsx`)
    }

    const getRowClass = (rowType) => {
        switch (rowType) {
            case 'Deposit': return 'bg-green-50'
            case 'Withdrawal': return 'bg-red-50'
            case 'Transfer': return 'bg-blue-50'
            case 'OpeningBalance': return 'bg-gray-100 font-semibold'
            default: return ''
        }
    }

    const getTransactionTypeLabel = (type) => {
        const labels = {
            'Deposit': t('banks.statement.deposit'),
            'Withdrawal': t('banks.statement.withdrawal'),
            'Transfer In': t('banks.statement.transferIn'),
            'Transfer Out': t('banks.statement.transferOut'),
            'Opening Balance': t('banks.statement.openingBalance'),
            'Balance Brought Forward': t('banks.statement.openingBalance'),
        }
        return labels[type] || type
    }

    return (
        <div dir={dir} className="space-y-6">
            {/* Header */}
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('banks.statement.title')}</h1>
                <div className="flex gap-2">
                    <button
                        onClick={handlePrint}
                        disabled={!reportData}
                        className="px-4 py-2 bg-gray-600 text-white rounded-lg hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        🖨️ {t('banks.statement.print')}
                    </button>
                    <button
                        onClick={handleExportExcel}
                        disabled={!reportData}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                        📊 {t('banks.statement.exportExcel')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-lg shadow p-6">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                    {/* Bank Selection */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('banks.statement.bank')} *
                        </label>
                        <AsyncEntitySelect
                            value={selectedBank}
                            onChange={(bank) => setFilters(prev => ({
                                ...prev,
                                bankId: bank?.id || '',
                                bankName: bank?.bankName || ''
                            }))}
                            loadOptions={loadBanksAsync}
                            getOptionLabel={(bank) => bank?.bankName || ''}
                            placeholder={t('banks.statement.selectBank')}
                            className="w-full"
                        />
                    </div>

                    {/* Date Range */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('banks.statement.fromDate')}
                        </label>
                        <input
                            type="date"
                            name="fromDate"
                            value={filters.fromDate}
                            onChange={handleFilterChange}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('banks.statement.toDate')}
                        </label>
                        <input
                            type="date"
                            name="toDate"
                            value={filters.toDate}
                            onChange={handleFilterChange}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Transaction Type */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('banks.statement.transactionType')}
                        </label>
                        <select
                            name="transactionType"
                            value={filters.transactionType}
                            onChange={handleFilterChange}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="0">{t('banks.statement.allTypes')}</option>
                            <option value="1">{t('banks.statement.deposit')}</option>
                            <option value="2">{t('banks.statement.withdrawal')}</option>
                            <option value="3">{t('banks.statement.transferIn')}</option>
                            <option value="4">{t('banks.statement.transferOut')}</option>
                            <option value="5">{t('banks.statement.openingBalance')}</option>
                        </select>
                    </div>

                    {/* Generate Button */}
                    <div className="flex items-end">
                        <button
                            onClick={fetchReport}
                            disabled={loading}
                            className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400"
                        >
                            {loading ? t('common.loading') : t('banks.statement.generateReport')}
                        </button>
                    </div>
                </div>

                {/* Checkboxes */}
                <div className="flex gap-6 mt-4">
                    <label className="flex items-center gap-2 cursor-pointer">
                        <input
                            type="checkbox"
                            name="showZeroBalances"
                            checked={filters.showZeroBalances}
                            onChange={handleFilterChange}
                            className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500"
                        />
                        <span className="text-sm text-gray-700">
                            {t('banks.statement.showZeroBalances')}
                        </span>
                    </label>
                    <label className="flex items-center gap-2 cursor-pointer">
                        <input
                            type="checkbox"
                            name="includeOpeningBalance"
                            checked={filters.includeOpeningBalance}
                            onChange={handleFilterChange}
                            className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500"
                        />
                        <span className="text-sm text-gray-700">
                            {t('banks.statement.includeOpeningBalance')}
                        </span>
                    </label>
                </div>
            </div>

            {/* Report Content */}
            {reportData && (
                <div className="bg-white rounded-lg shadow overflow-hidden" ref={componentRef}>
                    {/* Summary Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-4 gap-4 p-6 bg-gray-50">
                        <div className="bg-white p-4 rounded-lg shadow-sm">
                            <p className="text-sm text-gray-600 mb-1">{t('banks.statement.totalDeposits')}</p>
                            <p className="text-2xl font-bold text-green-600">{formatCurrency(reportData.summary.totalDeposits)}</p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm">
                            <p className="text-sm text-gray-600 mb-1">{t('banks.statement.totalWithdrawals')}</p>
                            <p className="text-2xl font-bold text-red-600">{formatCurrency(reportData.summary.totalWithdrawals)}</p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm">
                            <p className="text-sm text-gray-600 mb-1">{t('banks.statement.netBalance')}</p>
                            <p className={`text-2xl font-bold ${reportData.summary.netBalance >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                {formatCurrency(reportData.summary.netBalance)}
                            </p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm border-2 border-blue-500">
                            <p className="text-sm text-gray-600 mb-1">{t('banks.statement.endingBalance')}</p>
                            <p className="text-2xl font-bold text-blue-600">
                                {formatCurrency(reportData.summary.endingBalance)}
                            </p>
                        </div>
                    </div>

                    {/* Table */}
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('banks.statement.transactionDate')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('banks.statement.transactionType')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('banks.statement.reference')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('banks.statement.description')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('banks.statement.debit')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('banks.statement.credit')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('banks.statement.runningBalance')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {reportData.items.length > 0 ? (
                                    reportData.items.map((item, index) => (
                                        <tr key={index} className={`hover:bg-gray-50 ${getRowClass(item.rowType)}`}>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {formatDate(item.transactionDate, locale)}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                <span className={`px-2 py-1 text-xs font-medium rounded-full 
                                                    ${item.rowType === 'Deposit' ? 'bg-green-100 text-green-800' :
                                                        item.rowType === 'Withdrawal' ? 'bg-red-100 text-red-800' :
                                                            item.rowType === 'Transfer' ? 'bg-blue-100 text-blue-800' : 'bg-gray-100 text-gray-800'}`}>
                                                    {getTransactionTypeLabel(item.transactionType)}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                {item.referenceNumber || '-'}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-900">
                                                {item.description}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-green-600 font-medium">
                                                {item.debitAmount > 0 ? formatCurrency(item.debitAmount) : '-'}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-red-600 font-medium">
                                                {item.creditAmount > 0 ? formatCurrency(item.creditAmount) : '-'}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-bold text-gray-900">
                                                {formatCurrency(item.runningBalance)}
                                            </td>
                                        </tr>
                                    ))
                                ) : (
                                    <tr>
                                        <td colSpan="7" className="px-6 py-12 text-center text-gray-500">
                                            {t('banks.statement.noTransactions')}
                                        </td>
                                    </tr>
                                )}
                            </tbody>
                            {/* Summary Footer */}
                            <tfoot className="bg-gray-100 font-bold border-t-2 border-gray-300">
                                <tr>
                                    <td colSpan="4" className={`px-6 py-4 text-gray-700 ${footerAlignClass}`}>
                                        {t('common.total')}
                                    </td>
                                    <td className="px-6 py-4 text-right text-green-600">
                                        {formatCurrency(reportData.summary.totalDeposits)}
                                    </td>
                                    <td className="px-6 py-4 text-right text-red-600">
                                        {formatCurrency(reportData.summary.totalWithdrawals)}
                                    </td>
                                    <td className="px-6 py-4 text-right text-blue-600">
                                        {formatCurrency(reportData.summary.endingBalance)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>
                </div>
            )}
        </div>
    )
}

export default BankStatement
