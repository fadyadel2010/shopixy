/**
 * Campaign Performance Table
 * Phase 8A: Admin Attribution Dashboard UI
 * 
 * Displays campaign performance data in a sortable table
 * 
 * PURE RENDERER:
 * - All data from API
 * - AOV and % already returned by API
 * - Sorting is UI-only (frontend sort, not refetch)
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronUp, ChevronDown, Target } from 'lucide-react'
import { useSettings } from '../../../../contexts/SettingsContext'

export default function CampaignPerformanceTable({ data, loading }) {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()
    const [sortField, setSortField] = useState('revenue')
    const [sortDir, setSortDir] = useState('desc')

    if (loading) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
                <div className="p-6 border-b border-gray-100">
                    <div className="h-5 bg-gray-200 rounded w-48 animate-pulse" />
                </div>
                <div className="divide-y divide-gray-100">
                    {[1, 2, 3, 4, 5].map(i => (
                        <div key={i} className="p-4 animate-pulse">
                            <div className="flex gap-4">
                                <div className="h-4 bg-gray-200 rounded flex-1" />
                                <div className="h-4 bg-gray-200 rounded w-20" />
                                <div className="h-4 bg-gray-200 rounded w-20" />
                                <div className="h-4 bg-gray-200 rounded w-16" />
                            </div>
                        </div>
                    ))}
                </div>
            </div>
        )
    }

    const campaigns = data?.campaigns || []

    if (campaigns.length === 0) {
        return (
            <div className="bg-white rounded-xl border border-gray-200 p-6">
                <h3 className="text-lg font-semibold text-gray-900 mb-4">
                    {t('attribution.campaignPerformance', 'Campaign Performance')}
                </h3>
                <div className="text-center py-12">
                    <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <Target className="text-gray-400" size={28} />
                    </div>
                    <p className="text-gray-500">
                        {t('attribution.noCampaigns', 'No campaign data yet')}
                    </p>
                    <p className="text-sm text-gray-400 mt-1">
                        {t('attribution.noCampaignsHint', 'Orders from UTM campaigns will appear here')}
                    </p>
                </div>
            </div>
        )
    }

    // Sort campaigns (UI-only)
    const sortedCampaigns = [...campaigns].sort((a, b) => {
        const aVal = a[sortField] || 0
        const bVal = b[sortField] || 0
        return sortDir === 'desc' ? bVal - aVal : aVal - bVal
    })

    const handleSort = (field) => {
        if (sortField === field) {
            setSortDir(sortDir === 'desc' ? 'asc' : 'desc')
        } else {
            setSortField(field)
            setSortDir('desc')
        }
    }

    const SortIcon = ({ field }) => {
        if (sortField !== field) return null
        return sortDir === 'desc'
            ? <ChevronDown size={14} className="inline ml-1" />
            : <ChevronUp size={14} className="inline ml-1" />
    }

    const columns = [
        { key: 'campaign', label: t('attribution.campaign', 'Campaign'), sortable: true },
        { key: 'source', label: t('attribution.source', 'Source'), sortable: false },
        { key: 'medium', label: t('attribution.medium', 'Medium'), sortable: false },
        { key: 'revenue', label: t('attribution.revenue', 'Revenue'), sortable: true, align: 'right' },
        { key: 'orders', label: t('attribution.orders', 'Orders'), sortable: true, align: 'right' },
        { key: 'averageOrderValue', label: t('attribution.aov', 'AOV'), sortable: true, align: 'right' },
    ]

    return (
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <div className="p-6 border-b border-gray-100">
                <h3 className="text-lg font-semibold text-gray-900">
                    {t('attribution.campaignPerformance', 'Campaign Performance')}
                </h3>
            </div>

            <div className="overflow-x-auto">
                <table className="w-full">
                    <thead>
                        <tr className="bg-gray-50 border-b border-gray-100">
                            {columns.map(col => (
                                <th
                                    key={col.key}
                                    className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${col.align === 'right' ? 'text-right' : 'text-left'} ${col.sortable ? 'cursor-pointer hover:text-gray-700' : ''}`}
                                    onClick={() => col.sortable && handleSort(col.key)}
                                >
                                    {col.label}
                                    {col.sortable && <SortIcon field={col.key} />}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {sortedCampaigns.map((campaign, idx) => (
                            <tr key={idx} className="hover:bg-gray-50">
                                <td className="px-4 py-3">
                                    <div className="flex items-center gap-2">
                                        <div className="w-2 h-2 rounded-full bg-indigo-500" />
                                        <span className="font-medium text-gray-900">
                                            {campaign.campaign || '-'}
                                        </span>
                                    </div>
                                </td>
                                <td className="px-4 py-3 text-sm text-gray-600">
                                    {campaign.source}
                                </td>
                                <td className="px-4 py-3 text-sm text-gray-600">
                                    {campaign.medium || '-'}
                                </td>
                                <td className="px-4 py-3 text-sm font-semibold text-gray-900 text-right">
                                    {formatCurrency(campaign.revenue)}
                                </td>
                                <td className="px-4 py-3 text-sm text-gray-600 text-right">
                                    {campaign.orders}
                                </td>
                                <td className="px-4 py-3 text-sm text-gray-600 text-right">
                                    {formatCurrency(campaign.averageOrderValue)}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            {/* Summary */}
            <div className="p-4 bg-gray-50 border-t border-gray-100">
                <div className="flex justify-between text-sm">
                    <span className="text-gray-500">
                        {t('attribution.totalCampaigns', 'Total Campaigns')}: {campaigns.length}
                    </span>
                    <span className="text-gray-500">
                        {t('attribution.totalCampaignRevenue', 'Total Revenue')}: {formatCurrency(campaigns.reduce((sum, c) => sum + (c.revenue || 0), 0))}
                    </span>
                </div>
            </div>
        </div>
    )
}
