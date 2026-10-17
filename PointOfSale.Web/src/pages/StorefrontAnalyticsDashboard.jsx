import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Activity,
    ArrowDownRight,
    CreditCard,
    RefreshCw,
    ShoppingCart,
    Target,
    TrendingUp,
    Users
} from 'lucide-react'
import { getRequestErrorMessage, useLatestRequestGuard } from '../hooks/useLatestRequestGuard'
import { useSettings } from '../contexts/SettingsContext'
import storefrontAnalyticsApi from '../services/storefrontAnalyticsApi'
import StorefrontLiveMonitoring from '../components/StorefrontLiveMonitoring'

const RANGE_OPTIONS = [
    { value: 'today', label: 'Today' },
    { value: '7d', label: '7D' },
    { value: '30d', label: '30D' },
    { value: 'this_month', label: 'This Month' }
]

const CARD_STYLES = {
    blue: 'from-sky-50 to-blue-100 border-blue-200 text-blue-900',
    emerald: 'from-emerald-50 to-green-100 border-green-200 text-emerald-900',
    amber: 'from-amber-50 to-orange-100 border-orange-200 text-amber-900',
    rose: 'from-rose-50 to-red-100 border-red-200 text-rose-900',
    violet: 'from-violet-50 to-purple-100 border-purple-200 text-violet-900',
    slate: 'from-slate-50 to-gray-100 border-gray-200 text-slate-900'
}

function MetricCard({ label, value, hint, color, icon: Icon }) {
    return (
        <div className={`rounded-2xl border bg-gradient-to-br p-5 shadow-sm ${CARD_STYLES[color]}`}>
            <div className="flex items-start justify-between gap-4">
                <div>
                    <p className="text-sm font-medium text-gray-600">{label}</p>
                    <p className="mt-3 text-3xl font-bold">{value}</p>
                    {hint ? <p className="mt-2 text-sm text-gray-500">{hint}</p> : null}
                </div>
                <div className="rounded-xl bg-white/70 p-3 shadow-sm">
                    <Icon size={22} />
                </div>
            </div>
        </div>
    )
}

function SectionCard({ title, subtitle, children }) {
    return (
        <section className="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
            <div className="mb-5 flex items-start justify-between gap-4">
                <div>
                    <h3 className="text-lg font-semibold text-gray-900">{title}</h3>
                    {subtitle ? <p className="mt-1 text-sm text-gray-500">{subtitle}</p> : null}
                </div>
            </div>
            {children}
        </section>
    )
}

export default function StorefrontAnalyticsDashboard() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    const [range, setRange] = useState('7d')
    const [dashboard, setDashboard] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchDashboard = useCallback(async (silent = false) => {
        const requestId = beginRequest()

        try {
            if (!silent) {
                setLoading(true)
            }

            setError(null)
            const data = await storefrontAnalyticsApi.getStorefrontAnalyticsDashboard(range)

            if (!isLatestRequest(requestId)) {
                return
            }

            setDashboard(data)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Storefront Analytics] Failed to fetch dashboard:', err)
            setError(getRequestErrorMessage(err, 'Failed to load storefront analytics dashboard'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest, range])

    useEffect(() => {
        void fetchDashboard()
    }, [fetchDashboard])

    const topProducts = dashboard?.topSellingProducts || []
    const funnel = useMemo(() => dashboard?.funnel || {}, [dashboard?.funnel])
    const funnelSteps = useMemo(() => ([
        { key: 'pageView', label: t('storefrontAnalytics.pageView', 'Page View'), count: funnel.pageView || 0 },
        { key: 'viewProduct', label: t('storefrontAnalytics.viewProduct', 'View Product'), count: funnel.viewProduct || 0 },
        { key: 'addToCart', label: t('storefrontAnalytics.addToCart', 'Add To Cart'), count: funnel.addToCart || 0 },
        { key: 'beginCheckout', label: t('storefrontAnalytics.beginCheckout', 'Begin Checkout'), count: funnel.beginCheckout || 0 },
        { key: 'purchaseCompleted', label: t('storefrontAnalytics.purchaseCompleted', 'Purchase Completed'), count: funnel.purchaseCompleted || 0 }
    ]), [funnel, t])

    const maxProductSales = Math.max(...topProducts.map(product => product.sales), 0)
    const maxFunnelCount = Math.max(...funnelSteps.map(step => step.count), 0)

    if (loading && !dashboard) {
        return (
            <div className="flex h-80 items-center justify-center">
                <div className="text-center">
                    <div className="mx-auto h-12 w-12 animate-spin rounded-full border-b-2 border-indigo-600"></div>
                    <p className="mt-4 text-gray-600">{t('common.loading', 'Loading...')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                <div>
                    <h2 className="text-3xl font-bold text-gray-900">
                        {t('storefrontAnalytics.title', 'Storefront Analytics')}
                    </h2>
                    <p className="mt-1 text-gray-600">
                        {t('storefrontAnalytics.subtitle', 'A dedicated storefront dashboard built from existing analytics, order, and checkout data.')}
                    </p>
                </div>

                <div className="flex flex-wrap items-center gap-2">
                    <div className="inline-flex rounded-xl border border-gray-200 bg-white p-1 shadow-sm">
                        {RANGE_OPTIONS.map(option => (
                            <button
                                key={option.value}
                                onClick={() => setRange(option.value)}
                                className={`rounded-lg px-4 py-2 text-sm font-medium transition-colors ${range === option.value
                                    ? 'bg-indigo-600 text-white shadow-sm'
                                    : 'text-gray-600 hover:bg-gray-50'
                                    }`}
                            >
                                {t(`storefrontAnalytics.ranges.${option.value}`, option.label)}
                            </button>
                        ))}
                    </div>

                    <button
                        onClick={() => fetchDashboard()}
                        disabled={loading}
                        className="inline-flex items-center gap-2 rounded-xl border border-gray-200 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm transition-colors hover:bg-gray-50 disabled:opacity-60"
                    >
                        <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                        {t('common.refresh', 'Refresh')}
                    </button>
                </div>
            </div>

            {error ? (
                <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
                    {error}
                </div>
            ) : null}

            <StorefrontLiveMonitoring />

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard
                    label={t('storefrontAnalytics.visits', 'Visits')}
                    value={(dashboard?.visits || 0).toLocaleString()}
                    hint={t('storefrontAnalytics.uniqueSessions', 'Unique sessions from storefront page views')}
                    color="blue"
                    icon={Users}
                />
                <MetricCard
                    label={t('storefrontAnalytics.orders', 'Orders')}
                    value={(dashboard?.orders || 0).toLocaleString()}
                    hint={t('storefrontAnalytics.storefrontOrders', 'Storefront-origin orders only')}
                    color="emerald"
                    icon={ShoppingCart}
                />
                <MetricCard
                    label={t('storefrontAnalytics.revenue', 'Revenue')}
                    value={formatCurrency(dashboard?.revenue || 0)}
                    hint={t('storefrontAnalytics.grossRevenue', 'Revenue from storefront orders in the selected period')}
                    color="amber"
                    icon={TrendingUp}
                />
                <MetricCard
                    label={t('storefrontAnalytics.conversionRate', 'Conversion Rate')}
                    value={`${(dashboard?.conversionRate || 0).toFixed(2)}%`}
                    hint={t('storefrontAnalytics.visitsToOrders', 'Orders divided by unique visits')}
                    color="violet"
                    icon={Target}
                />
            </div>

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard
                    label={t('storefrontAnalytics.addToCart', 'Add To Cart')}
                    value={(dashboard?.addToCart || 0).toLocaleString()}
                    hint={t('storefrontAnalytics.cartIntent', 'Storefront intent before checkout')}
                    color="slate"
                    icon={Activity}
                />
                <MetricCard
                    label={t('storefrontAnalytics.checkoutStarted', 'Checkout Started')}
                    value={(dashboard?.checkoutStarted || 0).toLocaleString()}
                    hint={t('storefrontAnalytics.checkoutIntent', 'BeginCheckout analytics events')}
                    color="blue"
                    icon={CreditCard}
                />
                <MetricCard
                    label={t('storefrontAnalytics.abandonedOrders', 'Abandoned Orders')}
                    value={(dashboard?.abandonedCheckouts || 0).toLocaleString()}
                    hint={`${t('storefrontAnalytics.abandonedRevenue', 'Lost revenue')}: ${formatCurrency(dashboard?.abandonedRevenue || 0)}`}
                    color="rose"
                    icon={ArrowDownRight}
                />
                <MetricCard
                    label={t('storefrontAnalytics.averageOrderValue', 'Average Order Value')}
                    value={formatCurrency(dashboard?.averageOrderValue || 0)}
                    hint={t('storefrontAnalytics.revenuePerOrder', 'Revenue divided by storefront orders')}
                    color="emerald"
                    icon={TrendingUp}
                />
            </div>

            <SectionCard
                title={t('storefrontAnalytics.topSellingProducts', 'Top Selling Products')}
                subtitle={t('storefrontAnalytics.topSellingProductsSubtitle', 'Top 5 storefront products ranked by quantity sold.')}
            >
                {topProducts.length === 0 ? (
                    <div className="rounded-xl border border-dashed border-gray-200 bg-gray-50 px-6 py-10 text-center text-sm text-gray-500">
                        {t('storefrontAnalytics.noTopProducts', 'No storefront product sales found for this range.')}
                    </div>
                ) : (
                    <div className="space-y-4">
                        {topProducts.map((product, index) => {
                            const width = maxProductSales > 0 ? Math.max((product.sales / maxProductSales) * 100, 8) : 0

                            return (
                                <div key={`${product.productName}-${index}`} className="rounded-xl border border-gray-100 bg-gray-50 p-4">
                                    <div className="mb-3 flex items-center justify-between gap-4">
                                        <div>
                                            <p className="text-sm font-semibold text-gray-900">{product.productName}</p>
                                            <p className="text-xs text-gray-500">
                                                {t('storefrontAnalytics.unitsSold', 'Units sold')}: {product.sales.toLocaleString()}
                                            </p>
                                        </div>
                                        <div className="rounded-lg bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm">
                                            #{index + 1}
                                        </div>
                                    </div>
                                    <div className="h-3 rounded-full bg-white">
                                        <div
                                            className="h-3 rounded-full bg-gradient-to-r from-indigo-500 to-cyan-400"
                                            style={{ width: `${width}%` }}
                                        />
                                    </div>
                                </div>
                            )
                        })}
                    </div>
                )}
            </SectionCard>

            <SectionCard
                title={t('storefrontAnalytics.funnelOverview', 'Funnel Overview')}
                subtitle={t('storefrontAnalytics.funnelOverviewSubtitle', 'Simplified purchase funnel using the existing analytics funnel steps.')}
            >
                <div className="grid grid-cols-1 gap-4 xl:grid-cols-5">
                    {funnelSteps.map((step, index) => {
                        const width = maxFunnelCount > 0 ? Math.max((step.count / maxFunnelCount) * 100, 6) : 0

                        return (
                            <div key={step.key} className="rounded-xl border border-gray-100 bg-gray-50 p-4">
                                <div className="mb-3 flex items-center justify-between">
                                    <span className="text-xs font-semibold uppercase tracking-wide text-gray-400">
                                        {index + 1}
                                    </span>
                                    <span className="text-lg font-bold text-gray-900">
                                        {step.count.toLocaleString()}
                                    </span>
                                </div>
                                <p className="mb-3 text-sm font-semibold text-gray-900">{step.label}</p>
                                <div className="h-2 rounded-full bg-white">
                                    <div
                                        className="h-2 rounded-full bg-gradient-to-r from-emerald-500 to-indigo-500"
                                        style={{ width: `${width}%` }}
                                    />
                                </div>
                            </div>
                        )
                    })}
                </div>
            </SectionCard>
        </div>
    )
}
