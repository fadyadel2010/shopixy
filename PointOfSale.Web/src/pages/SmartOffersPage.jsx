import { useTranslation } from 'react-i18next'
import { useEffect, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import {
    archiveAdminSmartOffer,
    createAdminSmartOffer,
    getAdminSmartOffer,
    getAdminSmartOfferStats,
    listAdminSmartOffers,
    updateAdminSmartOffer,
} from '../services/adminSmartOffersApi'
import { useAuthStore } from '../store/authStore'
import SmartOfferEditor from './SmartOfferEditor'
import { useOfferOperations } from '../hooks/useOfferOperations'
import { useRTL } from '../hooks/useRTL'

const initialFilter = {
    search: '',
    status: 'All',
    sortBy: 'priority',
    sortDirection: 'desc',
    page: 1,
    pageSize: 100,
}

function formatOfferStatus(status, t) {
    switch (status) {
        case 'Active':
            return t('SmartOffersPage.status.active')
        case 'Inactive':
            return t('SmartOffersPage.status.inactive')
        default:
            return status
    }
}

function formatTriggerLabel(triggerType, t) {
    return t(`SmartOffersPage.triggerTypes.${triggerType}`, { defaultValue: triggerType })
}

function formatPageScopeLabel(pageScope, t) {
    return t(`SmartOffersPage.pageScopes.${pageScope}`, { defaultValue: pageScope })
}

export default function SmartOffersPage() {
    const { t } = useTranslation()
    const { confirm } = useConfirmDialog()
    const user = useAuthStore((state) => state.user)
    const { busy, beginRead, beginMutation } = useOfferOperations(user)
    const { dir } = useRTL()
    const [offers, setOffers] = useState([])
    const [stats, setStats] = useState(null)
    const [statsError, setStatsError] = useState(false)
    const [filter, setFilter] = useState(initialFilter)
    const [loading, setLoading] = useState(true)
    const [listError, setListError] = useState(false)
    const [detailsLoading, setDetailsLoading] = useState(false)
    const [saving, setSaving] = useState(false)
    const [editorOpen, setEditorOpen] = useState(false)
    const [editingOffer, setEditingOffer] = useState(null)
    const [selectedOffer, setSelectedOffer] = useState(null)
    const [error, setError] = useState('')
    const [editorError, setEditorError] = useState('')
    const [banner, setBanner] = useState('')

    useEffect(() => {
        loadOffers()
        loadStats()
    }, [filter, user])

    const loadOffers = async () => {
        const current = beginRead('list')
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setOffers([])
            setStats(null)
            setLoading(false)
            return
        }

        try {
            setLoading(true)
            setListError(false)
            setError('')
            const admittedOffers = await listAdminSmartOffers(filter, user)
            if (!current()) return false
            setOffers(admittedOffers)
            return true
        } catch (requestError) {
            if (!current()) return false
            setOffers([])
            setListError(true)
            setError(requestError.response?.data?.message || t('SmartOffersPage.messages.loadError'))
            return false
        } finally {
            if (current()) setLoading(false)
        }
    }

    const loadStats = async () => {
        const current = beginRead('stats')
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setStats(null)
            return
        }

        try {
            setStatsError(false)
            const admittedStats = await getAdminSmartOfferStats(user)
            if (!current()) return
            setStats({
                totalOffers: admittedStats.totalOffers ?? admittedStats.TotalOffers ?? 0,
                activeOffers: admittedStats.activeOffers ?? admittedStats.ActiveOffers ?? 0,
                exitIntentOffers: admittedStats.exitIntentOffers ?? admittedStats.ExitIntentOffers ?? 0,
            })
        } catch {
            if (current()) { setStats(null); setStatsError(true) }
        }
    }

    const openCreate = () => {
        setEditingOffer(null)
        setEditorError('')
        setEditorOpen(true)
    }

    const openEdit = (offer) => {
        setEditingOffer(offer)
        setEditorError('')
        setEditorOpen(true)
    }

    const openDetails = async (offer) => {
        const current = beginRead('details')
        const offerId = offer?.id ?? offer?.Id
        if (!offerId || !user?.tenantId || !(user?.id || user?.userId)) {
            setSelectedOffer(null)
            setError(t('SmartOffersPage.messages.loadError'))
            return
        }

        try {
            setDetailsLoading(true)
            setError('')
            const admittedOffer = await getAdminSmartOffer(offerId, user)
            if (current()) setSelectedOffer(admittedOffer)
        } catch (requestError) {
            if (!current()) return
            setSelectedOffer(null)
            setError(requestError.response?.data?.message || t('SmartOffersPage.messages.loadError'))
        } finally {
            if (current()) setDetailsLoading(false)
        }
    }

    const closeEditor = (force = false) => {
        if (saving && !force) {
            return
        }

        setEditorOpen(false)
        setEditingOffer(null)
        setEditorError('')
    }

    const handleSave = async (payload) => {
        const operation = beginMutation()
        if (!operation) return
        try {
            setSaving(true)
            setEditorError('')

            if (editingOffer) {
                await updateAdminSmartOffer(editingOffer, payload, user)
            } else {
                await createAdminSmartOffer(payload, user)
            }

            if (!operation.current()) return
            closeEditor(true)
            setBanner('')
            if (await loadOffers()) setBanner(t(editingOffer ? 'SmartOffersPage.messages.updateSuccess' : 'SmartOffersPage.messages.createSuccess'))
            else setError(t('offers.savedReloadFailed'))
            await loadStats()
        } catch (requestError) {
            if (operation.current()) setEditorError(requestError.response?.data?.message || t('SmartOffersPage.messages.saveError'))
        } finally {
            if (operation.current()) setSaving(false)
            operation.finish()
        }
    }

    const handleSort = (sortBy) => {
        setFilter((current) => ({
            ...current,
            sortBy,
            sortDirection: current.sortBy === sortBy && current.sortDirection === 'asc' ? 'desc' : 'asc',
            page: 1,
        }))
    }

    const handleDelete = async (offer) => {
        const operation = beginMutation()
        if (!operation) return
        try {
        const confirmed = await confirm({
            confirmLabel: t('SmartOffersPage.actions.delete'),
            description: t('SmartOffersPage.messages.deleteConfirm', { name: offer.name }),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed || !operation.current()) {
            return
        }

        try {
            await archiveAdminSmartOffer(offer)
            if (!operation.current()) return
            setBanner('')
            if (await loadOffers()) setBanner(t('SmartOffersPage.messages.deleteSuccess'))
            else setError(t('offers.savedReloadFailed'))
            await loadStats()
        } catch (requestError) {
            if (operation.current()) setError(requestError.response?.data?.message || t('SmartOffersPage.messages.deleteError'))
        }
        } finally { operation.finish() }
    }

    const handleToggleStatus = async (offer) => {
        const operation = beginMutation()
        if (!operation) return
        try {
            const nextStatus = offer.status === 'Active' ? 'Inactive' : 'Active'
            await updateAdminSmartOffer(offer, {
                ...offer,
                status: nextStatus,
            }, user)
            if (!operation.current()) return
            setBanner('')
            const loaded = await loadOffers()
            if (loaded) setBanner(
                t('SmartOffersPage.messages.statusUpdated', {
                    name: offer.name,
                    status: formatOfferStatus(nextStatus, t).toLowerCase(),
                }),
            )
            else setError(t('offers.savedReloadFailed'))
            await loadStats()
        } catch (requestError) {
            if (operation.current()) setError(requestError.response?.data?.message || t('SmartOffersPage.messages.statusUpdateError'))
        } finally { operation.finish() }
    }

    return (
        <div dir={dir} className="space-y-6">
            <div className="mx-auto max-w-7xl space-y-6">
                <section>
                    <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                        <div className="max-w-2xl">
                            <h1 className="text-2xl font-bold text-gray-900">{t('SmartOffersPage.hero.title')}</h1>
                            <p className="mt-1 text-sm leading-6 text-gray-500">{t('SmartOffersPage.hero.description')}</p>
                        </div>
                        <button
                            type="button"
                            disabled={busy} onClick={openCreate}
                            className="btn-primary shrink-0 disabled:opacity-50"
                        >{t('SmartOffersPage.actions.createOffer')}</button>
                    </div>
                </section>

                <section className="grid gap-4 sm:grid-cols-3">
                    <StatCard label={t('SmartOffersPage.stats.totalOffers')} value={stats?.totalOffers} />
                    <StatCard label={t('SmartOffersPage.stats.activeOffers')} value={stats?.activeOffers} />
                    <StatCard label={t('SmartOffersPage.stats.exitIntent')} value={stats?.exitIntentOffers} />
                </section>
                {statsError && <p role="status" className="text-sm text-red-700">{t('SmartOffersPage.messages.statsError')}</p>}

                <section className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm sm:p-6">
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                        <div className="grid gap-4 sm:grid-cols-2">
                            <input
                                value={filter.search}
                                aria-label={t('SmartOffersPage.filters.searchPlaceholder')}
                                onChange={(event) => setFilter((current) => ({ ...current, search: event.target.value, page: 1 }))}
                                className="input bg-white text-gray-900"
                                placeholder={t('SmartOffersPage.filters.searchPlaceholder')}
                            />
                            <select
                                value={filter.status}
                                aria-label={t('SmartOffersPage.table.status')}
                                onChange={(event) => setFilter((current) => ({ ...current, status: event.target.value, page: 1 }))}
                                className="input bg-white text-gray-900"
                            >
                                <option value="All">{t('SmartOffersPage.filters.allStatuses')}</option>
                                <option value="Active">{t('SmartOffersPage.status.active')}</option>
                                <option value="Inactive">{t('SmartOffersPage.status.inactive')}</option>
                            </select>
                        </div>
                        <button
                            type="button"
                            onClick={() => { loadOffers(); loadStats() }}
                            className="btn-secondary"
                        >{t('SmartOffersPage.actions.refresh')}</button>
                    </div>

                    {banner ? (
                        <div role="status" className="mt-5 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
                            {banner}
                        </div>
                    ) : null}

                    {error ? (
                        <div role="alert" className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                            {error}
                        </div>
                    ) : null}

                    <div className="mt-6 overflow-hidden rounded-lg border border-slate-200" aria-busy={loading}>
                        {loading ? (
                            <div className="grid gap-3 bg-slate-50 p-6">
                                {Array.from({ length: 4 }).map((_, index) => (
                                    <div key={index} className="h-16 animate-pulse rounded-2xl bg-slate-200" />
                                ))}
                            </div>
                        ) : listError ? (
                            <div className="p-6 text-center"><button type="button" onClick={() => { loadOffers(); loadStats() }} className="btn-secondary">{t('common.retry')}</button></div>
                        ) : offers.length === 0 ? (
                            <div className="bg-slate-50 px-6 py-16 text-center">
                                <h3 className="text-lg font-semibold text-slate-900">{t('SmartOffersPage.emptyState.title')}</h3>
                                <p className="mt-2 text-sm text-slate-500">{t('SmartOffersPage.emptyState.description')}</p>
                            </div>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-slate-200">
                                    <thead className="bg-slate-50">
                                        <tr className="text-start text-xs text-slate-500">
                                            <th className="px-5 py-4">
                                                <button type="button" onClick={() => handleSort('name')}>
                                                    {t('SmartOffersPage.offer')}
                                                </button>
                                            </th>
                                            <th className="px-5 py-4">{t('SmartOffersPage.table.trigger')}</th>
                                            <th className="px-5 py-4">{t('SmartOffersPage.table.pages')}</th>
                                            <th className="px-5 py-4">
                                                <button type="button" onClick={() => handleSort('priority')}>
                                                    {t('SmartOffersPage.table.priority')}
                                                </button>
                                            </th>
                                            <th className="px-5 py-4">{t('SmartOffersPage.table.frequency')}</th>
                                            <th className="px-5 py-4">{t('SmartOffersPage.table.status')}</th>
                                            <th className="px-5 py-4 text-end">{t('SmartOffersPage.table.actions')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-slate-100 bg-white">
                                        {offers.map((offer) => (
                                            <tr key={offer.id} className="align-top">
                                                <td className="px-5 py-5">
                                                    <div className="space-y-1">
                                                        <div className="font-semibold text-slate-900">{offer.name}</div>
                                                        <div className="text-sm text-slate-600">{offer.popupTitle}</div>
                                                        {offer.couponCode ? (
                                                            <div className="inline-flex rounded-full bg-orange-50 px-3 py-1 text-xs font-semibold tracking-[0.2em] text-orange-700">
                                                                {offer.couponCode}
                                                            </div>
                                                        ) : null}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-5 text-sm text-slate-700">{formatTriggerLabel(offer.triggerType, t)}</td>
                                                <td className="px-5 py-5 text-sm text-slate-700">{formatPageScopeLabel(offer.pagesScope, t)}</td>
                                                <td className="px-5 py-5 text-sm text-slate-700">{offer.priority}</td>
                                                <td className="px-5 py-5 text-sm text-slate-700">
                                                    {offer.showOncePerSession
                                                        ? t('SmartOffersPage.frequency.oncePerSession')
                                                        : t('SmartOffersPage.frequency.repeatable')}
                                                    <div className="mt-1 text-xs text-slate-500">
                                                        {t('SmartOffersPage.frequency.maxPerUser', {
                                                            value: offer.maxDisplaysPerUser ?? t('SmartOffersPage.frequency.unlimited'),
                                                        })}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-5">
                                                    <span
                                                        className={`inline-flex rounded-full px-3 py-1 text-xs font-semibold ${
                                                            offer.status === 'Active'
                                                                ? 'bg-emerald-50 text-emerald-700'
                                                                : 'bg-slate-100 text-slate-600'
                                                        }`}
                                                    >
                                                        {formatOfferStatus(offer.status, t)}
                                                    </span>
                                                </td>
                                                <td className="px-5 py-5">
                                                    <div className="flex justify-end gap-2">
                                                        <button
                                                            type="button"
                                                            onClick={() => openDetails(offer)}
                                                            className="rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                        >{t('SmartOffersPage.actions.details')}</button>
                                                        <button
                                                            type="button"
                                                            disabled={busy} onClick={() => handleToggleStatus(offer)}
                                                            className="rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                        >
                                                            {offer.status === 'Active'
                                                                ? t('SmartOffersPage.actions.disable')
                                                                : t('SmartOffersPage.actions.enable')}
                                                        </button>
                                                        <button
                                                            type="button"
                                                            disabled={busy} onClick={() => openEdit(offer)}
                                                            className="rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                        >{t('SmartOffersPage.actions.edit')}</button>
                                                        <button
                                                            type="button"
                                                            disabled={busy} onClick={() => handleDelete(offer)}
                                                            className="rounded-lg bg-rose-50 px-3 py-2 text-sm font-medium text-rose-700 transition hover:bg-rose-100"
                                                        >{t('SmartOffersPage.actions.delete')}</button>
                                                    </div>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                    <div className="mt-4 flex items-center justify-between">
                        <button type="button" className="btn-secondary disabled:opacity-50" disabled={loading || filter.page <= 1} onClick={() => setFilter(current => ({ ...current, page: current.page - 1 }))}>{t('common.previous')}</button>
                        <span>{filter.page}</span>
                        <button type="button" className="btn-secondary disabled:opacity-50" disabled={loading || offers.length < filter.pageSize} onClick={() => setFilter(current => ({ ...current, page: current.page + 1 }))}>{t('common.next')}</button>
                    </div>
                </section>
            </div>

            <SmartOfferEditor
                open={editorOpen}
                offer={editingOffer}
                saving={saving}
                error={editorError}
                onClose={closeEditor}
                onSave={handleSave}
            />

            <SmartOfferDetailsModal
                offer={selectedOffer}
                loading={detailsLoading}
                onClose={() => { beginRead('details'); setDetailsLoading(false); setSelectedOffer(null) }}
            />
        </div>
    )
}

function StatCard({ label, value }) {
    return (
        <div className="rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
            <div>
                <p className="text-sm text-slate-500">{label}</p>
                <p className="mt-2 text-2xl font-semibold text-gray-900">{value ?? '—'}</p>
            </div>
        </div>
    )
}

function SmartOfferDetailsModal({ offer, loading, onClose }) {
    const { t } = useTranslation()
    const { dir } = useRTL()
    if (!offer && !loading) {
        return null
    }

    return (
        <Dialog.Root open onOpenChange={open => { if (!open) onClose() }}>
            <Dialog.Portal>
            <Dialog.Overlay className="fixed inset-0 z-[80] bg-black/50" />
            <Dialog.Content dir={dir} aria-describedby={undefined} className="fixed left-1/2 top-1/2 z-[81] max-h-[calc(100dvh-2rem)] w-[calc(100%-2rem)] max-w-3xl -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-xl bg-white p-6 shadow-xl">
                <div className="flex items-start justify-between gap-4">
                    <div>
                        <Dialog.Title className="text-xl font-bold text-gray-900">{loading ? t('common.loading') : offer?.name}</Dialog.Title>
                    </div>
                    <button
                        type="button"
                        onClick={onClose}
                        className="btn-secondary"
                    >
                        {t('common.close')}
                    </button>
                </div>

                {loading ? (
                    <div className="mt-6 grid gap-3">
                        {Array.from({ length: 5 }).map((_, index) => (
                            <div key={index} className="h-12 animate-pulse rounded-2xl bg-slate-100" />
                        ))}
                    </div>
                ) : (
                    <div className="mt-6 grid gap-4 sm:grid-cols-2">
                        <DetailItem label={t('SmartOfferEditor.fields.status')} value={formatOfferStatus(offer.status, t)} />
                        <DetailItem label={t('SmartOfferEditor.fields.priority')} value={offer.priority} />
                        <DetailItem label={t('SmartOfferEditor.fields.triggerType')} value={formatTriggerLabel(offer.triggerType, t)} />
                        <DetailItem label={t('SmartOfferEditor.fields.pagesScope')} value={formatPageScopeLabel(offer.pagesScope, t)} />
                        <DetailItem label={t('SmartOffersPage.details.currency')} value={offer.currency} />
                        <DetailItem label={t('SmartOfferEditor.fields.couponDiscountType')} value={offer.couponDiscountType ? t(`SmartOfferEditor.options.couponDiscountType.${offer.couponDiscountType}`) : null} />
                        <DetailItem label={t('SmartOfferEditor.fields.discountValue')} value={offer.couponDiscountValue} />
                        <DetailItem label={t('SmartOffersPage.details.usage')} value={`${offer.usageCount ?? 0} / ${offer.usageLimit ?? t('SmartOffersPage.frequency.unlimited')}`} />
                        <DetailItem label={t('SmartOffersPage.details.customerLimit')} value={offer.usageLimitPerCustomer} />
                        <DetailItem label={t('SmartOffersPage.details.eligibility')} value={offer.eligibilitySummary} />
                        <DetailItem label={t('SmartOffersPage.details.created')} value={offer.createdAt ? new Date(offer.createdAt).toLocaleString() : null} />
                        <DetailItem label={t('SmartOffersPage.details.updated')} value={offer.updatedAt ? new Date(offer.updatedAt).toLocaleString() : null} />
                    </div>
                )}
            </Dialog.Content>
            </Dialog.Portal>
        </Dialog.Root>
    )
}

function DetailItem({ label, value }) {
    return (
        <div className="rounded-lg border border-gray-200 bg-gray-50 px-4 py-3">
            <p className="text-xs text-gray-500">{label}</p>
            <p className="mt-2 break-words text-sm font-semibold text-gray-900">{String(value ?? '—')}</p>
        </div>
    )
}
