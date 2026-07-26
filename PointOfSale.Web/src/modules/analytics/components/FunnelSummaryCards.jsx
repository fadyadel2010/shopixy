/**
 * Funnel Summary Cards Component
 * Phase 5A: Admin Funnel Dashboard UI
 * 
 * Displays summary metrics from API response:
 * - Entry Count
 * - Completion Count
 * - Overall Conversion Rate
 * - Overall Drop-off Rate
 * 
 * ALL VALUES COME DIRECTLY FROM API - NO FRONTEND MATH
 */

import { useTranslation } from 'react-i18next'
import { TrendingUp, TrendingDown, Users, Target } from 'lucide-react'

export default function FunnelSummaryCards({ result }) {
    const { t } = useTranslation()

    if (!result) return null

    const cards = [
        {
            key: 'entry',
            label: t('funnels.summary.entryCount'),
            value: result.entryCount?.toLocaleString() || '0',
            icon: Users,
            color: 'indigo',
            description: t('funnels.summary.entryDescription')
        },
        {
            key: 'completion',
            label: t('funnels.summary.completions'),
            value: result.completionCount?.toLocaleString() || '0',
            icon: Target,
            color: 'emerald',
            description: t('funnels.summary.completionsDescription')
        },
        {
            key: 'conversion',
            label: t('funnels.summary.conversionRate'),
            value: `${result.overallConversionRate?.toFixed(2) || '0'}%`,
            icon: TrendingUp,
            color: 'green',
            description: t('funnels.summary.conversionDescription')
        },
        {
            key: 'dropoff',
            label: t('funnels.summary.dropOffRate'),
            value: `${result.overallDropOffRate?.toFixed(2) || '0'}%`,
            icon: TrendingDown,
            color: 'red',
            description: t('funnels.summary.dropOffDescription')
        }
    ]

    const colorClasses = {
        indigo: 'bg-indigo-100 text-indigo-600',
        emerald: 'bg-emerald-100 text-emerald-600',
        green: 'bg-green-100 text-green-600',
        red: 'bg-red-100 text-red-600'
    }

    return (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
            {cards.map(card => {
                const Icon = card.icon
                return (
                    <div
                        key={card.key}
                        className="bg-white rounded-xl border border-gray-200 p-5 shadow-sm"
                    >
                        <div className="flex items-center gap-3 mb-3">
                            <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${colorClasses[card.color]}`}>
                                <Icon size={20} />
                            </div>
                            <div className="text-sm text-gray-500">{card.label}</div>
                        </div>
                        <div className="text-2xl font-bold text-gray-900 mb-1">
                            {card.value}
                        </div>
                        <div className="text-xs text-gray-400">
                            {card.description}
                        </div>
                    </div>
                )
            })}
        </div>
    )
}
