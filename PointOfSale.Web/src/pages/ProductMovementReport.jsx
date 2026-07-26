import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useSettings } from '../contexts/SettingsContext'
import ReportTruthBadge from '../components/ReportTruthBadge'
import { useRTL } from '../hooks/useRTL'
import { formatDate, formatDateTime, formatTime } from '../utils/dateFormat'
import { fetchAllPagedArray } from '../services/pagedArrayLoader'

export default function ProductMovementReport() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showSuccess, showError } = useToast()
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'

    const [loading, setLoading] = useState(false)
    const [products, setProducts] = useState([])
    const [warehouses, setWarehouses] = useState([])
    const [reportData, setReportData] = useState(null)

    // Filters
    const [selectedProduct, setSelectedProduct] = useState(null)
    const [selectedWarehouse, setSelectedWarehouse] = useState('')
    const [startDate, setStartDate] = useState('')
    const [endDate, setEndDate] = useState('')
    const [searchTerm, setSearchTerm] = useState('')

    useEffect(() => {
        fetchProducts()
        fetchWarehouses()
    }, [])

    const fetchProducts = async () => {
        try {
            const response = await fetchAllPagedArray('/products/lookup')
            setProducts(response)
        } catch (error) {
            console.error('Error fetching products:', error)
        }
    }

    const fetchWarehouses = async () => {
        try {
            const response = await api.get('/warehouses')
            setWarehouses(response.data)
        } catch (error) {
            console.error('Error fetching warehouses:', error)
        }
    }

    const handleGenerateReport = async () => {
        if (!selectedProduct) {
            showError(t('productMovement.selectProduct'))
            return
        }

        setLoading(true)
        try {
            const params = {
                productId: selectedProduct.id,
                ...(selectedWarehouse && { warehouseId: selectedWarehouse }),
                ...(startDate && { startDate }),
                ...(endDate && { endDate })
            }

            const response = await api.get('/reports/product-movement', { params })
            setReportData(response.data)
        } catch (error) {
            console.error('Error generating report:', error)
            showError(t('productMovement.errorGenerating'))
        } finally {
            setLoading(false)
        }
    }

    const handlePrint = () => {
        window.print()
    }

    const handleExportCSV = () => {
        if (!reportData || !reportData.movements.length) {
            showError(t('productMovement.noDataToExport'))
            return
        }

        const headers = [
            t('productMovement.date'),
            t('productMovement.type'),
            t('productMovement.reference'),
            t('productMovement.warehouse'),
            t('productMovement.unit'),
            t('productMovement.quantity'),
            t('productMovement.runningBalance'),
            t('productMovement.price'),
            t('productMovement.totalValue'),
            t('productMovement.notes')
        ].join(',')

        const rows = reportData.movements.map(m => [
            formatDateTime(m.date, locale),
            m.movementType,
            m.referenceNumber,
            m.warehouseName || '-',
            m.unitName,
            m.quantity,
            m.runningBalance,
            m.price || '-',
            m.totalValue || '-',
            m.notes || '-'
        ].join(','))

        const csv = [headers, ...rows].join('\n')
        const blob = new Blob([csv], { type: 'text/csv' })
        const url = window.URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = `product-movement-${reportData.productSku}-${new Date().toISOString().split('T')[0]}.csv`
        a.click()
        window.URL.revokeObjectURL(url)
        showSuccess(t('productMovement.exportSuccess'))
    }

    const filteredProducts = products.filter(p =>
        p.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
        p.sku.toLowerCase().includes(searchTerm.toLowerCase())
    )

    const translateMovementType = (type) => {
        const typeMap = {
            'Purchase': 'purchase',
            'Sale': 'sale',
            'Purchase Return': 'purchaseReturn',
            'Sales Return': 'salesReturn',
            'Adjustment': 'adjustment',
            'Transfer In': 'transferIn',
            'Transfer Out': 'transferOut',
            'Initial Stock': 'initialStock',
            'Opening Balance': 'openingBalance'
        }

        const key = typeMap[type]
        return key ? t(`productMovement.movementTypes.${key}`) : type
    }

    const getMovementColor = (type) => {
        const lowerType = type.toLowerCase()
        if (lowerType.includes('sale') && !lowerType.includes('return')) return 'text-red-600'
        if (lowerType.includes('purchase') && !lowerType.includes('return')) return 'text-green-600'
        if (lowerType.includes('return')) return 'text-blue-600'
        if (lowerType.includes('transfer')) return 'text-purple-600'
        if (lowerType.includes('adjustment')) return 'text-orange-600'
        return 'text-gray-600'
    }

    const getMovementIcon = (type) => {
        const lowerType = type.toLowerCase()
        if (lowerType.includes('sale') && !lowerType.includes('return')) return '📤'
        if (lowerType.includes('purchase') && !lowerType.includes('return')) return '📥'
        if (lowerType.includes('sales return')) return '↩️'
        if (lowerType.includes('purchase return')) return '↪️'
        if (lowerType.includes('transfer in')) return '⬅️'
        if (lowerType.includes('transfer out')) return '➡️'
        if (lowerType.includes('adjustment')) return '⚙️'
        if (lowerType.includes('opening')) return '🏁'
        return '📦'
    }

    return (
        <div dir={dir} className="p-6 max-w-7xl mx-auto">
            {/* Phase C4: UI Truth Label */}
            <ReportTruthBadge type="operational" />

            {/* Header */}
            <div className="mb-6 print:mb-4">
                <h1 className="text-3xl font-bold text-gray-800 mb-2 print:text-2xl">
                    {t('productMovement.title')}
                </h1>
                <p className="text-gray-600 print:hidden">{t('productMovement.subtitle')}</p>
            </div>

            {/* Filters */}
            <div className="bg-white rounded-xl shadow-md p-6 mb-6 print:hidden">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-4">
                    {/* Product Search & Select */}
                    <div className="lg:col-span-2">
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('productMovement.selectProduct')} *
                        </label>
                        <input
                            type="text"
                            placeholder={t('productMovement.searchProduct')}
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent mb-2"
                        />
                        {searchTerm && (
                            <div className="max-h-48 overflow-y-auto border border-gray-200 rounded-lg">
                                {filteredProducts.map(product => (
                                    <button
                                        key={product.id}
                                        onClick={() => {
                                            setSelectedProduct(product)
                                            setSearchTerm('')
                                        }}
                                        className={`w-full px-4 py-2 hover:bg-indigo-50 transition-colors ${tableTextAlignClass}`}
                                    >
                                        <div className="font-medium">{product.name}</div>
                                        <div className="text-sm text-gray-500">SKU: {product.sku}</div>
                                    </button>
                                ))}
                                {filteredProducts.length === 0 && (
                                    <div className="px-4 py-2 text-gray-500 text-center">
                                        {t('productMovement.noProductsFound')}
                                    </div>
                                )}
                            </div>
                        )}
                        {selectedProduct && !searchTerm && (
                            <div className="flex items-center justify-between bg-indigo-50 px-4 py-2 rounded-lg">
                                <div>
                                    <div className="font-medium text-indigo-900">{selectedProduct.name}</div>
                                    <div className="text-sm text-indigo-600">SKU: {selectedProduct.sku}</div>
                                </div>
                                <button
                                    onClick={() => setSelectedProduct(null)}
                                    className="text-red-500 hover:text-red-700"
                                >
                                    ✕
                                </button>
                            </div>
                        )}
                    </div>

                    {/* Warehouse Filter */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('productMovement.warehouse')}
                        </label>
                        <select
                            value={selectedWarehouse}
                            onChange={(e) => setSelectedWarehouse(e.target.value)}
                            className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                        >
                            <option value="">{t('productMovement.allWarehouses')}</option>
                            {warehouses.map(w => (
                                <option key={w.id} value={w.id}>{w.name}</option>
                            ))}
                        </select>
                    </div>

                    {/* Date Range */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('productMovement.startDate')}
                        </label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                        />
                    </div>

                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-2">
                            {t('productMovement.endDate')}
                        </label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                        />
                    </div>
                </div>

                {/* Action Buttons */}
                <div className="flex gap-3">
                    <button
                        onClick={handleGenerateReport}
                        disabled={loading || !selectedProduct}
                        className="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:bg-gray-300 disabled:cursor-not-allowed transition-colors"
                    >
                        {loading ? t('common.loading') : t('productMovement.generateReport')}
                    </button>
                    {reportData && (
                        <>
                            <button
                                onClick={handlePrint}
                                className="px-6 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors"
                            >
                                🖨️ {t('common.print')}
                            </button>
                            <button
                                onClick={handleExportCSV}
                                className="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
                            >
                                📊 {t('common.exportCSV')}
                            </button>
                        </>
                    )}
                </div>
            </div>

            {/* Report Content */}
            {reportData && (
                <div className="bg-white rounded-xl shadow-md p-6 print:shadow-none">
                    {/* Report Header */}
                    <div className="mb-6 pb-4 border-b border-gray-200">
                        <h2 className="text-2xl font-bold text-gray-800 mb-4">
                            {t('productMovement.reportTitle')}
                        </h2>
                        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                            <div>
                                <div className="text-sm text-gray-600">{t('productMovement.productName')}</div>
                                <div className="font-semibold text-gray-800">{reportData.productName}</div>
                            </div>
                            <div>
                                <div className="text-sm text-gray-600">{t('productMovement.sku')}</div>
                                <div className="font-semibold text-gray-800">{reportData.productSku}</div>
                            </div>
                            {reportData.warehouseName && (
                                <div>
                                    <div className="text-sm text-gray-600">{t('productMovement.warehouse')}</div>
                                    <div className="font-semibold text-gray-800">{reportData.warehouseName}</div>
                                </div>
                            )}
                            <div>
                                <div className="text-sm text-gray-600">{t('productMovement.dateRange')}</div>
                                <div className="font-semibold text-gray-800">
                                    {startDate || t('common.all')} - {endDate || t('common.all')}
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Summary Cards */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                        <div className="bg-blue-50 rounded-lg p-4">
                            <div className="text-sm text-blue-600 mb-1">{t('productMovement.openingBalance')}</div>
                            <div className="text-2xl font-bold text-blue-900">{reportData.openingBalance.toFixed(2)}</div>
                        </div>
                        <div className="bg-green-50 rounded-lg p-4">
                            <div className="text-sm text-green-600 mb-1">{t('productMovement.closingBalance')}</div>
                            <div className="text-2xl font-bold text-green-900">{reportData.closingBalance.toFixed(2)}</div>
                        </div>
                        <div className="bg-purple-50 rounded-lg p-4">
                            <div className="text-sm text-purple-600 mb-1">{t('productMovement.totalMovements')}</div>
                            <div className="text-2xl font-bold text-purple-900">{reportData.movements.length}</div>
                        </div>
                    </div>

                    {/* Movements Table */}
                    {reportData.movements.length > 0 ? (
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('productMovement.date')}
                                        </th>
                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('productMovement.type')}
                                        </th>
                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('productMovement.reference')}
                                        </th>
                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                            {t('productMovement.warehouse')}
                                        </th>
                                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            {t('productMovement.quantity')}
                                        </th>
                                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            {t('productMovement.runningBalance')}
                                        </th>
                                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider print:hidden">
                                            {t('productMovement.price')}
                                        </th>
                                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider print:hidden">
                                            {t('productMovement.totalValue')}
                                        </th>
                                    </tr>
                                </thead>
                                <tbody className="bg-white divide-y divide-gray-200">
                                    {reportData.movements.map((movement, index) => (
                                        <tr key={index} className="hover:bg-gray-50">
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900">
                                                {formatDate(movement.date, locale)}
                                                <div className="text-xs text-gray-500">
                                                    {formatTime(movement.date, locale)}
                                                </div>
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap">
                                                <span className={`text-sm font-medium ${getMovementColor(movement.movementType)}`}>
                                                    {getMovementIcon(movement.movementType)} {translateMovementType(movement.movementType)}
                                                </span>
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900">
                                                {movement.referenceNumber}
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-600">
                                                {movement.warehouseName}
                                                {movement.toWarehouseName && (
                                                    <div className="text-xs">→ {movement.toWarehouseName}</div>
                                                )}
                                            </td>
                                            <td className={`px-4 py-3 whitespace-nowrap text-sm text-right font-medium ${movement.quantity >= 0 ? 'text-green-600' : 'text-red-600'
                                                }`}>
                                                {movement.quantity >= 0 ? '+' : ''}{movement.quantity.toFixed(2)} {movement.unitName}
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-right font-bold text-gray-900">
                                                {movement.runningBalance.toFixed(2)}
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-600 print:hidden">
                                                {movement.price ? formatCurrency(movement.price) : '-'}
                                            </td>
                                            <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-900 print:hidden">
                                                {movement.totalValue ? formatCurrency(movement.totalValue) : '-'}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    ) : (
                        <div className="text-center py-12 text-gray-500">
                            <div className="text-4xl mb-2">📦</div>
                            <div>{t('productMovement.noMovements')}</div>
                        </div>
                    )}
                </div>
            )}

            {/* Empty State */}
            {!reportData && !loading && (
                <div className="bg-white rounded-xl shadow-md p-12 text-center">
                    <div className="text-6xl mb-4">📊</div>
                    <h3 className="text-xl font-semibold text-gray-700 mb-2">
                        {t('productMovement.emptyState')}
                    </h3>
                    <p className="text-gray-500">
                        {t('productMovement.emptyStateDesc')}
                    </p>
                </div>
            )}
        </div>
    )
}
