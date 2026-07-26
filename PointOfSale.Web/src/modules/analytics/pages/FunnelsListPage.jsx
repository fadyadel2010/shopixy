/**
 * Funnels List Page
 * Phase 5A: Admin Funnel Dashboard UI
 * 
 * Route: /analytics/funnels
 * 
 * Lists all available funnel definitions:
 * - Funnel name
 * - Step count
 * - Description
 * - Click → Funnel Details page
 * 
 * API: GET /api/admin/analytics/funnels
 * 
 * PURE RENDERER - NO FUNNEL MATH
 */

import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Activity, ArrowRight, TrendingUp, Filter, RefreshCw } from 'lucide-react'
import { useFunnelDefinitions } from '../hooks/useFunnelRealtime'

export default function FunnelsListPage() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    const { definitions, loading, error, refetch } = useFunnelDefinitions()

    // Icon mapping for funnel types
    const getFunnelIcon = (funnelKey) => {
        const icons = {
            purchase_funnel: '🛒',
            discovery_funnel: '🔍',
            checkout_funnel: '💳',
            cart_funnel: '🛍️',
            search_funnel: '🔎'
        }
        return icons[funnelKey] || '📊'
    }

    // Color mapping for funnel cards
    const getFunnelColor = (funnelKey) => {
        const colors = {
            purchase_funnel: 'indigo',
            discovery_funnel: 'purple',
            checkout_funnel: 'emerald',
            cart_funnel: 'orange',
            search_funnel: 'sky'
        }
        return colors[funnelKey] || 'gray'
    }

    const colorClasses = {
        indigo: {
            bg: 'bg-indigo-50',
            border: 'border-indigo-200 hover:border-indigo-300',
            icon: 'bg-indigo-100',
            text: 'text-indigo-600'
        },
        purple: {
            bg: 'bg-purple-50',
            border: 'border-purple-200 hover:border-purple-300',
            icon: 'bg-purple-100',
            text: 'text-purple-600'
        },
        emerald: {
            bg: 'bg-emerald-50',
            border: 'border-emerald-200 hover:border-emerald-300',
            icon: 'bg-emerald-100',
            text: 'text-emerald-600'
        },
        orange: {
            bg: 'bg-orange-50',
            border: 'border-orange-200 hover:border-orange-300',
            icon: 'bg-orange-100',
            text: 'text-orange-600'
        },
        sky: {
            bg: 'bg-sky-50',
            border: 'border-sky-200 hover:border-sky-300',
            icon: 'bg-sky-100',
            text: 'text-sky-600'
        },
        gray: {
            bg: 'bg-gray-50',
            border: 'border-gray-200 hover:border-gray-300',
            icon: 'bg-gray-100',
            text: 'text-gray-600'
        }
    }

    return (
        <div className="max-w-6xl mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between mb-8">
                <div>
                    <div className="flex items-center gap-3 mb-2">
                        <div className="w-10 h-10 bg-indigo-100 rounded-xl flex items-center justify-center">
                            <Activity className="text-indigo-600" size={22} />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('funnels.title')}
                        </h1>
                    </div>
                    <p className="text-gray-500">
                        {t('funnels.subtitle')}
                    </p>
                </div>

                <button
                    onClick={refetch}
                    disabled={loading}
                    className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                >
                    <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                    {t('funnels.refresh')}
                </button>
            </div>

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-xl p-4 mb-6">
                    <p className="text-red-600 text-sm">{error}</p>
                </div>
            )}

            {/* Loading State */}
            {loading && !definitions.length && (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {[1, 2, 3].map(i => (
                        <div key={i} className="bg-white border border-gray-200 rounded-xl p-6 animate-pulse">
                            <div className="w-12 h-12 bg-gray-200 rounded-xl mb-4"></div>
                            <div className="h-5 bg-gray-200 rounded w-3/4 mb-2"></div>
                            <div className="h-4 bg-gray-100 rounded w-full mb-4"></div>
                            <div className="h-3 bg-gray-100 rounded w-1/4"></div>
                        </div>
                    ))}
                </div>
            )}

            {/* Funnel Cards Grid */}
            {!loading && definitions.length > 0 && (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {definitions.map(funnel => {
                        const color = getFunnelColor(funnel.funnelKey)
                        const classes = colorClasses[color]
                        const icon = getFunnelIcon(funnel.funnelKey)

                        return (
                            <button
                                key={funnel.funnelKey}
                                onClick={() => navigate(`/analytics/funnels/${funnel.funnelKey}`)}
                                className={`text-left p-6 bg-white border rounded-xl transition-all hover:shadow-md group ${classes.border}`}
                            >
                                {/* Icon */}
                                <div className={`w-12 h-12 rounded-xl flex items-center justify-center mb-4 text-2xl ${classes.icon}`}>
                                    {icon}
                                </div>

                                {/* Title */}
                                <h3 className="text-lg font-semibold text-gray-900 mb-2 flex items-center justify-between">
                                    {funnel.name}
                                    <ArrowRight
                                        size={18}
                                        className={`text-gray-400 group-hover:${classes.text} transition-colors ${isRTL ? 'rotate-180' : ''}`}
                                    />
                                </h3>

                                {/* Description */}
                                <p className="text-sm text-gray-500 mb-4 line-clamp-2">
                                    {funnel.description}
                                </p>

                                {/* Steps Preview */}
                                <div className="flex items-center gap-2 text-xs text-gray-400">
                                    <Filter size={12} />
                                    <span>
                                        {funnel.stepCount} {t('funnels.steps')}
                                    </span>
                                    {funnel.isSystem && (
                                        <>
                                            <span>•</span>
                                            <span className={`${classes.text}`}>
                                                {t('funnels.system')}
                                            </span>
                                        </>
                                    )}
                                </div>

                                {/* Steps Flow Preview */}
                                {funnel.steps && (
                                    <div className="mt-4 pt-4 border-t border-gray-100">
                                        <div className="flex flex-wrap items-center gap-1 text-xs text-gray-400">
                                            {funnel.steps.slice(0, 4).map((step, idx) => (
                                                <span key={step.order} className="flex items-center gap-1">
                                                    <span className="text-gray-600">{step.name}</span>
                                                    {idx < Math.min(funnel.steps.length - 1, 3) && (
                                                        <span className="text-gray-300">→</span>
                                                    )}
                                                </span>
                                            ))}
                                            {funnel.steps.length > 4 && (
                                                <span className="text-gray-300">+{funnel.steps.length - 4}</span>
                                            )}
                                        </div>
                                    </div>
                                )}
                            </button>
                        )
                    })}
                </div>
            )}

            {/* Empty State */}
            {!loading && definitions.length === 0 && !error && (
                <div className="text-center py-16">
                    <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <TrendingUp className="text-gray-400" size={28} />
                    </div>
                    <h3 className="text-lg font-medium text-gray-900 mb-2">
                        {t('funnels.noFunnels')}
                    </h3>
                    <p className="text-gray-500">
                        {t('funnels.noFunnelsSubtitle')}
                    </p>
                </div>
            )}

            {/* Info Card */}
            <div className="mt-8 p-6 bg-gray-50 border border-gray-200 rounded-xl">
                <h3 className="text-sm font-semibold text-gray-700 mb-2">
                    {t('funnels.howItWorks')}
                </h3>
                <ul className="text-sm text-gray-500 space-y-1">
                    <li>• {t('funnels.howItWorksList.track')}</li>
                    <li>• {t('funnels.howItWorksList.shows')}</li>
                    <li>• {t('funnels.howItWorksList.calculated')}</li>
                    <li>• {t('funnels.howItWorksList.realtime')}</li>
                </ul>
            </div>
        </div>
    )
}
