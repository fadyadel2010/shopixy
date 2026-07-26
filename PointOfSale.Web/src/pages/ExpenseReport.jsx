import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useReactToPrint } from 'react-to-print'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { loadExcelModule } from '../utils/reportExports'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

const ExpenseReport = () => {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const [categories, setCategories] = useState([])
    const [reportData, setReportData] = useState(null)
    const [loading, setLoading] = useState(false)
    const componentRef = useRef()
    const [filters, setFilters] = useState({
        fromDate: '',
        toDate: '',
        categoryId: '',
        paymentMethod: '',
        supplierId: '',
        supplierName: '',
        isApproved: ''
    })

    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const summaryLabelAlignClass = isRTL ? 'text-left' : 'text-right'

    // PERFORMANCE: Async supplier loader instead of loading all with pageSize=1000
    const loadSuppliersAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/suppliers?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected supplier for display
    const selectedSupplier = useMemo(() =>
        filters.supplierId ? { id: filters.supplierId, name: filters.supplierName || '' } : null,
        [filters.supplierId, filters.supplierName]
    )

    useEffect(() => {
        loadCategories()
    }, [])

    const loadCategories = async () => {
        try {
            const response = await api.get('/expensecategories')
            setCategories(response.data)
        } catch (error) {
            console.error('Error loading categories:', error)
        }
    }

    const generateReport = async () => {
        setLoading(true)
        try {
            const payload = {
                fromDate: filters.fromDate || null,
                toDate: filters.toDate || null,
                categoryId: filters.categoryId || null,
                paymentMethod: filters.paymentMethod || null,
                supplierId: filters.supplierId || null,
                isApproved: filters.isApproved === '' ? null : filters.isApproved === 'true'
            }
            const response = await api.post('/expenses/report', payload)
            setReportData(response.data)
        } catch (error) {
            console.error('Error generating report:', error)
            showError(t('common.error'))
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: `Expense_Report_${new Date().toISOString().slice(0, 10)}`,
    })

    const handleExportExcel = async () => {
        if (!reportData?.items) return
        const XLSX = await loadExcelModule()

        const dataToExport = reportData.items.map(item => ({
            [t('common.date')]: formatDate(item.expenseDate, locale),
            [t('expenses.category')]: item.categoryName,
            [t('expenses.description')]: item.description,
            [t('expenses.amount')]: item.amount,
            [t('expenses.paymentMethod')]: item.paymentMethod,
            [t('expenses.supplier')]: item.supplierName || '-',
            [t('common.status')]: item.isApproved ? t('expenses.approved') : t('expenses.pending')
        }))

        const ws = XLSX.utils.json_to_sheet(dataToExport)
        const wb = XLSX.utils.book_new()
        XLSX.utils.book_append_sheet(wb, ws, "Expense Report")
        XLSX.writeFile(wb, `Expense_Report_${new Date().toISOString().slice(0, 10)}.xlsx`)
    }
    return (
        <div dir={dir} className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            {/* Header */}
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-900">{t('expenses.expenseReport')}</h1>
                <div className="flex gap-2">
                    <button
                        onClick={handlePrint}
                        disabled={!reportData}
                        className="px-4 py-2 bg-gray-600 text-white rounded-lg hover:bg-gray-700 disabled:opacity-50"
                    >
                        🖨️ {t('expenses.print')}
                    </button>
                    <button
                        onClick={handleExportExcel}
                        disabled={!reportData}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50"
                    >
                        📊 {t('expenses.exportExcel')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-lg shadow p-6">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.fromDate')}
                        </label>
                        <input
                            type="date"
                            value={filters.fromDate}
                            onChange={(e) => setFilters({ ...filters, fromDate: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.toDate')}
                        </label>
                        <input
                            type="date"
                            value={filters.toDate}
                            onChange={(e) => setFilters({ ...filters, toDate: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.category')}
                        </label>
                        <select
                            value={filters.categoryId}
                            onChange={(e) => setFilters({ ...filters, categoryId: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('expenses.allCategories')}</option>
                            {categories.map(cat => (
                                <option key={cat.id} value={cat.id}>{cat.name}</option>
                            ))}
                        </select>
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.paymentMethod')}
                        </label>
                        <select
                            value={filters.paymentMethod}
                            onChange={(e) => setFilters({ ...filters, paymentMethod: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('expenses.allPaymentMethods')}</option>
                            <option value="Cash">{t('expenses.cash')}</option>
                            <option value="Card">{t('expenses.card')}</option>
                            <option value="BankTransfer">{t('expenses.bankTransfer')}</option>
                            <option value="Cheque">{t('expenses.cheque')}</option>
                        </select>
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('expenses.supplier')}
                        </label>
                        <AsyncEntitySelect
                            value={selectedSupplier}
                            onChange={(supplier) => setFilters({
                                ...filters,
                                supplierId: supplier?.id || '',
                                supplierName: supplier?.name || ''
                            })}
                            loadOptions={loadSuppliersAsync}
                            placeholder={t('expenses.allSuppliers')}
                            isClearable
                        />
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('common.status')}
                        </label>
                        <select
                            value={filters.isApproved}
                            onChange={(e) => setFilters({ ...filters, isApproved: e.target.value })}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                        >
                            <option value="">{t('expenses.allStatuses')}</option>
                            <option value="true">{t('expenses.approved')}</option>
                            <option value="false">{t('expenses.pending')}</option>
                        </select>
                    </div>
                </div>
                <div className="flex justify-end mt-4">
                    <button
                        onClick={generateReport}
                        disabled={loading}
                        className="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400"
                    >
                        {loading ? t('common.loading') : t('expenses.generateReport')}
                    </button>
                </div>
            </div>

            {/* Report */}
            {reportData && (
                <div className="bg-white rounded-lg shadow overflow-hidden" ref={componentRef}>
                    {/* Summary Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 p-6 bg-gray-50">
                        <div className="bg-white p-4 rounded-lg shadow-sm">
                            <p className="text-sm text-gray-600 mb-1">{t('expenses.totalExpenses')}</p>
                            <p className="text-2xl font-bold text-red-600">{formatCurrency(reportData.summary.totalExpenses)}</p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm">
                            <p className="text-sm text-gray-600 mb-1">{t('expenses.approvedExpenses')}</p>
                            <p className="text-2xl font-bold text-green-600">{formatCurrency(reportData.summary.approvedExpenses)}</p>
                        </div>
                        <div className="bg-white p-4 rounded-lg shadow-sm">
                            <p className="text-sm text-gray-600 mb-1">{t('expenses.pendingExpenses')}</p>
                            <p className="text-2xl font-bold text-yellow-600">{formatCurrency(reportData.summary.pendingExpenses)}</p>
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
                                        {t('expenses.category')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('expenses.description')}
                                    </th>
                                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                        {t('expenses.amount')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('expenses.paymentMethod')}
                                    </th>
                                    <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                        {t('common.status')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="bg-white divide-y divide-gray-200">
                                {reportData.items.length > 0 ? (
                                    reportData.items.map((item, index) => (
                                        <tr key={index} className="hover:bg-gray-50">
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {formatDate(item.expenseDate, locale)}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {item.categoryName}
                                            </td>
                                            <td className="px-6 py-4 text-sm text-gray-900">
                                                {item.description}
                                                {item.supplierName && (
                                                    <div className="text-xs text-gray-500">{item.supplierName}</div>
                                                )}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-medium text-gray-900">
                                                {formatCurrency(item.amount)}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                                {item.paymentMethod}
                                            </td>
                                            <td className="px-6 py-4 whitespace-nowrap">
                                                <span className={`px-2 py-1 text-xs font-medium rounded-full ${item.isApproved
                                                    ? 'bg-green-100 text-green-800'
                                                    : 'bg-yellow-100 text-yellow-800'
                                                    }`}>
                                                    {item.isApproved ? t('expenses.approved') : t('expenses.pending')}
                                                </span>
                                            </td>
                                        </tr>
                                    ))
                                ) : (
                                    <tr>
                                        <td colSpan="6" className="px-6 py-12 text-center text-gray-500">
                                            {t('expenses.noExpenses')}
                                        </td>
                                    </tr>
                                )}
                            </tbody>
                            <tfoot className="bg-gray-100 font-bold border-t-2 border-gray-300">
                                <tr>
                                    <td colSpan="3" className={`px-6 py-4 text-gray-700 ${summaryLabelAlignClass}`}>
                                        {t('common.total')}
                                    </td>
                                    <td className="px-6 py-4 text-right text-red-600">
                                        {formatCurrency(reportData.summary.totalExpenses)}
                                    </td>
                                    <td colSpan="2"></td>
                                </tr>
                            </tfoot>
                        </table>
                    </div>

                    {/* Breakdown by Category */}
                    {Object.keys(reportData.summary.expensesByCategory).length > 0 && (
                        <div className="p-6 border-t border-gray-200">
                            <h3 className="text-lg font-semibold text-gray-900 mb-4">{t('expenses.expensesByCategory')}</h3>
                            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                                {Object.entries(reportData.summary.expensesByCategory).map(([category, amount]) => (
                                    <div key={category} className="bg-gray-50 p-3 rounded-lg">
                                        <p className="text-xs text-gray-600">{category}</p>
                                        <p className="text-lg font-bold text-gray-900">{formatCurrency(amount)}</p>
                                    </div>
                                ))}
                            </div>
                        </div>
                    )}
                </div>
            )}
        </div>
    )
}

export default ExpenseReport
