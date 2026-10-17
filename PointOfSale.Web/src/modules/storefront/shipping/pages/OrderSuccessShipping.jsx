/**
 * Order Success Shipping Component
 * Phase: Storefront Shipping Experience
 * 
 * Component for order confirmation page showing shipping info.
 * Displays shipment summary when order is completed.
 */

import { useTranslation } from 'react-i18next'
import { Package, ExternalLink, Truck, Clock } from 'lucide-react'
import { useShipmentTracking } from '../hooks/useShippingApi'
import CarrierBadge from '../components/CarrierBadge'
import ShipmentStatusBadge from '../components/ShipmentStatusBadge'

export default function OrderSuccessShipping({ orderId, orderNumber }) {
    const { t } = useTranslation()

    const { shipment, loading } = useShipmentTracking(orderId)

    // Loading
    if (loading) {
        return (
            <div className="bg-white rounded-2xl p-6 shadow-sm">
                <div className="animate-pulse">
                    <div className="flex items-center gap-3 mb-4">
                        <div className="w-10 h-10 bg-gray-200 rounded-xl" />
                        <div className="flex-1">
                            <div className="h-4 bg-gray-200 rounded w-1/3 mb-2" />
                            <div className="h-3 bg-gray-100 rounded w-1/2" />
                        </div>
                    </div>
                </div>
            </div>
        )
    }

    // No shipment yet (order just placed)
    if (!shipment) {
        return (
            <div className="bg-gradient-to-r from-blue-50 to-indigo-50 rounded-2xl p-6 border border-blue-100">
                <div className="flex items-center gap-4">
                    <div className="w-12 h-12 bg-blue-100 rounded-xl flex items-center justify-center">
                        <Package className="text-blue-600" size={24} />
                    </div>
                    <div>
                        <h3 className="font-semibold text-gray-900 mb-1">
                            {t('storefront.shipping.preparing', 'Preparing Your Shipment')}
                        </h3>
                        <p className="text-sm text-gray-600">
                            {t('storefront.shipping.preparingDesc', 'We\'re getting your order ready. You\'ll receive tracking information soon.')}
                        </p>
                    </div>
                </div>
            </div>
        )
    }

    return (
        <div className="bg-white rounded-2xl shadow-sm overflow-hidden">
            {/* Header */}
            <div className="p-6 border-b border-gray-100">
                <div className="flex items-center justify-between">
                    <h3 className="font-semibold text-gray-900 flex items-center gap-2">
                        <Truck size={20} className="text-blue-500" />
                        {t('storefront.shipping.shipmentInfo', 'Shipment Information')}
                    </h3>
                    <ShipmentStatusBadge status={shipment.status} />
                </div>
            </div>

            {/* Content */}
            <div className="p-6 space-y-4">
                {/* Carrier */}
                <div className="flex items-center justify-between">
                    <span className="text-gray-500">
                        {t('storefront.shipping.carrier', 'Carrier')}
                    </span>
                    <CarrierBadge
                        carrierCode={shipment.carrierCode}
                        carrierName={shipment.carrierName}
                    />
                </div>

                {/* Tracking Number */}
                {shipment.trackingNumber && (
                    <div className="flex items-center justify-between">
                        <span className="text-gray-500">
                            {t('storefront.shipping.trackingNumber', 'Tracking Number')}
                        </span>
                        <code className="px-3 py-1 bg-gray-100 rounded-lg font-mono text-sm">
                            {shipment.trackingNumber}
                        </code>
                    </div>
                )}

                {/* Estimated Delivery */}
                {shipment.estimatedDelivery && (
                    <div className="flex items-center justify-between">
                        <span className="text-gray-500 flex items-center gap-1">
                            <Clock size={14} />
                            {t('storefront.shipping.estimated', 'Estimated Delivery')}
                        </span>
                        <span className="font-medium text-gray-900">
                            {shipment.estimatedDelivery}
                        </span>
                    </div>
                )}
            </div>

            {/* Track Order CTA */}
            <div className="p-6 bg-gray-50 border-t border-gray-100">
                <div className="flex flex-col sm:flex-row gap-3">
                    {/* Internal Track */}
                    <a
                        href={`/track-order?email=&order=${orderNumber}`}
                        className="flex-1 flex items-center justify-center gap-2 px-4 py-3 bg-blue-600 text-white rounded-xl font-medium hover:bg-blue-700 transition-colors"
                    >
                        <Package size={18} />
                        {t('storefront.shipping.trackOrder', 'Track Your Order')}
                    </a>

                    {/* External Track */}
                    {shipment.trackingUrl && (
                        <a
                            href={shipment.trackingUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="flex-1 flex items-center justify-center gap-2 px-4 py-3 bg-white border border-gray-300 text-gray-700 rounded-xl font-medium hover:bg-gray-50 transition-colors"
                        >
                            {t('storefront.shipping.trackOnCarrier', 'Track on Carrier')}
                            <ExternalLink size={16} />
                        </a>
                    )}
                </div>
            </div>
        </div>
    )
}
