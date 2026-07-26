/**
 * Campaign ROI Table Component
 * Phase 8B: Campaign Cost & ROI (MVP)
 * 
 * Displays campaign ROI data in a sortable table with:
 * - Cost, Revenue, Profit columns
 * - ROI percentage with color coding
 * - Cost per Acquisition
 * 
 * PURE RENDERER:
 * - All calculations from backend
 * - NO ROI calculations in frontend
 * - Color rules: Green (positive), Red (negative), Gray (neutral)
 */

import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronUp, ChevronDown, TrendingUp, TrendingDown, Minus, Target } from 'lucide-react'
import { useSettings } from '../../../../contexts/SettingsContext'

export default function CampaignRoiTable({ data, loading }) {
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
                    {t('roi.campaignRoi', 'Campaign ROI')}
                </h3>
                <div className="text-center py-12">
                    <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4">
                        <Target className="text-gray-400" size={28} />
                    </div>
                    <p className="text-gray-500">
                        {t('roi.noCampaigns', 'No campaign data yet')}
                    </p>
                    <p className="text-sm text-gray-400 mt-1">
                        {t('roi.noCampaignsHint', 'Add campaign costs and get orders to see ROI')}
                    </p>
                </div>
            </div>
        )
    }

    // Sort campaigns (UI-only)
    const sortedCampaigns = [...campaigns].sort((a, b) => {
        const aVal = a[sortField] ?? 0
        const bVal = b[sortField] ?? 0
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

    const getRoiDisplay = (campaign) => {
        if (campaign.roiPercentage === null || campaign.roiPercentage === undefined) {
            return {
                value: '-',
                color: 'text-gray-400',
                icon: Minus,
                bgColor: 'bg-gray-100'
            }
        }
        if (campaign.roiPercentage > 0) {
            return {
                value: `+${campaign.roiPercentage.toFixed(1)}%`,
                color: 'text-green-600',
                icon: TrendingUp,
                bgColor: 'bg-green-100'
            }
        }
        if (campaign.roiPercentage < 0) {
            return {
                value: `${campaign.roiPercentage.toFixed(1)}%`,
                color: 'text-red-600',
                icon: TrendingDown,
                bgColor: 'bg-red-100'
            }
        }
        return {
            value: '0%',
            color: 'text-gray-500',
            icon: Minus,
            bgColor: 'bg-gray-100'
        }
    }

    const columns = [
        { key: 'campaign', label: t('roi.campaign', 'Campaign'), sortable: true },
        { key: 'source', label: t('roi.source', 'Source'), sortable: false },
        { key: 'cost', label: t('roi.cost', 'Cost'), sortable: true, align: 'right' },
        { key: 'revenue', label: t('roi.revenue', 'Revenue'), sortable: true, align: 'right' },
        { key: 'profit', label: t('roi.profit', 'Profit'), sortable: true, align: 'right' },
        { key: 'orders', label: t('roi.orders', 'Orders'), sortable: true, align: 'right' },
        { key: 'roiPercentage', label: t('roi.roi', 'ROI'), sortable: true, align: 'center' },
    ]

    return (
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <div className="p-6 border-b border-gray-100">
                <h3 className="text-lg font-semibold text-gray-900">
                    {t('roi.campaignRoi', 'Campaign ROI')}
                </h3>
                <p className="text-sm text-gray-500 mt-1">
                    {t('roi.campaignRoiDescription', 'Revenue and profit per campaign based on declared costs')}
                </p>
            </div>

            <div className="overflow-x-auto">
                <table className="w-full">
                    <thead>
                        <tr className="bg-gray-50 border-b border-gray-100">
                            {columns.map(col => (
                                <th
                                    key={col.key}
                                    className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider ${col.align === 'right' ? 'text-right' : col.align === 'center' ? 'text-center' : 'text-left'} ${col.sortable ? 'cursor-pointer hover:text-gray-700' : ''}`}
                                    onClick={() => col.sortable && handleSort(col.key)}
                                >
                                    {col.label}
                                    {col.sortable && <SortIcon field={col.key} />}
                                </th>
                            ))}
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                        {sortedCampaigns.map((campaign, idx) => {
                            const roiDisplay = getRoiDisplay(campaign)
                            const RoiIcon = roiDisplay.icon

                            return (
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
                                        {campaign.source} / {campaign.medium || '-'}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-600 text-right">
                                        {campaign.cost > 0 ? formatCurrency(campaign.cost) : '-'}
                                    </td>
                                    <td className="px-4 py-3 text-sm font-semibold text-gray-900 text-right">
                                        {formatCurrency(campaign.revenue)}
                                    </td>
                                    <td className={`px-4 py-3 text-sm font-medium text-right ${campaign.profit > 0 ? 'text-green-600' :
                                            campaign.profit < 0 ? 'text-red-600' : 'text-gray-500'
                                        }`}>
                                        {campaign.profit >= 0 ? '+' : ''}{formatCurrency(campaign.profit)}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-600 text-right">
                                        {campaign.orders}
                                    </td>
                                    <td className="px-4 py-3 text-center">
                                        <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-medium ${roiDisplay.bgColor} ${roiDisplay.color}`}>
                                            <RoiIcon size={12} />
                                            {roiDisplay.value}
                                        </span>
                                    </td>
                                </tr>
                            )
                        })}
                    </tbody>
                </table>
            </div>

            {/* Summary */}
            <div className="p-4 bg-gray-50 border-t border-gray-100">
                <div className="flex justify-between items-center">
                    <span className="text-sm text-gray-500">
                        {t('roi.totalCampaigns', 'Total Campaigns')}: {campaigns.length}
                    </span>
                    <div className="flex gap-6">
                        <span className="text-sm text-gray-500">
                            {t('roi.totalCost', 'Total Cost')}: {formatCurrency(data?.totalCost || 0)}
                        </span>
                        <span className="text-sm text-gray-500">
                            {t('roi.totalRevenue', 'Total Revenue')}: {formatCurrency(data?.totalRevenue || 0)}
                        </span>
                        <span className={`text-sm font-medium ${(data?.totalProfit || 0) > 0 ? 'text-green-600' :
                                (data?.totalProfit || 0) < 0 ? 'text-red-600' : 'text-gray-500'
                            }`}>
                            {t('roi.totalProfit', 'Total Profit')}: {formatCurrency(data?.totalProfit || 0)}
                        </span>
                    </div>
                </div>
            </div>
        </div>
    )
}
