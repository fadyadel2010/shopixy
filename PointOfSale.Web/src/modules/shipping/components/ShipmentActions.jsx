/**
 * Shipment Actions Component
 * Phase: Shipping Frontend
 * 
 * Admin action buttons for shipment management.
 * 
 * RULES:
 * - Buttons disabled for terminal states
 * - Confirmation modal required
 * - Backend errors shown as-is
 * - No retry logic
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { XCircle, RefreshCw, AlertTriangle } from 'lucide-react'
import { useShipmentActions } from '../hooks/useShipmentsApi'
import { hasShipmentActionAuthority } from '../shipmentActionAuthority'

export default function ShipmentActions({ shipment, actionAuthority, actionAuthorityLoading, actionAuthorityError, refetchActionAuthority, onActionComplete }) {
    const { t } = useTranslation()
    const { cancelShipment, loading, error, clearError } = useShipmentActions()

    const [showCancelModal, setShowCancelModal] = useState(false)
    const [cancelReason, setCancelReason] = useState('')

    if (!shipment) return null

    const canCancel = hasShipmentActionAuthority(actionAuthority, shipment, 'cancel')

    const handleCancel = async () => {
        if (!cancelReason.trim()) return

        const shipmentId = shipment.id || shipment.shipmentId
        if (!shipmentId) return

        let currentAuthority
        try {
            // Do not execute from the authority snapshot that rendered this button.
            currentAuthority = await refetchActionAuthority?.()
        } catch {
            return
        }

        if (!hasShipmentActionAuthority(currentAuthority, shipment, 'cancel')) return

        const result = await cancelShipment(shipmentId, cancelReason)

        if (result.success) {
            setShowCancelModal(false)
            setCancelReason('')
            if (onActionComplete) onActionComplete()
        }
    }

    return (
        <>
            <div className="bg-white border border-gray-200 rounded-xl p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    {t('shipping.actions', 'Actions')}
                </h3>

                {/* Terminal State Warning */}
                {!actionAuthorityLoading && !canCancel && (
                    <div className="flex items-start gap-3 p-4 bg-gray-50 rounded-lg mb-4">
                        <AlertTriangle size={18} className="text-gray-400 mt-0.5" />
                        <div>
                            <p className="text-sm font-medium text-gray-700">
                                No backend action authority
                            </p>
                            <p className="text-sm text-gray-500">
                                The current backend fulfillment authority did not grant a shipment cancellation action.
                            </p>
                        </div>
                    </div>
                )}

                {actionAuthorityLoading && (
                    <div className="flex items-start gap-3 p-4 bg-blue-50 rounded-lg mb-4 text-sm text-blue-800">
                        <RefreshCw size={18} className="animate-spin mt-0.5" />
                        Loading current backend action authority...
                    </div>
                )}

                {actionAuthorityError && (
                    <div className="flex items-start gap-3 p-4 bg-amber-50 rounded-lg mb-4 text-sm text-amber-900">
                        <AlertTriangle size={18} className="mt-0.5" />
                        Shipment actions are withheld until backend action authority is available.
                    </div>
                )}

                {/* Error Display */}
                {error && (
                    <div className="p-4 bg-red-50 border border-red-200 rounded-lg mb-4">
                        <div className="flex items-start gap-2">
                            <XCircle size={18} className="text-red-500 mt-0.5" />
                            <div>
                                <p className="text-sm font-medium text-red-700">Action Failed</p>
                                <p className="text-sm text-red-600">{error}</p>
                            </div>
                        </div>
                        <button
                            onClick={clearError}
                            className="mt-2 text-sm text-red-600 hover:underline"
                        >
                            Dismiss
                        </button>
                    </div>
                )}

                {/* Action Buttons */}
                <div className="space-y-3">
                    {/* Cancel Button */}
                    <button
                        onClick={() => setShowCancelModal(true)}
                        disabled={!canCancel || actionAuthorityLoading || loading}
                        className={`w-full flex items-center justify-center gap-2 px-4 py-3 rounded-lg font-medium transition-colors
                            ${canCancel
                                ? 'bg-red-50 text-red-700 hover:bg-red-100 border border-red-200'
                                : 'bg-gray-100 text-gray-400 cursor-not-allowed border border-gray-200'
                            }
                        `}
                    >
                        {loading ? (
                            <RefreshCw size={18} className="animate-spin" />
                        ) : (
                            <XCircle size={18} />
                        )}
                        Cancel Shipment
                    </button>
                </div>

                {/* Info Text */}
                    {canCancel && (
                    <p className="text-xs text-gray-500 mt-4">
                        Cancelling a shipment will notify the carrier and update the order status.
                    </p>
                )}
            </div>

            {/* Cancel Confirmation Modal */}
            {showCancelModal && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50">
                    <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md">
                        <div className="p-6">
                            <div className="flex items-center gap-3 mb-4">
                                <div className="w-12 h-12 bg-red-100 rounded-full flex items-center justify-center">
                                    <AlertTriangle className="text-red-600" size={24} />
                                </div>
                                <div>
                                    <h3 className="text-lg font-semibold text-gray-900">
                                        Cancel Shipment
                                    </h3>
                                    <p className="text-sm text-gray-500">
                                        This action cannot be undone
                                    </p>
                                </div>
                            </div>

                            <div className="mb-4">
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    Cancellation Reason *
                                </label>
                                <textarea
                                    value={cancelReason}
                                    onChange={(e) => setCancelReason(e.target.value)}
                                    placeholder="Enter reason for cancellation..."
                                    rows={3}
                                    className="w-full px-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-red-500 focus:border-transparent"
                                />
                            </div>

                            {error && (
                                <div className="p-3 bg-red-50 text-red-600 text-sm rounded-lg mb-4">
                                    {error}
                                </div>
                            )}

                            <div className="flex gap-3">
                                <button
                                    onClick={() => {
                                        setShowCancelModal(false)
                                        setCancelReason('')
                                        clearError()
                                    }}
                                    className="flex-1 px-4 py-3 border border-gray-300 rounded-lg font-medium text-gray-700 hover:bg-gray-50"
                                >
                                    Keep Shipment
                                </button>
                                <button
                                    onClick={handleCancel}
                                    disabled={!cancelReason.trim() || loading}
                                    className="flex-1 px-4 py-3 bg-red-600 text-white rounded-lg font-medium hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
                                >
                                    {loading && <RefreshCw size={16} className="animate-spin" />}
                                    Cancel Shipment
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </>
    )
}
