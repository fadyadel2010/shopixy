import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useToast } from '../components/Toast'
import api from '../services/api'
import BarChart from '../components/BarChart'
import { useSettings } from '../contexts/SettingsContext'

export default function InventoryValuation() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)

    useEffect(() => {
        fetchReport()
    }, [])

    const fetchReport = async () => {
        setLoading(true)
        try {
            const response = await api.get('/reports/inventory-valuation')
            setData(response.data)
        } catch (error) {
            console.error('Failed to fetch inventory report:', error)
            showError(t('inventoryValuation.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    // Chart Data
    const warehouseChartData = data?.warehouseValuations.map(w => ({
        label: w.warehouseName,
        value: w.totalValue
    })) || []

    return (
        <div className="space-y-6">
            <div>
                <h1 className="text-2xl font-bold text-gray-900">{t('inventoryValuation.title')}</h1>
                <p className="text-sm text-gray-500">{t('inventoryValuation.subtitle')}</p>
            </div>

            {loading ? (
                <div className="flex justify-center p-12">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
                </div>
            ) : data ? (
                <>
                    {/* Key Metrics */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                        <MetricCard
                            title={t('inventoryValuation.totalValue')}
                            value={formatCurrency(data.totalInventoryValue)}
                            icon="💰"
                            color="bg-green-100 text-green-800"
                        />
                        <MetricCard
                            title={t('inventoryValuation.totalItems')}
                            value={data.totalProductsInStock}
                            icon="📦"
                            color="bg-blue-100 text-blue-800"
                        />
                        <MetricCard
                            title={t('inventoryValuation.lowStockAlerts')}
                            value={data.lowStockItemsCount}
                            icon="⚠️"
                            color={data.lowStockItemsCount > 0 ? "bg-red-100 text-red-800" : "bg-gray-100 text-gray-800"}
                        />
                    </div>

                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        {/* Warehouse Distribution Chart */}
                        <div className="bg-white rounded-xl shadow-sm p-6">
                            <h3 className="text-lg font-bold text-gray-900 mb-4">{t('inventoryValuation.valueByWarehouse')}</h3>
                            <div className="h-64">
                                {warehouseChartData.length > 0 ? (
                                    <BarChart data={warehouseChartData} height={250} color="#3b82f6" />
                                ) : (
                                    <p className="text-center text-gray-500 py-10">{t('common.noData')}</p>
                                )}
                            </div>
                        </div>

                        {/* Warehouse Table */}
                        <div className="bg-white rounded-xl shadow-sm p-6 overflow-hidden">
                            <h3 className="text-lg font-bold text-gray-900 mb-4">{t('inventoryValuation.warehouseBreakdown')}</h3>
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('inventoryValuation.warehouse')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('inventoryValuation.items')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                                {t('inventoryValuation.value')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {data.warehouseValuations.map((w, idx) => (
                                            <tr key={idx} className="hover:bg-gray-50">
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                    {w.warehouseName}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                    {w.itemCount}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                    {formatCurrency(w.totalValue)}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </div>

                    {/* Low Stock Items */}
                    <div className="bg-white rounded-xl shadow-sm p-6 overflow-hidden">
                        <h3 className="text-lg font-bold text-gray-900 mb-4 text-red-600 flex items-center gap-2">
                            <span>⚠️</span> {t('inventoryValuation.lowStockItems')}
                        </h3>
                        {data.lowStockItems.length > 0 ? (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-red-50">
                                        <tr>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-red-700 uppercase tracking-wider">
                                                {t('common.product')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-red-700 uppercase tracking-wider">
                                                {t('common.sku')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-red-700 uppercase tracking-wider">
                                                {t('inventoryValuation.currentStock')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-red-700 uppercase tracking-wider">
                                                {t('inventoryValuation.minStock')}
                                            </th>
                                            <th className="px-6 py-3 text-left text-xs font-medium text-red-700 uppercase tracking-wider">
                                                {t('inventoryValuation.deficit')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {data.lowStockItems.map((item, idx) => (
                                            <tr key={idx} className="hover:bg-red-50/30">
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                    {item.productName}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                    {item.sku || '-'}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-bold text-red-600">
                                                    {item.currentStock}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                    {item.minimumStock}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-red-600">
                                                    {item.reorderQuantity}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        ) : (
                            <p className="text-green-600 font-medium py-4 text-center bg-green-50 rounded-lg">
                                {t('inventoryValuation.allGood')}
                            </p>
                        )}
                    </div>
                </>
            ) : (
                <div className="text-center p-12 bg-white rounded-lg shadow">
                    <p className="text-gray-500">{t('common.loading')}</p>
                </div>
            )}
        </div>
    )
}

function MetricCard({ title, value, icon, color }) {
    return (
        <div className="bg-white rounded-xl shadow-sm p-6 flex items-start justify-between">
            <div>
                <p className="text-sm font-medium text-gray-500">{title}</p>
                <p className="mt-1 text-2xl font-bold text-gray-900">{value}</p>
            </div>
            <div className={`p-3 rounded-lg ${color}`}>
                <span className="text-xl">{icon}</span>
            </div>
        </div>
    )
}
