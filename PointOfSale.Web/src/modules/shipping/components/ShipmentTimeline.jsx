/**
 * Shipment Timeline Component
 * Phase: Shipping Frontend
 * 
 * CORE FEATURE: Visual timeline of shipment lifecycle.
 * 
 * RULES:
 * - Timeline order = backend order (NO frontend reordering)
 * - Status colors from backend status
 * - Missing steps shown as "Not reached"
 * - Terminal states clearly marked
 * 
 * NO LIFECYCLE LOGIC - Pure renderer
 */

import { useTranslation } from 'react-i18next'
import { getStatusConfig } from '../hooks/useShipmentsApi'
import {
    Clock, Package, Truck, MapPin, CheckCircle, XCircle,
    RotateCcw, AlertCircle
} from 'lucide-react'

// Canonical lifecycle steps in order
const LIFECYCLE_STEPS = [
    { status: 'Pending', icon: Clock, label: 'Pending' },
    { status: 'ReadyForPickup', icon: Package, label: 'Ready for Pickup' },
    { status: 'PickedUp', icon: Truck, label: 'Picked Up' },
    { status: 'InTransit', icon: Truck, label: 'In Transit' },
    { status: 'OutForDelivery', icon: MapPin, label: 'Out for Delivery' },
    { status: 'Delivered', icon: CheckCircle, label: 'Delivered' }
]

// Terminal states (shown separately)
const TERMINAL_STATES = {
    Failed: { icon: XCircle, label: 'Failed', color: 'red' },
    Cancelled: { icon: AlertCircle, label: 'Cancelled', color: 'rose' },
    Returned: { icon: RotateCcw, label: 'Returned', color: 'orange' }
}

export default function ShipmentTimeline({ shipment }) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    if (!shipment) return null

    const currentStatus = shipment.status
    const config = getStatusConfig(currentStatus)

    // Find current step index
    const currentStepIndex = LIFECYCLE_STEPS.findIndex(s => s.status === currentStatus)
    const isTerminalState = TERMINAL_STATES[currentStatus]

    // Extract timestamps from shipment
    const timestamps = {
        Pending: shipment.createdAtUtc,
        ReadyForPickup: shipment.readyForPickupAtUtc,
        PickedUp: shipment.pickedUpAtUtc,
        InTransit: shipment.inTransitAtUtc,
        OutForDelivery: shipment.outForDeliveryAtUtc,
        Delivered: shipment.deliveredAtUtc,
        Failed: shipment.failedAtUtc,
        Cancelled: shipment.cancelledAtUtc,
        Returned: shipment.returnedAtUtc
    }

    const formatDate = (dateStr) => {
        if (!dateStr) return null
        const date = new Date(dateStr)
        return date.toLocaleDateString(i18n.language, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        })
    }

    return (
        <div className="bg-white border border-gray-200 rounded-xl p-6">
            <h3 className="text-lg font-semibold text-gray-900 mb-6 flex items-center gap-2">
                <Clock size={20} className="text-gray-400" />
                {t('shipping.closure.timeline')}
            </h3>

            {/* Main Timeline */}
            <div className={`relative ${isRTL ? 'pr-8' : 'pl-8'}`}>
                {/* Vertical line */}
                <div
                    className={`absolute top-0 bottom-0 w-0.5 bg-gray-200 ${isRTL ? 'right-3' : 'left-3'}`}
                />

                {LIFECYCLE_STEPS.map((step, index) => {
                    const timestamp = timestamps[step.status]
                    const isPast = Boolean(timestamp)
                    const isCurrent = index === currentStepIndex && !isTerminalState

                    // If we're in a terminal state, only show steps up to where we were
                    if (isTerminalState && currentStatus !== 'Delivered') {
                        // For failed/cancelled/returned, show last known position
                        const lastGoodIndex = Math.max(
                            timestamps.Pending ? 0 : -1,
                            timestamps.ReadyForPickup ? 1 : -1,
                            timestamps.PickedUp ? 2 : -1,
                            timestamps.InTransit ? 3 : -1,
                            timestamps.OutForDelivery ? 4 : -1,
                            timestamps.Delivered ? 5 : -1
                        )
                        if (index > lastGoodIndex + 1) {
                            // Skip steps that were never reached
                            return null
                        }
                    }

                    const StepIcon = step.icon
                    const stepConfig = getStatusConfig(step.status)

                    return (
                        <div
                            key={step.status}
                            className={`relative flex items-start gap-4 pb-6 last:pb-0`}
                        >
                            {/* Step dot/icon */}
                            <div
                                className={`absolute ${isRTL ? 'right-0' : 'left-0'} w-6 h-6 rounded-full flex items-center justify-center
                                    ${isPast || isCurrent
                                        ? `${stepConfig.bgColor} ${stepConfig.textColor}`
                                        : 'bg-gray-100 text-gray-400'
                                    }
                                    ${isCurrent ? 'ring-4 ring-blue-100' : ''}
                                `}
                            >
                                <StepIcon size={14} />
                            </div>

                            {/* Step content */}
                            <div className="flex-1 min-w-0">
                                <div className="flex items-center justify-between">
                                    <span
                                        className={`font-medium ${isPast || isCurrent ? 'text-gray-900' : 'text-gray-400'
                                            }`}
                                    >
                                        {t(`shipping.closure.statuses.${step.status}`)}
                                        {isCurrent && (
                                            <span className="ms-2 px-2 py-0.5 text-xs bg-blue-100 text-blue-700 rounded-full">
                                                {t('shipping.closure.current')}
                                            </span>
                                        )}
                                    </span>
                                    {timestamp && (
                                        <span className="text-sm text-gray-500">
                                            {formatDate(timestamp)}
                                        </span>
                                    )}
                                </div>
                                {!timestamp && !isCurrent && (
                                    <span className="text-sm text-gray-400 italic">
                                        {t('shipping.closure.notObserved')}
                                    </span>
                                )}
                            </div>
                        </div>
                    )
                })}

                {/* Terminal State (if applicable) */}
                {isTerminalState && (
                    <div className="relative flex items-start gap-4 pt-4 border-t border-gray-100 mt-4">
                        <div
                            className={`absolute ${isRTL ? 'right-0' : 'left-0'} w-6 h-6 rounded-full flex items-center justify-center
                                ${config.bgColor} ${config.textColor} ring-4 ring-red-50
                            `}
                        >
                            {(() => {
                                const TerminalIcon = TERMINAL_STATES[currentStatus].icon
                                return <TerminalIcon size={14} />
                            })()}
                        </div>
                        <div className="flex-1 min-w-0">
                            <div className="flex items-center justify-between">
                                <span className={`font-medium ${config.textColor}`}>
                                    {t(`shipping.closure.statuses.${currentStatus}`)}
                                    <span className="ms-2 px-2 py-0.5 text-xs bg-red-100 text-red-700 rounded-full">
                                        {t('shipping.closure.final')}
                                    </span>
                                </span>
                                {timestamps[currentStatus] && (
                                    <span className="text-sm text-gray-500">
                                        {formatDate(timestamps[currentStatus])}
                                    </span>
                                )}
                            </div>
                            {shipment.failureReason && currentStatus === 'Failed' && (
                                <p className="text-sm text-red-600 mt-1">
                                     {t('shipping.closure.reason')}: {shipment.failureReason}
                                </p>
                            )}
                            {shipment.cancellationReason && currentStatus === 'Cancelled' && (
                                <p className="text-sm text-rose-600 mt-1">
                                     {t('shipping.closure.reason')}: {shipment.cancellationReason}
                                </p>
                            )}
                            {shipment.returnReason && currentStatus === 'Returned' && (
                                <p className="text-sm text-orange-600 mt-1">
                                     {t('shipping.closure.reason')}: {shipment.returnReason}
                                </p>
                            )}
                        </div>
                    </div>
                )}
            </div>
        </div>
    )
}
