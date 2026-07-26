/**
 * FE-1: Stock Badge Component
 * AF-22: i18n support
 * 
 * Rules:
 * - isOutOfStock → gray "Out of stock"
 * - lowStock → orange "Low stock"
 * - else → green "In stock"
 */
import { Package, AlertTriangle, CheckCircle } from 'lucide-react'
import { useLanguage } from '../../../i18n/LanguageContext'

export default function StockBadge({ isOutOfStock, lowStock, className = '' }) {
    const { t } = useLanguage()
    
    if (isOutOfStock) {
        return (
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gray-500/10 text-gray-600 dark:text-gray-400 border border-gray-500/20 text-[10px] font-medium ${className}`}>
                <Package className="w-3 h-3" />
                {t('portal.badges.outOfStock')}
            </span>
        )
    }

    if (lowStock) {
        return (
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-400 border border-amber-500/20 text-[10px] font-medium ${className}`}>
                <AlertTriangle className="w-3 h-3" />
                {t('portal.badges.lowStock')}
            </span>
        )
    }

    return (
        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 text-[10px] font-medium ${className}`}>
            <CheckCircle className="w-3 h-3" />
            {t('portal.badges.inStock')}
        </span>
    )
}
