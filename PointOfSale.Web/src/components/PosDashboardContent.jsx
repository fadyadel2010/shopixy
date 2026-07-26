import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'
import { useToast } from './Toast'
import LineChart from './LineChart'
import BarChart from './BarChart'
import DonutChart from './DonutChart'
import StorefrontStatusCard from './StorefrontStatusCard'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'

export default function PosDashboardContent() {
    const { t } = useTranslation()
    const { isRTL, dir, locale } = useRTL()
    const navigate = useNavigate()
    const { showError } = useToast()
    const { formatCurrency } = useSettings()

    const [loading, setLoading] = useState(true)
    const [stats, setStats] = useState(null)
    const [salesTrend, setSalesTrend] = useState([])
    const [topProducts, setTopProducts] = useState([])
    const [recentSales, setRecentSales] = useState([])
    const [lowStockItems, setLowStockItems] = useState([])
    const [categorySales, setCategorySales] = useState([])
    const [paymentMethods, setPaymentMethods] = useState([])
    const [trendPeriod, setTrendPeriod] = useState('week')
    const [refreshKey, setRefreshKey] = useState(0)
    const requestIdRef = useRef(0)
    const renderedTrendPeriodRef = useRef(null)

    const fetchDashboardData = useCallback(async (silent = false) => {
        const requestId = ++requestIdRef.current

        try {
            if (!silent) {
                setLoading(true)
            }

            if (renderedTrendPeriodRef.current !== trendPeriod) {
                setSalesTrend([])
            }

            const [
                statsRes,
                trendRes,
                productsRes,
                salesRes,
                stockRes,
                categoryRes,
                paymentRes
            ] = await Promise.all([
                api.get('/dashboard/stats'),
                api.get(`/dashboard/sales-trend?period=${trendPeriod}`),
                api.get('/dashboard/top-products?limit=5'),
                api.get('/dashboard/recent-sales?limit=8'),
                api.get('/dashboard/low-stock?limit=8'),
                api.get('/dashboard/category-sales'),
                api.get('/dashboard/payment-methods')
            ])

            if (requestId !== requestIdRef.current) {
                return
            }

            setStats(statsRes.data)
            setSalesTrend(trendRes.data.data)
            renderedTrendPeriodRef.current = trendPeriod
            setTopProducts(productsRes.data)
            setRecentSales(salesRes.data)
            setLowStockItems(stockRes.data)
            setCategorySales(categoryRes.data)
            setPaymentMethods(paymentRes.data)
        } catch (error) {
            if (requestId !== requestIdRef.current) {
                return
            }

            console.error('Error fetching dashboard data:', error)
            if (!silent) {
                showError(t('common.failedToLoadData'))
            }
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [showError, t, trendPeriod])

    useEffect(() => {
        void fetchDashboardData()

        const interval = setInterval(() => {
            void fetchDashboardData(true)
        }, 60000)

        return () => {
            requestIdRef.current += 1
            clearInterval(interval)
        }
    }, [fetchDashboardData, refreshKey])

    if (loading && !stats) {
        return (
            <div className="flex items-center justify-center h-96" dir={dir}>
                <div className="text-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto"></div>
                    <p className="mt-4 text-gray-600">{t('common.loading')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6" dir={dir}>
            <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                <div className={isRTL ? 'text-right' : 'text-left'}>
                    <h2 className="text-3xl font-bold text-gray-900">{t('dashboard.title')}</h2>
                    <p className="text-gray-600 mt-1">{t('dashboard.welcomeBack')}</p>
                </div>
                <button
                    onClick={() => setRefreshKey(prev => prev + 1)}
                    className={`btn-secondary flex items-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}
                >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                    </svg>
                    {t('common.refresh')}
                </button>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
                <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200">
                    <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <div className={isRTL ? 'text-right' : 'text-left'}>
                            <p className="text-sm font-medium text-blue-600">{t('dashboard.todaySales')}</p>
                            <p className="text-3xl font-bold text-blue-900 mt-2">
                                {formatCurrency(stats?.todaySales || 0)}
                            </p>
                            <div className={`flex items-center mt-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                                {stats?.salesChangePercentage >= 0 ? (
                                    <svg className="w-4 h-4 text-green-600" fill="currentColor" viewBox="0 0 20 20">
                                        <path fillRule="evenodd" d="M5.293 9.707a1 1 0 010-1.414l4-4a1 1 0 011.414 0l4 4a1 1 0 01-1.414 1.414L11 7.414V15a1 1 0 11-2 0V7.414L6.707 9.707a1 1 0 01-1.414 0z" clipRule="evenodd" />
                                    </svg>
                                ) : (
                                    <svg className="w-4 h-4 text-red-600" fill="currentColor" viewBox="0 0 20 20">
                                        <path fillRule="evenodd" d="M14.707 10.293a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 111.414-1.414L9 12.586V5a1 1 0 012 0v7.586l2.293-2.293a1 1 0 011.414 0z" clipRule="evenodd" />
                                    </svg>
                                )}
                                <span className={`text-sm ${isRTL ? 'mr-1' : 'ml-1'} ${stats?.salesChangePercentage >= 0 ? 'text-green-600' : 'text-red-600'}`}>
                                    {Math.abs(stats?.salesChangePercentage || 0).toFixed(1)}% {t('dashboard.fromYesterday')}
                                </span>
                            </div>
                        </div>
                        <div className="text-4xl">💰</div>
                    </div>
                </div>

                <div className="card bg-gradient-to-br from-green-50 to-green-100 border-green-200">
                    <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <div className={isRTL ? 'text-right' : 'text-left'}>
                            <p className="text-sm font-medium text-green-600">{t('dashboard.totalProducts')}</p>
                            <p className="text-3xl font-bold text-green-900 mt-2">
                                {stats?.totalProducts || 0}
                            </p>
                            <p className="text-sm text-green-600 mt-2">
                                {t('dashboard.inventoryValue')}: {formatCurrency(stats?.inventoryValue || 0)}
                            </p>
                        </div>
                        <div className="text-4xl">📦</div>
                    </div>
                </div>

                <div className="card bg-gradient-to-br from-orange-50 to-orange-100 border-orange-200">
                    <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <div className={isRTL ? 'text-right' : 'text-left'}>
                            <p className="text-sm font-medium text-orange-600">{t('dashboard.lowStock')}</p>
                            <p className="text-3xl font-bold text-orange-900 mt-2">
                                {stats?.lowStockCount || 0}
                            </p>
                            <button
                                onClick={() => navigate('/inventory')}
                                className="text-sm text-orange-600 hover:text-orange-700 mt-2 underline"
                            >
                                {t('dashboard.viewDetails')} {isRTL ? '<-' : '->'}
                            </button>
                        </div>
                        <div className="text-4xl">⚠️</div>
                    </div>
                </div>

                <div className="card bg-gradient-to-br from-purple-50 to-purple-100 border-purple-200">
                    <div className={`flex items-center justify-between gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <div className={isRTL ? 'text-right' : 'text-left'}>
                            <p className="text-sm font-medium text-purple-600">{t('dashboard.totalCustomers')}</p>
                            <p className="text-3xl font-bold text-purple-900 mt-2">
                                {stats?.totalCustomers || 0}
                            </p>
                            <p className="text-sm text-purple-600 mt-2">
                                {stats?.todayTransactionCount || 0} {t('dashboard.transactionsToday')}
                            </p>
                        </div>
                        <div className="text-4xl">👥</div>
                    </div>
                </div>

                <StorefrontStatusCard />
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="card">
                    <h3 className={`text-sm font-medium text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.thisWeek')}</h3>
                    <p className="text-2xl font-bold text-gray-900 mt-2">
                        {formatCurrency(stats?.weekSales || 0)}
                    </p>
                </div>
                <div className="card">
                    <h3 className={`text-sm font-medium text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.thisMonth')}</h3>
                    <p className="text-2xl font-bold text-gray-900 mt-2">
                        {formatCurrency(stats?.monthSales || 0)}
                    </p>
                </div>
                <div className="card">
                    <h3 className={`text-sm font-medium text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.thisYear')}</h3>
                    <p className="text-2xl font-bold text-gray-900 mt-2">
                        {formatCurrency(stats?.yearSales || 0)}
                    </p>
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <div className="card">
                    <div className={`flex justify-between items-center mb-4 gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <h3 className={`text-lg font-semibold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.salesTrend')}</h3>
                        <select
                            value={trendPeriod}
                            onChange={(e) => setTrendPeriod(e.target.value)}
                            className={`input text-sm ${isRTL ? 'text-right' : 'text-left'}`}
                        >
                            <option value="week">{t('dashboard.last7Days')}</option>
                            <option value="month">{t('dashboard.last30Days')}</option>
                            <option value="year">{t('dashboard.last12Months')}</option>
                        </select>
                    </div>
                    {salesTrend.length > 0 ? (
                        <LineChart
                            data={salesTrend.map(d => ({ label: d.label, amount: d.amount }))}
                            height={250}
                            color="#3b82f6"
                        />
                    ) : (
                        <div className="h-64 flex items-center justify-center text-gray-500">
                            {t('dashboard.noSalesDataChart')}
                        </div>
                    )}
                </div>

                <div className="card">
                    <h3 className={`text-lg font-semibold text-gray-900 mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.topSellingProducts')}</h3>
                    {topProducts.length > 0 ? (
                        <BarChart
                            data={topProducts.map(p => ({
                                label: p.productName.length > 15
                                    ? p.productName.substring(0, 15) + '...'
                                    : p.productName,
                                value: p.totalRevenue
                            }))}
                            height={250}
                            color="#10b981"
                        />
                    ) : (
                        <div className="h-64 flex items-center justify-center text-gray-500">
                            {t('dashboard.noProductDataChart')}
                        </div>
                    )}
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <div className="card">
                    <h3 className={`text-lg font-semibold text-gray-900 mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.salesByCategory')}</h3>
                    {categorySales.length > 0 ? (
                        <DonutChart
                            data={categorySales.map(c => ({
                                label: c.categoryName,
                                value: c.totalRevenue
                            }))}
                            height={220}
                        />
                    ) : (
                        <div className="h-64 flex items-center justify-center text-gray-500">
                            {t('dashboard.noCategoryData')}
                        </div>
                    )}
                </div>

                <div className="card">
                    <h3 className={`text-lg font-semibold text-gray-900 mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.paymentMethods')}</h3>
                    {paymentMethods.length > 0 ? (
                        <DonutChart
                            data={paymentMethods.map(p => ({
                                label: p.paymentMethod,
                                value: p.totalAmount
                            }))}
                            height={220}
                        />
                    ) : (
                        <div className="h-64 flex items-center justify-center text-gray-500">
                            {t('dashboard.noPaymentData')}
                        </div>
                    )}
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                <div className="card">
                    <div className={`flex justify-between items-center mb-4 gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <h3 className={`text-lg font-semibold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.recentSales')}</h3>
                        <button
                            onClick={() => navigate('/sales-history')}
                            className="text-sm text-primary-600 hover:text-primary-700"
                        >
                            {t('dashboard.viewAll')} {isRTL ? '<-' : '->'}
                        </button>
                    </div>
                    {recentSales.length > 0 ? (
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead>
                                    <tr>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.invoice')}</th>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.customer')}</th>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-left' : 'text-right'}`}>{t('dashboard.amount')}</th>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.date')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {recentSales.map((sale) => (
                                        <tr key={sale.id} className="hover:bg-gray-50">
                                            <td className={`px-3 py-2 text-sm font-medium text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {sale.invoiceNumber}
                                            </td>
                                            <td className={`px-3 py-2 text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {sale.customerName}
                                            </td>
                                            <td className={`px-3 py-2 text-sm font-semibold text-gray-900 ${isRTL ? 'text-left' : 'text-right'}`}>
                                                {formatCurrency(sale.totalAmount)}
                                            </td>
                                            <td className={`px-3 py-2 text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {formatDateTime(sale.invoiceDate, locale)}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    ) : (
                        <p className="text-gray-500 text-center py-8">{t('dashboard.noRecentSales')}</p>
                    )}
                </div>

                <div className="card">
                    <div className={`flex justify-between items-center mb-4 gap-4 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <h3 className={`text-lg font-semibold text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.lowStockAlert')}</h3>
                        <button
                            onClick={() => navigate('/inventory')}
                            className="text-sm text-orange-600 hover:text-orange-700"
                        >
                            {t('dashboard.viewAll')} {isRTL ? '<-' : '->'}
                        </button>
                    </div>
                    {lowStockItems.length > 0 ? (
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead>
                                    <tr>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.product')}</th>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.warehouse')}</th>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-left' : 'text-right'}`}>{t('dashboard.stock')}</th>
                                        <th className={`px-3 py-2 text-xs font-medium text-gray-500 uppercase ${isRTL ? 'text-right' : 'text-left'}`}>{t('common.status')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {lowStockItems.map((item, index) => (
                                        <tr key={index} className="hover:bg-gray-50">
                                            <td className={`px-3 py-2 text-sm font-medium text-gray-900 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {item.productName}
                                            </td>
                                            <td className={`px-3 py-2 text-sm text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>
                                                {item.warehouseName}
                                            </td>
                                            <td className={`px-3 py-2 text-sm ${isRTL ? 'text-left' : 'text-right'}`}>
                                                <span className={`font-semibold ${item.currentStock === 0 ? 'text-red-600' : 'text-orange-600'}`}>
                                                    {item.currentStock}
                                                </span>
                                            </td>
                                            <td className={`px-3 py-2 text-sm ${isRTL ? 'text-right' : 'text-left'}`}>
                                                <span className={`px-2 py-1 rounded-full text-xs ${item.status === 'Out of Stock'
                                                    ? 'bg-red-100 text-red-800'
                                                    : 'bg-orange-100 text-orange-800'
                                                    }`}>
                                                    {item.status === 'Out of Stock' ? t('dashboard.outOfStock') : t('dashboard.lowStockStatus')}
                                                </span>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    ) : (
                        <p className="text-gray-500 text-center py-8">{t('dashboard.allStockGood')}</p>
                    )}
                </div>
            </div>

            <div className="card">
                <h3 className={`text-lg font-semibold text-gray-900 mb-4 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.quickActions')}</h3>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <button
                        onClick={() => navigate('/pos')}
                        className="p-4 bg-gradient-to-br from-blue-50 to-blue-100 hover:from-blue-100 hover:to-blue-200 rounded-lg border border-blue-200 transition-all"
                    >
                        <div className="text-3xl mb-2">🛒</div>
                        <div className="text-sm font-semibold text-blue-900">{t('dashboard.newSale')}</div>
                    </button>
                    <button
                        onClick={() => navigate('/products')}
                        className="p-4 bg-gradient-to-br from-green-50 to-green-100 hover:from-green-100 hover:to-green-200 rounded-lg border border-green-200 transition-all"
                    >
                        <div className="text-3xl mb-2">📦</div>
                        <div className="text-sm font-semibold text-green-900">{t('dashboard.addProduct')}</div>
                    </button>
                    <button
                        onClick={() => navigate('/customers')}
                        className="p-4 bg-gradient-to-br from-purple-50 to-purple-100 hover:from-purple-100 hover:to-purple-200 rounded-lg border border-purple-200 transition-all"
                    >
                        <div className="text-3xl mb-2">👤</div>
                        <div className="text-sm font-semibold text-purple-900">{t('dashboard.addCustomer')}</div>
                    </button>
                    <button
                        onClick={() => navigate('/purchase-invoice')}
                        className="p-4 bg-gradient-to-br from-orange-50 to-orange-100 hover:from-orange-100 hover:to-orange-200 rounded-lg border border-orange-200 transition-all"
                    >
                        <div className="text-3xl mb-2">📥</div>
                        <div className="text-sm font-semibold text-orange-900">{t('dashboard.newPurchase')}</div>
                    </button>
                </div>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="card bg-gradient-to-br from-gray-50 to-gray-100">
                    <h3 className={`text-sm font-medium text-gray-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.totalRevenue')}</h3>
                    <p className="text-2xl font-bold text-gray-900 mt-2">
                        {formatCurrency(stats?.totalRevenue || 0)}
                    </p>
                    <p className="text-xs text-gray-500 mt-1">{t('dashboard.allTime')}</p>
                </div>
                <div className="card bg-gradient-to-br from-green-50 to-green-100">
                    <h3 className={`text-sm font-medium text-green-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.accountsReceivable')}</h3>
                    <p className="text-2xl font-bold text-green-900 mt-2">
                        {formatCurrency(stats?.outstandingReceivables || 0)}
                    </p>
                    <p className="text-xs text-green-600 mt-1">{t('dashboard.outstandingFromCustomers')}</p>
                </div>
                <div className="card bg-gradient-to-br from-red-50 to-red-100">
                    <h3 className={`text-sm font-medium text-red-600 ${isRTL ? 'text-right' : 'text-left'}`}>{t('dashboard.accountsPayable')}</h3>
                    <p className="text-2xl font-bold text-red-900 mt-2">
                        {formatCurrency(stats?.outstandingPayables || 0)}
                    </p>
                    <p className="text-xs text-red-600 mt-1">{t('dashboard.outstandingToSuppliers')}</p>
                </div>
            </div>
        </div>
    )
}
