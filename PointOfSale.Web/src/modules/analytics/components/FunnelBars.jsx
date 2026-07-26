/**
 * Funnel Bars Visualization Component
 * Phase 5A: Admin Funnel Dashboard UI
 * 
 * Horizontal funnel bar chart visualization:
 * - Width proportional to Conversion from Entry (from API)
 * - Color based on conversion rate:
 *   - Green: >= 50%
 *   - Yellow: 20-49%
 *   - Red: < 20%
 * 
 * COLOR IS PRESENTATION ONLY - NO LOGIC
 * ALL VALUES COME DIRECTLY FROM API
 */

import { useTranslation } from 'react-i18next'

export default function FunnelBars({ steps }) {
    const { t } = useTranslation()

    if (!steps || steps.length === 0) {
        return null
    }

    // Get bar color based on conversion rate - PRESENTATION ONLY
    const getBarColor = (conversionFromEntry) => {
        if (conversionFromEntry >= 50) return 'bg-emerald-500'
        if (conversionFromEntry >= 20) return 'bg-yellow-500'
        return 'bg-red-500'
    }

    const getBarBgColor = (conversionFromEntry) => {
        if (conversionFromEntry >= 50) return 'bg-emerald-100'
        if (conversionFromEntry >= 20) return 'bg-yellow-100'
        return 'bg-red-100'
    }

    return (
        <div className="bg-white rounded-xl border border-gray-200 p-6 mb-6">
            <h3 className="text-sm font-semibold text-gray-700 mb-4">
                {t('funnels.bars.funnelFlow')}
            </h3>

            <div className="space-y-4">
                {steps.map((step, index) => {
                    // Width is based on conversionFromEntry from API
                    const width = Math.max(step.conversionFromEntry || 0, 2) // Min 2% for visibility
                    const barColor = getBarColor(step.conversionFromEntry)
                    const bgColor = getBarBgColor(step.conversionFromEntry)

                    return (
                        <div key={step.order} className="relative">
                            {/* Step Label */}
                            <div className="flex justify-between items-center mb-1">
                                <div className="flex items-center gap-2">
                                    <span className="w-6 h-6 rounded-full bg-gray-200 text-gray-600 text-xs flex items-center justify-center font-medium">
                                        {step.order}
                                    </span>
                                    <span className="text-sm font-medium text-gray-700">
                                        {step.name}
                                    </span>
                                </div>
                                <div className="text-sm">
                                    <span className="font-semibold text-gray-900">
                                        {step.count?.toLocaleString() || 0}
                                    </span>
                                    <span className="text-gray-400 ml-2">
                                        ({step.conversionFromEntry?.toFixed(1) || 0}%)
                                    </span>
                                </div>
                            </div>

                            {/* Bar Container */}
                            <div className={`h-8 rounded-lg ${bgColor} overflow-hidden relative`}>
                                {/* Filled Bar */}
                                <div
                                    className={`h-full ${barColor} rounded-lg transition-all duration-500 ease-out`}
                                    style={{ width: `${width}%` }}
                                />

                                {/* Drop-off indicator (between steps) */}
                                {index < steps.length - 1 && step.dropOffFromPrevious > 0 && (
                                    <div className="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-gray-500">
                                        {step.order > 1 && (
                                            <span className="text-red-500">
                                                ↓ {step.dropOffFromPrevious?.toFixed(0)}%
                                            </span>
                                        )}
                                    </div>
                                )}
                            </div>

                            {/* Drop-off connector line */}
                            {index < steps.length - 1 && (
                                <div className="flex justify-center my-1">
                                    <div className="w-0.5 h-3 bg-gray-200"></div>
                                </div>
                            )}
                        </div>
                    )
                })}
            </div>

            {/* Legend */}
            <div className="mt-6 pt-4 border-t border-gray-100 flex flex-wrap gap-4 text-xs text-gray-500">
                <div className="flex items-center gap-1.5">
                    <div className="w-3 h-3 rounded bg-emerald-500"></div>
                    <span>{t('funnels.bars.conversionHigh')}</span>
                </div>
                <div className="flex items-center gap-1.5">
                    <div className="w-3 h-3 rounded bg-yellow-500"></div>
                    <span>{t('funnels.bars.conversionMedium')}</span>
                </div>
                <div className="flex items-center gap-1.5">
                    <div className="w-3 h-3 rounded bg-red-500"></div>
                    <span>{t('funnels.bars.conversionLow')}</span>
                </div>
            </div>
        </div>
    )
}
