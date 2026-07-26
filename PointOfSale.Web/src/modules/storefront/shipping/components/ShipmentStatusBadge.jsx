/**
 * Shipment Status Badge Component
 * Phase: Storefront Shipping Experience
 * 
 * Displays shipment status with appropriate color.
 * NO status logic - just display.
 */

import { getStatusDisplay } from '../hooks/useShippingApi'

export default function ShipmentStatusBadge({ status, size = 'md' }) {
    const display = getStatusDisplay(status)

    const sizeClasses = {
        sm: 'px-2 py-0.5 text-xs',
        md: 'px-3 py-1 text-sm',
        lg: 'px-4 py-1.5 text-base'
    }

    return (
        <span className={`inline-flex items-center gap-1.5 rounded-full font-medium ${display.bgColor} ${display.textColor} ${sizeClasses[size]}`}>
            <span>{display.icon}</span>
            <span>{status}</span>
            {display.isTerminal && (
                <span className="text-xs opacity-75">(Final)</span>
            )}
        </span>
    )
}
