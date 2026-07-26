import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import LineChart from '../components/LineChart'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { format } from 'date-fns'
import { loadExcelModule } from '../utils/reportExports'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

export default function SalesSummaryReport() {
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
        paymentMethod: ''
    })

    const [warehouses, setWarehouses] = useState([])
    const [categories, setCategories] = useState([])

    // PERFORMANCE: Async sales rep loader instead of loading all with pageSize=1000
    const loadSalesRepsAsync = useCallback(async ({ search, page, pageSize = 20 }) => {
        const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
        if (search) params.set('search', search)
        const response = await api.get(`/SalesRepresentatives?${params}`)
        return { items: response.data.items || [], hasMore: page < (response.data.totalPages || 1) }
    }, [])

    // PERFORMANCE: Memoized selected sales rep for display
    const selectedSalesRep = useMemo(() =>
        filters.salesRepresentativeId ? { id: filters.salesRepresentativeId, name: filters.salesRepName || '' } : null,
        [filters.salesRepresentativeId, filters.salesRepName]
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
                paymentMethod: filters.paymentMethod || undefined
            }
            const response = await api.get('/reports/sales-summary', { params })
            setData(response.data)
        } catch (error) {
            console.error('Failed to load report', error)
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: 'Sales Summary Report'
    })

    const handleExportExcel = async () => {
        if (!data) return
        const XLSX = await loadExcelModule()

        const wb = XLSX.utils.book_new()

        // Summary Sheet
        const summaryData = [
            ['Metric', 'Value'],
            ['Total Sales (Gross)', data.totalSalesGross],
            ['Total Discounts', data.totalDiscounts],
            ['Net Sales', data.netSales],
            ['Returns', data.totalReturns],
            ['Net Revenue', data.netRevenue],
            ['COGS', data.totalCost],
            ['Total Profit', data.totalProfit],
            ['Invoice Count', data.invoiceCount],
            ['Avg Invoice Value', data.averageInvoiceValue]
        ]
        const wsSummary = XLSX.utils.aoa_to_sheet(summaryData)
        XLSX.utils.book_append_sheet(wb, wsSummary, "Summary")

        // Top Products
        if (data.topProducts?.length > 0) {
            const wsProd = XLSX.utils.json_to_sheet(data.topProducts)
            XLSX.utils.book_append_sheet(wb, wsProd, "Top Products")
        }

        // Daily Sales
        if (data.dailySales?.length > 0) {
            const formattedDaily = data.dailySales.map(d => ({
                Date: format(new Date(d.date), 'yyyy-MM-dd'),
                Sales: d.sales,
                Returns: d.returns,
                Net: d.net,
                Profit: d.profit
            }))
            const wsDaily = XLSX.utils.json_to_sheet(formattedDaily)
            XLSX.utils.book_append_sheet(wb, wsDaily, "Daily Trend")
        }

        XLSX.writeFile(wb, "SalesSummaryReport.xlsx")
    }

    const chartData = data?.dailySales?.map(d => ({
        label: format(new Date(d.date), 'MM/dd'),
        amount: d.net // Using Net Sales for trend
    })) || []

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('salesSummary.title')}</h1>
                    <p className="text-gray-500">{t('salesSummary.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handleExportExcel}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50"
                    >
                        {t('salesSummary.exportExcel')}
                    </button>
                    <button
                        onClick={handlePrint}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50"
                    >
                        {t('salesSummary.exportPDF')}
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
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('purchases.paymentMethod')}</label>
                    <select
                        value={filters.paymentMethod}
                        onChange={(e) => setFilters(prev => ({ ...prev, paymentMethod: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        <option value="Cash">{t('purchases.cash')}</option>
                        <option value="Visa">{t('pos.card')}</option>
                        <option value="Bank">{t('purchases.bankTransfer')}</option>
                        <option value="Credit">{t('purchases.credit')}</option>
                    </select>
                </div>
                <div className="md:col-span-3 lg:col-span-6 flex justify-end">
                    <button
                        onClick={fetchReport}
                        disabled={loading}
                        className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        {loading ? 'Processing...' : t('salesSummary.generate')}
                    </button>
                </div>
            </div>

            {/* Report Content */}
            <div ref={componentRef} className="space-y-6 print:p-8">
                {/* Print Header */}
                <div className="hidden print:block text-center mb-8">
                    <h1 className="text-2xl font-bold">{t('salesSummary.title')}</h1>
                    <p className="text-gray-500">{filters.startDate} - {filters.endDate}</p>
                </div>

                {data && (
                    <>
                        {/* KPI Cards */}
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                            <KpiCard title={t('salesSummary.totalSales')} value={formatCurrency(data.totalSalesGross)} color="text-indigo-600" />
                            <KpiCard title={t('salesSummary.totalDiscounts')} value={formatCurrency(data.totalDiscounts)} color="text-red-500" />
                            <KpiCard title={t('salesSummary.netSales')} value={formatCurrency(data.netSales)} color="text-green-600" />
                            <KpiCard title={t('salesSummary.returns')} value={formatCurrency(data.totalReturns)} color="text-orange-500" />

                            <KpiCard title={t('salesSummary.netRevenue')} value={formatCurrency(data.netRevenue)} color="text-blue-600" />
                            <KpiCard title={t('salesSummary.cogs')} value={formatCurrency(data.totalCost)} color="text-gray-600" />
                            <KpiCard title={t('salesSummary.profit')} value={formatCurrency(data.totalProfit)} color="text-emerald-600" />
                            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100">
                                <h3 className="text-sm font-medium text-gray-500">{t('salesSummary.invoiceCount')}</h3>
                                <p className="text-2xl font-bold text-gray-800 mt-1">{data.invoiceCount}</p>
                                <p className="text-xs text-gray-400 mt-1">{t('salesSummary.avgInvoice')}: {formatCurrency(data.averageInvoiceValue)}</p>
                            </div>
                        </div>

                        {/* Chart */}
                        <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100 print:break-inside-avoid">
                            <h3 className="text-lg font-bold text-gray-800 mb-4">{t('salesSummary.dailyTrend')}</h3>
                            <div className="h-64">
                                <LineChart data={chartData} height={250} />
                            </div>
                        </div>

                        {/* Tables */}
                        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 print:break-inside-avoid">
                            {/* Top Products */}
                            <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
                                <h3 className="text-lg font-bold text-gray-800 mb-4">{t('salesSummary.topProducts')}</h3>
                                <div className="overflow-x-auto">
                                    <table className="w-full text-sm text-left">
                                        <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                            <tr>
                                                <th className="px-3 py-2">{t('common.name')}</th>
                                                <th className="px-3 py-2 text-right">{t('common.amount')}</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {data.topProducts?.map((p, i) => (
                                                <tr key={i} className="border-b">
                                                    <td className="px-3 py-2 font-medium">{p.productName}</td>
                                                    <td className="px-3 py-2 text-right">{formatCurrency(p.salesAmount)}</td>
                                                </tr>
                                            ))}
                                            {(!data.topProducts || data.topProducts.length === 0) && (
                                                <tr><td colSpan="2" className="px-3 py-2 text-center text-gray-500">{t('common.noData')}</td></tr>
                                            )}
                                        </tbody>
                                    </table>
                                </div>
                            </div>

                            {/* Top Customers */}
                            <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
                                <h3 className="text-lg font-bold text-gray-800 mb-4">{t('salesSummary.topCustomers')}</h3>
                                <div className="overflow-x-auto">
                                    <table className="w-full text-sm text-left">
                                        <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                            <tr>
                                                <th className="px-3 py-2">{t('common.name')}</th>
                                                <th className="px-3 py-2 text-right">{t('common.amount')}</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {data.topCustomers?.map((c, i) => (
                                                <tr key={i} className="border-b">
                                                    <td className="px-3 py-2 font-medium">{c.customerName}</td>
                                                    <td className="px-3 py-2 text-right">{formatCurrency(c.totalSales)}</td>
                                                </tr>
                                            ))}
                                            {(!data.topCustomers || data.topCustomers.length === 0) && (
                                                <tr><td colSpan="2" className="px-3 py-2 text-center text-gray-500">{t('common.noData')}</td></tr>
                                            )}
                                        </tbody>
                                    </table>
                                </div>
                            </div>

                            {/* Payment Methods */}
                            <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-100">
                                <h3 className="text-lg font-bold text-gray-800 mb-4">{t('salesSummary.paymentMethods')}</h3>
                                <div className="overflow-x-auto">
                                    <table className="w-full text-sm text-left">
                                        <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                            <tr>
                                                <th className="px-3 py-2">{t('purchases.paymentMethod')}</th>
                                                <th className="px-3 py-2 text-right">{t('common.amount')}</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {data.salesByPaymentMethod?.map((pm, i) => (
                                                <tr key={i} className="border-b">
                                                    <td className="px-3 py-2 font-medium">{pm.paymentMethod}</td>
                                                    <td className="px-3 py-2 text-right">{formatCurrency(pm.amount)}</td>
                                                </tr>
                                            ))}
                                            {(!data.salesByPaymentMethod || data.salesByPaymentMethod.length === 0) && (
                                                <tr><td colSpan="2" className="px-3 py-2 text-center text-gray-500">{t('common.noData')}</td></tr>
                                            )}
                                        </tbody>
                                    </table>
                                </div>
                            </div>
                        </div>
                    </>
                )}

                {/* Empty State */}
                {!data && !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <span className="text-4xl">📊</span>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('salesAnalysis.startPrompt')}</h3>
                    </div>
                )}
            </div>
            {/* Loading Overlay or Component? Just utilizing button state is fine for now but I can add a spinner if I want */}
        </div>
    )
}

function KpiCard({ title, value, color }) {
    return (
        <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100">
            <h3 className="text-sm font-medium text-gray-500">{title}</h3>
            <p className={`text-2xl font-bold mt-1 ${color}`}>{value}</p>
        </div>
    )
}
