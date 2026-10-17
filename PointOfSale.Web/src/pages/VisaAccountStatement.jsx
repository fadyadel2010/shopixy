import { useState, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useReactToPrint } from 'react-to-print'
import * as XLSX from 'xlsx'
import { useRTL } from '../hooks/useRTL'
import { formatDate, formatDateTime } from '../utils/dateFormat'

const VisaAccountStatement = () => {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const footerAlignClass = isRTL ? 'text-left' : 'text-right'

    const [filters, setFilters] = useState({
        fromDate: '',
        toDate: '',
        transactionType: 0,
        searchTerm: '',
        includeOpeningBalance: true
    })
    const [reportData, setReportData] = useState(null)
    const [loading, setLoading] = useState(false)
    const [selectedTransaction, setSelectedTransaction] = useState(null)
    const [showDetailsModal, setShowDetailsModal] = useState(false)
    const [detailsLoading, setDetailsLoading] = useState(false)
    const componentRef = useRef()

    // Transaction types
    const transactionTypes = [
        { value: 0, label: t('visaStatement.allTypes') },
        { value: 1, label: t('visaStatement.saleByVisa') },
        { value: 2, label: t('visaStatement.cashTransferToVisa') },
        { value: 3, label: t('visaStatement.cashTransferFromVisa') },
        { value: 4, label: t('visaStatement.bankTransferToVisa') },
        { value: 5, label: t('visaStatement.bankTransferFromVisa') },
        { value: 6, label: t('visaStatement.return') },
        { value: 7, label: t('visaStatement.expense') },
        { value: 8, label: t('visaStatement.openingBalance') },
        { value: 9, label: t('visaStatement.adjustment') },
        { value: 10, label: t('visaStatement.purchase') }
    ]

    const handleFilterChange = (e) => {
        const { name, value, type, checked } = e.target
        setFilters(prev => ({
            ...prev,
            [name]: type === 'checkbox' ? checked : value
        }))
    }

    const fetchStatement = async () => {
        setLoading(true)
        try {
            const response = await api.post('/visastatement/statement', {
                fromDate: filters.fromDate || null,
                toDate: filters.toDate || null,
                transactionType: parseInt(filters.transactionType),
                searchTerm: filters.searchTerm || null,
                includeOpeningBalance: filters.includeOpeningBalance
            })
            setReportData(response.data)
        } catch (error) {
            console.error('Error fetching Visa statement:', error)
            showError(t('visaStatement.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const handleRowClick = async (transaction) => {
        if (!transaction.sourceEntityId || transaction.id === '00000000-0000-0000-0000-000000000000') {
            return // Skip opening balance or transactions without source
        }

        setDetailsLoading(true)
        setShowDetailsModal(true)

        try {
            const response = await api.get(`/visastatement/transaction/${transaction.id}`)
            setSelectedTransaction(response.data)
        } catch (error) {
            console.error('Error fetching transaction details:', error)
            showError(t('visaStatement.failedToLoadDetails'))
        } finally {
            setDetailsLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: `Visa_Statement_${new Date().toISOString().slice(0, 10)}`,
    })

    const handleExportExcel = () => {
        if (!reportData?.items) return

        const dataToExport = reportData.items.map(item => ({
            [t('common.date')]: formatDate(item.transactionDate, locale),
            [t('visaStatement.transactionType')]: item.transactionType,
            [t('visaStatement.reference')]: item.referenceNumber,
            [t('common.description')]: item.description,
            [t('visaStatement.debit')]: item.debitAmount,
            [t('visaStatement.credit')]: item.creditAmount,
            [t('visaStatement.balanceAfter')]: item.balanceAfter
        }))

        const ws = XLSX.utils.json_to_sheet(dataToExport)
        const wb = XLSX.utils.book_new()
        XLSX.utils.book_append_sheet(wb, ws, "Visa Statement")
        XLSX.writeFile(wb, `Visa_Statement_${new Date().toISOString().slice(0, 10)}.xlsx`)
    }

    const getRowClass = (rowType) => {
        switch (rowType) {
            case 'OpeningBalance': return 'bg-gray-100 font-semibold'
            default: return ''
        }
    }

    const getTransactionTypeColor = (typeId) => {
        switch (typeId) {
            case 1: return 'bg-green-100 text-green-800' // Sale by Visa
            case 2: return 'bg-blue-100 text-blue-800'   // Cash to Visa
            case 3: return 'bg-orange-100 text-orange-800' // Cash from Visa
            case 4: return 'bg-purple-100 text-purple-800' // Bank to Visa
            case 5: return 'bg-pink-100 text-pink-800'   // Bank from Visa
            case 6: return 'bg-red-100 text-red-800'     // Return
            case 7: return 'bg-yellow-100 text-yellow-800' // Expense
            case 8: return 'bg-gray-100 text-gray-800'   // Opening Balance
            case 9: return 'bg-indigo-100 text-indigo-800' // Adjustment
            case 10: return 'bg-teal-100 text-teal-800'  // Purchase
            default: return 'bg-gray-100 text-gray-800'
        }
    }

    return (
        <div dir={dir} className="space-y-6">
            {/* Header */}
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('visaStatement.title')}</h1>
                    <p className="text-gray-600 mt-1">{t('visaStatement.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handlePrint}
                        disabled={!reportData}
                        className="px-4 py-2 bg-gray-600 text-white rounded-lg hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                    >
                        🖨️ {t('common.print')}
                    </button>
                    <button
                        onClick={handleExportExcel}
                        disabled={!reportData}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                    >
                        📊 {t('visaStatement.exportExcel')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-lg shadow p-6">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                    {/* Date From */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('visaStatement.dateFrom')}
                        </label>
                        <input
                            type="date"
                            name="fromDate"
                            value={filters.fromDate}
                            onChange={handleFilterChange}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Date To */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('visaStatement.dateTo')}
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
                            {t('visaStatement.transactionType')}
                        </label>
                        <select
                            name="transactionType"
                            value={filters.transactionType}
                            onChange={handleFilterChange}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            {transactionTypes.map(type => (
                                <option key={type.value} value={type.value}>
                                    {type.label}
                                </option>
                            ))}
                        </select>
                    </div>

                    {/* Search */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('visaStatement.search')}
                        </label>
                        <input
                            type="text"
                            name="searchTerm"
                            value={filters.searchTerm}
                            onChange={handleFilterChange}
                            placeholder={t('visaStatement.searchPlaceholder')}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>

                    {/* Generate Button */}
                    <div className="flex items-end">
                        <button
                            onClick={fetchStatement}
                            disabled={loading}
                            className="w-full px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 flex items-center justify-center gap-2"
                        >
                            {loading ? (
                                <>
                                    <span className="animate-spin">⏳</span>
                                    {t('common.loading')}
                                </>
                            ) : (
                                <>
                                    📋 {t('visaStatement.generateReport')}
                                </>
                            )}
                        </button>
                    </div>
                </div>

                {/* Include Opening Balance Checkbox */}
                <div className="mt-4">
                    <label className="flex items-center gap-2 cursor-pointer">
                        <input
                            type="checkbox"
                            name="includeOpeningBalance"
                            checked={filters.includeOpeningBalance}
                            onChange={handleFilterChange}
                            className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500"
                        />
                        <span className="text-sm text-gray-700">
                            {t('visaStatement.includeOpeningBalance')}
                        </span>
                    </label>
                </div>
            </div>

            {/* Report Content */}
            {reportData && (
                <div className="bg-white rounded-lg shadow overflow-hidden" ref={componentRef}>
                    {/* Summary Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-4 gap-4 p-6 bg-gradient-to-r from-indigo-50 to-purple-50">
                        <div className="bg-white p-4 rounded-lg shadow-sm border-l-4 border-gray-500">
                            <p className="text-sm text-gray-600 mb-1">{t('visaStatement.openingBalance')}</p>
                            <p className={`text-2xl font-bold ${reportData.summary.openingBalance >= 0 ? 'text-gray-700' : 'text-red-600'}`}>
                                {formatCurrency(reportData.summary.openingBalance)}
                            </p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm border-l-4 border-green-500">
                            <p className="text-sm text-gray-600 mb-1">{t('visaStatement.totalDebit')}</p>
                            <p className="text-2xl font-bold text-green-600">
                                {formatCurrency(reportData.summary.totalDebit)}
                            </p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm border-l-4 border-red-500">
                            <p className="text-sm text-gray-600 mb-1">{t('visaStatement.totalCredit')}</p>
                            <p className="text-2xl font-bold text-red-600">
                                {formatCurrency(reportData.summary.totalCredit)}
                            </p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm border-l-4 border-blue-500">
                            <p className="text-sm text-gray-600 mb-1">{t('visaStatement.closingBalance')}</p>
                            <p className={`text-2xl font-bold ${reportData.summary.closingBalance >= 0 ? 'text-blue-600' : 'text-red-600'}`}>
                                {formatCurrency(reportData.summary.closingBalance)}
                            </p>
                        </div>
                    </div>

                    {/* Table */}
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('common.date')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('visaStatement.transactionType')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('visaStatement.reference')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('common.description')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('visaStatement.debit')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('visaStatement.credit')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('visaStatement.balanceAfter')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {reportData.items.length > 0 ? (
                                    reportData.items.map((item, index) => (
                                        <tr
                                            key={index}
                                            className={`hover:bg-gray-50 ${getRowClass(item.rowType)} ${item.sourceEntityId ? 'cursor-pointer' : ''}`}
                                            onClick={() => handleRowClick(item)}
                                        >
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {formatDate(item.transactionDate, locale)}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                <span className={`px-2 py-1 text-xs font-medium rounded-full ${getTransactionTypeColor(item.transactionTypeId)}`}>
                                                    {item.transactionType}
                                                </span>
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                {item.referenceNumber}
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
                                                {formatCurrency(item.balanceAfter)}
                                            </td>
                                        </tr>
                                    ))
                                ) : (
                                    <tr>
                                        <td colSpan="7" className="px-6 py-12 text-center text-gray-500">
                                            {t('visaStatement.noTransactions')}
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
                                        {formatCurrency(reportData.summary.totalDebit)}
                                    </td>
                                    <td className="px-6 py-4 text-right text-red-600">
                                        {formatCurrency(reportData.summary.totalCredit)}
                                    </td>
                                    <td className="px-6 py-4 text-right text-blue-600">
                                        {formatCurrency(reportData.summary.closingBalance)}
                                    </td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>
                </div>
            )}

            {/* Transaction Details Modal */}
            {showDetailsModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <div dir={dir} className="bg-white rounded-lg shadow-xl max-w-2xl w-full mx-4 max-h-[90vh] overflow-y-auto">
                        <div className="p-6">
                            <div className="flex justify-between items-center mb-4">
                                <h2 className="text-xl font-bold text-gray-900">
                                    {t('visaStatement.transactionDetails')}
                                </h2>
                                <button
                                    onClick={() => {
                                        setShowDetailsModal(false)
                                        setSelectedTransaction(null)
                                    }}
                                    className="text-gray-400 hover:text-gray-600"
                                >
                                    <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                    </svg>
                                </button>
                            </div>

                            {detailsLoading ? (
                                <div className="text-center py-8">
                                    <span className="text-2xl animate-spin inline-block">⏳</span>
                                    <p className="mt-2 text-gray-600">{t('common.loading')}</p>
                                </div>
                            ) : selectedTransaction ? (
                                <div className="space-y-4">
                                    {/* Transaction Info */}
                                    <div className="grid grid-cols-2 gap-4 bg-gray-50 p-4 rounded-lg">
                                        <div>
                                            <p className="text-sm text-gray-600">{t('common.date')}</p>
                                            <p className="font-medium">{formatDateTime(selectedTransaction.transactionDate, locale)}</p>
                                        </div>
                                        <div>
                                            <p className="text-sm text-gray-600">{t('visaStatement.transactionType')}</p>
                                            <p className="font-medium">{selectedTransaction.transactionType}</p>
                                        </div>
                                        <div>
                                            <p className="text-sm text-gray-600">{t('visaStatement.reference')}</p>
                                            <p className="font-medium">{selectedTransaction.referenceNumber || '-'}</p>
                                        </div>
                                        <div>
                                            <p className="text-sm text-gray-600">{t('common.amount')}</p>
                                            <p className={`font-medium ${selectedTransaction.isDebit ? 'text-green-600' : 'text-red-600'}`}>
                                                {selectedTransaction.isDebit ? '+' : '-'}{formatCurrency(selectedTransaction.amount)}
                                            </p>
                                        </div>
                                    </div>

                                    {/* Description & Notes */}
                                    {selectedTransaction.description && (
                                        <div>
                                            <p className="text-sm text-gray-600">{t('common.description')}</p>
                                            <p className="font-medium">{selectedTransaction.description}</p>
                                        </div>
                                    )}
                                    {selectedTransaction.notes && (
                                        <div>
                                            <p className="text-sm text-gray-600">{t('visaStatement.notes')}</p>
                                            <p className="font-medium">{selectedTransaction.notes}</p>
                                        </div>
                                    )}

                                    {/* Source Details */}
                                    {selectedTransaction.sourceDetails && (
                                        <div className="border-t pt-4">
                                            <h3 className="font-bold text-gray-900 mb-3">{t('visaStatement.sourceDetails')}</h3>
                                            <div className="bg-blue-50 p-4 rounded-lg">
                                                <p className="text-sm text-blue-600 font-medium mb-2">
                                                    {selectedTransaction.sourceDetails.type}
                                                </p>
                                                <div className="grid grid-cols-2 gap-2 text-sm">
                                                    {Object.entries(selectedTransaction.sourceDetails)
                                                        .filter(([key]) => key !== 'type')
                                                        .map(([key, value]) => (
                                                            <div key={key}>
                                                                <span className="text-gray-600 capitalize">{key}: </span>
                                                                <span className="font-medium">
                                                                    {typeof value === 'number' ? formatCurrency(value) :
                                                                        value instanceof Date ? formatDateTime(value, locale) :
                                                                            String(value)}
                                                                </span>
                                                            </div>
                                                        ))
                                                    }
                                                </div>
                                            </div>
                                        </div>
                                    )}

                                    {/* Balance After */}
                                    <div className="border-t pt-4">
                                        <p className="text-sm text-gray-600">{t('visaStatement.balanceAfter')}</p>
                                        <p className="text-xl font-bold text-blue-600">
                                            {formatCurrency(selectedTransaction.balanceAfter)}
                                        </p>
                                    </div>
                                </div>
                            ) : null}

                            <div className="mt-6 flex justify-end">
                                <button
                                    onClick={() => {
                                        setShowDetailsModal(false)
                                        setSelectedTransaction(null)
                                    }}
                                    className="px-4 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300"
                                >
                                    {t('common.close')}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    )
}

export default VisaAccountStatement
