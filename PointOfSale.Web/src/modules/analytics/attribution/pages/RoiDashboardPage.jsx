/**
 * ROI Dashboard Page
 * Phase 8B: Campaign Cost & ROI (MVP)
 * 
 * Route: /analytics/attribution/roi
 * 
 * FEATURES:
 * - ROI summary cards
 * - Campaign ROI table
 * - Period and model selectors
 * - Link to manage campaign costs
 * 
 * PURE RENDERER:
 * - All data from API
 * - No ROI calculations in frontend
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import {
    ArrowLeft, RefreshCw, Settings, TrendingUp,
    AlertCircle, Lightbulb
} from 'lucide-react'
import { useRoiByCampaign } from '../hooks/useCampaignCost'
import RoiSummaryCards from '../components/RoiSummaryCards'
import CampaignRoiTable from '../components/CampaignRoiTable'

const PERIOD_OPTIONS = [
    { value: 'today', label: 'Today' },
    { value: 'last_7_days', label: 'Last 7 Days' },
    { value: 'last_30_days', label: 'Last 30 Days' },
    { value: 'this_month', label: 'This Month' },
    { value: 'last_month', label: 'Last Month' },
]

const MODEL_OPTIONS = [
    { value: 'FirstTouch', label: 'First Touch' },
    { value: 'LastTouch', label: 'Last Touch' },
]

export default function RoiDashboardPage() {
    const { t } = useTranslation()
    const [period, setPeriod] = useState('last_30_days')
    const [model, setModel] = useState('LastTouch')

    const { data, loading, error, refetch } = useRoiByCampaign(model, period)

    return (
        <div className="p-6 max-w-7xl mx-auto">
            {/* Header */}
            <div className="mb-6">
                <Link
                    to="/analytics/attribution"
                    className="inline-flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4"
                >
                    <ArrowLeft size={16} />
                    {t('roi.backToAttribution', 'Back to Attribution')}
                </Link>
                <div className="flex flex-wrap gap-4 items-center justify-between">
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('roi.title', 'Marketing ROI')}
                        </h1>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('roi.subtitle', 'See which campaigns make you money')}
                        </p>
                    </div>
                    <Link
                        to="/analytics/attribution/costs"
                        className="flex items-center gap-2 px-4 py-2 border border-gray-300 rounded-lg text-sm hover:bg-gray-50 transition-colors"
                    >
                        <Settings size={16} />
                        {t('roi.manageCosts', 'Manage Costs')}
                    </Link>
                </div>
            </div>

            {/* Controls */}
            <div className="flex flex-wrap gap-3 items-center justify-between mb-6">
                <div className="flex flex-wrap items-center gap-4">
                    <select
                        value={period}
                        onChange={(e) => setPeriod(e.target.value)}
                        className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500"
                    >
                        {PERIOD_OPTIONS.map(opt => (
                            <option key={opt.value} value={opt.value}>
                                {t(`roi.periods.${opt.value}`, opt.label)}
                            </option>
                        ))}
                    </select>
                    <select
                        value={model}
                        onChange={(e) => setModel(e.target.value)}
                        className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500"
                    >
                        {MODEL_OPTIONS.map(opt => (
                            <option key={opt.value} value={opt.value}>
                                {t(`roi.models.${opt.value}`, opt.label)}
                            </option>
                        ))}
                    </select>
                </div>
                <button
                    onClick={refetch}
                    disabled={loading}
                    className="flex items-center gap-1 text-sm text-gray-600 hover:text-gray-800"
                >
                    <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
                    {t('common.refresh', 'Refresh')}
                </button>
            </div>

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4 mb-6">
                    <div className="flex items-center gap-2 text-red-700">
                        <AlertCircle size={18} />
                        {error}
                    </div>
                </div>
            )}

            {/* ROI Summary Cards */}
            <div className="mb-6">
                <RoiSummaryCards data={data} loading={loading} />
            </div>

            {/* Campaign ROI Table */}
            <div className="mb-6">
                <CampaignRoiTable data={data} loading={loading} />
            </div>

            {/* How ROI Works */}
            <div className="bg-gradient-to-br from-indigo-50 to-purple-50 rounded-xl p-6 border border-indigo-100">
                <div className="flex items-start gap-3">
                    <div className="w-10 h-10 bg-indigo-100 rounded-lg flex items-center justify-center flex-shrink-0">
                        <Lightbulb className="text-indigo-600" size={20} />
                    </div>
                    <div>
                        <h3 className="font-semibold text-gray-900 mb-2">
                            {t('roi.howItWorks', 'How ROI is Calculated')}
                        </h3>
                        <ul className="text-sm text-gray-600 space-y-2">
                            <li className="flex items-start gap-2">
                                <TrendingUp size={14} className="text-indigo-500 mt-0.5 flex-shrink-0" />
                                <span>
                                    {t('roi.howPoint1', 'Revenue is calculated from orders attributed to each campaign')}
                                </span>
                            </li>
                            <li className="flex items-start gap-2">
                                <TrendingUp size={14} className="text-indigo-500 mt-0.5 flex-shrink-0" />
                                <span>
                                    {t('roi.howPoint2', 'Cost is taken from your declared campaign spending')}
                                </span>
                            </li>
                            <li className="flex items-start gap-2">
                                <TrendingUp size={14} className="text-indigo-500 mt-0.5 flex-shrink-0" />
                                <span>
                                    {t('roi.howPoint3', 'Profit = Revenue - Cost')}
                                </span>
                            </li>
                            <li className="flex items-start gap-2">
                                <TrendingUp size={14} className="text-indigo-500 mt-0.5 flex-shrink-0" />
                                <span>
                                    {t('roi.howPoint4', 'ROI% = (Profit / Cost) × 100')}
                                </span>
                            </li>
                            <li className="flex items-start gap-2">
                                <TrendingUp size={14} className="text-indigo-500 mt-0.5 flex-shrink-0" />
                                <span>
                                    {t('roi.howPoint5', 'CPA = Cost / Orders (cost per acquisition)')}
                                </span>
                            </li>
                        </ul>
                        <div className="mt-4 p-3 bg-white/50 rounded-lg text-sm text-indigo-700">
                            💡 {t('roi.tip', 'Tip: Add campaign costs to see accurate ROI. Without declared costs, you\'ll only see revenue.')}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    )
}
