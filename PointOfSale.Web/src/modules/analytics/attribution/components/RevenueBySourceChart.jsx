/**
 * Revenue By Source Chart
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Displays revenue breakdown by marketing source as a horizontal bar chart
 * 
 * PURE RENDERER:
 * - All data comes from API
 * - No grouping logic
 * - No normalization
 * - Percentages are from API
 */

import { useTranslation } from 'react-i18next'
import { useSettings } from '../../../../contexts/SettingsContext'

// Color palette for sources
const SOURCE_COLORS = {
    facebook: '#1877F2',
    google: '#4285F4',
    instagram: '#E4405F',
    twitter: '#1DA1F2',
    tiktok: '#000000',
    email: '#EA4335',
    direct: '#6B7280',
    referral: '#8B5CF6',
    organic: '#10B981',
    cpc: '#F59E0B',
    social: '#EC4899'
}

const getSourceColor = (source) => {
    const lowerSource = source?.toLowerCase() || 'direct'
    return SOURCE_COLORS[lowerSource] || '#6B7280'
}

export default function RevenueBySourceChart({ data, loading }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    if (loading) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-6">
                <div className="h-5 bg-gray-200 rounded w-40 mb-6 animate-pulse" />
                <div className="space-y-4">
                    {[1, 2, 3, 4].map(i => (
                        <div key={i} className="animate-pulse">
                            <div className="flex justify-between mb-1">
                                <div className="h-4 bg-gray-200 rounded w-24" />
                                <div className="h-4 bg-gray-200 rounded w-16" />
                            </div>
                            <div className="h-6 bg-gray-100 rounded w-full" />
                        </div>
                    ))}
                </div>
            </div>
        )
    }

    const sources = data?.revenueBySource || []

    if (sources.length === 0) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    {t('attribution.revenueBySource', 'Revenue by Source')}
                </h3>
                <div className="text-center py-12">
                    <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <span className="text-2xl">📊</span>
                    </div>
                    <p className="text-gray-500">
                        {t('attribution.noSourceData', 'No attributed orders yet')}
                    </p>
                </div>
            </div>
        )
    }

    // Find the max revenue for scaling bars
    const maxRevenue = Math.max(...sources.map(s => s.revenue || 0), 1)

    return (
        <div className="bg-white rounded-xl border border-gray-200 p-6">
            <h3 className="text-lg font-semibold text-gray-900 mb-6">
                {t('attribution.revenueBySource', 'Revenue by Source')}
            </h3>

            <div className="space-y-4">
                {sources.map((source, idx) => {
                    const barWidth = ((source.revenue || 0) / maxRevenue) * 100
                    const color = getSourceColor(source.source)

                    return (
                        <div key={idx}>
                            {/* Labels */}
                            <div className="flex justify-between items-center mb-1">
                                <div className="flex items-center gap-2">
                                    <div
                                        className="w-3 h-3 rounded-full"
                                        style={{ backgroundColor: color }}
                                    />
                                    <span className="text-sm font-medium text-gray-700">
                                        {source.source}
                                        {source.medium && source.medium !== 'none' && (
                                            <span className="text-gray-400 font-normal"> / {source.medium}</span>
                                        )}
                                    </span>
                                </div>
                                <div className="text-right">
                                    <span className="text-sm font-semibold text-gray-900">
                                        {formatCurrency(source.revenue)}
                                    </span>
                                    <span className="text-xs text-gray-500 ml-2">
                                        ({source.percentageOfTotal?.toFixed(1) || 0}%)
                                    </span>
                                </div>
                            </div>

                            {/* Bar */}
                            <div className="w-full h-6 bg-gray-100 rounded-lg overflow-hidden">
                                <div
                                    className="h-full rounded-lg transition-all duration-500 flex items-center justify-end pr-2"
                                    style={{
                                        width: `${Math.max(barWidth, 2)}%`,
                                        backgroundColor: color
                                    }}
                                >
                                    {barWidth > 15 && (
                                        <span className="text-xs text-white font-medium">
                                            {source.orders} orders
                                        </span>
                                    )}
                                </div>
                            </div>
                        </div>
                    )
                })}
            </div>

            {/* Legend / Summary */}
            <div className="mt-6 pt-4 border-t border-gray-100">
                <div className="flex flex-wrap gap-4 text-xs text-gray-500">
                    <div className="flex items-center gap-2">
                        <div className="w-2 h-2 rounded-full bg-emerald-500" />
                        <span>{t('attribution.organic', 'Organic')}</span>
                    </div>
                    <div className="flex items-center gap-2">
                        <div className="w-2 h-2 rounded-full bg-blue-500" />
                        <span>{t('attribution.paid', 'Paid')}</span>
                    </div>
                    <div className="flex items-center gap-2">
                        <div className="w-2 h-2 rounded-full bg-gray-500" />
                        <span>{t('attribution.direct', 'Direct')}</span>
                    </div>
                </div>
            </div>
        </div>
    )
}
