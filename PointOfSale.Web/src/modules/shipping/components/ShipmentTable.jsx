/**
 * Shipments Table Component
 * Phase: Shipping Frontend
 * 
 * Paginated table for shipments list.
 * Pure renderer - no business logic.
 */

import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Package } from 'lucide-react'
import CarrierBadge from './CarrierBadge'
import CodStatusBadge from './CodStatusBadge'
import { getStatusConfig } from '../hooks/useShipmentsApi'

export default function ShipmentTable({
    shipments,
    pagination,
    loading,
    onPageChange
}) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    const formatDate = (dateStr) => {
        if (!dateStr) return '-'
        return new Date(dateStr).toLocaleDateString(i18n.language, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    // Status Badge Component
    const StatusBadge = ({ status }) => {
        const config = getStatusConfig(status)
        return (
            <span className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-sm font-medium ${config.bgColor} ${config.textColor}`}>
                <span>{config.icon}</span>
                {status}
            </span>
        )
    }

    if (loading && !shipments.length) {
        return (
            <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
                <div className="p-8 text-center">
                    <div className="w-12 h-12 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4 animate-pulse">
                        <Package className="text-gray-400" size={24} />
                    </div>
                    <p className="text-gray-500">Loading shipments...</p>
                </div>
            </div>
        )
    }

    if (!shipments.length) {
        return (
            <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
                <div className="p-8 text-center">
                    <div className="w-12 h-12 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <Package className="text-gray-400" size={24} />
                    </div>
                    <h3 className="text-lg font-medium text-gray-900 mb-2">
                        {t('shipping.noShipments', 'No shipments found')}
                    </h3>
                    <p className="text-gray-500">
                        {t('shipping.noShipmentsDesc', 'Try adjusting your filters')}
                    </p>
                </div>
            </div>
        )
    }

    return (
        <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
            {/* Table */}
            <div className="overflow-x-auto">
                <table className="w-full">
                    <thead className="bg-gray-50 border-b border-gray-200">
                        <tr>
                            <th className="px-6 py-4 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                Shipment
                            </th>
                            <th className="px-6 py-4 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                Carrier
                            </th>
                            <th className="px-6 py-4 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                Status
                            </th>
                            <th className="px-6 py-4 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                Payment
                            </th>
                            <th className="px-6 py-4 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                Created
                            </th>
                            <th className="px-6 py-4 text-left text-xs font-semibold text-gray-600 uppercase tracking-wider">
                                Updated
                            </th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {shipments.map((shipment, index) => (
                            <tr
                                key={shipment.id || shipment.shipmentNumber || index}
                                onClick={() => navigate(`/admin/shipping/shipments/${shipment.id}`)}
                                className="hover:bg-gray-50 cursor-pointer transition-colors"
                            >
                                <td className="px-6 py-4">
                                    <div>
                                        <p className="font-medium text-gray-900">
                                            {shipment.shipmentNumber}
                                        </p>
                                        {shipment.trackingNumber && (
                                            <p className="text-sm text-gray-500 font-mono">
                                                {shipment.trackingNumber}
                                            </p>
                                        )}
                                    </div>
                                </td>
                                <td className="px-6 py-4">
                                    <CarrierBadge
                                        carrierCode={shipment.carrierCode}
                                        size="sm"
                                    />
                                </td>
                                <td className="px-6 py-4">
                                    <StatusBadge status={shipment.status} />
                                </td>
                                <td className="px-6 py-4">
                                    <CodStatusBadge
                                        isCod={shipment.isCashOnDelivery}
                                        codAmount={shipment.codAmount}
                                        codCollected={shipment.codCollected}
                                        currency={shipment.currency}
                                        size="sm"
                                        showAmount={false}
                                    />
                                </td>
                                <td className="px-6 py-4 text-sm text-gray-500">
                                    {formatDate(shipment.createdAt)}
                                </td>
                                <td className="px-6 py-4 text-sm text-gray-500">
                                    {formatDate(shipment.updatedAt)}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            {/* Pagination */}
            {pagination.totalPages > 1 && (
                <div className="px-6 py-4 border-t border-gray-200 flex items-center justify-between">
                    <p className="text-sm text-gray-500">
                        Showing {((pagination.page - 1) * pagination.pageSize) + 1} to{' '}
                        {Math.min(pagination.page * pagination.pageSize, pagination.totalCount)} of{' '}
                        {pagination.totalCount} shipments
                    </p>
                    <div className="flex items-center gap-2">
                        <button
                            onClick={() => onPageChange(pagination.page - 1)}
                            disabled={pagination.page <= 1}
                            className="p-2 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            <ChevronLeft size={18} className={isRTL ? 'rotate-180' : ''} />
                        </button>

                        <span className="px-4 py-2 text-sm font-medium text-gray-700">
                            Page {pagination.page} of {pagination.totalPages}
                        </span>

                        <button
                            onClick={() => onPageChange(pagination.page + 1)}
                            disabled={pagination.page >= pagination.totalPages}
                            className="p-2 border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                            <ChevronRight size={18} className={isRTL ? 'rotate-180' : ''} />
                        </button>
                    </div>
                </div>
            )}
        </div>
    )
}
