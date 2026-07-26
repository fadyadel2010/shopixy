/**
 * Shipping Health Page
 * Phase: Shipping Frontend
 * 
 * Route: /admin/shipping/health
 * 
 * Displays:
 * - Webhook health per carrier
 * - Tracking sync status
 * - Supported carriers
 * 
 * API: GET /api/webhooks/shipping/health
 * 
 * PURE RENDERER - READ ONLY
 */

import { useTranslation } from 'react-i18next'
import {
    Activity, CheckCircle, AlertCircle, XCircle,
    RefreshCw, Truck, Webhook, Clock
} from 'lucide-react'
import { useShippingHealth } from '../hooks/useShipmentsApi'

export default function ShippingHealthPage() {
    const { t, i18n } = useTranslation()

    const { health, loading, error, refetch } = useShippingHealth()

    const formatDate = (dateStr) => {
        if (!dateStr) return 'Never'
        return new Date(dateStr).toLocaleDateString(i18n.language, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    // Carrier display info
    const carrierInfo = {
        bosta: { name: 'Bosta', color: 'emerald', icon: '🚛' },
        aramex: { name: 'Aramex', color: 'red', icon: '📦' },
        mylerz: { name: 'Mylerz', color: 'blue', icon: '🚚' },
        dhl: { name: 'DHL Express', color: 'yellow', icon: '✈️' }
    }

    return (
        <div className="max-w-4xl mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between mb-8">
                <div className="flex items-center gap-3">
                    <div className="w-10 h-10 bg-emerald-100 rounded-xl flex items-center justify-center">
                        <Activity className="text-emerald-600" size={22} />
                    </div>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('shipping.health', 'Shipping Health')}
                        </h1>
                        <p className="text-gray-500 text-sm">
                            {t('shipping.healthDesc', 'Monitor webhook and tracking sync status')}
                        </p>
                    </div>
                </div>

                <button
                    onClick={refetch}
                    disabled={loading}
                    className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                >
                    <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                    Refresh
                </button>
            </div>

            {/* Loading State */}
            {loading && !health && (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                    {[1, 2, 3, 4].map(i => (
                        <div key={i} className="bg-white border border-gray-200 rounded-xl p-6 animate-pulse">
                            <div className="w-12 h-12 bg-gray-200 rounded-xl mb-4" />
                            <div className="h-5 bg-gray-200 rounded w-1/2 mb-2" />
                            <div className="h-4 bg-gray-100 rounded w-3/4" />
                        </div>
                    ))}
                </div>
            )}

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-xl p-6 mb-6">
                    <div className="flex items-center gap-3">
                        <XCircle className="text-red-500" size={24} />
                        <div>
                            <h3 className="font-semibold text-red-700">Health Check Failed</h3>
                            <p className="text-red-600 text-sm">{error}</p>
                        </div>
                    </div>
                    <button
                        onClick={refetch}
                        className="mt-4 text-sm text-red-700 hover:underline"
                    >
                        Try again
                    </button>
                </div>
            )}

            {/* Health Data */}
            {health && (
                <>
                    {/* Overall Status */}
                    <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                        <div className="flex items-center justify-between">
                            <div className="flex items-center gap-4">
                                <div className={`w-16 h-16 rounded-2xl flex items-center justify-center ${health.status === 'healthy'
                                        ? 'bg-green-100'
                                        : 'bg-yellow-100'
                                    }`}>
                                    {health.status === 'healthy' ? (
                                        <CheckCircle className="text-green-600" size={32} />
                                    ) : (
                                        <AlertCircle className="text-yellow-600" size={32} />
                                    )}
                                </div>
                                <div>
                                    <h2 className="text-xl font-bold text-gray-900">
                                        {health.status === 'healthy' ? 'All Systems Operational' : 'Degraded Performance'}
                                    </h2>
                                    <p className="text-gray-500">
                                        Last checked: {formatDate(health.timestamp)}
                                    </p>
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Supported Carriers */}
                    <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                        <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                            <Truck size={20} className="text-gray-400" />
                            Supported Carriers
                        </h3>

                        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                            {(health.supportedCarriers || []).map(code => {
                                const info = carrierInfo[code] || {
                                    name: code,
                                    color: 'gray',
                                    icon: '📦'
                                }
                                return (
                                    <div
                                        key={code}
                                        className={`p-4 rounded-xl border-2 border-${info.color}-200 bg-${info.color}-50`}
                                    >
                                        <div className="text-2xl mb-2">{info.icon}</div>
                                        <p className="font-semibold text-gray-900">{info.name}</p>
                                        <p className="text-sm text-gray-500 font-mono">{code}</p>
                                    </div>
                                )
                            })}
                        </div>
                    </div>

                    {/* Webhook Status */}
                    <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                        <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                            <Webhook size={20} className="text-gray-400" />
                            Webhook Endpoints
                        </h3>

                        <div className="space-y-3">
                            {(health.supportedCarriers || []).map(code => {
                                const info = carrierInfo[code] || { name: code }
                                return (
                                    <div
                                        key={code}
                                        className="flex items-center justify-between p-4 bg-gray-50 rounded-lg"
                                    >
                                        <div className="flex items-center gap-3">
                                            <CheckCircle className="text-green-500" size={18} />
                                            <span className="font-medium text-gray-900">{info.name}</span>
                                        </div>
                                        <code className="text-sm text-gray-500 bg-gray-200 px-2 py-1 rounded">
                                            /api/webhooks/shipping/{code}
                                        </code>
                                    </div>
                                )
                            })}
                        </div>
                    </div>

                    {/* Tracking Sync Info */}
                    <div className="bg-white border border-gray-200 rounded-xl p-6">
                        <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                            <Clock size={20} className="text-gray-400" />
                            Tracking Sync Job
                        </h3>

                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <div className="p-4 bg-blue-50 rounded-lg">
                                <p className="text-sm text-blue-600 mb-1">Schedule</p>
                                <p className="text-lg font-semibold text-blue-900">Every 30 minutes</p>
                            </div>
                            <div className="p-4 bg-gray-50 rounded-lg">
                                <p className="text-sm text-gray-600 mb-1">Job ID</p>
                                <code className="text-sm font-mono text-gray-900">shipping-tracking-sync</code>
                            </div>
                        </div>

                        <p className="text-sm text-gray-500 mt-4">
                            The tracking sync job automatically polls carrier APIs to update shipment statuses
                            for all active (non-terminal) shipments.
                        </p>
                    </div>

                    {/* Info Card */}
                    <div className="mt-6 p-4 bg-gray-50 border border-gray-200 rounded-xl">
                        <h4 className="text-sm font-semibold text-gray-700 mb-2">
                            ℹ️ About Shipping Health
                        </h4>
                        <ul className="text-sm text-gray-600 space-y-1">
                            <li>• Webhooks receive real-time updates from carriers</li>
                            <li>• Tracking sync runs every 30 minutes as fallback</li>
                            <li>• Terminal states (Delivered, Cancelled, Failed, Returned) don't sync</li>
                            <li>• Status progression is enforced server-side</li>
                        </ul>
                    </div>
                </>
            )}
        </div>
    )
}
