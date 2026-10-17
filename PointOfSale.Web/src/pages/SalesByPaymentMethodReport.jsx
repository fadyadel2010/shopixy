import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { loadExcelModule } from '../utils/reportExports'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

export default function SalesByPaymentMethodReport() {
    const { t } = useTranslation()
    const componentRef = useRef()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [filters, setFilters] = useState({
        startDate: new Date().toISOString().split('T')[0],
        endDate: new Date().toISOString().split('T')[0],
        warehouseId: '',
        categoryId: '',
        salesRepresentativeId: '',
        salesRepName: '',
        customerId: '',
        customerName: ''
    })

    const [warehouses, setWarehouses] = useState([])
    const [categories, setCategories] = useState([])

    // PERFORMANCE: Async loaders instead of loading all with pageSize=1000
    const loadSalesRepsAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/SalesRepresentatives?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    const loadCustomersAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/customers?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected entities for display
    const selectedSalesRep = useMemo(() =>
        filters.salesRepresentativeId ? { id: filters.salesRepresentativeId, name: filters.salesRepName || '' } : null,
        [filters.salesRepresentativeId, filters.salesRepName]
    )
    const selectedCustomer = useMemo(() =>
        filters.customerId ? { id: filters.customerId, name: filters.customerName || '' } : null,
        [filters.customerId, filters.customerName]
    )

    useEffect(() => {
        fetchMetadata()
    }, [])

    const fetchMetadata = async () => {
        try {
            const [wRes, cRes] = await Promise.all([
                api.get('/warehouses'),
                api.get('/categories')
            ])
            setWarehouses(wRes.data)
            setCategories(cRes.data)
        } catch (error) {
            console.error('Failed to load metadata', error)
        }
    }

    const fetchReport = async () => {
        setLoading(true)
        try {
            const params = {
                startDate: filters.startDate,
                endDate: filters.endDate,
                warehouseId: filters.warehouseId || undefined,
                categoryId: filters.categoryId || undefined,
                salesRepresentativeId: filters.salesRepresentativeId || undefined,
                customerId: filters.customerId || undefined
            }
            const response = await api.get('/reports/sales-by-payment-method', { params })
            setData(response.data)
        } catch (error) {
            console.error('Failed to load report', error)
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: 'Sales by Payment Method Report'
    })

    const handleExportExcel = async () => {
        if (!data) return
        const XLSX = await loadExcelModule()

        const wb = XLSX.utils.book_new()

        // Summary Sheet
        const summaryData = [
            ['Metric', 'Value'],
            ['Total Revenue', data.totalRevenue],
            ['Total Returns', data.totalReturns],
            ['Net Revenue', data.netRevenue],
            ['Total Invoices', data.totalInvoices],
            ['Total COGS', data.totalCost],
            ['Total Profit', data.totalProfit]
        ]
        const wsSummary = XLSX.utils.aoa_to_sheet(summaryData)
        XLSX.utils.book_append_sheet(wb, wsSummary, "Summary")

        // Payment Methods Detail
        if (data.paymentMethods?.length > 0) {
            const pmData = data.paymentMethods.map(pm => ({
                'Payment Method': getPaymentMethodLabel(pm.paymentMethod),
                'Invoices': pm.invoiceCount,
                'Gross Sales': pm.grossSales,
                'Discounts': pm.discounts,
                'Net Sales': pm.netSales,
                'Returns': pm.returns,
                'Net Revenue': pm.netRevenue,
                'COGS': pm.cogs,
                'Profit': pm.profit,
                '% of Total': pm.percentageOfTotal
            }))
            const wsPM = XLSX.utils.json_to_sheet(pmData)
            XLSX.utils.book_append_sheet(wb, wsPM, "Payment Methods")
        }

        XLSX.writeFile(wb, "SalesByPaymentMethodReport.xlsx")
    }

    const getPaymentMethodLabel = (method) => {
        const labels = {
            'Cash': t('purchases.cash'),
            'Card': t('pos.card'),
            'BankTransfer': t('purchases.bankTransfer'),
            'Mixed': t('paymentMethod.mixed'),
            'Credit': t('purchases.credit')
        }
        return labels[method] || method
    }

    const getPaymentMethodIcon = (method) => {
        const icons = {
            'Cash': '💵',
            'Card': '💳',
            'BankTransfer': '🏦',
            'Mixed': '🔀',
            'Credit': '📋'
        }
        return icons[method] || '💰'
    }

    const getPaymentMethodColor = (method) => {
        const colors = {
            'Cash': 'from-green-500 to-emerald-600',
            'Card': 'from-blue-500 to-indigo-600',
            'BankTransfer': 'from-purple-500 to-violet-600',
            'Mixed': 'from-amber-500 to-orange-600',
            'Credit': 'from-rose-500 to-pink-600'
        }
        return colors[method] || 'from-gray-500 to-gray-600'
    }

    // Calculate chart data for visualization
    const getChartWidth = (amount) => {
        if (!data || data.netRevenue <= 0) return 0
        return Math.max((amount / data.netRevenue) * 100, 0)
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('paymentMethodReport.title')}</h1>
                    <p className="text-gray-500">{t('paymentMethodReport.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handleExportExcel}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        📊 {t('salesSummary.exportExcel')}
                    </button>
                    <button
                        onClick={handlePrint}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        🖨️ {t('salesSummary.exportPDF')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 grid grid-cols-1 md:grid-cols-3 lg:grid-cols-6 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesSummary.from')}</label>
                    <input
                        type="date"
                        value={filters.startDate}
                        onChange={(e) => setFilters(prev => ({ ...prev, startDate: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesSummary.to')}</label>
                    <input
                        type="date"
                        value={filters.endDate}
                        onChange={(e) => setFilters(prev => ({ ...prev, endDate: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('dashboard.warehouse')}</label>
                    <select
                        value={filters.warehouseId}
                        onChange={(e) => setFilters(prev => ({ ...prev, warehouseId: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {warehouses.map(w => (
                            <option key={w.id} value={w.id}>{w.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('nav.mainSubCategories')}</label>
                    <select
                        value={filters.categoryId}
                        onChange={(e) => setFilters(prev => ({ ...prev, categoryId: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {categories.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('sales.representatives')}</label>
                    <AsyncEntitySelect
                        value={selectedSalesRep}
                        onChange={(rep) => setFilters(prev => ({ 
                            ...prev, 
                            salesRepresentativeId: rep?.id || '',
                            salesRepName: rep?.name || ''
                        }))}
                        loadOptions={loadSalesRepsAsync}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('pos.customer')}</label>
                    <AsyncEntitySelect
                        value={selectedCustomer}
                        onChange={(cust) => setFilters(prev => ({ 
                            ...prev, 
                            customerId: cust?.id || '',
                            customerName: cust?.name || ''
                        }))}
                        loadOptions={loadCustomersAsync}
                        placeholder={t('common.all')}
                        className="w-full"
                    />
                </div>
                <div className="md:col-span-3 lg:col-span-6 flex justify-end">
                    <button
                        onClick={fetchReport}
                        disabled={loading}
                        className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        {loading ? t('common.processing') : t('salesSummary.generate')}
                    </button>
                </div>
            </div>

            {/* Report Content */}
            <div ref={componentRef} className="space-y-6 print:p-8">
                {/* Print Header */}
                <div className="hidden print:block text-center mb-8">
                    <h1 className="text-2xl font-bold">{t('paymentMethodReport.title')}</h1>
                    <p className="text-gray-500">{filters.startDate} - {filters.endDate}</p>
                </div>

                {data && (
                    <>
                        {/* KPI Cards */}
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-6 gap-4">
                            {data.paymentMethods?.filter(pm => pm.netRevenue !== 0 || pm.invoiceCount > 0).map(pm => (
                                <div key={pm.paymentMethod} className={`bg-gradient-to-br ${getPaymentMethodColor(pm.paymentMethod)} p-4 rounded-xl shadow-lg text-white`}>
                                    <div className="flex items-center gap-2 mb-2">
                                        <span className="text-2xl">{getPaymentMethodIcon(pm.paymentMethod)}</span>
                                        <h3 className="text-sm font-medium opacity-90">{getPaymentMethodLabel(pm.paymentMethod)}</h3>
                                    </div>
                                    <p className="text-2xl font-bold">{formatCurrency(pm.netRevenue)}</p>
                                    <p className="text-xs opacity-75 mt-1">{pm.invoiceCount} {t('paymentMethodReport.invoices')}</p>
                                </div>
                            ))}
                            <div className="bg-gradient-to-br from-gray-700 to-gray-900 p-4 rounded-xl shadow-lg text-white">
                                <div className="flex items-center gap-2 mb-2">
                                    <span className="text-2xl">📈</span>
                                    <h3 className="text-sm font-medium opacity-90">{t('salesSummary.netRevenue')}</h3>
                                </div>
                                <p className="text-2xl font-bold">{formatCurrency(data.netRevenue)}</p>
                                <p className="text-xs opacity-75 mt-1">{data.totalInvoices} {t('paymentMethodReport.totalInvoices')}</p>
                            </div>
                        </div>

                        {/* Visual Chart - Bar Representation */}
                        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
                            <h3 className="text-lg font-bold text-gray-800 mb-6">{t('paymentMethodReport.distribution')}</h3>
                            <div className="space-y-4">
                                {data.paymentMethods?.filter(pm => pm.netRevenue > 0).map(pm => (
                                    <div key={pm.paymentMethod} className="flex items-center gap-4">
                                        <div className="w-32 flex items-center gap-2">
                                            <span className="text-xl">{getPaymentMethodIcon(pm.paymentMethod)}</span>
                                            <span className="text-sm font-medium text-gray-700">{getPaymentMethodLabel(pm.paymentMethod)}</span>
                                        </div>
                                        <div className="flex-1 h-8 bg-gray-100 rounded-full overflow-hidden">
                                            <div
                                                className={`h-full bg-gradient-to-r ${getPaymentMethodColor(pm.paymentMethod)} rounded-full transition-all duration-500 flex items-center justify-end pr-3`}
                                                style={{ width: `${Math.max(getChartWidth(pm.netRevenue), 5)}%` }}
                                            >
                                                {pm.percentageOfTotal >= 10 && (
                                                    <span className="text-xs font-bold text-white">{pm.percentageOfTotal}%</span>
                                                )}
                                            </div>
                                        </div>
                                        <div className="w-28 text-right">
                                            <span className="text-sm font-bold text-gray-800">{formatCurrency(pm.netRevenue)}</span>
                                            {pm.percentageOfTotal < 10 && (
                                                <span className="text-xs text-gray-500 ml-2">({pm.percentageOfTotal}%)</span>
                                            )}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>

                        {/* Detailed Table */}
                        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100 overflow-x-auto print:break-inside-avoid">
                            <h3 className="text-lg font-bold text-gray-800 mb-4">{t('paymentMethodReport.detailedBreakdown')}</h3>
                            <table className="w-full text-sm">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th className="px-4 py-3 text-left font-semibold text-gray-700">{t('purchases.paymentMethod')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('paymentMethodReport.numInvoices')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('paymentMethodReport.grossSales')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('salesSummary.totalDiscounts')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('salesSummary.netSales')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('salesSummary.returns')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('salesSummary.netRevenue')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('salesSummary.cogs')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">{t('salesSummary.profit')}</th>
                                        <th className="px-4 py-3 text-right font-semibold text-gray-700">%</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-100">
                                    {data.paymentMethods?.map(pm => (
                                        <tr key={pm.paymentMethod} className="hover:bg-gray-50">
                                            <td className="px-4 py-3">
                                                <div className="flex items-center gap-2">
                                                    <span>{getPaymentMethodIcon(pm.paymentMethod)}</span>
                                                    <span className="font-medium">{getPaymentMethodLabel(pm.paymentMethod)}</span>
                                                </div>
                                            </td>
                                            <td className="px-4 py-3 text-right font-medium">{pm.invoiceCount}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(pm.grossSales)}</td>
                                            <td className="px-4 py-3 text-right text-red-600">{formatCurrency(pm.discounts)}</td>
                                            <td className="px-4 py-3 text-right">{formatCurrency(pm.netSales)}</td>
                                            <td className="px-4 py-3 text-right text-orange-600">{formatCurrency(pm.returns)}</td>
                                            <td className="px-4 py-3 text-right font-bold text-indigo-600">{formatCurrency(pm.netRevenue)}</td>
                                            <td className="px-4 py-3 text-right text-gray-600">{formatCurrency(pm.cogs)}</td>
                                            <td className="px-4 py-3 text-right font-bold text-emerald-600">{formatCurrency(pm.profit)}</td>
                                            <td className="px-4 py-3 text-right">
                                                <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${pm.percentageOfTotal >= 25 ? 'bg-green-100 text-green-800' :
                                                    pm.percentageOfTotal >= 10 ? 'bg-blue-100 text-blue-800' :
                                                        'bg-gray-100 text-gray-800'
                                                    }`}>
                                                    {pm.percentageOfTotal}%
                                                </span>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                                <tfoot className="bg-gray-100 font-bold">
                                    <tr>
                                        <td className="px-4 py-3">{t('common.total')}</td>
                                        <td className="px-4 py-3 text-right">{data.totalInvoices}</td>
                                        <td className="px-4 py-3 text-right">{formatCurrency(data.paymentMethods?.reduce((sum, pm) => sum + pm.grossSales, 0) || 0)}</td>
                                        <td className="px-4 py-3 text-right text-red-600">{formatCurrency(data.paymentMethods?.reduce((sum, pm) => sum + pm.discounts, 0) || 0)}</td>
                                        <td className="px-4 py-3 text-right">{formatCurrency(data.totalRevenue)}</td>
                                        <td className="px-4 py-3 text-right text-orange-600">{formatCurrency(data.totalReturns)}</td>
                                        <td className="px-4 py-3 text-right text-indigo-600">{formatCurrency(data.netRevenue)}</td>
                                        <td className="px-4 py-3 text-right text-gray-600">{formatCurrency(data.totalCost)}</td>
                                        <td className="px-4 py-3 text-right text-emerald-600">{formatCurrency(data.totalProfit)}</td>
                                        <td className="px-4 py-3 text-right">100%</td>
                                    </tr>
                                </tfoot>
                            </table>
                        </div>

                        {/* Reconciliation Summary */}
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 print:break-inside-avoid">
                            <div className="bg-gradient-to-br from-green-50 to-emerald-50 p-4 rounded-xl border border-green-200">
                                <h4 className="text-sm font-medium text-green-700 mb-2">💵 {t('paymentMethodReport.cashReconciliation')}</h4>
                                <p className="text-xl font-bold text-green-800">
                                    {formatCurrency(data.paymentMethods?.find(pm => pm.paymentMethod === 'Cash')?.netRevenue || 0)}
                                </p>
                                <p className="text-xs text-green-600 mt-1">{t('paymentMethodReport.shouldMatchTreasury')}</p>
                            </div>
                            <div className="bg-gradient-to-br from-blue-50 to-indigo-50 p-4 rounded-xl border border-blue-200">
                                <h4 className="text-sm font-medium text-blue-700 mb-2">💳 {t('paymentMethodReport.visaReconciliation')}</h4>
                                <p className="text-xl font-bold text-blue-800">
                                    {formatCurrency(data.paymentMethods?.find(pm => pm.paymentMethod === 'Card')?.netRevenue || 0)}
                                </p>
                                <p className="text-xs text-blue-600 mt-1">{t('paymentMethodReport.shouldMatchVisa')}</p>
                            </div>
                            <div className="bg-gradient-to-br from-purple-50 to-violet-50 p-4 rounded-xl border border-purple-200">
                                <h4 className="text-sm font-medium text-purple-700 mb-2">🏦 {t('paymentMethodReport.bankReconciliation')}</h4>
                                <p className="text-xl font-bold text-purple-800">
                                    {formatCurrency(data.paymentMethods?.find(pm => pm.paymentMethod === 'BankTransfer')?.netRevenue || 0)}
                                </p>
                                <p className="text-xs text-purple-600 mt-1">{t('paymentMethodReport.shouldMatchBank')}</p>
                            </div>
                            <div className="bg-gradient-to-br from-rose-50 to-pink-50 p-4 rounded-xl border border-rose-200">
                                <h4 className="text-sm font-medium text-rose-700 mb-2">📋 {t('paymentMethodReport.creditReconciliation')}</h4>
                                <p className="text-xl font-bold text-rose-800">
                                    {formatCurrency(data.paymentMethods?.find(pm => pm.paymentMethod === 'Credit')?.netRevenue || 0)}
                                </p>
                                <p className="text-xs text-rose-600 mt-1">{t('paymentMethodReport.shouldMatchAR')}</p>
                            </div>
                        </div>
                    </>
                )}

                {/* Empty State */}
                {!data && !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <span className="text-4xl">💳</span>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('paymentMethodReport.emptyState')}</h3>
                        <p className="mt-1 text-sm text-gray-500">{t('paymentMethodReport.emptyStateDesc')}</p>
                    </div>
                )}
            </div>
        </div>
    )
}
