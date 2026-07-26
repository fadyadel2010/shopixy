import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CreditCard, Eye, Package, RefreshCw, ShoppingCart } from 'lucide-react'
import { getRequestErrorMessage, useLatestRequestGuard } from '../hooks/useLatestRequestGuard'
import storefrontAnalyticsApi from '../services/storefrontAnalyticsApi'

const POLL_INTERVAL_MS = 10000

const LIVE_CARD_STYLES = {
    cyan: 'from-cyan-50 to-sky-100 border-cyan-200 text-cyan-900',
    amber: 'from-amber-50 to-orange-100 border-amber-200 text-amber-900',
    indigo: 'from-indigo-50 to-blue-100 border-indigo-200 text-indigo-900',
    emerald: 'from-emerald-50 to-green-100 border-emerald-200 text-emerald-900'
}

function LiveMetricCard({ label, value, icon: Icon, color }) {
    return (
        <div className={`rounded-2xl border bg-gradient-to-br p-5 shadow-sm ${LIVE_CARD_STYLES[color]}`}>
            <div className="flex items-center justify-between gap-4">
                <div>
                    <p className="text-sm font-medium text-gray-600">{label}</p>
                    <p className="mt-3 text-3xl font-bold">{value}</p>
                </div>
                <div className="rounded-xl bg-white/70 p-3 shadow-sm">
                    <Icon size={22} />
                </div>
            </div>
        </div>
    )
}

function formatRelativeTime(occurredAtUtc) {
    const seconds = Math.max(0, Math.floor((Date.now() - new Date(occurredAtUtc).getTime()) / 1000))

    if (seconds < 60) return `${seconds}s ago`

    const minutes = Math.floor(seconds / 60)
    if (minutes < 60) return `${minutes}m ago`

    const hours = Math.floor(minutes / 60)
    return `${hours}h ago`
}

function getActivityLabel(activity, t) {
    switch (activity.type) {
        case 'AddToCart':
            return t('storefrontLive.activity.addToCart', 'Added to cart')
        case 'BeginCheckout':
            return t('storefrontLive.activity.beginCheckout', 'Checkout started')
        case 'OrderCompleted':
            return t('storefrontLive.activity.orderCompleted', 'Order completed')
        default:
            return activity.type
    }
}

export default function StorefrontLiveMonitoring() {
    const { t } = useTranslation()
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(null)
    const { beginRequest, isLatestRequest } = useLatestRequestGuard()

    const fetchLiveMonitoring = useCallback(async (silent = false) => {
        const requestId = beginRequest()

        try {
            if (!silent) {
                setLoading(true)
            }

            setError(null)
            const response = await storefrontAnalyticsApi.getStorefrontLiveMonitoring()

            if (!isLatestRequest(requestId)) {
                return
            }

            setData(response)
        } catch (err) {
            if (!isLatestRequest(requestId)) {
                return
            }

            console.error('[Storefront Live] Failed to fetch live monitoring:', err)
            setError(getRequestErrorMessage(err, 'Failed to load live store monitoring'))
        } finally {
            if (isLatestRequest(requestId)) {
                setLoading(false)
            }
        }
    }, [beginRequest, isLatestRequest])

    useEffect(() => {
        void fetchLiveMonitoring()

        const intervalId = window.setInterval(() => {
            void fetchLiveMonitoring(true)
        }, POLL_INTERVAL_MS)

        return () => {
            window.clearInterval(intervalId)
        }
    }, [fetchLiveMonitoring])

    return (
        <section className="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
            <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <h3 className="text-lg font-semibold text-gray-900">
                        {t('storefrontLive.title', 'Live Store Monitoring')}
                    </h3>
                    <p className="mt-1 text-sm text-gray-500">
                        {t('storefrontLive.subtitle', 'Real-time storefront activity from the last 5 minutes.')}
                    </p>
                </div>

                <button
                    onClick={() => fetchLiveMonitoring()}
                    disabled={loading}
                    className="inline-flex items-center gap-2 rounded-xl border border-gray-200 bg-white px-4 py-2 text-sm font-medium text-gray-700 transition-colors hover:bg-gray-50 disabled:opacity-60"
                >
                    <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                    {t('common.refresh', 'Refresh')}
                </button>
            </div>

            {error ? (
                <div className="mb-5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">
                    {error}
                </div>
            ) : null}

            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
                <LiveMetricCard
                    label={t('storefrontLive.liveVisitors', 'Live Visitors')}
                    value={(data?.liveVisitors || 0).toLocaleString()}
                    icon={Eye}
                    color="cyan"
                />
                <LiveMetricCard
                    label={t('storefrontLive.activeCarts', 'Active Carts')}
                    value={(data?.activeCarts || 0).toLocaleString()}
                    icon={ShoppingCart}
                    color="amber"
                />
                <LiveMetricCard
                    label={t('storefrontLive.checkoutInProgress', 'Checkout In Progress')}
                    value={(data?.checkoutInProgress || 0).toLocaleString()}
                    icon={CreditCard}
                    color="indigo"
                />
                <LiveMetricCard
                    label={t('storefrontLive.recentOrders', 'Orders in Last 5 Minutes')}
                    value={(data?.recentOrders || 0).toLocaleString()}
                    icon={Package}
                    color="emerald"
                />
            </div>

            <div className="mt-6 rounded-2xl border border-gray-100 bg-gray-50 p-5">
                <div className="mb-4 flex items-center justify-between gap-4">
                    <div>
                        <h4 className="text-sm font-semibold text-gray-900">
                            {t('storefrontLive.recentActivity', 'Recent Activity')}
                        </h4>
                        <p className="mt-1 text-xs text-gray-500">
                            {t('storefrontLive.recentActivitySubtitle', 'Latest storefront checkout and cart events.')}
                        </p>
                    </div>
                    <div className="text-xs text-gray-400">
                        {t('storefrontLive.polling', 'Updates every 10 seconds')}
                    </div>
                </div>

                {!data?.recentActivity?.length ? (
                    <div className="rounded-xl border border-dashed border-gray-200 bg-white px-4 py-8 text-center text-sm text-gray-500">
                        {t('storefrontLive.noRecentActivity', 'No recent storefront activity in the live window.')}
                    </div>
                ) : (
                    <div className="space-y-3">
                        {data.recentActivity.map((activity, index) => (
                            <div
                                key={`${activity.type}-${activity.occurredAtUtc}-${index}`}
                                className="flex items-center justify-between gap-4 rounded-xl border border-white bg-white px-4 py-3 shadow-sm"
                            >
                                <div className="min-w-0">
                                    <p className="text-sm font-semibold text-gray-900">
                                        {getActivityLabel(activity, t)}
                                    </p>
                                    <p className="truncate text-xs text-gray-500">
                                        {activity.product || activity.orderNumber || t('storefrontLive.generalActivity', 'Storefront session activity')}
                                    </p>
                                </div>
                                <div className="whitespace-nowrap text-xs font-medium text-gray-400">
                                    {formatRelativeTime(activity.occurredAtUtc)}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </section>
    )
}
