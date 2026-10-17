import React, { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { format } from 'date-fns'
import {
    RefreshCw,
    Download,
    Filter,
    TrendingUp,
    TrendingDown,
    Minus,
    ChevronDown,
    Package,
    DollarSign,
    BarChart3,
    AlertTriangle,
    X
} from 'lucide-react'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { createPdfDocument, loadExcelModule } from '../utils/reportExports'
import { fetchAllPagedArray } from '../services/pagedArrayLoader'

const CostAnalysisReport = () => {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [showDrillDown, setShowDrillDown] = useState(false)
    const [selectedProduct, setSelectedProduct] = useState(null)

    const [filters, setFilters] = useState({
        fromDate: new Date(new Date().setMonth(new Date().getMonth() - 3)).toISOString().split('T')[0],
        toDate: new Date().toISOString().split('T')[0],
        warehouseId: '',
        supplierId: '',
        productId: '',
        categoryId: '',
        subCategoryId: ''
    })

    // Metadata for dropdowns
    const [warehouses, setWarehouses] = useState([])
    const [suppliers, setSuppliers] = useState([])
    const [categories, setCategories] = useState([])
    const [subCategories, setSubCategories] = useState([])
    const [products, setProducts] = useState([])

    useEffect(() => {
        let mounted = true

        const fetchMetadata = async () => {
            try {
                const [whRes, supRes, catRes, prodRes] = await Promise.all([
                    api.get('/warehouses'),
                    api.get('/suppliers'),
                    api.get('/categories'),
                    fetchAllPagedArray('/products')
                ])

                if (!mounted) return

                setWarehouses(whRes.data)
                setSuppliers(supRes.data.items || supRes.data)
                setCategories(catRes.data)
                setProducts(prodRes)
            } catch (error) {
                console.error('Error fetching metadata', error)
            }
        }

        fetchMetadata()

        return () => {
            mounted = false
        }
    }, [])

    const generateReport = async () => {
        setLoading(true)
        try {
            const params = {
                fromDate: filters.fromDate,
                toDate: filters.toDate,
                warehouseId: filters.warehouseId || null,
                supplierId: filters.supplierId || null,
                productId: filters.productId || null,
                categoryId: filters.categoryId || null,
                subCategoryId: filters.subCategoryId || null
            }

            const response = await api.get('/reports/cost-analysis', { params })
            setData(response.data)
        } catch (error) {
            console.error('Error generating report', error)
        } finally {
            setLoading(false)
        }
    }

    const handleExportExcel = async () => {
        if (!data || !data.items?.length) return
        const XLSX = await loadExcelModule()

        const exportData = data.items.map(item => ({
            [t('costAnalysis.productCode')]: item.productCode,
            [t('costAnalysis.productName')]: item.productName,
            [t('costAnalysis.category')]: item.categoryName,
            [t('costAnalysis.subCategory')]: item.subCategoryName,
            [t('costAnalysis.totalQtyPurchased')]: item.totalQuantityPurchased,
            [t('costAnalysis.avgCostUnit')]: item.averageCostPerUnit,
            [t('costAnalysis.lastCost')]: item.lastPurchaseCost,
            [t('costAnalysis.minCost')]: item.minPurchaseCost,
            [t('costAnalysis.maxCost')]: item.maxPurchaseCost,
            [t('costAnalysis.costVariance')]: item.costVariancePercentage + '%',
            [t('costAnalysis.totalPurchaseValue')]: item.totalPurchaseValue,
            [t('costAnalysis.invoiceCount')]: item.invoiceCount,
            [t('costAnalysis.topSupplier')]: item.topSupplierName,
            [t('costAnalysis.costTrend')]: t(`costAnalysis.trend${item.costTrend}`)
        }))

        const ws = XLSX.utils.json_to_sheet(exportData)
        const wb = XLSX.utils.book_new()
        XLSX.utils.book_append_sheet(wb, ws, "Cost Analysis")
        XLSX.writeFile(wb, `Cost_Analysis_${filters.fromDate}_${filters.toDate}.xlsx`)
    }

    const handleExportPDF = async () => {
        if (!data || !data.items?.length) return

        const doc = await createPdfDocument('landscape')
        doc.text(t('costAnalysis.title'), 14, 15)
        doc.setFontSize(10)
        doc.text(`${t('common.from')}: ${filters.fromDate} ${t('common.to')}: ${filters.toDate}`, 14, 25)

        const tableColumn = [
            t('costAnalysis.productCode'),
            t('costAnalysis.productName'),
            t('costAnalysis.avgCostUnit'),
            t('costAnalysis.lastCost'),
            t('costAnalysis.minCost'),
            t('costAnalysis.maxCost'),
            t('costAnalysis.costVariance'),
            t('costAnalysis.topSupplier'),
            t('costAnalysis.costTrend')
        ]

        const tableRows = data.items.map(item => [
            item.productCode,
            item.productName,
            formatCurrency(item.averageCostPerUnit),
            formatCurrency(item.lastPurchaseCost),
            formatCurrency(item.minPurchaseCost),
            formatCurrency(item.maxPurchaseCost),
            item.costVariancePercentage.toFixed(2) + '%',
            item.topSupplierName,
            item.costTrend
        ])

        doc.autoTable({
            head: [tableColumn],
            body: tableRows,
            startY: 30,
            styles: { fontSize: 8 }
        })

        doc.save(`Cost_Analysis_${filters.fromDate}_${filters.toDate}.pdf`)
    }

    const CostTrendIcon = ({ trend }) => {
        switch (trend) {
            case 'Increasing':
                return <TrendingUp className="h-5 w-5 text-red-500" />
            case 'Decreasing':
                return <TrendingDown className="h-5 w-5 text-green-500" />
            default:
                return <Minus className="h-5 w-5 text-gray-500" />
        }
    }

    const openDrillDown = (product) => {
        setSelectedProduct(product)
        setShowDrillDown(true)
    }

    return (
        <div className="space-y-6">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            {/* Header */}
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('costAnalysis.title')}</h1>
                    <p className="text-gray-500">{t('costAnalysis.subtitle')}</p>
                </div>
                <div className="flex gap-2">
                    <button
                        onClick={handleExportExcel}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <Download className="h-4 w-4" />
                        {t('common.exportExcel')}
                    </button>
                    <button
                        onClick={handleExportPDF}
                        disabled={!data || loading}
                        className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50 flex items-center gap-2"
                    >
                        <Download className="h-4 w-4" />
                        {t('common.exportPDF')}
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div className="bg-white p-4 rounded-xl shadow-sm border border-gray-100 grid grid-cols-1 md:grid-cols-4 gap-4">
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.from')}</label>
                    <input
                        type="date"
                        value={filters.fromDate}
                        onChange={(e) => setFilters({ ...filters, fromDate: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.to')}</label>
                    <input
                        type="date"
                        value={filters.toDate}
                        onChange={(e) => setFilters({ ...filters, toDate: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    />
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('common.warehouse')}</label>
                    <select
                        value={filters.warehouseId}
                        onChange={(e) => setFilters({ ...filters, warehouseId: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {warehouses.map(w => (
                            <option key={w.id} value={w.id}>{w.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('costAnalysis.supplier')}</label>
                    <select
                        value={filters.supplierId}
                        onChange={(e) => setFilters({ ...filters, supplierId: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {suppliers?.map(sup => (
                            <option key={sup.id} value={sup.id}>{sup.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('costAnalysis.product')}</label>
                    <select
                        value={filters.productId}
                        onChange={(e) => setFilters({ ...filters, productId: e.target.value })}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {products.map(p => (
                            <option key={p.id} value={p.id}>{p.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('categories.title')}</label>
                    <select
                        value={filters.categoryId}
                        onChange={e => {
                            const newCatId = e.target.value
                            setFilters({ ...filters, categoryId: newCatId, subCategoryId: '' })
                            const selectedCat = categories.find(c => c.id === newCatId)
                            setSubCategories(selectedCat?.subCategories || [])
                        }}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {categories.map(c => (
                            <option key={c.id} value={c.id}>{c.name}</option>
                        ))}
                    </select>
                </div>
                <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('products.subCategory')}</label>
                    <select
                        value={filters.subCategoryId}
                        onChange={(e) => setFilters({ ...filters, subCategoryId: e.target.value })}
                        disabled={!filters.categoryId}
                        className="w-full px-3 py-2 border rounded-lg focus:ring-2 focus:ring-indigo-500"
                    >
                        <option value="">{t('common.all')}</option>
                        {subCategories?.map(sc => (
                            <option key={sc.id} value={sc.id}>{sc.name}</option>
                        ))}
                    </select>
                </div>
            </div>

            <div className="flex justify-end">
                <button
                    onClick={generateReport}
                    disabled={loading}
                    className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                >
                    {loading ? <RefreshCw className="h-5 w-5 animate-spin" /> : <Filter className="h-5 w-5" />}
                    {t('costAnalysis.generate')}
                </button>
            </div>

            {data && data.items?.length > 0 ? (
                <>
                    {/* KPI Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                        <KpiCard
                            icon={<TrendingUp className="h-6 w-6 text-red-500" />}
                            title={t('costAnalysis.highestCostIncrease')}
                            value={data.summary.highestCostIncreaseProduct}
                            subtitle={data.summary.highestCostIncreasePercentage > 0
                                ? `+${data.summary.highestCostIncreasePercentage.toFixed(1)}%`
                                : '-'}
                            color="text-red-600"
                        />
                        <KpiCard
                            icon={<AlertTriangle className="h-6 w-6 text-orange-500" />}
                            title={t('costAnalysis.largestVariance')}
                            value={data.summary.largestCostVarianceProduct}
                            subtitle={data.summary.largestCostVariancePercentage > 0
                                ? `${data.summary.largestCostVariancePercentage.toFixed(1)}%`
                                : '-'}
                            color="text-orange-600"
                        />
                        <KpiCard
                            icon={<DollarSign className="h-6 w-6 text-green-500" />}
                            title={t('costAnalysis.totalPurchaseValue')}
                            value={formatCurrency(data.summary.totalPurchaseValue)}
                            subtitle={`${data.summary.totalInvoiceCount} ${t('costAnalysis.invoices')}`}
                            color="text-green-600"
                        />
                        <KpiCard
                            icon={<BarChart3 className="h-6 w-6 text-blue-500" />}
                            title={t('costAnalysis.avgCostAllProducts')}
                            value={formatCurrency(data.summary.averageCostPerUnit)}
                            subtitle={`${data.summary.totalQuantityPurchased.toFixed(0)} ${t('costAnalysis.units')}`}
                            color="text-blue-600"
                        />
                    </div>

                    {/* Main Data Table */}
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                        <div className="overflow-x-auto">
                            <table className="w-full text-sm text-left">
                                <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                    <tr>
                                        <th className="px-3 py-3">{t('costAnalysis.productCode')}</th>
                                        <th className="px-3 py-3">{t('costAnalysis.productName')}</th>
                                        <th className="px-3 py-3">{t('costAnalysis.category')}</th>
                                        <th className="px-3 py-3 text-center">{t('costAnalysis.totalQty')}</th>
                                        <th className="px-3 py-3 text-right">{t('costAnalysis.avgCostUnit')}</th>
                                        <th className="px-3 py-3 text-right">{t('costAnalysis.lastCost')}</th>
                                        <th className="px-3 py-3 text-right">{t('costAnalysis.minCost')}</th>
                                        <th className="px-3 py-3 text-right">{t('costAnalysis.maxCost')}</th>
                                        <th className="px-3 py-3 text-center">{t('costAnalysis.variance')}</th>
                                        <th className="px-3 py-3">{t('costAnalysis.topSupplier')}</th>
                                        <th className="px-3 py-3 text-center">{t('costAnalysis.trend')}</th>
                                        <th className="px-3 py-3 text-center"></th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {data.items.map((item, index) => (
                                        <tr key={index} className="hover:bg-gray-50">
                                            <td className="px-3 py-3 font-medium text-blue-600">{item.productCode}</td>
                                            <td className="px-3 py-3 font-medium text-gray-900">{item.productName}</td>
                                            <td className="px-3 py-3 text-gray-500">
                                                <div className="text-xs">
                                                    <div>{item.categoryName}</div>
                                                    {item.subCategoryName !== '-' && (
                                                        <div className="text-gray-400">{item.subCategoryName}</div>
                                                    )}
                                                </div>
                                            </td>
                                            <td className="px-3 py-3 text-center">{item.totalQuantityPurchased.toFixed(0)}</td>
                                            <td className="px-3 py-3 text-right font-semibold">{formatCurrency(item.averageCostPerUnit)}</td>
                                            <td className="px-3 py-3 text-right">{formatCurrency(item.lastPurchaseCost)}</td>
                                            <td className="px-3 py-3 text-right text-green-600">{formatCurrency(item.minPurchaseCost)}</td>
                                            <td className="px-3 py-3 text-right text-red-600">{formatCurrency(item.maxPurchaseCost)}</td>
                                            <td className="px-3 py-3 text-center">
                                                <span className={`px-2 py-1 rounded-full text-xs font-medium ${item.costVariancePercentage > 20
                                                    ? 'bg-red-100 text-red-700'
                                                    : item.costVariancePercentage > 10
                                                        ? 'bg-yellow-100 text-yellow-700'
                                                        : 'bg-green-100 text-green-700'
                                                    }`}>
                                                    {item.costVariancePercentage.toFixed(1)}%
                                                </span>
                                            </td>
                                            <td className="px-3 py-3 text-gray-700">{item.topSupplierName}</td>
                                            <td className="px-3 py-3 text-center">
                                                <div className="flex items-center justify-center gap-1">
                                                    <CostTrendIcon trend={item.costTrend} />
                                                    <span className={`text-xs ${item.costTrend === 'Increasing'
                                                        ? 'text-red-600'
                                                        : item.costTrend === 'Decreasing'
                                                            ? 'text-green-600'
                                                            : 'text-gray-500'
                                                        }`}>
                                                        {item.costChangePercentage !== 0 &&
                                                            `${item.costChangePercentage > 0 ? '+' : ''}${item.costChangePercentage.toFixed(1)}%`
                                                        }
                                                    </span>
                                                </div>
                                            </td>
                                            <td className="px-3 py-3 text-center">
                                                <button
                                                    onClick={() => openDrillDown(item)}
                                                    className="text-indigo-600 hover:text-indigo-800 p-1"
                                                    title={t('costAnalysis.viewHistory')}
                                                >
                                                    <ChevronDown className="h-4 w-4" />
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                                <tfoot className="bg-gray-50 font-bold">
                                    <tr>
                                        <td colSpan="3" className="px-3 py-3 text-right">{t('common.total')}</td>
                                        <td className="px-3 py-3 text-center">{data.summary.totalQuantityPurchased.toFixed(0)}</td>
                                        <td className="px-3 py-3 text-right">{formatCurrency(data.summary.averageCostPerUnit)}</td>
                                        <td colSpan="3"></td>
                                        <td colSpan="4" className="px-3 py-3 text-right">
                                            {t('costAnalysis.totalValue')}: {formatCurrency(data.summary.totalPurchaseValue)}
                                        </td>
                                    </tr>
                                </tfoot>
                            </table>
                        </div>
                    </div>
                </>
            ) : (
                !loading && (
                    <div className="text-center py-12 bg-white rounded-xl shadow-sm border border-gray-100">
                        <div className="mx-auto h-12 w-12 text-gray-400">
                            <Package className="h-12 w-12" />
                        </div>
                        <h3 className="mt-2 text-sm font-medium text-gray-900">{t('common.noData')}</h3>
                        <p className="mt-1 text-sm text-gray-500">{t('costAnalysis.startPrompt')}</p>
                    </div>
                )
            )}

            {/* Drill-Down Modal */}
            {showDrillDown && selectedProduct && (
                <DrillDownModal
                    product={selectedProduct}
                    onClose={() => {
                        setShowDrillDown(false)
                        setSelectedProduct(null)
                    }}
                    formatCurrency={formatCurrency}
                    t={t}
                />
            )}
        </div>
    )
}

const KpiCard = ({ icon, title, value, subtitle, color }) => (
    <div className="bg-white p-5 rounded-xl shadow-sm border border-gray-100">
        <div className="flex items-start justify-between">
            <div className="flex-1">
                <h3 className="text-sm font-medium text-gray-500">{title}</h3>
                <p className={`text-xl font-bold mt-1 ${color}`}>{value}</p>
                <p className="text-xs text-gray-400 mt-1">{subtitle}</p>
            </div>
            <div className="bg-gray-50 p-2 rounded-lg">
                {icon}
            </div>
        </div>
    </div>
)

const DrillDownModal = ({ product, onClose, formatCurrency, t }) => {
    const [activeTab, setActiveTab] = useState('history')

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
            <div className="bg-white rounded-xl shadow-2xl w-full max-w-5xl max-h-[90vh] overflow-hidden">
                {/* Header */}
                <div className="bg-gradient-to-r from-indigo-600 to-indigo-700 px-6 py-4 text-white">
                    <div className="flex justify-between items-center">
                        <div>
                            <h2 className="text-xl font-bold">{product.productName}</h2>
                            <p className="text-indigo-200 text-sm">{product.productCode} • {product.categoryName}</p>
                        </div>
                        <button onClick={onClose} className="text-white hover:bg-indigo-500 p-2 rounded-lg">
                            <X className="h-5 w-5" />
                        </button>
                    </div>
                </div>

                {/* Summary Stats */}
                <div className="grid grid-cols-4 gap-4 p-4 bg-gray-50 border-b">
                    <div className="text-center">
                        <div className="text-xs text-gray-500">{t('costAnalysis.avgCostUnit')}</div>
                        <div className="text-lg font-bold text-indigo-600">{formatCurrency(product.averageCostPerUnit)}</div>
                    </div>
                    <div className="text-center">
                        <div className="text-xs text-gray-500">{t('costAnalysis.minCost')}</div>
                        <div className="text-lg font-bold text-green-600">{formatCurrency(product.minPurchaseCost)}</div>
                    </div>
                    <div className="text-center">
                        <div className="text-xs text-gray-500">{t('costAnalysis.maxCost')}</div>
                        <div className="text-lg font-bold text-red-600">{formatCurrency(product.maxPurchaseCost)}</div>
                    </div>
                    <div className="text-center">
                        <div className="text-xs text-gray-500">{t('costAnalysis.variance')}</div>
                        <div className={`text-lg font-bold ${product.costVariancePercentage > 20 ? 'text-red-600' : 'text-gray-700'}`}>
                            {product.costVariancePercentage.toFixed(1)}%
                        </div>
                    </div>
                </div>

                {/* Tabs */}
                <div className="flex border-b px-4">
                    <button
                        onClick={() => setActiveTab('history')}
                        className={`px-4 py-3 text-sm font-medium border-b-2 transition ${activeTab === 'history'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {t('costAnalysis.purchaseHistory')}
                    </button>
                    <button
                        onClick={() => setActiveTab('monthly')}
                        className={`px-4 py-3 text-sm font-medium border-b-2 transition ${activeTab === 'monthly'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {t('costAnalysis.monthlyTrend')}
                    </button>
                    <button
                        onClick={() => setActiveTab('suppliers')}
                        className={`px-4 py-3 text-sm font-medium border-b-2 transition ${activeTab === 'suppliers'
                            ? 'border-indigo-600 text-indigo-600'
                            : 'border-transparent text-gray-500 hover:text-gray-700'
                            }`}
                    >
                        {t('costAnalysis.bySupplier')}
                    </button>
                </div>

                {/* Tab Content */}
                <div className="p-4 overflow-y-auto" style={{ maxHeight: '400px' }}>
                    {activeTab === 'history' && (
                        <table className="w-full text-sm">
                            <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                <tr>
                                    <th className="px-3 py-2">{t('costAnalysis.invoiceNo')}</th>
                                    <th className="px-3 py-2">{t('costAnalysis.date')}</th>
                                    <th className="px-3 py-2">{t('costAnalysis.supplier')}</th>
                                    <th className="px-3 py-2">{t('common.warehouse')}</th>
                                    <th className="px-3 py-2 text-center">{t('costAnalysis.qty')}</th>
                                    <th className="px-3 py-2 text-right">{t('costAnalysis.unitPrice')}</th>
                                    <th className="px-3 py-2 text-right">{t('costAnalysis.costPerUnit')}</th>
                                    <th className="px-3 py-2 text-right">{t('costAnalysis.totalValue')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {product.purchaseHistory?.map((h, i) => (
                                    <tr key={i} className="hover:bg-gray-50">
                                        <td className="px-3 py-2 text-blue-600">{h.invoiceNumber}</td>
                                        <td className="px-3 py-2">{format(new Date(h.invoiceDate), 'dd/MM/yyyy')}</td>
                                        <td className="px-3 py-2">{h.supplierName}</td>
                                        <td className="px-3 py-2">{h.warehouseName}</td>
                                        <td className="px-3 py-2 text-center">{h.quantity} {h.unitName}</td>
                                        <td className="px-3 py-2 text-right">{formatCurrency(h.unitPrice)}</td>
                                        <td className="px-3 py-2 text-right font-medium">{formatCurrency(h.costPerBaseUnit)}</td>
                                        <td className="px-3 py-2 text-right">{formatCurrency(h.totalValue)}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    )}

                    {activeTab === 'monthly' && (
                        <div className="space-y-4">
                            <table className="w-full text-sm">
                                <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                    <tr>
                                        <th className="px-3 py-2">{t('costAnalysis.month')}</th>
                                        <th className="px-3 py-2 text-center">{t('costAnalysis.invoices')}</th>
                                        <th className="px-3 py-2 text-center">{t('costAnalysis.totalQty')}</th>
                                        <th className="px-3 py-2 text-right">{t('costAnalysis.totalValue')}</th>
                                        <th className="px-3 py-2 text-right">{t('costAnalysis.avgCostUnit')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {product.monthlyCostTrend?.map((m, i) => (
                                        <tr key={i} className="hover:bg-gray-50">
                                            <td className="px-3 py-2 font-medium">{m.monthName}</td>
                                            <td className="px-3 py-2 text-center">{m.invoiceCount}</td>
                                            <td className="px-3 py-2 text-center">{m.totalQuantity.toFixed(0)}</td>
                                            <td className="px-3 py-2 text-right">{formatCurrency(m.totalValue)}</td>
                                            <td className="px-3 py-2 text-right font-medium text-indigo-600">
                                                {formatCurrency(m.averageCost)}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>

                            {/* Simple Trend Bar Chart */}
                            {product.monthlyCostTrend?.length > 0 && (
                                <div className="mt-4">
                                    <h4 className="text-sm font-medium text-gray-700 mb-3">{t('costAnalysis.costTrendChart')}</h4>
                                    <div className="flex items-end gap-2 h-32 bg-gray-50 p-4 rounded-lg">
                                        {product.monthlyCostTrend.map((m, i) => {
                                            const maxCost = Math.max(...product.monthlyCostTrend.map(x => x.averageCost))
                                            const heightPercent = maxCost > 0 ? (m.averageCost / maxCost) * 100 : 0
                                            return (
                                                <div key={i} className="flex-1 flex flex-col items-center">
                                                    <div
                                                        className="w-full bg-indigo-500 rounded-t transition-all hover:bg-indigo-600"
                                                        style={{ height: `${heightPercent}%`, minHeight: '4px' }}
                                                        title={`${m.monthName}: ${formatCurrency(m.averageCost)}`}
                                                    />
                                                    <span className="text-xs text-gray-500 mt-1 rotate-45 origin-left whitespace-nowrap">
                                                        {m.monthName.split(' ')[0]}
                                                    </span>
                                                </div>
                                            )
                                        })}
                                    </div>
                                </div>
                            )}
                        </div>
                    )}

                    {activeTab === 'suppliers' && (
                        <table className="w-full text-sm">
                            <thead className="text-xs text-gray-700 uppercase bg-gray-50">
                                <tr>
                                    <th className="px-3 py-2">{t('costAnalysis.supplier')}</th>
                                    <th className="px-3 py-2 text-center">{t('costAnalysis.invoices')}</th>
                                    <th className="px-3 py-2 text-center">{t('costAnalysis.totalQty')}</th>
                                    <th className="px-3 py-2 text-right">{t('costAnalysis.totalValue')}</th>
                                    <th className="px-3 py-2 text-right">{t('costAnalysis.avgCostUnit')}</th>
                                    <th className="px-3 py-2 text-center">{t('costAnalysis.contribution')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200">
                                {product.supplierCostBreakdown?.map((s, i) => (
                                    <tr key={i} className="hover:bg-gray-50">
                                        <td className="px-3 py-2 font-medium">{s.supplierName}</td>
                                        <td className="px-3 py-2 text-center">{s.invoiceCount}</td>
                                        <td className="px-3 py-2 text-center">{s.totalQuantity.toFixed(0)}</td>
                                        <td className="px-3 py-2 text-right">{formatCurrency(s.totalValue)}</td>
                                        <td className="px-3 py-2 text-right font-medium text-indigo-600">
                                            {formatCurrency(s.averageCost)}
                                        </td>
                                        <td className="px-3 py-2 text-center">
                                            <div className="flex items-center justify-center gap-2">
                                                <div className="w-16 bg-gray-200 rounded-full h-2">
                                                    <div
                                                        className="bg-indigo-500 h-2 rounded-full"
                                                        style={{ width: `${s.contributionPercentage}%` }}
                                                    />
                                                </div>
                                                <span className="text-xs">{s.contributionPercentage.toFixed(1)}%</span>
                                            </div>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    )}
                </div>
            </div>
        </div>
    )
}

export default CostAnalysisReport
