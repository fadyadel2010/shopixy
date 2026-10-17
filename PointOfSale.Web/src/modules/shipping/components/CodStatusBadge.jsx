/**
 * COD Status Badge Component
 * Phase: Shipping Frontend
 * 
 * Displays Cash on Delivery status.
 * Shows amount and collection status.
 */

import { Banknote, CheckCircle2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'

export default function CodStatusBadge({
    isCod,
    codAmount,
    codCollected,
    currency,
    showAmount = true,
    size = 'md'
}) {
    const { t } = useTranslation()
    // Not COD - don't show anything
    if (!isCod) {
        return (
            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs text-gray-500 bg-gray-50 rounded-full">
                {t('shipping.closure.prepaid')}
            </span>
        )
    }

    const sizeClasses = {
        sm: 'px-2 py-0.5 text-xs',
        md: 'px-2.5 py-1 text-sm',
        lg: 'px-3 py-1.5 text-base'
    }

    const iconSize = size === 'sm' ? 12 : size === 'lg' ? 18 : 14

    // COD Collected
    if (codCollected) {
        return (
            <span className={`inline-flex items-center gap-1.5 rounded-full font-medium bg-green-100 text-green-700 ${sizeClasses[size]}`}>
                <CheckCircle2 size={iconSize} />
                <span>{t('shipping.closure.collected')}</span>
                {showAmount && codAmount > 0 && (
                    <span className="font-bold">
                        {codAmount.toLocaleString()} {currency}
                    </span>
                )}
            </span>
        )
    }

    // COD Pending
    return (
        <span className={`inline-flex items-center gap-1.5 rounded-full font-medium bg-amber-100 text-amber-700 ${sizeClasses[size]}`}>
            <Banknote size={iconSize} />
            <span>{t('shipping.closure.collectionPending')}</span>
            {showAmount && codAmount > 0 && (
                <span className="font-bold">
                    {codAmount.toLocaleString()} {currency}
                </span>
            )}
        </span>
    )
}
