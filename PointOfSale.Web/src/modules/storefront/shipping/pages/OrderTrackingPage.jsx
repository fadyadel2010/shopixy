/**
 * Order Tracking Page
 * Phase: Storefront Shipping Experience
 * 
 * Route: /track-order or /track
 * 
 * Purpose: Allow customers to track shipments without login.
 * 
 * Features:
 * - Guest tracking by email + order number
 * - Shipment timeline display
 * - No login required
 * - Mobile responsive
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Package, Search, Mail, Hash, RefreshCw, AlertCircle
} from 'lucide-react'
import { useGuestTracking } from '../hooks/useShippingApi'
import TrackingSummaryCard from '../components/TrackingSummaryCard'
import ShipmentTimeline from '../components/ShipmentTimeline'
import ShipmentStatusBadge from '../components/ShipmentStatusBadge'

export default function OrderTrackingPage() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const [email, setEmail] = useState('')
    const [orderNumber, setOrderNumber] = useState('')
    const [hasSearched, setHasSearched] = useState(false)

    const { order, loading, error, trackOrder, clearError } = useGuestTracking()

    const handleSubmit = async (e) => {
        e.preventDefault()
        if (!email.trim() || !orderNumber.trim()) return

        setHasSearched(true)
        await trackOrder(email.trim(), orderNumber.trim())
    }

    return (
        <div className="min-h-screen bg-gradient-to-b from-gray-50 to-white">
            {/* Header */}
            <div className="bg-gradient-to-r from-blue-600 to-blue-700 text-white py-12 px-4">
                <div className="max-w-2xl mx-auto text-center">
                    <div className="w-16 h-16 bg-white/20 rounded-2xl flex items-center justify-center mx-auto mb-4">
                        <Package size={32} />
                    </div>
                    <h1 className="text-3xl font-bold mb-2">
                        {t('storefront.shipping.trackOrder', 'Track Your Order')}
                    </h1>
                    <p className="text-blue-100">
                        {t('storefront.shipping.trackOrderDesc', 'Enter your email and order number to track your shipment')}
                    </p>
                </div>
            </div>

            {/* Search Form */}
            <div className="max-w-xl mx-auto px-4 -mt-8">
                <form
                    onSubmit={handleSubmit}
                    className="bg-white rounded-2xl shadow-xl p-6"
                >
                    <div className="space-y-4">
                        {/* Email */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('storefront.shipping.email', 'Email Address')}
                            </label>
                            <div className="relative">
                                <Mail
                                    size={18}
                                    className={`absolute top-1/2 -translate-y-1/2 text-gray-400 ${isRTL ? 'right-3' : 'left-3'}`}
                                />
                                <input
                                    type="email"
                                    value={email}
                                    onChange={(e) => setEmail(e.target.value)}
                                    placeholder={t('storefront.shipping.emailPlaceholder', 'Enter your email')}
                                    required
                                    className={`w-full py-3 border border-gray-300 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent ${isRTL ? 'pr-10 pl-4' : 'pl-10 pr-4'}`}
                                />
                            </div>
                        </div>

                        {/* Order Number */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('storefront.shipping.orderNumber', 'Order Number')}
                            </label>
                            <div className="relative">
                                <Hash
                                    size={18}
                                    className={`absolute top-1/2 -translate-y-1/2 text-gray-400 ${isRTL ? 'right-3' : 'left-3'}`}
                                />
                                <input
                                    type="text"
                                    value={orderNumber}
                                    onChange={(e) => setOrderNumber(e.target.value)}
                                    placeholder={t('storefront.shipping.orderPlaceholder', 'e.g., ORD-2024-XXXXX')}
                                    required
                                    className={`w-full py-3 border border-gray-300 rounded-xl focus:ring-2 focus:ring-blue-500 focus:border-transparent ${isRTL ? 'pr-10 pl-4' : 'pl-10 pr-4'}`}
                                />
                            </div>
                        </div>

                        {/* Submit */}
                        <button
                            type="submit"
                            disabled={loading || !email.trim() || !orderNumber.trim()}
                            className="w-full py-3 bg-blue-600 text-white rounded-xl font-semibold hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2 transition-colors"
                        >
                            {loading ? (
                                <>
                                    <RefreshCw size={18} className="animate-spin" />
                                    {t('storefront.shipping.tracking', 'Tracking...')}
                                </>
                            ) : (
                                <>
                                    <Search size={18} />
                                    {t('storefront.shipping.trackButton', 'Track Order')}
                                </>
                            )}
                        </button>
                    </div>
                </form>
            </div>

            {/* Results */}
            <div className="max-w-2xl mx-auto px-4 py-8">
                {/* Error */}
                {error && hasSearched && (
                    <div className="bg-red-50 rounded-2xl p-6 text-center">
                        <AlertCircle size={32} className="text-red-400 mx-auto mb-3" />
                        <h3 className="font-semibold text-red-700 mb-2">
                            {t('storefront.shipping.notFound', 'Order Not Found')}
                        </h3>
                        <p className="text-red-600 text-sm">{error}</p>
                    </div>
                )}

                {/* Order Found */}
                {order && !error && (
                    <div className="space-y-6">
                        {/* Order Info */}
                        <div className="bg-white rounded-2xl p-6 shadow-sm">
                            <div className="flex items-center justify-between mb-4">
                                <div>
                                    <p className="text-sm text-gray-500">
                                        {t('storefront.shipping.orderNumber', 'Order Number')}
                                    </p>
                                    <p className="text-xl font-bold text-gray-900">
                                        {order.orderNumber}
                                    </p>
                                </div>
                                <ShipmentStatusBadge status={order.status} />
                            </div>
                        </div>

                        {/* Shipment Info */}
                        {order.shipment ? (
                            <>
                                <TrackingSummaryCard shipment={order.shipment} />
                                <ShipmentTimeline shipment={order.shipment} />
                            </>
                        ) : (
                            <div className="bg-gray-50 rounded-2xl p-6 text-center">
                                <Package size={32} className="text-gray-400 mx-auto mb-3" />
                                <p className="text-gray-600">
                                    {t('storefront.shipping.noShipmentYet', 'Shipment information not available yet')}
                                </p>
                                <p className="text-gray-500 text-sm mt-1">
                                    {t('storefront.shipping.checkBack', 'Please check back later for tracking updates')}
                                </p>
                            </div>
                        )}
                    </div>
                )}

                {/* Initial State */}
                {!hasSearched && !order && (
                    <div className="text-center py-8">
                        <Package size={48} className="text-gray-300 mx-auto mb-4" />
                        <p className="text-gray-500">
                            {t('storefront.shipping.enterDetails', 'Enter your details above to track your order')}
                        </p>
                    </div>
                )}
            </div>
        </div>
    )
}
