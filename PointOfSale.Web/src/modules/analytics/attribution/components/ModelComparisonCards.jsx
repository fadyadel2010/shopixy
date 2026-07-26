/**
 * Model Comparison Cards
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Side-by-side comparison of First Touch vs Last Touch attribution
 * 
 * PURE RENDERER:
 * - All data from API
 * - No diff calculations
 * - No derived metrics
 */

import { useTranslation } from 'react-i18next'
import { MousePointer, Flag } from 'lucide-react'
import { useSettings } from '../../../../contexts/SettingsContext'

export default function ModelComparisonCards({ data, loading }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    if (loading) {
        return (
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {[1, 2].map(i => (
                    <div key={i} className="bg-white rounded-xl border border-gray-200 p-6 animate-pulse">
                        <div className="flex items-center gap-3 mb-6">
                            <div className="w-10 h-10 bg-gray-200 rounded-xl" />
                            <div className="h-5 bg-gray-200 rounded w-32" />
                        </div>
                        <div className="space-y-4">
                            <div className="h-8 bg-gray-200 rounded w-40" />
                            <div className="h-4 bg-gray-200 rounded w-28" />
                            <div className="space-y-2 mt-6">
                                {[1, 2, 3].map(j => (
                                    <div key={j} className="h-10 bg-gray-100 rounded" />
                                ))}
                            </div>
                        </div>
                    </div>
                ))}
            </div>
        )
    }

    if (!data) return null

    const models = [
        {
            key: 'firstTouch',
            data: data.firstTouch,
            icon: MousePointer,
            title: t('attribution.firstTouch', 'First Touch'),
            description: t('attribution.firstTouchDesc', 'Which source brought customers in'),
            color: 'emerald',
        },
        {
            key: 'lastTouch',
            data: data.lastTouch,
            icon: Flag,
            title: t('attribution.lastTouch', 'Last Touch'),
            description: t('attribution.lastTouchDesc', 'Which source closed the sale'),
            color: 'blue',
        }
    ]

    const colorClasses = {
        emerald: {
            bg: 'bg-emerald-50',
            icon: 'bg-emerald-100 text-emerald-600',
            border: 'border-emerald-200',
            bar: 'bg-emerald-500'
        },
        blue: {
            bg: 'bg-blue-50',
            icon: 'bg-blue-100 text-blue-600',
            border: 'border-blue-200',
            bar: 'bg-blue-500'
        }
    }

    return (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            {models.map(model => {
                const colors = colorClasses[model.color]
                const modelData = model.data
                const topSources = modelData?.topSources || []
                const maxRevenue = Math.max(...topSources.map(s => s.revenue || 0), 1)

                return (
                    <div
                        key={model.key}
                        className={`bg-white rounded-xl border ${colors.border} p-6`}
                    >
                        {/* Header */}
                        <div className="flex items-center gap-3 mb-6">
                            <div className={`w-10 h-10 rounded-xl flex items-center justify-center ${colors.icon}`}>
                                <model.icon size={20} />
                            </div>
                            <div>
                                <h3 className="font-semibold text-gray-900">
                                    {model.title}
                                </h3>
                                <p className="text-xs text-gray-500">
                                    {model.description}
                                </p>
                            </div>
                        </div>

                        {/* Summary Stats */}
                        <div className="grid grid-cols-2 gap-4 mb-6">
                            <div className={`p-4 rounded-lg ${colors.bg}`}>
                                <p className="text-sm text-gray-500">
                                    {t('attribution.totalRevenue', 'Total Revenue')}
                                </p>
                                <p className="text-xl font-bold text-gray-900">
                                    {formatCurrency(modelData?.totalRevenue)}
                                </p>
                            </div>
                            <div className={`p-4 rounded-lg ${colors.bg}`}>
                                <p className="text-sm text-gray-500">
                                    {t('attribution.totalOrders', 'Total Orders')}
                                </p>
                                <p className="text-xl font-bold text-gray-900">
                                    {modelData?.totalOrders?.toLocaleString() || 0}
                                </p>
                            </div>
                        </div>

                        {/* Top Sources */}
                        <div>
                            <h4 className="text-sm font-medium text-gray-700 mb-3">
                                {t('attribution.topSources', 'Top 5 Sources')}
                            </h4>

                            {topSources.length === 0 ? (
                                <p className="text-sm text-gray-400 text-center py-4">
                                    {t('attribution.noData', 'No data available')}
                                </p>
                            ) : (
                                <div className="space-y-2">
                                    {topSources.map((source, idx) => {
                                        const barWidth = ((source.revenue || 0) / maxRevenue) * 100

                                        return (
                                            <div key={idx} className="flex items-center gap-3">
                                                <div className="w-6 text-xs text-gray-400 text-right">
                                                    #{idx + 1}
                                                </div>
                                                <div className="flex-1">
                                                    <div className="flex justify-between text-sm mb-1">
                                                        <span className="font-medium text-gray-700">
                                                            {source.source}
                                                            {source.medium && source.medium !== 'none' && (
                                                                <span className="text-gray-400 font-normal"> / {source.medium}</span>
                                                            )}
                                                        </span>
                                                        <span className="text-gray-600">
                                                            {formatCurrency(source.revenue)}
                                                        </span>
                                                    </div>
                                                    <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
                                                        <div
                                                            className={`h-full rounded-full ${colors.bar}`}
                                                            style={{ width: `${barWidth}%` }}
                                                        />
                                                    </div>
                                                </div>
                                            </div>
                                        )
                                    })}
                                </div>
                            )}
                        </div>
                    </div>
                )
            })}
        </div>
    )
}
