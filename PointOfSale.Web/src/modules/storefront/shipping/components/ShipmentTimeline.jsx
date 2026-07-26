/**
 * Shipment Timeline Component (Storefront)
 * Phase: Storefront Shipping Experience
 * 
 * CORE FEATURE: Visual shipment lifecycle timeline.
 * 
 * STRICT RULES:
 * ❌ NO inferred states
 * ❌ NO reordering
 * ❌ NO skipping steps
 * ❌ NO "estimated" logic
 * ✅ Highlight current status
 * ✅ Terminal states visually locked
 * 
 * Uses backend timestamps ONLY.
 */

import { useTranslation } from 'react-i18next'
import { getStatusDisplay } from '../hooks/useShippingApi'
import {
    Clock, Package, Truck, MapPin, CheckCircle,
    XCircle, RotateCcw, AlertCircle
} from 'lucide-react'

// Canonical lifecycle steps in order (from backend)
const LIFECYCLE_STEPS = [
    { status: 'ReadyForPickup', icon: Package, label: 'Ready for Pickup' },
    { status: 'PickedUp', icon: Truck, label: 'Picked Up' },
    { status: 'InTransit', icon: Truck, label: 'In Transit' },
    { status: 'OutForDelivery', icon: MapPin, label: 'Out for Delivery' },
    { status: 'Delivered', icon: CheckCircle, label: 'Delivered' }
]

// Terminal states
const TERMINAL_STATES = {
    Failed: { icon: XCircle, label: 'Delivery Failed' },
    Cancelled: { icon: AlertCircle, label: 'Cancelled' },
    Returned: { icon: RotateCcw, label: 'Returned to Sender' }
}

export default function ShipmentTimeline({ shipment }) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    if (!shipment) return null

    const currentStatus = shipment.status
    const statusDisplay = getStatusDisplay(currentStatus)
    const isTerminalState = TERMINAL_STATES[currentStatus]

    // Find current step index
    const currentStepIndex = LIFECYCLE_STEPS.findIndex(s => s.status === currentStatus)

    // Extract timestamps from shipment (use exact backend field names)
    const timestamps = {
        ReadyForPickup: shipment.readyForPickupAt || shipment.readyForPickupAtUtc,
        PickedUp: shipment.pickedUpAt || shipment.pickedUpAtUtc,
        InTransit: shipment.inTransitAt || shipment.inTransitAtUtc,
        OutForDelivery: shipment.outForDeliveryAt || shipment.outForDeliveryAtUtc,
        Delivered: shipment.deliveredAt || shipment.deliveredAtUtc,
        Failed: shipment.failedAt || shipment.failedAtUtc,
        Cancelled: shipment.cancelledAt || shipment.cancelledAtUtc,
        Returned: shipment.returnedAt || shipment.returnedAtUtc
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
        <div className="bg-white rounded-2xl p-6 shadow-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-6 flex items-center gap-2">
                <Clock size={20} className="text-blue-500" />
                {t('storefront.shipping.timeline', 'Tracking Timeline')}
            </h3>

            {/* Timeline */}
            <div className={`relative ${isRTL ? 'pr-8' : 'pl-8'}`}>
                {/* Vertical line */}
                <div
                    className={`absolute top-0 bottom-0 w-0.5 bg-gray-200 ${isRTL ? 'right-3' : 'left-3'}`}
                />

                {LIFECYCLE_STEPS.map((step, index) => {
                    const timestamp = timestamps[step.status]
                    const isPast = currentStepIndex >= 0 && index < currentStepIndex
                    const isCurrent = index === currentStepIndex && !isTerminalState
                    const isFuture = currentStepIndex < 0 || index > currentStepIndex || isTerminalState

                    // Skip future steps if terminal state
                    if (isTerminalState && !isPast && !isCurrent) {
                        return null
                    }

                    const StepIcon = step.icon
                    const stepDisplay = getStatusDisplay(step.status)

                    return (
                        <div
                            key={step.status}
                            className="relative flex items-start gap-4 pb-6 last:pb-0"
                        >
                            {/* Step dot/icon */}
                            <div
                                className={`absolute ${isRTL ? 'right-0' : 'left-0'} w-6 h-6 rounded-full flex items-center justify-center transition-all
                                    ${isPast || isCurrent
                                        ? `${stepDisplay.bgColor} ${stepDisplay.textColor}`
                                        : 'bg-gray-100 text-gray-400'
                                    }
                                    ${isCurrent ? 'ring-4 ring-blue-100 scale-110' : ''}
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
                                        {step.label}
                                        {isCurrent && (
                                            <span className="ml-2 px-2 py-0.5 text-xs bg-blue-500 text-white rounded-full animate-pulse">
                                                {t('storefront.shipping.current', 'Current')}
                                            </span>
                                        )}
                                    </span>
                                    {timestamp && (
                                        <span className="text-sm text-gray-500">
                                            {formatDate(timestamp)}
                                        </span>
                                    )}
                                </div>
                                {!timestamp && isFuture && !isTerminalState && (
                                    <span className="text-sm text-gray-400 italic">
                                        {t('storefront.shipping.pending', 'Pending')}
                                    </span>
                                )}
                            </div>
                        </div>
                    )
                })}

                {/* Terminal State (if applicable) */}
                {isTerminalState && (
                    <div className={`relative flex items-start gap-4 pt-4 border-t border-gray-200 mt-4`}>
                        <div
                            className={`absolute ${isRTL ? 'right-0' : 'left-0'} w-6 h-6 rounded-full flex items-center justify-center
                                ${statusDisplay.bgColor} ${statusDisplay.textColor} ring-4 ring-red-100
                            `}
                        >
                            {(() => {
                                const TerminalIcon = TERMINAL_STATES[currentStatus].icon
                                return <TerminalIcon size={14} />
                            })()}
                        </div>
                        <div className="flex-1 min-w-0">
                            <div className="flex items-center justify-between">
                                <span className={`font-semibold ${statusDisplay.textColor}`}>
                                    {TERMINAL_STATES[currentStatus].label}
                                    <span className="ml-2 px-2 py-0.5 text-xs bg-gray-800 text-white rounded-full">
                                        {t('storefront.shipping.final', 'Final')}
                                    </span>
                                </span>
                                {timestamps[currentStatus] && (
                                    <span className="text-sm text-gray-500">
                                        {formatDate(timestamps[currentStatus])}
                                    </span>
                                )}
                            </div>
                        </div>
                    </div>
                )}
            </div>
        </div>
    )
}
