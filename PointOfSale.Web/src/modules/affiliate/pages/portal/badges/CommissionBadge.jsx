/**
 * FE-1: Commission Badge Component
 * AF-22: i18n support
 * 
 * Rules:
 * - > 25% → gold "High commission"
 * - 10%-25% → blue
 * - < 10% → gray
 */
import { TrendingUp, DollarSign } from 'lucide-react'
import { useLanguage } from '../../../i18n/LanguageContext'

export default function CommissionBadge({ commissionRate, className = '' }) {
    const { t } = useLanguage()
    const percentage = (commissionRate * 100).toFixed(0)

    if (commissionRate > 0.25) {
        return (
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-400 border border-amber-500/20 text-[10px] font-bold ${className}`}>
                <TrendingUp className="w-3 h-3" />
                {percentage}% {t('portal.badges.highCommission')}
            </span>
        )
    }

    if (commissionRate >= 0.10) {
        return (
            <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 border border-blue-500/20 text-[10px] font-bold ${className}`}>
                <DollarSign className="w-3 h-3" />
                {percentage}%
            </span>
        )
    }

    return (
        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gray-500/10 text-gray-600 dark:text-gray-400 border border-gray-500/20 text-[10px] font-medium ${className}`}>
            <DollarSign className="w-3 h-3" />
            {percentage}%
        </span>
    )
}
