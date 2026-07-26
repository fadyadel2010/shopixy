import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { formatDate } from '../utils/dateFormat'

export default function SalesRepReports() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const { formatCurrency } = useSettings()
    const { showError } = useToast()

    const [loading, setLoading] = useState(false)
    const [activeTab, setActiveTab] = useState('performance') // performance, commission, comparison
    const [startDate, setStartDate] = useState(new Date(new Date().setDate(1)).toISOString().split('T')[0]) // First day of month
    const [endDate, setEndDate] = useState(new Date().toISOString().split('T')[0])

    // Performance data
    const [performances, setPerformances] = useState([])
    const [selectedRep, setSelectedRep] = useState(null)
    const [detailedPerformance, setDetailedPerformance] = useState(null)

    // Commission data
    const [commissionReports, setCommissionReports] = useState([])

    // Comparison data
    const [comparison, setComparison] = useState(null)

    const textAlignClass = isRTL ? 'text-right' : 'text-left'
    const numericAlignClass = isRTL ? 'text-left' : 'text-right'

    useEffect(() => {
        fetchData()
    }, [startDate, endDate, activeTab])

    const fetchData = async () => {
        setLoading(true)
        try {
            if (activeTab === 'performance') {
                await fetchPerformance()
            } else if (activeTab === 'commission') {
                await fetchCommission()
            } else if (activeTab === 'comparison') {
                await fetchComparison()
            }
        } catch (error) {
            console.error('Error fetching data:', error)
            showError(t('salesRepReports.failedToLoad'))
        } finally {
            setLoading(false)
        }
    }

    const fetchPerformance = async () => {
        const response = await api.get(`/salesrepreports/performance-summary?startDate=${startDate}&endDate=${endDate}`)
        setPerformances(response.data)
    }

    const fetchDetailedPerformance = async (salesRepId) => {
        const response = await api.get(`/salesrepreports/${salesRepId}/detailed-performance?startDate=${startDate}&endDate=${endDate}`)
        setDetailedPerformance(response.data)
        setSelectedRep(salesRepId)
    }

    const fetchCommission = async () => {
        const response = await api.get(`/salesrepreports/commission-report?startDate=${startDate}&endDate=${endDate}`)
        setCommissionReports(response.data)
    }

    const fetchComparison = async () => {
        const response = await api.get(`/salesrepreports/comparison?startDate=${startDate}&endDate=${endDate}`)
        setComparison(response.data)
    }

    const getAchievementColor = (percentage) => {
        if (percentage >= 100) return 'text-green-600 bg-green-100'
        if (percentage >= 80) return 'text-yellow-600 bg-yellow-100'
        return 'text-red-600 bg-red-100'
    }

    return (
        <div dir={dir} className="h-full flex flex-col gap-6">
            <div className="flex justify-between items-center">
                <h1 className="text-2xl font-bold text-gray-800">{t('salesRepReports.title')}</h1>
            </div>

            {/* Date Range Filter */}
            <div className="bg-white p-4 rounded-lg shadow">
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    <div>
                        <label className="label">{t('salesRepReports.startDate')}</label>
                        <input
                            type="date"
                            value={startDate}
                            onChange={(e) => setStartDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div>
                        <label className="label">{t('salesRepReports.endDate')}</label>
                        <input
                            type="date"
                            value={endDate}
                            onChange={(e) => setEndDate(e.target.value)}
                            className="input w-full"
                        />
                    </div>
                    <div className="flex items-end">
                        <button onClick={fetchData} className="btn-primary w-full">
                            {t('salesRepReports.refreshData')}
                        </button>
                    </div>
                </div>
            </div>

            {/* Tabs */}
            <div className="bg-white rounded-lg shadow">
                <div className="border-b border-gray-200">
                    <nav className="flex -mb-px">
                        <button
                            onClick={() => setActiveTab('performance')}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'performance'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            {t('salesRepReports.performance')}
                        </button>
                        <button
                            onClick={() => setActiveTab('commission')}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'commission'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            {t('salesRepReports.commission')}
                        </button>
                        <button
                            onClick={() => setActiveTab('comparison')}
                            className={`px-6 py-3 text-sm font-medium border-b-2 ${activeTab === 'comparison'
                                ? 'border-blue-500 text-blue-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            {t('salesRepReports.comparison')}
                        </button>
                    </nav>
                </div>

                <div className="p-6">
                    {loading ? (
                        <div className="text-center py-12 text-gray-500">{t('salesRepReports.loading')}</div>
                    ) : (
                        <>
                            {/* Performance Tab */}
                            {activeTab === 'performance' && (
        <div className="space-y-6">
                                    {!selectedRep ? (
                                        <div className="overflow-x-auto">
                                            <table className="w-full">
                                                <thead className="bg-gray-50">
                                                    <tr>
                                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${textAlignClass}`}>{t('salesRepReports.salesRep')}</th>
                                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.salesCount')}</th>
                                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.totalSales')}</th>
                                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.avgSale')}</th>
                                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.target')}</th>
                                                        <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('salesRepReports.achievement')}</th>
                                                        <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.commissionEarned')}</th>
                                                        <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('common.actions')}</th>
                                                    </tr>
                                                </thead>
                                                <tbody className="divide-y divide-gray-200">
                                                    {performances.length === 0 ? (
                                                        <tr>
                                                            <td colSpan="8" className="px-4 py-8 text-center text-gray-500">
                                                                {t('salesRepReports.noData')}
                                                            </td>
                                                        </tr>
                                                    ) : (
                                                        performances.map(perf => (
                                                            <tr key={perf.salesRepId} className="hover:bg-gray-50">
                                                                <td className="px-4 py-3 text-sm font-medium text-gray-900">
                                                                    {perf.salesRepName}
                                                                </td>
                                                                    <td className={`px-4 py-3 text-sm text-gray-600 ${numericAlignClass}`}>
                                                                    {perf.salesCount}
                                                                </td>
                                                                    <td className={`px-4 py-3 text-sm font-medium text-gray-900 ${numericAlignClass}`}>
                                                                    {formatCurrency(perf.totalSales)}
                                                                </td>
                                                                    <td className={`px-4 py-3 text-sm text-gray-600 ${numericAlignClass}`}>
                                                                    {formatCurrency(perf.averageSaleValue)}
                                                                </td>
                                                                    <td className={`px-4 py-3 text-sm text-gray-600 ${numericAlignClass}`}>
                                                                    {formatCurrency(perf.periodTarget)}
                                                                </td>
                                                                <td className="px-4 py-3 text-center">
                                                                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${getAchievementColor(perf.achievementPercentage)}`}>
                                                                        {perf.achievementPercentage.toFixed(1)}%
                                                                    </span>
                                                                </td>
                                                                    <td className={`px-4 py-3 text-sm font-medium text-green-600 ${numericAlignClass}`}>
                                                                    {formatCurrency(perf.commissionEarned)}
                                                                </td>
                                                                <td className="px-4 py-3 text-center">
                                                                    <button
                                                                        onClick={() => fetchDetailedPerformance(perf.salesRepId)}
                                                                        className="text-blue-600 hover:text-blue-800 text-sm"
                                                                    >
                                                                        {t('salesRepReports.viewDetails')}
                                                                    </button>
                                                                </td>
                                                            </tr>
                                                        ))
                                                    )}
                                                </tbody>
                                            </table>
                                        </div>
                                    ) : (
                                        // Detailed Performance View
                                        detailedPerformance && (
                                            <div className="space-y-6">
                                                <div className="flex justify-between items-center">
                                                    <h2 className="text-xl font-bold">{detailedPerformance.salesRepName}</h2>
                                                    <button
                                                        onClick={() => {
                                                            setSelectedRep(null)
                                                            setDetailedPerformance(null)
                                                        }}
                                                        className="btn-secondary"
                                                    >
                                                        {t('salesRepReports.backToSummary')}
                                                    </button>
                                                </div>

                                                {/* Summary Cards */}
                                                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                                                    <div className="bg-blue-50 p-4 rounded-lg">
                                                        <p className="text-sm text-gray-600">{t('salesRepReports.totalSales')}</p>
                                                        <p className="text-2xl font-bold text-blue-600">{formatCurrency(detailedPerformance.totalSales)}</p>
                                                    </div>
                                                    <div className="bg-green-50 p-4 rounded-lg">
                                                        <p className="text-sm text-gray-600">{t('salesRepReports.salesCount')}</p>
                                                        <p className="text-2xl font-bold text-green-600">{detailedPerformance.salesCount}</p>
                                                    </div>
                                                    <div className="bg-purple-50 p-4 rounded-lg">
                                                        <p className="text-sm text-gray-600">{t('salesRepReports.avgSaleValue')}</p>
                                                        <p className="text-2xl font-bold text-purple-600">{formatCurrency(detailedPerformance.averageSaleValue)}</p>
                                                    </div>
                                                    <div className="bg-orange-50 p-4 rounded-lg">
                                                        <p className="text-sm text-gray-600">{t('salesRepReports.achievement')}</p>
                                                        <p className={`text-2xl font-bold ${detailedPerformance.achievementPercentage >= 100 ? 'text-green-600' : 'text-orange-600'}`}>
                                                            {detailedPerformance.achievementPercentage.toFixed(1)}%
                                                        </p>
                                                    </div>
                                                </div>

                                                {/* Top Customers */}
                                                <div>
                                                    <h3 className="text-lg font-semibold mb-3">{t('salesRepReports.topCustomers')}</h3>
                                                    <div className="overflow-x-auto">
                                                        <table className="w-full">
                                                            <thead className="bg-gray-50">
                                                                <tr>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${textAlignClass}`}>{t('salesRepReports.customer')}</th>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${numericAlignClass}`}>{t('salesRepReports.salesCount')}</th>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${numericAlignClass}`}>{t('salesRepReports.totalAmount')}</th>
                                                                </tr>
                                                            </thead>
                                                            <tbody className="divide-y divide-gray-200">
                                                                {detailedPerformance.topCustomers.map((customer, idx) => (
                                                                    <tr key={idx}>
                                                                        <td className="px-4 py-2 text-sm">{customer.customerName}</td>
                                                                    <td className={`px-4 py-2 text-sm ${numericAlignClass}`}>{customer.salesCount}</td>
                                                                    <td className={`px-4 py-2 text-sm font-medium ${numericAlignClass}`}>{formatCurrency(customer.totalAmount)}</td>
                                                                    </tr>
                                                                ))}
                                                            </tbody>
                                                        </table>
                                                    </div>
                                                </div>

                                                {/* Recent Sales */}
                                                <div>
                                                    <h3 className="text-lg font-semibold mb-3">{t('salesRepReports.recentSales')}</h3>
                                                    <div className="overflow-x-auto">
                                                        <table className="w-full">
                                                            <thead className="bg-gray-50">
                                                                <tr>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${textAlignClass}`}>{t('salesRepReports.invoice')}</th>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${textAlignClass}`}>{t('common.date')}</th>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${textAlignClass}`}>{t('salesRepReports.customer')}</th>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${numericAlignClass}`}>{t('salesRepReports.items')}</th>
                                                                    <th className={`px-4 py-2 text-xs font-medium text-gray-500 ${numericAlignClass}`}>{t('salesRepReports.amount')}</th>
                                                                </tr>
                                                            </thead>
                                                            <tbody className="divide-y divide-gray-200">
                                                                {detailedPerformance.recentSales.map(sale => (
                                                                    <tr key={sale.invoiceId}>
                                                                        <td className="px-4 py-2 text-sm font-medium">{sale.invoiceNumber}</td>
                                                                        <td className="px-4 py-2 text-sm">{formatDate(sale.invoiceDate, locale)}</td>
                                                                        <td className="px-4 py-2 text-sm">{sale.customerName}</td>
                                                                    <td className={`px-4 py-2 text-sm ${numericAlignClass}`}>{sale.itemCount}</td>
                                                                    <td className={`px-4 py-2 text-sm font-medium ${numericAlignClass}`}>{formatCurrency(sale.totalAmount)}</td>
                                                                    </tr>
                                                                ))}
                                                            </tbody>
                                                        </table>
                                                    </div>
                                                </div>
                                            </div>
                                        )
                                    )}
                                </div>
                            )}

                            {/* Commission Tab */}
                            {activeTab === 'commission' && (
                                <div className="overflow-x-auto">
                                    <table className="w-full">
                                        <thead className="bg-gray-50">
                                            <tr>
                                                <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${textAlignClass}`}>{t('salesRepReports.salesRep')}</th>
                                                <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.totalSales')}</th>
                                                <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.target')}</th>
                                                <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('salesRepReports.achievement')}</th>
                                                <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.commissionPercent')}</th>
                                                <th className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase ${numericAlignClass}`}>{t('salesRepReports.commissionEarned')}</th>
                                                <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('salesRepReports.status')}</th>
                                            </tr>
                                        </thead>
                                        <tbody className="divide-y divide-gray-200">
                                            {commissionReports.length === 0 ? (
                                                <tr>
                                                    <td colSpan="7" className="px-4 py-8 text-center text-gray-500">
                                                        {t('salesRepReports.noCommissionData')}
                                                    </td>
                                                </tr>
                                            ) : (
                                                commissionReports.map(report => (
                                                    <tr key={report.salesRepId} className="hover:bg-gray-50">
                                                        <td className="px-4 py-3 text-sm font-medium text-gray-900">{report.salesRepName}</td>
                                                        <td className={`px-4 py-3 text-sm font-medium ${numericAlignClass}`}>{formatCurrency(report.totalSales)}</td>
                                                        <td className={`px-4 py-3 text-sm ${numericAlignClass}`}>{formatCurrency(report.target)}</td>
                                                        <td className="px-4 py-3 text-center">
                                                            <span className={`px-2 py-1 rounded-full text-xs font-medium ${getAchievementColor(report.achievementPercentage)}`}>
                                                                {report.achievementPercentage.toFixed(1)}%
                                                            </span>
                                                        </td>
                                                        <td className={`px-4 py-3 text-sm ${numericAlignClass}`}>{report.commissionPercentage}%</td>
                                                        <td className={`px-4 py-3 text-sm font-bold text-green-600 ${numericAlignClass}`}>{formatCurrency(report.commissionEarned)}</td>
                                                        <td className="px-4 py-3 text-center">
                                                            <span className={`px-2 py-1 rounded-full text-xs font-medium ${report.commissionStatus === 'Eligible' ? 'bg-green-100 text-green-800' :
                                                                report.commissionStatus === 'Partial (80%+)' ? 'bg-yellow-100 text-yellow-800' :
                                                                    'bg-gray-100 text-gray-800'
                                                                }`}>
                                                                {report.commissionStatus}
                                                            </span>
                                                        </td>
                                                    </tr>
                                                ))
                                            )}
                                        </tbody>
                                        <tfoot className="bg-gray-50 font-bold">
                                            <tr>
                                                <td className="px-4 py-3 text-sm">{t('salesRepReports.total')}</td>
                                                <td className={`px-4 py-3 text-sm ${numericAlignClass}`}>{formatCurrency(commissionReports.reduce((sum, r) => sum + r.totalSales, 0))}</td>
                                                <td colSpan="4"></td>
                                                <td className={`px-4 py-3 text-sm text-green-600 ${numericAlignClass}`}>{formatCurrency(commissionReports.reduce((sum, r) => sum + r.commissionEarned, 0))}</td>
                                            </tr>
                                        </tfoot>
                                    </table>
                                </div>
                            )}

                            {/* Comparison Tab */}
                            {activeTab === 'comparison' && comparison && (
                                <div className="space-y-6">
                                    {/* Summary Cards */}
                                    <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                                        <div className="bg-blue-50 p-4 rounded-lg">
                                            <p className="text-sm text-gray-600">{t('salesRepReports.totalRevenue')}</p>
                                            <p className="text-2xl font-bold text-blue-600">{formatCurrency(comparison.totalRevenue)}</p>
                                        </div>
                                        <div className="bg-green-50 p-4 rounded-lg">
                                            <p className="text-sm text-gray-600">{t('salesRepReports.totalSales')}</p>
                                            <p className="text-2xl font-bold text-green-600">{comparison.totalSalesCount}</p>
                                        </div>
                                        <div className="bg-purple-50 p-4 rounded-lg">
                                            <p className="text-sm text-gray-600">{t('salesRepReports.avgPerRep')}</p>
                                            <p className="text-2xl font-bold text-purple-600">{formatCurrency(comparison.averageRevenuePerRep)}</p>
                                        </div>
                                        <div className="bg-orange-50 p-4 rounded-lg">
                                            <p className="text-sm text-gray-600">{t('salesRepReports.topPerformer')}</p>
                                            <p className="text-lg font-bold text-orange-600">{comparison.topPerformer}</p>
                                            <p className="text-sm text-gray-600">{formatCurrency(comparison.topPerformerSales)}</p>
                                        </div>
                                    </div>

                                    {/* Bar Chart Visualization */}
                                    <div>
                                        <h3 className="text-lg font-semibold mb-4">{t('salesRepReports.salesComparison')}</h3>
                                        <div className="space-y-3">
                                            {comparison.performances.map((perf, idx) => {
                                                const maxSales = Math.max(...comparison.performances.map(p => p.totalSales))
                                                const percentage = (perf.totalSales / maxSales) * 100

                                                return (
                                                    <div key={perf.salesRepId} className="flex items-center gap-4">
                                                        <div className="w-32 text-sm font-medium text-gray-700">{perf.salesRepName}</div>
                                                        <div className="flex-1 bg-gray-200 rounded-full h-8 relative">
                                                            <div
                                                                className={`h-8 rounded-full flex items-center justify-end pr-3 text-white text-sm font-medium ${idx === 0 ? 'bg-blue-600' :
                                                                    idx === 1 ? 'bg-blue-500' :
                                                                        idx === 2 ? 'bg-blue-400' :
                                                                            'bg-blue-300'
                                                                    }`}
                                                                style={{ width: `${percentage}%` }}
                                                            >
                                                                {formatCurrency(perf.totalSales)}
                                                            </div>
                                                        </div>
                                                        <div className={`w-20 text-sm text-gray-600 ${numericAlignClass}`}>{perf.salesCount} {t('salesRepReports.sales')}</div>
                                                    </div>
                                                )
                                            })}
                                        </div>
                                    </div>
                                </div>
                            )}
                        </>
                    )}
                </div>
            </div>
        </div>
    )
}
