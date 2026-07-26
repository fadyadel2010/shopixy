/**
 * ROI Summary Cards Component
 * Phase 8B: Campaign Cost & ROI (MVP)
 * 
 * Displays ROI summary with:
 * - Total Cost, Revenue, Profit cards
 * - Overall ROI percentage
 * - Cost per Acquisition
 * 
 * PURE RENDERER:
 * - All calculations from backend
 * - NO ROI calculations in frontend
 */

import { useTranslation } from 'react-i18next'
import { DollarSign, TrendingUp, TrendingDown, ShoppingCart, Target } from 'lucide-react'
import { useSettings } from '../../../../contexts/SettingsContext'

export default function RoiSummaryCards({ data, loading }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    if (loading) {
        return (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
                {[1, 2, 3, 4, 5].map(i => (
                    <div key={i} className="bg-white rounded-xl p-4 border border-gray-200 animate-pulse">
                        <div className="h-4 bg-gray-200 rounded w-20 mb-3" />
                        <div className="h-8 bg-gray-200 rounded w-24" />
                    </div>
                ))}
            </div>
        )
    }

    if (!data) return null

    // Safely destructure with default values
    const totalCost = data.totalCost ?? 0
    const totalRevenue = data.totalRevenue ?? 0
    const totalProfit = data.totalProfit ?? 0
    const totalOrders = data.totalOrders ?? 0
    const overallRoi = data.overallRoi

    // Calculate CPA (Cost Per Acquisition)
    const cpa = totalOrders > 0 && totalCost > 0 ? totalCost / totalOrders : null

    // Determine overall ROI status
    const getRoiStatus = () => {
        if (overallRoi === null || overallRoi === undefined) return 'neutral'
        if (overallRoi > 0) return 'positive'
        if (overallRoi < 0) return 'negative'
        return 'neutral'
    }
    const roiStatus = getRoiStatus()

    // Format ROI value safely
    const formatRoi = (roi) => {
        if (roi === null || roi === undefined) return '-'
        const sign = roi > 0 ? '+' : ''
        return `${sign}${Number(roi).toFixed(1)}%`
    }

    const cards = [
        {
            label: t('roi.totalCost', 'Total Cost'),
            value: formatCurrency(totalCost),
            icon: DollarSign,
            iconBg: 'bg-purple-100',
            iconColor: 'text-purple-600',
            subtitle: totalCost > 0 ? t('roi.marketingSpend', 'Marketing Spend') : t('roi.noCosts', 'No costs recorded')
        },
        {
            label: t('roi.totalRevenue', 'Total Revenue'),
            value: formatCurrency(totalRevenue),
            icon: TrendingUp,
            iconBg: 'bg-blue-100',
            iconColor: 'text-blue-600',
            subtitle: t('roi.fromAttributedOrders', 'From attributed orders')
        },
        {
            label: t('roi.totalProfit', 'Profit'),
            value: `${totalProfit >= 0 ? '+' : ''}${formatCurrency(totalProfit)}`,
            valueColor: totalProfit > 0 ? 'text-green-600' : totalProfit < 0 ? 'text-red-600' : 'text-gray-900',
            icon: totalProfit >= 0 ? TrendingUp : TrendingDown,
            iconBg: totalProfit >= 0 ? 'bg-green-100' : 'bg-red-100',
            iconColor: totalProfit >= 0 ? 'text-green-600' : 'text-red-600',
            subtitle: t('roi.revenueMinusCost', 'Revenue - Cost')
        },
        {
            label: t('roi.orders', 'Orders'),
            value: (totalOrders ?? 0).toLocaleString(),
            icon: ShoppingCart,
            iconBg: 'bg-amber-100',
            iconColor: 'text-amber-600',
            subtitle: t('roi.attributedOrders', 'Attributed orders')
        },
        {
            label: t('roi.overallRoi', 'Overall ROI'),
            value: formatRoi(overallRoi),
            valueColor: roiStatus === 'positive' ? 'text-green-600' : roiStatus === 'negative' ? 'text-red-600' : 'text-gray-500',
            icon: Target,
            iconBg: roiStatus === 'positive' ? 'bg-green-100' : roiStatus === 'negative' ? 'bg-red-100' : 'bg-gray-100',
            iconColor: roiStatus === 'positive' ? 'text-green-600' : roiStatus === 'negative' ? 'text-red-600' : 'text-gray-500',
            subtitle: cpa !== null ? `CPA: ${formatCurrency(cpa)}` : t('roi.noDataYet', 'No data yet')
        }
    ]

    return (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
            {cards.map((card, idx) => {
                const Icon = card.icon
                return (
                    <div
                        key={idx}
                        className="bg-white rounded-xl p-4 border border-gray-200 hover:shadow-md transition-shadow"
                    >
                        <div className="flex items-center justify-between mb-3">
                            <span className="text-sm text-gray-500">{card.label}</span>
                            <div className={`w-8 h-8 rounded-lg ${card.iconBg} flex items-center justify-center`}>
                                <Icon size={16} className={card.iconColor} />
                            </div>
                        </div>
                        <div className={`text-2xl font-bold ${card.valueColor || 'text-gray-900'}`}>
                            {card.value}
                        </div>
                        <div className="text-xs text-gray-400 mt-1">
                            {card.subtitle}
                        </div>
                    </div>
                )
            })}
        </div>
    )
}
