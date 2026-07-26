import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useToast } from '../components/Toast'
import api from '../services/api'
import BarChart from '../components/BarChart'
import { useSettings } from '../contexts/SettingsContext'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

export default function SalesAnalysis() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()
    const [loading, setLoading] = useState(false)
    const [data, setData] = useState(null)
    const [dateRange, setDateRange] = useState({
        startDate: new Date().toISOString().split('T')[0],
        endDate: new Date().toISOString().split('T')[0]
    })

    useEffect(() => {
        fetchReport()
    }, [dateRange])

    const fetchReport = async () => {
        setLoading(true)
        try {
            const response = await api.get('/reports/sales-analysis', {
                params: dateRange
            })
            setData(response.data)
        } catch (error) {
            console.error('Failed to fetch sales analysis:', error)
            showError(t('salesAnalysis.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    // Chart Data Preparation
    const salesChartData = data?.dailySales.map(d => ({
        label: formatDate(d.date, locale),
        value: d.revenue
    })) || []

    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'

    return (
        <div dir={dir} className="space-y-6">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('salesAnalysis.title')}</h1>
                    <p className="text-sm text-gray-500">{t('salesAnalysis.subtitle')}</p>
                </div>

                <div className="flex items-center gap-2">
                    <input
                        type="date"
                        value={dateRange.startDate}
                        onChange={(e) => setDateRange(prev => ({ ...prev, startDate: e.target.value }))}
                        className="rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                    />
                    <span className="text-gray-500">{t('common.to')}</span>
                    <input
                        type="date"
                        value={dateRange.endDate}
                        onChange={(e) => setDateRange(prev => ({ ...prev, endDate: e.target.value }))}
                        className="rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                    />
                    <button
                        onClick={fetchReport}
                        className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors"
                    >
                        {t('common.refresh')}
                    </button>
                </div>
            </div>

            {loading ? (
                <div className="flex justify-center p-12">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
                </div>
            ) : data ? (
                <>
                    {/* Key Metrics */}
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
                        <MetricCard
                            title={t('salesAnalysis.totalRevenue')}
                            value={formatCurrency(data.totalRevenue)}
                            icon="💰"
                            color="bg-green-100 text-green-800"
                        />
                        <MetricCard
                            title={t('salesAnalysis.totalProfit')}
                            value={formatCurrency(data.totalProfit)}
                            icon="📈"
                            color="bg-blue-100 text-blue-800"
                        />
                        <MetricCard
                            title={t('salesAnalysis.totalOrders')}
                            value={data.totalOrders}
                            icon="🛒"
                            color="bg-indigo-100 text-indigo-800"
                        />
                        <MetricCard
                            title={t('salesAnalysis.avgOrderValue')}
                            value={formatCurrency(data.averageOrderValue)}
                            icon="🏷️"
                            color="bg-purple-100 text-purple-800"
                        />
                    </div>

                    {/* Charts & Tables */}
                    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                        {/* Daily Sales Chart */}
                        <div className="bg-white rounded-xl shadow-sm p-6">
                            <h3 className="text-lg font-bold text-gray-900 mb-4">{t('salesAnalysis.dailySalesTrend')}</h3>
                            <div className="h-64">
                                {salesChartData.length > 0 ? (
                                    <BarChart data={salesChartData} height={250} />
                                ) : (
                                    <p className="text-center text-gray-500 py-10">{t('common.noData')}</p>
                                )}
                            </div>
                        </div>

                        {/* Top Products Table */}
                        <div className="bg-white rounded-xl shadow-sm p-6 overflow-hidden">
                            <h3 className="text-lg font-bold text-gray-900 mb-4">{t('salesAnalysis.topSellingProducts')}</h3>
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                                {t('common.product')}
                                            </th>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                                {t('common.quantity')}
                                            </th>
                                            <th className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${tableTextAlignClass}`}>
                                                {t('common.total')}
                                            </th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {data.topProducts.map((product, idx) => (
                                            <tr key={idx} className="hover:bg-gray-50">
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                                    {product.productName}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                    {product.quantity}
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                                    {formatCurrency(product.revenue)}
                                                </td>
                                            </tr>
                                        ))}
                                        {data.topProducts.length === 0 && (
                                            <tr>
                                                <td colSpan="3" className="px-6 py-4 text-center text-gray-500">
                                                    {t('common.noData')}
                                                </td>
                                            </tr>
                                        )}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    </div>
                </>
            ) : (
                <div className="text-center p-12 bg-white rounded-lg shadow">
                    <p className="text-gray-500">{t('salesAnalysis.startPrompt')}</p>
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
