/**
 * Attribution Summary Cards
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Displays:
 * - Total Revenue
 * - Total Orders
 * - Average Order Value (from API)
 * 
 * PURE RENDERER - NO CALCULATIONS
 */

import { DollarSign, ShoppingCart, TrendingUp } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../../../contexts/SettingsContext'

export default function AttributionSummaryCards({ data, loading }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    if (loading) {
        return (
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
                {[1, 2, 3].map(i => (
                    <div key={i} className="bg-white rounded-xl border border-gray-200 p-6 animate-pulse">
                        <div className="flex items-center gap-4">
                            <div className="w-12 h-12 bg-gray-200 rounded-xl" />
                            <div className="flex-1">
                                <div className="h-3 bg-gray-200 rounded w-20 mb-2" />
                                <div className="h-6 bg-gray-200 rounded w-28" />
                            </div>
                        </div>
                    </div>
                ))}
            </div>
        )
    }

    if (!data) return null

    const avgOrderValue = data.totalOrders > 0
        ? data.totalRevenue / data.totalOrders
        : 0

    const cards = [
        {
            icon: DollarSign,
            label: t('attribution.totalRevenue', 'Total Revenue'),
            value: formatCurrency(data.totalRevenue),
            color: 'emerald',
        },
        {
            icon: ShoppingCart,
            label: t('attribution.totalOrders', 'Total Orders'),
            value: data.totalOrders?.toLocaleString() || '0',
            color: 'blue',
        },
        {
            icon: TrendingUp,
            label: t('attribution.avgOrderValue', 'Avg. Order Value'),
            value: formatCurrency(avgOrderValue),
            color: 'purple',
        }
    ]

    const colorClasses = {
        emerald: 'bg-emerald-100 text-emerald-600',
        blue: 'bg-blue-100 text-blue-600',
        purple: 'bg-purple-100 text-purple-600',
    }

    return (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
            {cards.map((card, idx) => (
                <div key={idx} className="bg-white rounded-xl border border-gray-200 p-6">
                    <div className="flex items-center gap-4">
                        <div className={`w-12 h-12 rounded-xl flex items-center justify-center ${colorClasses[card.color]}`}>
                            <card.icon size={24} />
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">{card.label}</p>
                            <p className="text-2xl font-bold text-gray-900">{card.value}</p>
                        </div>
                    </div>
                </div>
            ))}
        </div>
    )
}
