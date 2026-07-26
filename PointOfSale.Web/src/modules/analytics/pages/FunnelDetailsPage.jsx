/**
 * Funnel Details Page
 * Phase 5A: Admin Funnel Dashboard UI
 * 
 * Route: /analytics/funnels/:funnelKey
 * 
 * Features:
 * - Time window selector (15min, 30min, 1h, 6h, 24h)
 * - Manual refresh button
 * - Last updated timestamp
 * - Summary cards
 * - Funnel bar visualization
 * - Step-by-step table
 * 
 * API: GET /api/admin/analytics/funnels/{key}/realtime?windowMinutes=X
 * 
 * PURE RENDERER - NO FUNNEL MATH - ALL VALUES FROM API
 */

import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    ArrowLeft,
    RefreshCw,
    Clock,
    Activity,
    ChevronDown
} from 'lucide-react'
import { useFunnelRealtime } from '../hooks/useFunnelRealtime'
import FunnelSummaryCards from '../components/FunnelSummaryCards'
import FunnelBars from '../components/FunnelBars'
import FunnelStepTable from '../components/FunnelStepTable'

// Time window options
export default function FunnelDetailsPage() {
    const { funnelKey } = useParams()
    const navigate = useNavigate()
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    const [windowMinutes, setWindowMinutes] = useState(60)
    const [showWindowDropdown, setShowWindowDropdown] = useState(false)

    const { result, loading, error, lastUpdated, refetch } = useFunnelRealtime(funnelKey, windowMinutes)

    // Time window options
    // NOTE: Labels must be dynamic to support language switching
    const getWindowOptions = () => [
        { value: 15, label: t('funnels.timeWindows.last15min') },
        { value: 30, label: t('funnels.timeWindows.last30min') },
        { value: 60, label: t('funnels.timeWindows.lastHour') },
        { value: 180, label: t('funnels.timeWindows.last3hours') },
        { value: 360, label: t('funnels.timeWindows.last6hours') },
        { value: 720, label: t('funnels.timeWindows.last12hours') },
        { value: 1440, label: t('funnels.timeWindows.last24hours') }
    ]

    // Format last updated time
    const formatLastUpdated = () => {
        if (!lastUpdated) return ''
        return lastUpdated.toLocaleTimeString(isRTL ? 'ar-EG' : 'en-US', {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        })
    }

    // Get current window label
    const getCurrentWindowLabel = () => {
        const options = getWindowOptions()
        const option = options.find(o => o.value === windowMinutes)
        return option?.label || ''
    }

    return (
        <div className="max-w-6xl mx-auto">
            {/* Header */}
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
                <div className="flex items-center gap-4">
                    <button
                        onClick={() => navigate('/analytics/funnels')}
                        className="w-10 h-10 flex items-center justify-center rounded-lg border border-gray-200 hover:bg-gray-50 transition-colors"
                    >
                        <ArrowLeft size={18} className={isRTL ? 'rotate-180' : ''} />
                    </button>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {result?.funnelName || funnelKey}
                        </h1>
                        <div className="flex items-center gap-2 text-sm text-gray-500 mt-1">
                            <Clock size={14} />
                            <span>
                                {result?.windowLabel || getCurrentWindowLabel()}
                            </span>
                            {lastUpdated && (
                                <>
                                    <span>•</span>
                                    <span>
                                        {t('funnels.details.updated')} {formatLastUpdated()}
                                    </span>
                                </>
                            )}
                        </div>
                    </div>
                </div>

                {/* Controls */}
                <div className="flex items-center gap-3">
                    {/* Time Window Dropdown */}
                    <div className="relative">
                        <button
                            onClick={() => setShowWindowDropdown(!showWindowDropdown)}
                            className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50"
                        >
                            <Clock size={16} />
                            <span>{getCurrentWindowLabel()}</span>
                            <ChevronDown size={16} className={showWindowDropdown ? 'rotate-180' : ''} />
                        </button>

                        {showWindowDropdown && (
                            <>
                                <div
                                    className="fixed inset-0 z-10"
                                    onClick={() => setShowWindowDropdown(false)}
                                />
                                <div className="absolute right-0 mt-2 w-48 bg-white border border-gray-200 rounded-lg shadow-lg z-20">
                                    {getWindowOptions().map(option => (
                                        <button
                                            key={option.value}
                                            onClick={() => {
                                                setWindowMinutes(option.value)
                                                setShowWindowDropdown(false)
                                            }}
                                            className={`w-full text-left px-4 py-2 text-sm hover:bg-gray-50 first:rounded-t-lg last:rounded-b-lg ${windowMinutes === option.value
                                                ? 'bg-indigo-50 text-indigo-600 font-medium'
                                                : 'text-gray-700'
                                                }`}
                                        >
                                            {option.label}
                                        </button>
                                    ))}
                                </div>
                            </>
                        )}
                    </div>

                    {/* Refresh Button */}
                    <button
                        onClick={refetch}
                        disabled={loading}
                        className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50"
                    >
                        <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                        {t('funnels.refresh')}
                    </button>
                </div>
            </div>

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-xl p-4 mb-6">
                    <p className="text-red-600 text-sm">{error}</p>
                </div>
            )}

            {/* Loading State */}
            {loading && !result && (
                <div className="space-y-6">
                    {/* Summary Cards Skeleton */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                        {[1, 2, 3, 4].map(i => (
                            <div key={i} className="bg-white rounded-xl border border-gray-200 p-5 animate-pulse">
                                <div className="flex items-center gap-3 mb-3">
                                    <div className="w-10 h-10 bg-gray-200 rounded-lg"></div>
                                    <div className="w-20 h-4 bg-gray-200 rounded"></div>
                                </div>
                                <div className="w-24 h-7 bg-gray-200 rounded mb-1"></div>
                                <div className="w-16 h-3 bg-gray-100 rounded"></div>
                            </div>
                        ))}
                    </div>

                    {/* Bars Skeleton */}
                    <div className="bg-white rounded-xl border border-gray-200 p-6 animate-pulse">
                        <div className="w-32 h-4 bg-gray-200 rounded mb-4"></div>
                        <div className="space-y-4">
                            {[1, 2, 3, 4, 5].map(i => (
                                <div key={i}>
                                    <div className="flex justify-between mb-1">
                                        <div className="w-24 h-4 bg-gray-200 rounded"></div>
                                        <div className="w-16 h-4 bg-gray-200 rounded"></div>
                                    </div>
                                    <div className="h-8 bg-gray-100 rounded-lg"></div>
                                </div>
                            ))}
                        </div>
                    </div>
                </div>
            )}

            {/* Main Content */}
            {result && (
                <>
                    {/* Summary Cards */}
                    <FunnelSummaryCards result={result} />

                    {/* Funnel Visualization */}
                    <FunnelBars steps={result.steps} />

                    {/* Step Table */}
                    <div className="mb-6">
                        <h3 className="text-sm font-semibold text-gray-700 mb-3">
                            {t('funnels.details.stepDetails')}
                        </h3>
                        <FunnelStepTable steps={result.steps} />
                    </div>

                    {/* Additional Context */}
                    <div className="p-4 bg-gray-50 border border-gray-200 rounded-xl text-xs text-gray-500">
                        <div className="flex flex-wrap gap-4">
                            <span>
                                <strong>{t('funnels.details.funnelKey')}</strong> {result.funnelKey}
                            </span>
                            <span>
                                <strong>{t('funnels.details.window')}</strong> {result.windowMinutes} {t('funnels.details.minutes')}
                            </span>
                            <span>
                                <strong>{t('funnels.details.computedAt')}</strong> {new Date(result.computedAtUtc).toLocaleString()}
                            </span>
                        </div>
                    </div>
                </>
            )}

            {/* Empty State */}
            {!loading && !result && !error && (
                <div className="text-center py-16">
                    <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <Activity className="text-gray-400" size={28} />
                    </div>
                    <h3 className="text-lg font-medium text-gray-900 mb-2">
                        {t('funnels.details.noData')}
                    </h3>
                    <p className="text-gray-500 mb-4">
                        {t('funnels.details.noEvents')}
                    </p>
                    <button
                        onClick={refetch}
                        className="text-indigo-600 hover:text-indigo-700 font-medium"
                    >
                        {t('funnels.details.tryAgain')}
                    </button>
                </div>
            )}
        </div>
    )
}
