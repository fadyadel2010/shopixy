/**
 * Carrier Badge Component (Storefront)
 * Phase: Storefront Shipping Experience
 * 
 * Displays carrier name with badge styling.
 * NO carrier-specific logic.
 */

import { Truck } from 'lucide-react'

export default function CarrierBadge({ carrierCode, carrierName, size = 'md' }) {
    const sizeClasses = {
        sm: 'px-2 py-0.5 text-xs',
        md: 'px-2.5 py-1 text-sm',
        lg: 'px-3 py-1.5 text-base'
    }

    const iconSize = size === 'sm' ? 12 : size === 'lg' ? 18 : 14

    return (
        <span className={`inline-flex items-center gap-1.5 rounded-full font-medium bg-blue-100 text-blue-700 ${sizeClasses[size]}`}>
            <Truck size={iconSize} />
            {carrierName || carrierCode}
        </span>
    )
}
