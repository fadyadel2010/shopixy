/**
 * Attribution Overview Page
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Route: /analytics/attribution
 * 
 * High-level marketing performance overview:
 * - Total Revenue, Total Orders, AOV cards
 * - Attribution Model Selector (First Touch / Last Touch)
 * - Date Range Selector
 * - Revenue by Source chart
 * - Campaign Performance table
 * 
 * API: GET /api/admin/analytics/attribution/summary
 *      GET /api/admin/analytics/attribution/campaigns
 * 
 * PURE RENDERER - NO ATTRIBUTION LOGIC
 */

import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
    TrendingUp,
    RefreshCw,
    Calendar,
    ChevronDown,
    ArrowRight,
    GitCompare
} from 'lucide-react'
import { useAttributionSummary, useAttributionCampaigns } from '../hooks/useAttribution'
import AttributionSummaryCards from '../components/AttributionSummaryCards'
import RevenueBySourceChart from '../components/RevenueBySourceChart'
import CampaignPerformanceTable from '../components/CampaignPerformanceTable'

const DATE_PERIODS = [
    { value: 'today', label: 'Today' },
    { value: 'yesterday', label: 'Yesterday' },
    { value: 'last_7_days', label: 'Last 7 Days' },
    { value: 'last_30_days', label: 'Last 30 Days' },
    { value: 'this_month', label: 'This Month' },
    { value: 'last_month', label: 'Last Month' }
]

const ATTRIBUTION_MODELS = [
    { value: 'LastTouch', label: 'Last Touch', description: 'Credit to last source before purchase' },
    { value: 'FirstTouch', label: 'First Touch', description: 'Credit to first source that brought customer' }
]

export default function AttributionOverviewPage() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const navigate = useNavigate()

    const [model, setModel] = useState('LastTouch')
    const [period, setPeriod] = useState('last_7_days')
    const [showModelDropdown, setShowModelDropdown] = useState(false)
    const [showPeriodDropdown, setShowPeriodDropdown] = useState(false)

    const {
        data: summaryData,
        loading: summaryLoading,
        error: summaryError,
        refetch: refetchSummary
    } = useAttributionSummary(model, period)

    const {
        data: campaignData,
        loading: campaignLoading,
        error: campaignError,
        refetch: refetchCampaigns
    } = useAttributionCampaigns(model, period)

    const loading = summaryLoading || campaignLoading
    const error = summaryError || campaignError

    const handleRefresh = () => {
        refetchSummary()
        refetchCampaigns()
    }

    const modelLabel = value => t(value === 'FirstTouch' ? 'attribution.firstTouch' : 'attribution.lastTouch')
    const periodLabel = value => t(`attribution.periods.${({ last_7_days: 'last7Days', last_30_days: 'last30Days', this_month: 'thisMonth', last_month: 'lastMonth' })[value] || value}`)

    return (
        <div className="max-w-7xl mx-auto">
            {/* Header */}
            <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 mb-8">
                <div>
                    <div className="flex items-center gap-3 mb-2">
                        <div className="w-10 h-10 bg-indigo-100 rounded-xl flex items-center justify-center">
                            <TrendingUp className="text-indigo-600" size={22} />
                        </div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('attribution.title', 'Marketing Attribution')}
                        </h1>
                    </div>
                    <p className="text-gray-500">
                        {t('attribution.subtitle', 'See which marketing sources drive your revenue')}
                    </p>
                </div>

                <div className="flex items-center gap-2">
                    {/* Compare Models Button */}
                    <button
                        onClick={() => navigate('/analytics/attribution/models')}
                        className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-indigo-600 bg-indigo-50 border border-indigo-200 rounded-lg hover:bg-indigo-100 transition-colors"
                    >
                        <GitCompare size={16} />
                        {t('attribution.compareModels', 'Compare Models')}
                    </button>

                    {/* Refresh Button */}
                    <button
                        onClick={handleRefresh}
                        disabled={loading}
                        className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50 transition-colors"
                    >
                        <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                        {t('common.refresh', 'Refresh')}
                    </button>
                </div>
            </div>

            {/* Filters Row */}
            <div className="flex flex-wrap items-center gap-3 mb-6">
                {/* Attribution Model Selector */}
                <div className="relative">
                    <button
                        onClick={() => {
                            setShowModelDropdown(!showModelDropdown)
                            setShowPeriodDropdown(false)
                        }}
                        className="flex items-center gap-2 px-4 py-2.5 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                    >
                        <span className="text-sm font-medium text-gray-700">
                            {modelLabel(model)}
                        </span>
                        <ChevronDown size={16} className="text-gray-400" />
                    </button>

                    {showModelDropdown && (
                        <div className="absolute top-full mt-1 left-0 bg-white border border-gray-200 rounded-lg shadow-lg z-10 w-64">
                            {ATTRIBUTION_MODELS.map(m => (
                                <button
                                    key={m.value}
                                    onClick={() => {
                                        setModel(m.value)
                                        setShowModelDropdown(false)
                                    }}
                                    className={`w-full px-4 py-3 text-left hover:bg-gray-50 first:rounded-t-lg last:rounded-b-lg ${model === m.value ? 'bg-indigo-50' : ''}`}
                                >
                                    <div className="font-medium text-gray-900">{modelLabel(m.value)}</div>
                                    <div className="text-xs text-gray-500">{t(m.value === 'FirstTouch' ? 'attribution.firstTouchDesc' : 'attribution.lastTouchDesc')}</div>
                                </button>
                            ))}
                        </div>
                    )}
                </div>

                {/* Date Range Selector */}
                <div className="relative">
                    <button
                        onClick={() => {
                            setShowPeriodDropdown(!showPeriodDropdown)
                            setShowModelDropdown(false)
                        }}
                        className="flex items-center gap-2 px-4 py-2.5 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                    >
                        <Calendar size={16} className="text-gray-400" />
                        <span className="text-sm font-medium text-gray-700">
                            {periodLabel(period)}
                        </span>
                        <ChevronDown size={16} className="text-gray-400" />
                    </button>

                    {showPeriodDropdown && (
                        <div className="absolute top-full mt-1 left-0 bg-white border border-gray-200 rounded-lg shadow-lg z-10 w-48">
                            {DATE_PERIODS.map(p => (
                                <button
                                    key={p.value}
                                    onClick={() => {
                                        setPeriod(p.value)
                                        setShowPeriodDropdown(false)
                                    }}
                                    className={`w-full px-4 py-2.5 text-left text-sm hover:bg-gray-50 first:rounded-t-lg last:rounded-b-lg ${period === p.value ? 'bg-indigo-50 text-indigo-600' : 'text-gray-700'}`}
                                >
                                    {periodLabel(p.value)}
                                </button>
                            ))}
                        </div>
                    )}
                </div>

                {/* Current Model Badge */}
                <div className="ml-auto px-3 py-1.5 bg-gray-100 rounded-lg text-xs text-gray-500">
                    {t('attribution.modelLabel', 'Model')}: {modelLabel(model)}
                </div>
            </div>

            {/* Click outside to close dropdowns */}
            {(showModelDropdown || showPeriodDropdown) && (
                <div
                    className="fixed inset-0 z-0"
                    onClick={() => {
                        setShowModelDropdown(false)
                        setShowPeriodDropdown(false)
                    }}
                />
            )}

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-xl p-4 mb-6">
                    <p className="text-red-600 text-sm">{error}</p>
                </div>
            )}

            {/* Summary Cards */}
            <AttributionSummaryCards data={summaryData} loading={summaryLoading} />

            {/* Charts Grid */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-6">
                {/* Revenue by Source */}
                <RevenueBySourceChart data={summaryData} loading={summaryLoading} />

                {/* Quick Links Card */}
                <div className="bg-white rounded-xl border border-gray-200 p-6">
                    <h3 className="text-lg font-semibold text-gray-900 mb-4">
                        {t('attribution.insights', 'Attribution Insights')}
                    </h3>

                    <div className="space-y-3">
                        <button
                            onClick={() => navigate('/analytics/attribution/models')}
                            className={`w-full flex items-center justify-between p-4 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors group`}
                        >
                            <div className="flex items-center gap-3">
                                <GitCompare className="text-indigo-500" size={20} />
                                <div className="text-left">
                                    <div className="font-medium text-gray-900">
                                        {t('attribution.compareModelsTitle', 'Compare Attribution Models')}
                                    </div>
                                    <div className="text-xs text-gray-500">
                                        {t('attribution.compareModelsDesc', 'See First Touch vs Last Touch side by side')}
                                    </div>
                                </div>
                            </div>
                            <ArrowRight size={16} className={`text-gray-400 group-hover:text-indigo-500 ${isRTL ? 'rotate-180' : ''}`} />
                        </button>

                        <button
                            onClick={() => navigate('/analytics/attribution/orders')}
                            className={`w-full flex items-center justify-between p-4 bg-gray-50 rounded-lg hover:bg-gray-100 transition-colors group`}
                        >
                            <div className="flex items-center gap-3">
                                <TrendingUp className="text-emerald-500" size={20} />
                                <div className="text-left">
                                    <div className="font-medium text-gray-900">
                                        {t('attribution.viewOrdersTitle', 'View Order Attribution')}
                                    </div>
                                    <div className="text-xs text-gray-500">
                                        {t('attribution.viewOrdersDesc', 'See attribution for each order')}
                                    </div>
                                </div>
                            </div>
                            <ArrowRight size={16} className={`text-gray-400 group-hover:text-emerald-500 ${isRTL ? 'rotate-180' : ''}`} />
                        </button>
                    </div>

                    {/* Info Box */}
                    <div className="mt-6 p-4 bg-indigo-50 rounded-lg border border-indigo-100">
                        <h4 className="text-sm font-medium text-indigo-900 mb-2">
                            {t('attribution.howItWorks', 'How Attribution Works')}
                        </h4>
                        <ul className="text-xs text-indigo-700 space-y-1">
                            <li>• {t('attribution.howPoint1', 'POS is the single source of truth')}</li>
                            <li>• {t('attribution.howPoint2', 'UTM parameters are captured on first page view')}</li>
                            <li>• {t('attribution.howPoint3', 'Attribution is linked when order completes')}</li>
                            <li>• {t('attribution.howPoint4', 'Works without GA or Meta enabled')}</li>
                        </ul>
                    </div>
                </div>
            </div>

            {/* Campaign Performance */}
            <CampaignPerformanceTable data={campaignData} loading={campaignLoading} />
        </div>
    )
}
