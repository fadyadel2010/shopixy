/**
 * Carrier Badge Component
 * Phase: Shipping Frontend
 * 
 * Displays carrier information with icon/badge.
 * No carrier-specific logic - just display.
 */

import { Truck } from 'lucide-react'

// Carrier display config (display only - no logic)
// PHASE 1: 'manual' is a FIRST-CLASS carrier, not a fallback
const CARRIER_DISPLAY = {
    manual: {
        name: 'Manual Fulfillment',
        color: 'slate',
        bgColor: 'bg-slate-100',
        textColor: 'text-slate-600'
    },
    bosta: {
        name: 'Bosta',
        color: 'emerald',
        bgColor: 'bg-emerald-100',
        textColor: 'text-emerald-700'
    },
    aramex: {
        name: 'Aramex',
        color: 'red',
        bgColor: 'bg-red-100',
        textColor: 'text-red-700'
    },
    mylerz: {
        name: 'Mylerz',
        color: 'blue',
        bgColor: 'bg-blue-100',
        textColor: 'text-blue-700'
    },
    dhl: {
        name: 'DHL Express',
        color: 'yellow',
        bgColor: 'bg-yellow-100',
        textColor: 'text-yellow-700'
    }
}

export default function CarrierBadge({ carrierCode, size = 'md' }) {
    const carrier = CARRIER_DISPLAY[carrierCode?.toLowerCase()] || {
        name: carrierCode || 'Unknown',
        bgColor: 'bg-gray-100',
        textColor: 'text-gray-700'
    }

    const sizeClasses = {
        sm: 'px-2 py-0.5 text-xs',
        md: 'px-2.5 py-1 text-sm',
        lg: 'px-3 py-1.5 text-base'
    }

    return (
        <span
            className={`inline-flex items-center gap-1.5 rounded-full font-medium ${carrier.bgColor} ${carrier.textColor} ${sizeClasses[size]}`}
        >
            <Truck size={size === 'sm' ? 12 : size === 'lg' ? 18 : 14} />
            {carrier.name}
        </span>
    )
}
