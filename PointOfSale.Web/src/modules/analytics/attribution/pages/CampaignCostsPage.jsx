/**
 * Campaign Cost Management Page
 * Phase 8B: Campaign Cost & ROI (MVP)
 * 
 * Route: /analytics/attribution/costs
 * 
 * FEATURES:
 * - Add / Edit / Delete campaign costs
 * - Period selector
 * - Notes field
 * - Validation against empty UTMs
 * 
 * PURE RENDERER:
 * - All data from API
 * - No calculations in frontend
 */

import { useState, useRef, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import {
    Plus, Trash2, Edit2, DollarSign, Calendar,
    ArrowLeft, RefreshCw, X, AlertCircle
} from 'lucide-react'
import { useSettings } from '../../../../contexts/SettingsContext'
import { useConfirmDialog } from '../../../../common/confirm/ConfirmDialogProvider'
import {
    useCampaignCosts,
    useCreateCampaignCost,
    useUpdateCampaignCost,
    useDeleteCampaignCost
} from '../hooks/useCampaignCost'

const PERIOD_OPTIONS = [
    { value: 'today', label: 'Today' },
    { value: 'last_7_days', label: 'Last 7 Days' },
    { value: 'last_30_days', label: 'Last 30 Days' },
    { value: 'this_month', label: 'This Month' },
    { value: 'last_month', label: 'Last Month' },
]

export default function CampaignCostsPage() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const { formatCurrency } = useSettings()
    const [period, setPeriod] = useState('last_30_days')
    const [showModal, setShowModal] = useState(false)
    const [editingCost, setEditingCost] = useState(null)
    const [mutationMessage, setMutationMessage] = useState(null)
    const busy = useRef(false)
    const lifetime = useRef(0)
    useEffect(() => () => { lifetime.current++ }, [])

    const { data, loading, error, refetch } = useCampaignCosts(period)
    const { createCost, loading: creating } = useCreateCampaignCost()
    const { updateCost, loading: updating } = useUpdateCampaignCost()
    const { deleteCost, loading: deleting } = useDeleteCampaignCost()

    const handleCreate = () => {
        setEditingCost(null)
        setShowModal(true)
    }

    const handleEdit = (cost) => {
        setEditingCost(cost)
        setShowModal(true)
    }

    const handleDelete = async (id) => {
        if (busy.current) return
        busy.current = true
        const scope = lifetime.current
        try {
        const confirmed = await confirm({
            confirmLabel: t('common.delete', 'Delete'),
            description: t('campaignCost.deleteConfirm', 'Are you sure you want to delete this campaign cost?'),
            title: t('common.warning', 'Warning'),
            variant: 'danger',
        })

        if (!confirmed || scope !== lifetime.current) {
            return
        }

            await deleteCost(id)
            if (scope !== lifetime.current) return
            const readBack = await refetch()
            if (scope === lifetime.current) setMutationMessage(readBack ? t('common.deletedSuccessfully', 'Deleted and verified') : t('campaignCost.savedReadFailed', 'Saved, but the list could not be reloaded. Refresh to verify.'))
        } catch (err) {
            if (scope === lifetime.current) setMutationMessage(err.message)
        } finally {
            busy.current = false
        }
    }

    const handleModalSubmit = async (formData) => {
        if (busy.current) return
        busy.current = true
        const scope = lifetime.current
        try {
            if (editingCost) await updateCost(editingCost.campaignCostId, formData, editingCost.updatedAtUtc)
            else await createCost(formData)
            if (scope !== lifetime.current) return
            setShowModal(false)
            const readBack = await refetch()
            if (scope === lifetime.current) setMutationMessage(readBack ? t('campaignCost.savedVerified', 'Campaign cost saved and verified') : t('campaignCost.savedReadFailed', 'Saved, but the list could not be reloaded. Refresh to verify.'))
        } catch (err) {
            if (scope === lifetime.current) setMutationMessage(err.message)
        } finally {
            busy.current = false
        }
    }

    const formatDate = (dateStr) => {
        if (!dateStr) return '-'
        return new Date(dateStr).toLocaleDateString()
    }

    return (
        <div className="p-6 max-w-7xl mx-auto">
            {/* Header */}
            <div className="mb-6">
                <Link
                    to="/analytics/attribution"
                    className="inline-flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4"
                >
                    <ArrowLeft size={16} />
                    {t('campaignCost.backToAttribution', 'Back to Attribution')}
                </Link>
                <div className="flex flex-wrap gap-4 items-center justify-between">
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('campaignCost.title', 'Campaign Costs')}
                        </h1>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('campaignCost.subtitle', 'Manage marketing spend for ROI calculations')}
                        </p>
                    </div>
                    <button
                        onClick={handleCreate}
                        className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors"
                    >
                        <Plus size={18} />
                        {t('campaignCost.addCost', 'Add Cost')}
                    </button>
                </div>
            </div>

            {/* Controls */}
            <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-4">
                    <select
                        value={period}
                        onChange={(e) => setPeriod(e.target.value)}
                        className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-indigo-500"
                    >
                        {PERIOD_OPTIONS.map(opt => (
                            <option key={opt.value} value={opt.value}>
                                {t(`campaignCost.periods.${opt.value}`, opt.label)}
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

            {/* Summary Card */}
            {data && (
                <div className="bg-gradient-to-br from-indigo-500 to-purple-600 rounded-xl p-6 text-white mb-6">
                    <div className="flex items-center gap-3 mb-2">
                        <DollarSign size={24} />
                        <span className="text-lg font-medium">
                            {t('campaignCost.totalSpend', 'Total Marketing Spend')}
                        </span>
                    </div>
                    <div className="text-3xl font-bold">
                        {data.totalCost == null ? t('campaignCost.incompatibleCurrency', 'Unavailable: incompatible currency') : formatCurrency(data.totalCost)}
                    </div>
                    <div className="mt-1 text-indigo-100 text-sm">
                        {data.totalCount} {t('campaignCost.entries', 'entries')}
                    </div>
                </div>
            )}

            {/* Error State */}
            {mutationMessage && <div role="status" className="mb-4 rounded border p-4">{mutationMessage}</div>}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-lg p-4 mb-6">
                    <div className="flex items-center gap-2 text-red-700">
                        <AlertCircle size={18} />
                        {error}
                    </div>
                </div>
            )}

            {/* Loading State */}
            {loading && !data && (
                <div className="bg-white rounded-xl border border-gray-200 p-12 text-center">
                    <RefreshCw size={32} className="animate-spin mx-auto text-gray-400 mb-4" />
                    <p className="text-gray-500">{t('common.loading', 'Loading...')}</p>
                </div>
            )}

            {/* Costs Table */}
            {data && (
                <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
                    <div className="overflow-x-auto">
                        <table className="w-full">
                            <thead>
                                <tr className="bg-gray-50 border-b border-gray-100">
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('campaignCost.campaign', 'Campaign')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('campaignCost.sourceMedium', 'Source / Medium')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('campaignCost.period', 'Period')}
                                    </th>
                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                                        {t('campaignCost.cost', 'Cost')}
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                                        {t('campaignCost.notes', 'Notes')}
                                    </th>
                                    <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">
                                        {t('common.actions', 'Actions')}
                                    </th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {data.costs.length === 0 ? (
                                    <tr>
                                        <td colSpan={6} className="px-4 py-12 text-center">
                                            <DollarSign size={32} className="mx-auto text-gray-300 mb-3" />
                                            <p className="text-gray-500">
                                                {t('campaignCost.noCosts', 'No campaign costs recorded yet')}
                                            </p>
                                            <p className="text-sm text-gray-400 mt-1">
                                                {t('campaignCost.noCostsHint', 'Add your marketing spend to calculate ROI')}
                                            </p>
                                        </td>
                                    </tr>
                                ) : (
                                    data.costs.map((cost) => (
                                        <tr key={cost.campaignCostId} className="hover:bg-gray-50">
                                            <td className="px-4 py-3">
                                                <div className="flex items-center gap-2">
                                                    <div className="w-2 h-2 rounded-full bg-indigo-500" />
                                                    <span className="font-medium text-gray-900">
                                                        {cost.campaign || '-'}
                                                    </span>
                                                </div>
                                            </td>
                                            <td className="px-4 py-3 text-sm text-gray-600">
                                                {cost.source} / {cost.medium}
                                            </td>
                                            <td className="px-4 py-3 text-sm text-gray-600">
                                                <div className="flex items-center gap-1">
                                                    <Calendar size={14} className="text-gray-400" />
                                                    {formatDate(cost.periodStartUtc)} - {formatDate(cost.periodEndUtc)}
                                                </div>
                                            </td>
                                            <td className="px-4 py-3 text-right font-semibold text-gray-900">
                                    {Number(cost.costAmount).toFixed(2)} {cost.currency}
                                            </td>
                                            <td className="px-4 py-3 text-sm text-gray-500 max-w-xs truncate">
                                                {cost.notes || '-'}
                                            </td>
                                            <td className="px-4 py-3">
                                                <div className="flex items-center justify-center gap-2">
                                                    <button
                                                        onClick={() => handleEdit(cost)}
                                                        className="p-1 text-gray-400 hover:text-indigo-600"
                                                        title={t('common.edit', 'Edit')}
                                                    >
                                                        <Edit2 size={16} />
                                                    </button>
                                                    <button
                                                        onClick={() => handleDelete(cost.campaignCostId)}
                                                        disabled={deleting}
                                                        className="p-1 text-gray-400 hover:text-red-600"
                                                        title={t('common.delete', 'Delete')}
                                                    >
                                                        <Trash2 size={16} />
                                                    </button>
                                                </div>
                                            </td>
                                        </tr>
                                    ))
                                )}
                            </tbody>
                        </table>
                    </div>
                </div>
            )}

            {/* Create/Edit Modal */}
            {showModal && (
                <CampaignCostModal
                    cost={editingCost}
                    onClose={() => setShowModal(false)}
                    onSubmit={handleModalSubmit}
                    loading={creating || updating}
                />
            )}
        </div>
    )
}

function CampaignCostModal({ cost, onClose, onSubmit, loading }) {
    const { t } = useTranslation()
    const [formData, setFormData] = useState({
        source: cost?.source || '',
        medium: cost?.medium || '',
        campaign: cost?.campaign || '',
        periodStartUtc: cost?.periodStartUtc?.split('T')[0] || new Date().toISOString().split('T')[0],
        periodEndUtc: cost?.periodEndUtc?.split('T')[0] || new Date(Date.now() + 86400000).toISOString().split('T')[0],
        costAmount: cost?.costAmount ?? '',
        notes: cost?.notes || ''
    })
    const [errors, setErrors] = useState({})

    const validate = () => {
        const newErrors = {}
        if (!formData.source.trim()) newErrors.source = t('campaignCost.errors.sourceRequired', 'Source is required')
        if (!formData.medium.trim()) newErrors.medium = t('campaignCost.errors.mediumRequired', 'Medium is required')
        if (!formData.campaign.trim()) newErrors.campaign = t('campaignCost.errors.campaignRequired', 'Campaign is required')
        if (formData.costAmount === '' || !Number.isFinite(Number(formData.costAmount)) || Number(formData.costAmount) < 0) newErrors.costAmount = t('campaignCost.errors.costRequired', 'Valid cost amount is required')
        if (!formData.periodStartUtc || !formData.periodEndUtc || new Date(formData.periodEndUtc) <= new Date(formData.periodStartUtc)) {
            newErrors.periodEndUtc = t('campaignCost.errors.invalidDates', 'End date must be after start date')
        }
        setErrors(newErrors)
        return Object.keys(newErrors).length === 0
    }

    const handleSubmit = (e) => {
        e.preventDefault()
        if (loading || !validate()) return
        onSubmit({
            ...formData,
            periodStartUtc: cost?.periodStartUtc?.startsWith(formData.periodStartUtc) ? cost.periodStartUtc : new Date(formData.periodStartUtc).toISOString(),
            periodEndUtc: cost?.periodEndUtc?.startsWith(formData.periodEndUtc) ? cost.periodEndUtc : new Date(formData.periodEndUtc).toISOString(),
            costAmount: parseFloat(formData.costAmount)
        })
    }

    return (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
            <div role="dialog" aria-modal="true" aria-label={t('campaignCost.title', 'Campaign Costs')} className="bg-white rounded-xl shadow-xl w-full max-w-lg mx-4 max-h-[90vh] overflow-y-auto">
                <div className="flex items-center justify-between p-4 border-b">
                    <h2 className="text-lg font-semibold text-gray-900">
                        {cost
                            ? t('campaignCost.editCost', 'Edit Campaign Cost')
                            : t('campaignCost.addCost', 'Add Campaign Cost')
                        }
                    </h2>
                    <button disabled={loading} aria-label={t('common.close', 'Close')} onClick={onClose} className="text-gray-400 hover:text-gray-600">
                        <X size={20} />
                    </button>
                </div>

                <form onSubmit={handleSubmit} className="p-4 space-y-4">
                  <fieldset disabled={loading} className="space-y-4 min-w-0">
                    {/* UTM Fields */}
                    <div className="grid grid-cols-3 gap-3">
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('campaignCost.source', 'Source')} *
                            </label>
                            <input
                                aria-label={t('campaignCost.source', 'Source')} maxLength={100}
                                type="text"
                                value={formData.source}
                                onChange={(e) => setFormData({ ...formData, source: e.target.value })}
                                placeholder="facebook"
                                className={`w-full px-3 py-2 border rounded-lg text-sm ${errors.source ? 'border-red-500' : 'border-gray-300'}`}
                            />
                            {errors.source && <p className="text-red-500 text-xs mt-1">{errors.source}</p>}
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('campaignCost.medium', 'Medium')} *
                            </label>
                            <input
                                aria-label={t('campaignCost.medium', 'Medium')} maxLength={100}
                                type="text"
                                value={formData.medium}
                                onChange={(e) => setFormData({ ...formData, medium: e.target.value })}
                                placeholder="cpc"
                                className={`w-full px-3 py-2 border rounded-lg text-sm ${errors.medium ? 'border-red-500' : 'border-gray-300'}`}
                            />
                            {errors.medium && <p className="text-red-500 text-xs mt-1">{errors.medium}</p>}
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('campaignCost.campaign', 'Campaign')} *
                            </label>
                            <input
                                aria-label={t('campaignCost.campaign', 'Campaign')} maxLength={200}
                                type="text"
                                value={formData.campaign}
                                onChange={(e) => setFormData({ ...formData, campaign: e.target.value })}
                                placeholder="ramadan_sale"
                                className={`w-full px-3 py-2 border rounded-lg text-sm ${errors.campaign ? 'border-red-500' : 'border-gray-300'}`}
                            />
                            {errors.campaign && <p className="text-red-500 text-xs mt-1">{errors.campaign}</p>}
                        </div>
                    </div>

                    {/* Period */}
                    <div className="grid grid-cols-2 gap-3">
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('campaignCost.periodStart', 'Period Start')} *
                            </label>
                            <input
                                aria-label={t('campaignCost.periodStart', 'Period Start')}
                                type="date"
                                value={formData.periodStartUtc}
                                onChange={(e) => setFormData({ ...formData, periodStartUtc: e.target.value })}
                                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                {t('campaignCost.periodEnd', 'Period End')} *
                            </label>
                            <input
                                aria-label={t('campaignCost.periodEnd', 'Period End')}
                                type="date"
                                value={formData.periodEndUtc}
                                onChange={(e) => setFormData({ ...formData, periodEndUtc: e.target.value })}
                                className={`w-full px-3 py-2 border rounded-lg text-sm ${errors.periodEndUtc ? 'border-red-500' : 'border-gray-300'}`}
                            />
                            {errors.periodEndUtc && <p className="text-red-500 text-xs mt-1">{errors.periodEndUtc}</p>}
                        </div>
                    </div>

                    {/* Cost Amount */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('campaignCost.costAmount', 'Cost Amount')} *
                        </label>
                        <div className="relative">
                            <input
                                aria-label={t('campaignCost.costAmount', 'Cost Amount')}
                                type="number"
                                step="0.01"
                                min="0"
                                value={formData.costAmount}
                                onChange={(e) => setFormData({ ...formData, costAmount: e.target.value })}
                                placeholder="0.00"
                                className={`w-full pl-8 pr-3 py-2 border rounded-lg text-sm ${errors.costAmount ? 'border-red-500' : 'border-gray-300'}`}
                            />
                        </div>
                        {errors.costAmount && <p className="text-red-500 text-xs mt-1">{errors.costAmount}</p>}
                    </div>

                    {/* Notes */}
                    <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                            {t('campaignCost.notes', 'Notes')}
                        </label>
                        <textarea
                            aria-label={t('campaignCost.notes', 'Notes')} maxLength={500}
                            value={formData.notes}
                            onChange={(e) => setFormData({ ...formData, notes: e.target.value })}
                            placeholder={t('campaignCost.notesPlaceholder', 'Optional notes about this campaign cost...')}
                            rows={3}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm"
                        />
                    </div>

                    {/* Actions */}
                    <div className="flex justify-end gap-3 pt-4 border-t">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-4 py-2 border border-gray-300 rounded-lg text-sm hover:bg-gray-50"
                        >
                            {t('common.cancel', 'Cancel')}
                        </button>
                        <button
                            type="submit"
                            disabled={loading}
                            className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm hover:bg-indigo-700 disabled:opacity-50"
                        >
                            {loading ? t('common.saving', 'Saving...') : t('common.save', 'Save')}
                        </button>
                    </div>
                  </fieldset>
                </form>
            </div>
        </div>
    )
}
