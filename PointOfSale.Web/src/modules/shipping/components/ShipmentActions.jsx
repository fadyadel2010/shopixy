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

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { XCircle, RefreshCw, AlertTriangle } from 'lucide-react'
import { useShipmentActions } from '../hooks/useShipmentsApi'
import { hasShipmentActionAuthority } from '../shipmentActionAuthority'
import CancellationOutcome from './CancellationOutcome'

export default function ShipmentActions({ shipment, actionAuthority, actionAuthorityLoading, actionAuthorityError, refetchActionAuthority, onActionComplete }) {
    const { t } = useTranslation()
    const { cancelShipment, loading, error, clearError } = useShipmentActions()

    const [showCancelModal, setShowCancelModal] = useState(false)
    const [cancelReason, setCancelReason] = useState('')
    const [cancellation, setCancellation] = useState(null)
    const [validating, setValidating] = useState(false)
    const activeTargetRef = useRef(null)
    const pendingConfirmationRef = useRef(null)
    useEffect(() => {
        const target = { shipmentId: shipment?.id || shipment?.shipmentId, orderId: shipment?.orderId }
        activeTargetRef.current = target
        pendingConfirmationRef.current = null
        setValidating(false)
        setCancellation(null)
        setShowCancelModal(false)
        setCancelReason('')
        clearError()
        return () => {
            activeTargetRef.current = null
            pendingConfirmationRef.current = null
        }
    }, [shipment?.shipmentId, shipment?.id, shipment?.orderId, clearError])

    if (!shipment) return null

    const canCancel = !shipment.cancellation && !cancellation && hasShipmentActionAuthority(actionAuthority, shipment, 'cancel')

    const handleCancel = async () => {
        if (!canCancel || actionAuthorityLoading || actionAuthorityError || loading || pendingConfirmationRef.current || !cancelReason.trim()) return

        const shipmentId = shipment.id || shipment.shipmentId
        if (!shipmentId) return
        const target = activeTargetRef.current
        if (!target || target.shipmentId !== shipmentId || target.orderId !== shipment.orderId) return
        const confirmation = { target }
        // Synchronous single-flight guard includes the authority-refresh interval,
        // before the mutation hook's loading flag becomes true.
        pendingConfirmationRef.current = confirmation
        setValidating(true)
        try {
            // Do not execute from the authority snapshot that rendered this button.
            let currentAuthority
            try {
                currentAuthority = await refetchActionAuthority?.()
            } catch {
                // Authority errors remain owned/displayed by the refresh hook.
                // Do not retry or treat a failed refresh as permission to mutate.
                return
            }
            // Switching targets or unmounting withdraws this UI confirmation.
            // Never issue a late cancellation of the previous selection.
            if (activeTargetRef.current !== target || pendingConfirmationRef.current !== confirmation ||
                !hasShipmentActionAuthority(currentAuthority, shipment, 'cancel')) return

            const result = await cancelShipment(shipmentId, cancelReason)
            if (activeTargetRef.current !== target || pendingConfirmationRef.current !== confirmation) return
            if (result.data) {
                setCancellation(result.data)
                if (onActionComplete) onActionComplete()
            }

            if (result.success) {
                setShowCancelModal(false)
                setCancelReason('')
            }
        } finally {
            if (pendingConfirmationRef.current === confirmation) {
                pendingConfirmationRef.current = null
                setValidating(false)
            }
        }
    }

    return (
        <>
            <div className="bg-white border border-gray-200 rounded-xl p-6">
                {!showCancelModal && <CancellationOutcome result={shipment.cancellation || cancellation} />}
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    {t('shipping.closure.actions')}
                </h3>

                {/* Terminal State Warning */}
                {!actionAuthorityLoading && !canCancel && (
                    <div className="flex items-start gap-3 p-4 bg-gray-50 rounded-lg mb-4">
                        <AlertTriangle size={18} className="text-gray-400 mt-0.5" />
                        <div>
                            <p className="text-sm font-medium text-gray-700">
                                {t('shipping.closure.noActionAuthority')}
                            </p>
                            <p className="text-sm text-gray-500">
                                {t('shipping.closure.actionAuthorityBoundary')}
                            </p>
                        </div>
                    </div>
                )}

                {actionAuthorityLoading && (
                    <div className="flex items-start gap-3 p-4 bg-blue-50 rounded-lg mb-4 text-sm text-blue-800">
                        <RefreshCw size={18} className="animate-spin mt-0.5" />
                        {t('shipping.closure.loadingActionAuthority')}
                    </div>
                )}

                {actionAuthorityError && (
                    <div className="flex items-start gap-3 p-4 bg-amber-50 rounded-lg mb-4 text-sm text-amber-900">
                        <AlertTriangle size={18} className="mt-0.5" />
                        {t('shipping.closure.actionsWithheld')}
                    </div>
                )}

                {/* Error Display */}
                {error && (
                    <div className="p-4 bg-red-50 border border-red-200 rounded-lg mb-4">
                        <div className="flex items-start gap-2">
                            <XCircle size={18} className="text-red-500 mt-0.5" />
                            <div>
                                <p className="text-sm font-medium text-red-700">{t('shipping.closure.actionFailed')}</p>
                                <p className="text-sm text-red-600">{error}</p>
                            </div>
                        </div>
                        <button
                            onClick={clearError}
                            className="mt-2 text-sm text-red-600 hover:underline"
                        >
                            {t('common.close')}
                        </button>
                    </div>
                )}

                {/* Action Buttons */}
                <div className="space-y-3">
                    {/* Cancel Button */}
                    <button
                        onClick={() => setShowCancelModal(true)}
                        disabled={!canCancel || actionAuthorityLoading || actionAuthorityError || loading || validating}
                        className={`w-full flex items-center justify-center gap-2 px-4 py-3 rounded-lg font-medium transition-colors
                            ${canCancel
                                ? 'bg-red-50 text-red-700 hover:bg-red-100 border border-red-200'
                                : 'bg-gray-100 text-gray-400 cursor-not-allowed border border-gray-200'
                            }
                        `}
                    >
                        {loading || validating ? (
                            <RefreshCw size={18} className="animate-spin" />
                        ) : (
                            <XCircle size={18} />
                        )}
                        {t('shipping.cancelShipment')}
                    </button>
                </div>

                {/* Info Text */}
                    {canCancel && (
                    <p className="text-xs text-gray-500 mt-4">
                        {t('shipping.cancellationScope')}
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
                                        {t('shipping.cancelShipment')}
                                    </h3>
                                    <p className="text-sm text-gray-500">
                                        {t('shipping.cancellationScope')}
                                    </p>
                                </div>
                            </div>

                            <div className="mb-4">
                                <label className="block text-sm font-medium text-gray-700 mb-2">
                                    {t('shipping.cancellationReason')} *
                                </label>
                                <textarea
                                    disabled={loading || validating}
                                    value={cancelReason}
                                    onChange={(e) => setCancelReason(e.target.value)}
                                    placeholder={t('shipping.cancellationReason')}
                                    rows={3}
                                    className="w-full px-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-red-500 focus:border-transparent"
                                />
                            </div>

                            <CancellationOutcome result={shipment.cancellation || cancellation} />
                            {error && (
                                <div className="p-3 bg-red-50 text-red-600 text-sm rounded-lg mb-4">
                                    {error}
                                </div>
                            )}

                            <div className="flex gap-3">
                                <button
                                    disabled={loading || validating}
                                    onClick={() => {
                                        setShowCancelModal(false)
                                        setCancelReason('')
                                        clearError()
                                    }}
                                    className="flex-1 px-4 py-3 border border-gray-300 rounded-lg font-medium text-gray-700 hover:bg-gray-50"
                                >
                                    {t('shipping.keepShipment')}
                                </button>
                                <button
                                    onClick={handleCancel}
                                    disabled={!canCancel || actionAuthorityLoading || actionAuthorityError || !cancelReason.trim() || loading || validating}
                                    className="flex-1 px-4 py-3 bg-red-600 text-white rounded-lg font-medium hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
                                >
                                    {(loading || validating) && <RefreshCw size={16} className="animate-spin" />}
                                    {t('shipping.cancelShipment')}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </>
    )
}
