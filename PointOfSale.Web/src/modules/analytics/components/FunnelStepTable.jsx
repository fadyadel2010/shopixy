/**
 * Funnel Step Table Component
 * Phase 5A: Admin Funnel Dashboard UI
 * 
 * Displays step-by-step funnel data in a table:
 * - Step Order
 * - Step Name
 * - Event Type
 * - Count
 * - Conversion from Previous (%)
 * - Drop-off from Previous (%)
 * - Drop-off Count
 * - Conversion from Entry (%)
 * 
 * ALL VALUES COME DIRECTLY FROM API - NO FRONTEND MATH
 */

import React from 'react'
import { useTranslation } from 'react-i18next'

import TopPageStatsTable from './TopPageStatsTable'
import TopProductStatsTable from './TopProductStatsTable'

export default function FunnelStepTable({ steps }) {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    if (!steps || steps.length === 0) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-8 text-center text-gray-500">
                {t('funnels.table.noData')}
            </div>
        )
    }

    const headers = [
        { key: 'order', label: isRTL ? '#' : '#', width: 'w-12' },
        { key: 'name', label: t('funnels.table.step'), width: 'w-40' },
        { key: 'eventType', label: t('funnels.table.event'), width: 'w-36' },
        { key: 'count', label: t('funnels.table.count'), width: 'w-24', align: 'right' },
        { key: 'conversionFromPrevious', label: t('funnels.table.fromPrevious'), width: 'w-28', align: 'right' },
        { key: 'dropOffFromPrevious', label: t('funnels.table.dropOff'), width: 'w-24', align: 'right' },
        { key: 'dropOffCount', label: t('funnels.table.lost'), width: 'w-20', align: 'right' },
        { key: 'conversionFromEntry', label: t('funnels.table.fromEntry'), width: 'w-28', align: 'right' }
    ]

    // Color helper for conversion/drop-off - PRESENTATION ONLY
    const getConversionColor = (value) => {
        if (value >= 50) return 'text-green-600'
        if (value >= 20) return 'text-yellow-600'
        return 'text-red-600'
    }

    const getDropOffColor = (value) => {
        if (value <= 20) return 'text-green-600'
        if (value <= 50) return 'text-yellow-600'
        return 'text-red-600'
    }

    return (
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-200">
                    <thead className="bg-gray-50">
                        <tr>
                            {headers.map(header => (
                                <th
                                    key={header.key}
                                    className={`px-4 py-3 text-xs font-semibold text-gray-500 uppercase tracking-wider ${header.width} ${header.align === 'right' ? 'text-right' : 'text-left'}`}
                                >
                                    {header.label}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {steps.map((step, index) => (
                            <React.Fragment key={step.order}>
                                <tr
                                    className={index % 2 === 0 ? 'bg-white' : 'bg-gray-50/50'}
                                >
                                    <td className="px-4 py-3 text-sm text-gray-600">
                                        {step.order}
                                    </td>
                                    <td className="px-4 py-3">
                                        <div className="text-sm font-medium text-gray-900">
                                            {step.name}
                                        </div>
                                    </td>
                                    <td className="px-4 py-3">
                                        <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-700">
                                            {step.eventType}
                                        </span>
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-900 text-right font-medium">
                                        {step.count?.toLocaleString() || 0}
                                    </td>
                                    <td className={`px-4 py-3 text-sm text-right font-semibold ${getConversionColor(step.conversionFromPrevious)}`}>
                                        {step.conversionFromPrevious?.toFixed(2) || 0}%
                                    </td>
                                    <td className={`px-4 py-3 text-sm text-right font-semibold ${getDropOffColor(step.dropOffFromPrevious)}`}>
                                        {step.dropOffFromPrevious?.toFixed(2) || 0}%
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-500 text-right">
                                        {step.dropOffCount?.toLocaleString() || 0}
                                    </td>
                                    <td className={`px-4 py-3 text-sm text-right font-semibold ${getConversionColor(step.conversionFromEntry)}`}>
                                        {step.conversionFromEntry?.toFixed(2) || 0}%
                                    </td>
                                </tr>
                                {(step.topPages?.length > 0 || step.topProducts?.length > 0) && (
                                    <tr className={index % 2 === 0 ? 'bg-white' : 'bg-gray-50/50'}>
                                        <td colSpan={8} className="px-4 pb-4 pt-0">
                                            <div className="flex gap-4">
                                                {step.topPages?.length > 0 && (
                                                    <div className="flex-1">
                                                        <TopPageStatsTable pages={step.topPages} />
                                                    </div>
                                                )}
                                                {step.topProducts?.length > 0 && (
                                                    <div className="flex-1">
                                                        <TopProductStatsTable
                                                            products={step.topProducts}
                                                            titleKey={step.eventType === 'AddToCart' ? 'topAddedToCart' : 'topViewedProducts'}
                                                        />
                                                    </div>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                )}
                            </React.Fragment>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    )
}
