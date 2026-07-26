/**
 * Tracking Summary Card Component
 * Phase: Storefront Shipping Experience
 * 
 * Displays shipment summary for tracking page.
 */

import { useTranslation } from 'react-i18next'
import { Package, ExternalLink, Clock, MapPin } from 'lucide-react'
import CarrierBadge from './CarrierBadge'
import ShipmentStatusBadge from './ShipmentStatusBadge'

export default function TrackingSummaryCard({ shipment }) {
    const { t, i18n } = useTranslation()

    if (!shipment) return null

    const formatDate = (dateStr) => {
        if (!dateStr) return '-'
        return new Date(dateStr).toLocaleDateString(i18n.language, {
            month: 'short',
            day: 'numeric',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    return (
        <div className="bg-white rounded-2xl shadow-sm overflow-hidden">
            {/* Header */}
            <div className="bg-gradient-to-r from-blue-500 to-blue-600 p-6 text-white">
                <div className="flex items-center gap-3 mb-4">
                    <div className="w-12 h-12 bg-white/20 rounded-xl flex items-center justify-center">
                        <Package size={24} />
                    </div>
                    <div>
                        <p className="text-blue-100 text-sm">
                            {t('storefront.shipping.trackingNumber', 'Tracking Number')}
                        </p>
                        <p className="text-xl font-bold font-mono">
                            {shipment.trackingNumber || shipment.shipmentNumber}
                        </p>
                    </div>
                </div>

                <ShipmentStatusBadge status={shipment.status} size="lg" />
            </div>

            {/* Details */}
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

                {/* Last Update */}
                <div className="flex items-center justify-between">
                    <span className="text-gray-500 flex items-center gap-2">
                        <Clock size={16} />
                        {t('storefront.shipping.lastUpdate', 'Last Update')}
                    </span>
                    <span className="text-gray-900 font-medium">
                        {formatDate(shipment.updatedAt || shipment.lastCarrierSyncAtUtc)}
                    </span>
                </div>

                {/* Destination */}
                {(shipment.destinationCity || shipment.shippingCity) && (
                    <div className="flex items-center justify-between">
                        <span className="text-gray-500 flex items-center gap-2">
                            <MapPin size={16} />
                            {t('storefront.shipping.destination', 'Destination')}
                        </span>
                        <span className="text-gray-900">
                            {shipment.destinationCity || shipment.shippingCity}
                        </span>
                    </div>
                )}

                {/* External Tracking Link */}
                {shipment.trackingUrl && (
                    <div className="pt-4 border-t border-gray-100">
                        <a
                            href={shipment.trackingUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="w-full flex items-center justify-center gap-2 px-4 py-3 bg-gray-100 hover:bg-gray-200 text-gray-700 rounded-xl font-medium transition-colors"
                        >
                            {t('storefront.shipping.trackOnCarrier', 'Track on Carrier Website')}
                            <ExternalLink size={16} />
                        </a>
                    </div>
                )}
            </div>
        </div>
    )
}
