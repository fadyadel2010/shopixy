/**
 * Order Attribution Page
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Route: /analytics/attribution/orders
 * 
 * Shows individual orders with their attribution data:
 * - Order ID
 * - Date
 * - Revenue
 * - Source
 * - Medium
 * - Campaign
 * - Attribution Model
 * 
 * API: GET /api/admin/analytics/attribution/orders
 * 
 * PURE RENDERER - NO ATTRIBUTION LOGIC
 */

import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    FileText,
    ArrowLeft,
    RefreshCw,
    Calendar,
    ChevronDown,
    ExternalLink
} from 'lucide-react'
import { useAttributionOrders } from '../hooks/useAttribution'
import { useSettings } from '../../../../contexts/SettingsContext'

const DATE_PERIODS = [
    { value: 'today', label: 'Today' },
    { value: 'yesterday', label: 'Yesterday' },
    { value: 'last_7_days', label: 'Last 7 Days' },
    { value: 'last_30_days', label: 'Last 30 Days' },
    { value: 'this_month', label: 'This Month' },
    { value: 'last_month', label: 'Last Month' }
]

const ATTRIBUTION_MODELS = [
    { value: 'LastTouch', label: 'Last Touch' },
    { value: 'FirstTouch', label: 'First Touch' }
]

export default function AttributionOrdersPage() {
    const { t, i18n } = useTranslation()
    const { formatCurrency } = useSettings()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    const [model, setModel] = useState('LastTouch')
    const [period, setPeriod] = useState('last_7_days')
    const [showModelDropdown, setShowModelDropdown] = useState(false)
    const [showPeriodDropdown, setShowPeriodDropdown] = useState(false)

    const { data, loading, error, refetch } = useAttributionOrders(model, period)

    const currentModel = ATTRIBUTION_MODELS.find(m => m.value === model)
    const currentPeriod = DATE_PERIODS.find(p => p.value === period)

    const formatDate = (dateString) => {
        return new Date(dateString).toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    const orders = data?.orders || []

    return (
        <div className="max-w-7xl mx-auto">
            {/* Header */}
            <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 mb-8">
                <div>
                    <button
                        onClick={() => navigate('/analytics/attribution')}
                        className={`flex items-center gap-2 text-sm text-gray-500 hover:text-gray-700 mb-3 transition-colors`}
                    >
                        <ArrowLeft size={16} className={isRTL ? 'rotate-180' : ''} />
                        {t('attribution.backToOverview', 'Back to Attribution')}
                    </button>
                    <div className="flex items-center gap-3 mb-2">
                        <div className="w-10 h-10 bg-emerald-100 rounded-xl flex items-center justify-center">
                            <FileText className="text-emerald-600" size={22} />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('attribution.ordersTitle', 'Order Attribution')}
                        </h1>
                    </div>
                    <p className="text-gray-500">
                        {t('attribution.ordersSubtitle', 'See the marketing source for each order')}
                    </p>
                </div>

                <button
                    onClick={refetch}
                    disabled={loading}
                    className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
                >
                    <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                    {t('common.refresh', 'Refresh')}
                </button>
            </div>

            {/* Filters Row */}
            <div className="flex flex-wrap items-center gap-3 mb-6">
                {/* Model Selector */}
                <div className="relative">
                    <button
                        onClick={() => {
                            setShowModelDropdown(!showModelDropdown)
                            setShowPeriodDropdown(false)
                        }}
                        className="flex items-center gap-2 px-4 py-2.5 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                    >
                        <span className="text-sm font-medium text-gray-700">
                            {currentModel?.label}
                        </span>
                        <ChevronDown size={16} className="text-gray-400" />
                    </button>

                    {showModelDropdown && (
                        <div className="absolute top-full mt-1 left-0 bg-white border border-gray-200 rounded-lg shadow-lg z-10 w-40">
                            {ATTRIBUTION_MODELS.map(m => (
                                <button
                                    key={m.value}
                                    onClick={() => {
                                        setModel(m.value)
                                        setShowModelDropdown(false)
                                    }}
                                    className={`w-full px-4 py-2.5 text-left text-sm hover:bg-gray-50 first:rounded-t-lg last:rounded-b-lg ${model === m.value ? 'bg-indigo-50 text-indigo-600' : 'text-gray-700'}`}
                                >
                                    {m.label}
                                </button>
                            ))}
                        </div>
                    )}
                </div>

                {/* Period Selector */}
                <div className="relative">
                    <button
                        onClick={() => {
                            setShowPeriodDropdown(!showPeriodDropdown)
                            setShowModelDropdown(false)
                        }}
                        className="flex items-center gap-2 px-4 py-2.5 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                    >
                        <Calendar size={16} className="text-gray-400" />
                        <span className="text-sm font-medium text-gray-700">
                            {currentPeriod?.label}
                        </span>
                        <ChevronDown size={16} className="text-gray-400" />
                    </button>

                    {showPeriodDropdown && (
                        <div className="absolute top-full mt-1 left-0 bg-white border border-gray-200 rounded-lg shadow-lg z-10 w-48">
                            {DATE_PERIODS.map(p => (
                                <button
                                    key={p.value}
                                    onClick={() => {
                                        setPeriod(p.value)
                                        setShowPeriodDropdown(false)
                                    }}
                                    className={`w-full px-4 py-2.5 text-left text-sm hover:bg-gray-50 first:rounded-t-lg last:rounded-b-lg ${period === p.value ? 'bg-indigo-50 text-indigo-600' : 'text-gray-700'}`}
                                >
                                    {p.label}
                                </button>
                            ))}
                        </div>
                    )}
                </div>

                {/* Count Badge */}
                <div className="ml-auto text-sm text-gray-500">
                    {data?.totalCount || 0} {t('attribution.ordersFound', 'orders')}
                </div>
            </div>

            {/* Click outside to close dropdowns */}
            {(showModelDropdown || showPeriodDropdown) && (
                <div
                    className="fixed inset-0 z-0"
                    onClick={() => {
                        setShowModelDropdown(false)
                        setShowPeriodDropdown(false)
                    }}
                />
            )}

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-xl p-4 mb-6">
                    <p className="text-red-600 text-sm">{error}</p>
                </div>
            )}

            {/* Loading State */}
            {loading && (
                <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
                    <div className="divide-y divide-gray-100">
                        {[1, 2, 3, 4, 5].map(i => (
                            <div key={i} className="p-4 animate-pulse">
                                <div className="flex gap-4">
                                    <div className="h-4 bg-gray-200 rounded w-24" />
                                    <div className="h-4 bg-gray-200 rounded w-32" />
                                    <div className="h-4 bg-gray-200 rounded w-20" />
                                    <div className="h-4 bg-gray-200 rounded flex-1" />
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* Orders Table */}
            {!loading && orders.length > 0 && (
                <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
                    <div className="overflow-x-auto">
                        <table className="w-full">
                            <thead>
                                <tr className="bg-gray-50 border-b border-gray-100">
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.orderId', 'Order ID')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.date', 'Date')}
                                    </th>
                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.revenue', 'Revenue')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.source', 'Source')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.medium', 'Medium')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.campaign', 'Campaign')}
                                    </th>
                                    <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">
                                        {t('attribution.actions', 'Actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {orders.map((order, idx) => (
                                    <tr key={idx} className="hover:bg-gray-50">
                                        <td className="px-4 py-3">
                                            <span className="font-mono text-sm text-gray-900">
                                                {order.orderId?.substring(0, 8)}...
                                            </span>
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600">
                                            {formatDate(order.attributedAt)}
                                        </td>
                                        <td className="px-4 py-3 text-right">
                                            <span className="font-semibold text-gray-900">
                                                {formatCurrency(order.orderValue)}
                                            </span>
                                        </td>
                                        <td className="px-4 py-3">
                                            <span className={`inline-flex items-center px-2 py-1 rounded-full text-xs font-medium ${order.source === 'direct'
                                                ? 'bg-gray-100 text-gray-700'
                                                : 'bg-indigo-100 text-indigo-700'
                                                }`}>
                                                {order.source}
                                            </span>
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600">
                                            {order.medium || '-'}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600">
                                            {order.campaign || '-'}
                                        </td>
                                        <td className="px-4 py-3 text-center">
                                            <button
                                                onClick={() => navigate(`/ecommerce/orders?orderId=${order.orderId}`)}
                                                className="text-indigo-600 hover:text-indigo-800"
                                                title={t('attribution.viewOrder', 'View Order')}
                                            >
                                                <ExternalLink size={16} />
                                            </button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}

            {/* Empty State */}
            {!loading && orders.length === 0 && !error && (
                <div className="bg-white rounded-xl border border-gray-200 p-12 text-center">
                    <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <FileText className="text-gray-400" size={28} />
                    </div>
                    <h3 className="text-lg font-medium text-gray-900 mb-2">
                        {t('attribution.noOrders', 'No Attributed Orders')}
                    </h3>
                    <p className="text-gray-500 mb-4">
                        {t('attribution.noOrdersSubtitle', 'No orders found in this time period with attribution data.')}
                    </p>
                    <p className="text-sm text-gray-400">
                        {t('attribution.noOrdersHint', 'Orders will appear here after customers complete purchases from tracked campaigns.')}
                    </p>
                </div>
            )}
        </div>
    )
}
