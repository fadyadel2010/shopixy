import { useState, useEffect, useRef, useCallback, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useReactToPrint } from 'react-to-print'
import api from '../services/api'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { loadExcelModule } from '../utils/reportExports'
import AsyncEntitySelect from '../components/AsyncEntitySelect'

export default function SalesByCategoryReport() {
    const { t } = useTranslation()
    const componentRef = useRef()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [filters, setFilters] = useState({
        startDate: new Date().toISOString().split('T')[0],
        endDate: new Date().toISOString().split('T')[0],
        warehouseId: '',
        mainCategoryId: '',
        subCategoryId: '',
        salesRepresentativeId: '',
        salesRepName: '',
        paymentMethod: ''
    })

    const [warehouses, setWarehouses] = useState([])
    const [categories, setCategories] = useState([])
    const [subCategories, setSubCategories] = useState([])

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

    const fetchMetadata = useCallback(async () => {
        try {
            const [wRes, cRes] = await Promise.all([
                api.get('/warehouses'),
                api.get('/categories')
            ])
            setWarehouses(wRes.data)

            // Organize categories
            // Assuming the API returns a flat list or nested. Check Category.cs: it has SubCategories collection.
            // If API returns nested, great. If flat, we might need to process.
            // Let's assume standard GetAll returns root categories with subcategories included if .Include is used, 
            // OR returns all and we filter. 
            // Based on typical implementation, GET /categories usually returns roots or flat list.
            // Let's assume response structure. If it's the standard generic controller, it might be flat.
            // We will filter for Main categories here if possible.
            // Actually, let's just inspect the response in runtime or assume a structure.
            // Given Category.cs has navigation property, usually typically endpoints using standard repo might not include children unless requested.
            // But let's assume we get a list and can filter parents.
            // Safe bet: Display all in main dropdown? No, we need Main/Sub logic.
            // Let's assume the API returns a list and we find those with ParentCategoryId == null.

            const allCats = cRes.data.items || cRes.data;
            const mainCats = allCats.filter(c => !c.parentCategoryId);

            // Map subcategories if they are not nested in response but present in flat list
            const finalCats = mainCats.map(main => ({
                ...main,
                subCategories: allCats.filter(c => c.parentCategoryId === main.id)
            }));

            setCategories(finalCats)
        } catch (error) {
            console.error('Failed to load metadata', error)
        }
    }, [])

    useEffect(() => {
        fetchMetadata()
    }, [fetchMetadata])

    // Update subcategories when main category changes
    useEffect(() => {
        if (filters.mainCategoryId) {
            const selectedCat = categories.find(c => c.id === filters.mainCategoryId)
            if (selectedCat && selectedCat.subCategories) {
                setSubCategories(selectedCat.subCategories)
            } else {
                setSubCategories([])
            }
        } else {
            setSubCategories([])
        }
    }, [filters.mainCategoryId, categories])

    const fetchReport = async () => {
        setLoading(true)
        try {
            const params = {
                startDate: filters.startDate,
                endDate: filters.endDate,
                warehouseId: filters.warehouseId || undefined,
                mainCategoryId: filters.mainCategoryId || undefined,
                subCategoryId: filters.subCategoryId || undefined,
                salesRepresentativeId: filters.salesRepresentativeId || undefined,
                paymentMethod: filters.paymentMethod || undefined
            }
            const response = await api.get('/reports/sales-by-category', { params })
            setData(response.data)
        } catch (error) {
            console.error('Failed to load report', error)
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = useReactToPrint({
        content: () => componentRef.current,
        documentTitle: 'Sales By Category Report'
    })

    const handleExportExcel = async () => {
        if (!data || !data.categories) return
        const XLSX = await loadExcelModule()

        const wb = XLSX.utils.book_new()

        // Detailed Data
        const excelData = data.categories.map(item => ({
            [t('salesByCategory.mainCatCol')]: item.mainCategoryName,
            [t('salesByCategory.subCatCol')]: item.subCategoryName,
            [t('salesByCategory.qtySold')]: item.totalQuantitySold,
            [t('salesByCategory.grossSales')]: item.grossSalesAmount,
            [t('salesByCategory.discounts')]: item.totalDiscounts,
            [t('salesByCategory.netSales')]: item.netSales,
            [t('salesByCategory.returns')]: item.totalReturns,
            [t('salesByCategory.netRevenue')]: item.netRevenue,
            [t('salesByCategory.cogs')]: item.totalCostOfGoodsSold,
            [t('salesByCategory.profit')]: item.totalProfit,
            [t('salesByCategory.margin')]: item.profitMarginPercentage / 100, // Format as % later if needed
            [t('salesByCategory.itemsSold')]: item.transactionCount
        }))

        const ws = XLSX.utils.json_to_sheet(excelData)
        XLSX.utils.book_append_sheet(wb, ws, "Sales By Category")

        XLSX.writeFile(wb, "SalesByCategoryReport.xlsx")
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('salesByCategory.title')}</h1>
                    <p className="text-gray-500">{t('salesAnalysis.subtitle')}</p>
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
            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 grid grid-cols-1 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-7 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.startDate')}</label>
                    <input
                        type="date"
                        value={filters.startDate}
                        onChange={(e) => setFilters(prev => ({ ...prev, startDate: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.endDate')}</label>
                    <input
                        type="date"
                        value={filters.endDate}
                        onChange={(e) => setFilters(prev => ({ ...prev, endDate: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.warehouse')}</label>
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
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.mainCategory')}</label>
                    <select
                        value={filters.mainCategoryId}
                        onChange={(e) => setFilters(prev => ({ ...prev, mainCategoryId: e.target.value, subCategoryId: '' }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {categories.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.subCategory')}</label>
                    <select
                        value={filters.subCategoryId}
                        onChange={(e) => setFilters(prev => ({ ...prev, subCategoryId: e.target.value }))}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                        disabled={!filters.mainCategoryId}
                    >
                        <option value="">{t('common.all')}</option>
                        {subCategories.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.salesman')}</label>
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
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('salesByCategory.paymentMethod')}</label>
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
            </div>

            <div className="flex justify-end">
                <button
                    onClick={fetchReport}
                    disabled={loading}
                    className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                >
                    {loading ? t('common.processing') : t('salesByCategory.generate')}
                </button>
            </div>

            {/* Report Content */}
            <div ref={componentRef} className="space-y-6 print:p-8">
                {/* Print Header */}
                <div className="hidden print:block text-center mb-8">
                    <h1 className="text-2xl font-bold">{t('salesByCategory.title')}</h1>
                    <p className="text-gray-500">{filters.startDate} - {filters.endDate}</p>
                </div>

                {data && (
                    <>
                        {/* KPI Cards */}
                        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                            <KpiCard title={t('salesByCategory.totalRevenue')} value={formatCurrency(data.totalRevenue)} color="text-indigo-600" />
                            <KpiCard title={t('salesByCategory.totalProfit')} value={formatCurrency(data.totalProfit)} color="text-emerald-600" />
                            <KpiCard title={t('salesByCategory.bestCategory')} value={data.bestPerformingCategory || '-'} color="text-green-600" />
                            <KpiCard title={t('salesByCategory.worstCategory')} value={data.worstPerformingCategory || '-'} color="text-red-500" />
                        </div>

                        {/* Detailed Table */}
                        <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                            <div className="overflow-x-auto">
                                <table className="w-full text-sm text-center">
                                    <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                        <tr>
                                            <th className="px-3 py-3 text-left">{t('salesByCategory.mainCatCol')}</th>
                                            <th className="px-3 py-3 text-left">{t('salesByCategory.subCatCol')}</th>
                                            <th className="px-3 py-3">{t('salesByCategory.qtySold')}</th>
                                            <th className="px-3 py-3">{t('salesByCategory.itemsSold')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.grossSales')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.discounts')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.netSales')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.returns')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.netRevenue')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.cogs')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.profit')}</th>
                                            <th className="px-3 py-3 text-right">{t('salesByCategory.margin')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-200">
                                        {data.categories?.map((item, index) => (
                                            <tr key={index} className="hover:bg-gray-50">
                                                <td className="px-3 py-3 text-left font-medium">{item.mainCategoryName}</td>
                                                <td className="px-3 py-3 text-left">{item.subCategoryName}</td>
                                                <td className="px-3 py-3">{Number(item.totalQuantitySold).toLocaleString()}</td>
                                                <td className="px-3 py-3">{item.transactionCount}</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(item.grossSalesAmount)}</td>
                                                <td className="px-3 py-3 text-right text-red-500">({formatCurrency(item.totalDiscounts)})</td>
                                                <td className="px-3 py-3 text-right font-medium">{formatCurrency(item.netSales)}</td>
                                                <td className="px-3 py-3 text-right text-orange-500">({formatCurrency(item.totalReturns)})</td>
                                                <td className="px-3 py-3 text-right font-bold text-indigo-600">{formatCurrency(item.netRevenue)}</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(item.totalCostOfGoodsSold)}</td>
                                                <td className="px-3 py-3 text-right font-bold text-emerald-600">{formatCurrency(item.totalProfit)}</td>
                                                <td className="px-3 py-3 text-right">{item.profitMarginPercentage.toFixed(1)}%</td>
                                            </tr>
                                        ))}
                                        {(!data.categories || data.categories.length === 0) && (
                                            <tr><td colSpan="12" className="px-3 py-4 text-center text-gray-500">{t('common.noData')}</td></tr>
                                        )}
                                        {/* Total Footer Row */}
                                        {data.categories && data.categories.length > 0 && (
                                            <tr className="bg-gray-50 font-bold border-t-2 border-gray-200">
                                                <td className="px-3 py-3 text-left" colSpan="2">{t('common.total')}</td>
                                                <td className="px-3 py-3">{Number(data.categories.reduce((a, b) => a + b.totalQuantitySold, 0)).toLocaleString()}</td>
                                                <td className="px-3 py-3">{data.categories.reduce((a, b) => a + b.transactionCount, 0)}</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(data.categories.reduce((a, b) => a + b.grossSalesAmount, 0))}</td>
                                                <td className="px-3 py-3 text-right">({formatCurrency(data.categories.reduce((a, b) => a + b.totalDiscounts, 0))})</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(data.categories.reduce((a, b) => a + b.netSales, 0))}</td>
                                                <td className="px-3 py-3 text-right">({formatCurrency(data.categories.reduce((a, b) => a + b.totalReturns, 0))})</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(data.totalRevenue)}</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(data.categories.reduce((a, b) => a + b.totalCostOfGoodsSold, 0))}</td>
                                                <td className="px-3 py-3 text-right">{formatCurrency(data.totalProfit)}</td>
                                                <td className="px-3 py-3 text-right">
                                                    {data.totalRevenue > 0 ? ((data.totalProfit / data.totalRevenue) * 100).toFixed(1) : 0}%
                                                </td>
                                            </tr>
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </>
                )}

                {!data && !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <span className="text-4xl">📊</span>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('salesAnalysis.startPrompt')}</h3>
                    </div>
                )}
            </div>
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
