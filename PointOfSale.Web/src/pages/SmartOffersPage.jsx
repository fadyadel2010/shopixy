import { useTranslation } from 'react-i18next'
import { useEffect, useState } from 'react'
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

const initialFilter = {
    search: '',
    status: 'All',
    sortBy: 'priority',
    sortDirection: 'desc',
    page: 1,
    pageSize: 100,
}

const emptyState = {
    totalOffers: 0,
    activeOffers: 0,
    exitIntentOffers: 0,
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
    const [offers, setOffers] = useState([])
    const [stats, setStats] = useState(emptyState)
    const [filter, setFilter] = useState(initialFilter)
    const [loading, setLoading] = useState(true)
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
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setOffers([])
            setStats(emptyState)
            setLoading(false)
            return
        }

        try {
            setLoading(true)
            setError('')
            const admittedOffers = await listAdminSmartOffers(filter, user)
            setOffers(admittedOffers)
        } catch (requestError) {
            setOffers([])
            setError(requestError.response?.data?.message || t('SmartOffersPage.messages.loadError'))
        } finally {
            setLoading(false)
        }
    }

    const loadStats = async () => {
        if (!user?.tenantId || !(user?.id || user?.userId)) {
            setStats(emptyState)
            return
        }

        try {
            const admittedStats = await getAdminSmartOfferStats(user)
            setStats({
                totalOffers: admittedStats.totalOffers ?? admittedStats.TotalOffers ?? 0,
                activeOffers: admittedStats.activeOffers ?? admittedStats.ActiveOffers ?? 0,
                exitIntentOffers: admittedStats.exitIntentOffers ?? admittedStats.ExitIntentOffers ?? 0,
            })
        } catch (requestError) {
            setStats(emptyState)
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
            setSelectedOffer(admittedOffer)
        } catch (requestError) {
            setSelectedOffer(null)
            setError(requestError.response?.data?.message || t('SmartOffersPage.messages.loadError'))
        } finally {
            setDetailsLoading(false)
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
        try {
            setSaving(true)
            setEditorError('')

            if (editingOffer) {
                await updateAdminSmartOffer(editingOffer, payload, user)
                setBanner(t('SmartOffersPage.messages.updateSuccess'))
            } else {
                await createAdminSmartOffer(payload, user)
                setBanner(t('SmartOffersPage.messages.createSuccess'))
            }

            closeEditor(true)
            await loadOffers()
        } catch (requestError) {
            setEditorError(requestError.response?.data?.message || t('SmartOffersPage.messages.saveError'))
        } finally {
            setSaving(false)
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
        const confirmed = await confirm({
            confirmLabel: t('SmartOffersPage.actions.delete'),
            description: t('SmartOffersPage.messages.deleteConfirm', { name: offer.name }),
            title: t('common.warning'),
            variant: 'danger',
        })

        if (!confirmed) {
            return
        }

        try {
            await archiveAdminSmartOffer(offer)
            setBanner(t('SmartOffersPage.messages.deleteSuccess'))
            await loadOffers()
        } catch (requestError) {
            setError(requestError.response?.data?.message || t('SmartOffersPage.messages.deleteError'))
        }
    }

    const handleToggleStatus = async (offer) => {
        try {
            const nextStatus = offer.status === 'Active' ? 'Inactive' : 'Active'
            await updateAdminSmartOffer(offer, {
                ...offer,
                status: nextStatus,
            }, user)
            setBanner(
                t('SmartOffersPage.messages.statusUpdated', {
                    name: offer.name,
                    status: formatOfferStatus(nextStatus, t).toLowerCase(),
                }),
            )
            await loadOffers()
        } catch (requestError) {
            setError(requestError.response?.data?.message || t('SmartOffersPage.messages.statusUpdateError'))
        }
    }

    return (
        <div className="min-h-screen bg-[radial-gradient(circle_at_top,_rgba(249,115,22,0.18),_transparent_35%),linear-gradient(180deg,_#fff7ed_0%,_#ffffff_45%,_#f8fafc_100%)] px-4 py-8 sm:px-6 lg:px-8">
            <div className="mx-auto max-w-7xl space-y-8">
                <section className="rounded-[32px] bg-slate-950 px-6 py-8 text-white shadow-[0_25px_80px_rgba(15,23,42,0.24)]">
                    <div className="flex flex-col gap-6 lg:flex-row lg:items-end lg:justify-between">
                        <div className="max-w-2xl">
                            <p className="text-xs uppercase tracking-[0.35em] text-orange-300">{t('SmartOffersPage.hero.badge')}</p>
                            <h1 className="mt-3 text-3xl font-semibold sm:text-4xl">{t('SmartOffersPage.hero.title')}</h1>
                            <p className="mt-3 text-sm leading-7 text-slate-300 sm:text-base">{t('SmartOffersPage.hero.description')}</p>
                        </div>
                        <button
                            type="button"
                            onClick={openCreate}
                            className="rounded-2xl bg-orange-500 px-5 py-3 text-sm font-semibold text-slate-950 transition hover:bg-orange-400"
                        >{t('SmartOffersPage.actions.createOffer')}</button>
                    </div>
                </section>

                <section className="grid gap-4 md:grid-cols-3">
                    <StatCard label={t('SmartOffersPage.stats.totalOffers')} value={stats.totalOffers} accent="from-slate-950 to-slate-700" />
                    <StatCard label={t('SmartOffersPage.stats.activeOffers')} value={stats.activeOffers} accent="from-emerald-500 to-teal-400" />
                    <StatCard label={t('SmartOffersPage.stats.exitIntent')} value={stats.exitIntentOffers} accent="from-orange-500 to-amber-300" />
                </section>

                <section className="rounded-[32px] border border-slate-200 bg-white/90 p-6 shadow-xl backdrop-blur">
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                        <div className="grid gap-4 sm:grid-cols-2">
                            <input
                                value={filter.search}
                                onChange={(event) => setFilter((current) => ({ ...current, search: event.target.value, page: 1 }))}
                                className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-orange-400 focus:ring-4 focus:ring-orange-100"
                                placeholder={t('SmartOffersPage.filters.searchPlaceholder')}
                            />
                            <select
                                value={filter.status}
                                onChange={(event) => setFilter((current) => ({ ...current, status: event.target.value, page: 1 }))}
                                className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-900 outline-none transition focus:border-orange-400 focus:ring-4 focus:ring-orange-100"
                            >
                                <option value="All">{t('SmartOffersPage.filters.allStatuses')}</option>
                                <option value="Active">{t('SmartOffersPage.status.active')}</option>
                                <option value="Inactive">{t('SmartOffersPage.status.inactive')}</option>
                            </select>
                        </div>
                        <button
                            type="button"
                            onClick={loadOffers}
                            className="rounded-2xl border border-slate-200 px-4 py-3 text-sm font-medium text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        >{t('SmartOffersPage.actions.refresh')}</button>
                    </div>

                    {banner ? (
                        <div className="mt-5 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
                            {banner}
                        </div>
                    ) : null}

                    {error ? (
                        <div className="mt-5 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                            {error}
                        </div>
                    ) : null}

                    <div className="mt-6 overflow-hidden rounded-[28px] border border-slate-200">
                        {loading ? (
                            <div className="grid gap-3 bg-slate-50 p-6">
                                {Array.from({ length: 4 }).map((_, index) => (
                                    <div key={index} className="h-16 animate-pulse rounded-2xl bg-slate-200" />
                                ))}
                            </div>
                        ) : offers.length === 0 ? (
                            <div className="bg-slate-50 px-6 py-16 text-center">
                                <h3 className="text-lg font-semibold text-slate-900">{t('SmartOffersPage.emptyState.title')}</h3>
                                <p className="mt-2 text-sm text-slate-500">{t('SmartOffersPage.emptyState.description')}</p>
                            </div>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-slate-200">
                                    <thead className="bg-slate-50">
                                        <tr className="text-left text-xs uppercase tracking-[0.2em] text-slate-500">
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
                                            <th className="px-5 py-4 text-right">{t('SmartOffersPage.table.actions')}</th>
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
                                                            className="rounded-2xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                        >Details</button>
                                                        <button
                                                            type="button"
                                                            onClick={() => handleToggleStatus(offer)}
                                                            className="rounded-2xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                        >
                                                            {offer.status === 'Active'
                                                                ? t('SmartOffersPage.actions.disable')
                                                                : t('SmartOffersPage.actions.enable')}
                                                        </button>
                                                        <button
                                                            type="button"
                                                            onClick={() => openEdit(offer)}
                                                            className="rounded-2xl border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                                                        >{t('SmartOffersPage.actions.edit')}</button>
                                                        <button
                                                            type="button"
                                                            onClick={() => handleDelete(offer)}
                                                            className="rounded-2xl bg-rose-50 px-3 py-2 text-sm font-medium text-rose-700 transition hover:bg-rose-100"
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
                onClose={() => setSelectedOffer(null)}
            />
        </div>
    )
}

function StatCard({ label, value, accent }) {
    return (
        <div className={`rounded-[28px] bg-gradient-to-br ${accent} p-[1px] shadow-lg`}>
            <div className="rounded-[27px] bg-white/90 px-5 py-5 backdrop-blur">
                <p className="text-sm text-slate-500">{label}</p>
                <p className="mt-3 text-3xl font-semibold text-slate-950">{value}</p>
            </div>
        </div>
    )
}

function SmartOfferDetailsModal({ offer, loading, onClose }) {
    if (!offer && !loading) {
        return null
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/60 px-4">
            <div className="w-full max-w-3xl rounded-[28px] bg-white p-6 shadow-2xl">
                <div className="flex items-start justify-between gap-4">
                    <div>
                        <p className="text-xs uppercase tracking-[0.28em] text-orange-600">Smart Offer</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{offer?.name || 'Loading'}</h2>
                    </div>
                    <button
                        type="button"
                        onClick={onClose}
                        className="rounded-2xl border border-slate-200 px-4 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-50"
                    >
                        Close
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
                        <DetailItem label="Status" value={offer.status} />
                        <DetailItem label="Priority" value={offer.priority} />
                        <DetailItem label="Offer Type" value={offer.triggerType} />
                        <DetailItem label="Offer Scope" value={offer.pagesScope} />
                        <DetailItem label="Currency" value={offer.currency} />
                        <DetailItem label="Discount Type" value={offer.couponDiscountType || 'None'} />
                        <DetailItem label="Discount Value" value={offer.couponDiscountValue ?? 'None'} />
                        <DetailItem label="Usage" value={`${offer.usageCount ?? 0} / ${offer.usageLimit ?? 'Unlimited'}`} />
                        <DetailItem label="Usage Limit Per Customer" value={offer.usageLimitPerCustomer ?? 'None'} />
                        <DetailItem label="Eligibility" value={offer.eligibilitySummary} />
                        <DetailItem label="Created" value={offer.createdAt ? new Date(offer.createdAt).toLocaleString() : 'None'} />
                        <DetailItem label="Updated" value={offer.updatedAt ? new Date(offer.updatedAt).toLocaleString() : 'None'} />
                    </div>
                )}
            </div>
        </div>
    )
}

function DetailItem({ label, value }) {
    return (
        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
            <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{label}</p>
            <p className="mt-2 break-words text-sm font-semibold text-slate-900">{String(value ?? 'None')}</p>
        </div>
    )
}
