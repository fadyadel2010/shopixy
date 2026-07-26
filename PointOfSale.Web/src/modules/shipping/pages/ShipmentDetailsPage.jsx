/**
 * Shipment Details Page
 * Phase: Shipping Frontend
 * 
 * Route: /admin/shipping/shipments/:id
 * 
 * Sections:
 * - Shipment Summary Card
 * - Lifecycle Timeline (CORE FEATURE)
 * - Carrier Metadata
 * - COD Section
 * - Admin Actions
 * 
 * API: GET /api/admin/shipments/:id
 * 
 * PURE RENDERER - NO LIFECYCLE LOGIC
 */

import { useParams, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    ArrowLeft, Package, RefreshCw, Truck,
    ExternalLink, FileText
} from 'lucide-react'
import { useShipmentActionAuthority, useShipmentDetails } from '../hooks/useShipmentsApi'
import ShipmentSummaryCard from '../components/ShipmentSummaryCard'
import ShipmentTimeline from '../components/ShipmentTimeline'
import ShipmentActions from '../components/ShipmentActions'

export default function ShipmentDetailsPage() {
    const { id } = useParams()
    const navigate = useNavigate()
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const { shipment, loading, error, refetch } = useShipmentDetails(id)
    const {
        authority: actionAuthority,
        loading: actionAuthorityLoading,
        error: actionAuthorityError,
        refetch: refetchActionAuthority
    } = useShipmentActionAuthority(shipment?.orderId)

    const formatDate = (dateStr) => {
        if (!dateStr) return '-'
        return new Date(dateStr).toLocaleDateString(i18n.language, {
            year: 'numeric',
            month: 'long',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    // Loading State
    if (loading) {
        return (
            <div className="max-w-5xl mx-auto">
                <div className="flex items-center gap-4 mb-6">
                    <button
                        onClick={() => navigate('/admin/shipping/shipments')}
                        className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
                    >
                        <ArrowLeft size={20} className={isRTL ? 'rotate-180' : ''} />
                    </button>
                    <div className="h-8 w-48 bg-gray-200 rounded animate-pulse" />
                </div>

                <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                    <div className="lg:col-span-2 space-y-6">
                        <div className="bg-white border border-gray-200 rounded-xl p-6 h-64 animate-pulse" />
                        <div className="bg-white border border-gray-200 rounded-xl p-6 h-96 animate-pulse" />
                    </div>
                    <div className="space-y-6">
                        <div className="bg-white border border-gray-200 rounded-xl p-6 h-48 animate-pulse" />
                    </div>
                </div>
            </div>
        )
    }

    // Error State
    if (error) {
        return (
            <div className="max-w-5xl mx-auto">
                <button
                    onClick={() => navigate('/admin/shipping/shipments')}
                    className="flex items-center gap-2 text-gray-600 hover:text-gray-900 mb-6"
                >
                    <ArrowLeft size={20} className={isRTL ? 'rotate-180' : ''} />
                    Back to Shipments
                </button>

                <div className="bg-red-50 border border-red-200 rounded-xl p-8 text-center">
                    <Package className="text-red-400 mx-auto mb-4" size={48} />
                    <h2 className="text-xl font-semibold text-gray-900 mb-2">
                        Failed to load shipment
                    </h2>
                    <p className="text-gray-600 mb-4">{error}</p>
                    <button
                        onClick={refetch}
                        className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700"
                    >
                        Try Again
                    </button>
                </div>
            </div>
        )
    }

    // Not Found
    if (!shipment) {
        return (
            <div className="max-w-5xl mx-auto">
                <button
                    onClick={() => navigate('/admin/shipping/shipments')}
                    className="flex items-center gap-2 text-gray-600 hover:text-gray-900 mb-6"
                >
                    <ArrowLeft size={20} className={isRTL ? 'rotate-180' : ''} />
                    Back to Shipments
                </button>

                <div className="bg-gray-50 border border-gray-200 rounded-xl p-8 text-center">
                    <Package className="text-gray-400 mx-auto mb-4" size={48} />
                    <h2 className="text-xl font-semibold text-gray-900 mb-2">
                        Shipment not found
                    </h2>
                    <p className="text-gray-600">
                        The shipment you're looking for doesn't exist or has been removed.
                    </p>
                </div>
            </div>
        )
    }

    return (
        <div className="max-w-5xl mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-4">
                    <button
                        onClick={() => navigate('/admin/shipping/shipments')}
                        className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
                    >
                        <ArrowLeft size={20} className={isRTL ? 'rotate-180' : ''} />
                    </button>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {shipment.shipmentNumber}
                        </h1>
                        <p className="text-gray-500 text-sm">
                            Created {formatDate(shipment.createdAt)}
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

            {/* Main Content Grid */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                {/* Left Column - Main Content */}
                <div className="lg:col-span-2 space-y-6">
                    {/* Summary Card */}
                    <ShipmentSummaryCard shipment={shipment} />

                    {/* Timeline */}
                    <ShipmentTimeline shipment={shipment} />

                    {/* Carrier Metadata */}
                    <div className="bg-white border border-gray-200 rounded-xl p-6">
                        <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                            <Truck size={20} className="text-gray-400" />
                            Carrier Information
                        </h3>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <p className="text-sm text-gray-500">Carrier Code</p>
                                <p className="font-mono text-gray-900">{shipment.carrierCode}</p>
                            </div>

                            {shipment.awbNumber && (
                                <div>
                                    <p className="text-sm text-gray-500">AWB Number</p>
                                    <p className="font-mono text-gray-900">{shipment.awbNumber}</p>
                                </div>
                            )}

                            {shipment.labelUrl && (
                                <div className="col-span-2">
                                    <p className="text-sm text-gray-500 mb-2">Shipping Label</p>
                                    <a
                                        href={shipment.labelUrl}
                                        target="_blank"
                                        rel="noopener noreferrer"
                                        className="inline-flex items-center gap-2 px-4 py-2 bg-blue-50 text-blue-700 rounded-lg hover:bg-blue-100"
                                    >
                                        <FileText size={16} />
                                        Download Label
                                        <ExternalLink size={14} />
                                    </a>
                                </div>
                            )}

                            {shipment.lastCarrierSyncAtUtc && (
                                <div className="col-span-2 pt-4 border-t border-gray-100">
                                    <p className="text-sm text-gray-500">
                                        Last synced with carrier: {formatDate(shipment.lastCarrierSyncAtUtc)}
                                    </p>
                                </div>
                            )}
                        </div>
                    </div>

                    {/* COD Details (if applicable) */}
                    {shipment.isCashOnDelivery && (
                        <div className="bg-white border border-gray-200 rounded-xl p-6">
                            <h3 className="text-lg font-semibold text-gray-900 mb-4 flex items-center gap-2">
                                💰 Cash on Delivery
                            </h3>

                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <p className="text-sm text-gray-500">COD Amount</p>
                                    <p className="text-2xl font-bold text-gray-900">
                                        {shipment.codAmount?.toLocaleString()} {shipment.currency}
                                    </p>
                                </div>

                                <div>
                                    <p className="text-sm text-gray-500">Collection Status</p>
                                    <p className={`text-lg font-semibold ${shipment.codCollected ? 'text-green-600' : 'text-amber-600'
                                        }`}>
                                        {shipment.codCollected ? '✅ Collected' : '⏳ Pending'}
                                    </p>
                                </div>

                                {shipment.codCollectedAtUtc && (
                                    <div className="col-span-2">
                                        <p className="text-sm text-gray-500">Collected At</p>
                                        <p className="text-gray-900">{formatDate(shipment.codCollectedAtUtc)}</p>
                                    </div>
                                )}
                            </div>
                        </div>
                    )}
                </div>

                {/* Right Column - Actions & Sidebar */}
                <div className="space-y-6">
                    {/* Actions */}
                    <ShipmentActions
                        shipment={shipment}
                        actionAuthority={actionAuthority}
                        actionAuthorityLoading={actionAuthorityLoading}
                        actionAuthorityError={actionAuthorityError}
                        refetchActionAuthority={refetchActionAuthority}
                        onActionComplete={() => {
                            refetch()
                            refetchActionAuthority()
                        }}
                    />

                    {/* Quick Info */}
                    <div className="bg-gray-50 border border-gray-200 rounded-xl p-4">
                        <h4 className="text-sm font-semibold text-gray-700 mb-3">
                            Quick Info
                        </h4>
                        <ul className="text-sm text-gray-600 space-y-2">
                            <li className="flex items-center justify-between">
                                <span>Shipment ID</span>
                                <code className="text-xs bg-gray-200 px-2 py-0.5 rounded">
                                    {(shipment.id || shipment.shipmentId)?.slice(0, 8)}...
                                </code>
                            </li>
                            <li className="flex items-center justify-between">
                                <span>Delivery Attempts</span>
                                <span className="font-medium">{shipment.deliveryAttempts || 0}</span>
                            </li>
                            <li className="flex items-center justify-between">
                                <span>Package Count</span>
                                <span className="font-medium">{shipment.packageCount || 1}</span>
                            </li>
                            {shipment.weightKg && (
                                <li className="flex items-center justify-between">
                                    <span>Weight</span>
                                    <span className="font-medium">{shipment.weightKg} kg</span>
                                </li>
                            )}
                        </ul>
                    </div>

                    {/* Notes */}
                    {shipment.notes && (
                        <div className="bg-white border border-gray-200 rounded-xl p-4">
                            <h4 className="text-sm font-semibold text-gray-700 mb-2">
                                Notes
                            </h4>
                            <p className="text-sm text-gray-600">{shipment.notes}</p>
                        </div>
                    )}
                </div>
            </div>
        </div>
    )
}
