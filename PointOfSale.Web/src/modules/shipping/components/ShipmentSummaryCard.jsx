/**
 * Shipment Summary Card Component
 * Phase: Shipping Frontend
 * 
 * Displays shipment summary information.
 */

import { useTranslation } from 'react-i18next'
import {
    Package, Hash, ShoppingCart, Truck, MapPin,
    ExternalLink, Tag, Clock
} from 'lucide-react'
import { Link } from 'react-router-dom'
import CarrierBadge from './CarrierBadge'
import CodStatusBadge from './CodStatusBadge'
import { getStatusConfig } from '../hooks/useShipmentsApi'

export default function ShipmentSummaryCard({ shipment }) {
    const { t, i18n } = useTranslation()

    if (!shipment) return null

    const statusConfig = getStatusConfig(shipment.status)

    const formatDate = (dateStr) => {
        if (!dateStr) return '-'
        return new Date(dateStr).toLocaleDateString(i18n.language, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    return (
        <div className="bg-white border border-gray-200 rounded-xl overflow-hidden min-w-0 break-words">
            {/* Header with status */}
            <div className={`px-6 py-4 ${statusConfig.bgColor} border-b ${statusConfig.borderColor}`}>
                <div className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                        <span className="text-2xl">{statusConfig.icon}</span>
                        <div>
                            <h2 className={`text-lg font-bold ${statusConfig.textColor}`}>
                                {t(`shipping.closure.statuses.${shipment.status}`, { defaultValue: shipment.status })}
                            </h2>
                            <p className="text-sm text-gray-600">
                                {t('shipping.closure.shipment')} #{shipment.shipmentNumber}
                            </p>
                        </div>
                    </div>
                    {statusConfig.isTerminal && (
                        <span className="px-3 py-1 bg-gray-800 text-white text-xs font-medium rounded-full">
                            {t('shipping.closure.final')}
                        </span>
                    )}
                </div>
            </div>

            {/* Content */}
            <div className="p-6">
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                    {/* Left Column */}
                    <div className="space-y-4">
                        {/* Order Link */}
                        <div className="flex items-start gap-3">
                            <ShoppingCart size={18} className="text-gray-400 mt-0.5" />
                            <div>
                                <p className="text-sm text-gray-500">{t('shipping.closure.order')}</p>
                                <Link
                                    to={`/ecommerce/orders?id=${shipment.orderId}`}
                                    className="text-blue-600 hover:underline font-medium flex items-center gap-1"
                                >
                                    {t('shipping.closure.viewOrder')}
                                    <ExternalLink size={14} />
                                </Link>
                            </div>
                        </div>

                        {/* Carrier */}
                        <div className="flex items-start gap-3">
                            <Truck size={18} className="text-gray-400 mt-0.5" />
                            <div>
                                <p className="text-sm text-gray-500">{t('shipping.closure.carrier')}</p>
                                <CarrierBadge carrierCode={shipment.carrierCode} />
                            </div>
                        </div>

                        {/* Service Level */}
                        {shipment.serviceLevel && (
                            <div className="flex items-start gap-3">
                                <Tag size={18} className="text-gray-400 mt-0.5" />
                                <div>
                                    <p className="text-sm text-gray-500">{t('shipping.closure.service')}</p>
                                    <p className="font-medium text-gray-900">{shipment.serviceLevel}</p>
                                </div>
                            </div>
                        )}

                        {/* Tracking Number */}
                        {shipment.trackingNumber && (
                            <div className="flex items-start gap-3">
                                <Hash size={18} className="text-gray-400 mt-0.5" />
                                <div>
                                    <p className="text-sm text-gray-500">{t('shipping.closure.tracking')}</p>
                                    <div className="flex items-center gap-2">
                                        <code className="px-2 py-1 bg-gray-100 rounded text-sm font-mono break-all">
                                            {shipment.trackingNumber}
                                        </code>
                                        {shipment.trackingUrl && (
                                            <a
                                                href={shipment.trackingUrl}
                                                target="_blank"
                                                rel="noopener noreferrer"
                                                className="text-blue-600 hover:underline text-sm flex items-center gap-1"
                                            >
                                                {t('shipping.closure.track')}
                                                <ExternalLink size={12} />
                                            </a>
                                        )}
                                    </div>
                                </div>
                            </div>
                        )}
                    </div>

                    {/* Right Column */}
                    <div className="space-y-4">
                        {/* COD Status */}
                        <div className="flex items-start gap-3">
                            <Package size={18} className="text-gray-400 mt-0.5" />
                            <div>
                                <p className="text-sm text-gray-500">{t('shipping.closure.payment')}</p>
                                <CodStatusBadge
                                    isCod={shipment.isCashOnDelivery}
                                    codAmount={shipment.codAmount}
                                    codCollected={shipment.codCollected}
                                    currency={shipment.currency}
                                />
                            </div>
                        </div>

                        {/* Recipient */}
                        <div className="flex items-start gap-3">
                            <MapPin size={18} className="text-gray-400 mt-0.5" />
                            <div>
                                <p className="text-sm text-gray-500">{t('shipping.closure.recipient')}</p>
                                <p className="font-medium text-gray-900">{shipment.recipient?.name}</p>
                                {shipment.recipient?.phone && (
                                    <p className="text-sm text-gray-600">{shipment.recipient.phone}</p>
                                )}
                                <p className="text-sm text-gray-600 mt-1">
                                    {[shipment.recipient?.address, shipment.recipient?.city, shipment.recipient?.country].filter(Boolean).join(', ')}
                                </p>
                            </div>
                        </div>

                        {/* Created Date */}
                        <div className="flex items-start gap-3">
                            <Clock size={18} className="text-gray-400 mt-0.5" />
                            <div>
                                <p className="text-sm text-gray-500">{t('shipping.closure.created')}</p>
                                <p className="font-medium text-gray-900">{formatDate(shipment.createdAtUtc)}</p>
                            </div>
                        </div>

                        {/* Last Sync */}
                        {shipment.lastCarrierSyncAtUtc && (
                            <div className="flex items-start gap-3">
                                <Clock size={18} className="text-gray-400 mt-0.5" />
                                <div>
                                    <p className="text-sm text-gray-500">{t('shipping.closure.lastSync')}</p>
                                    <p className="text-sm text-gray-600">{formatDate(shipment.lastCarrierSyncAtUtc)}</p>
                                </div>
                            </div>
                        )}
                    </div>
                </div>

                {/* Shipping Cost */}
                <div className="mt-6 pt-4 border-t border-gray-100">
                    <div className="flex items-center justify-between">
                        <span className="text-gray-500">{t('shipping.closure.customerCharge')}</span>
                        <span className="text-lg font-bold text-gray-900">
                            {shipment.shippingCost?.toLocaleString()} {shipment.currency}
                        </span>
                    </div>
                </div>
            </div>
        </div>
    )
}
