import { useMemo, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, RefreshCw, ShieldAlert, ShieldCheck } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useDebounce } from '../../hooks/useDebounce'
import {
    applyGovernanceAction,
    getGovernedUserDetails,
    getGovernedUsers,
    type GovernanceRiskLevel,
    type GovernanceRole,
    type GovernanceStatus,
    type GovernanceUserListItem,
    type GovernanceUsersFilters,
} from './usersService'

const DEFAULT_FILTERS: GovernanceUsersFilters = {
    page: 1,
    pageSize: 20,
    search: '',
    role: '',
    status: '',
    riskLevel: '',
    createdFromUtc: '',
    createdToUtc: '',
    hasDebt: '',
}

const ROLE_LABEL_KEYS: Record<string, string> = {
    Customer: 'superAdmin.usersPage.roles.customer',
    Vendor: 'superAdmin.usersPage.roles.vendor',
    Admin: 'superAdmin.usersPage.roles.admin',
}

const STATUS_LABEL_KEYS: Record<string, string> = {
    Active: 'superAdmin.usersPage.statuses.active',
    Suspended: 'superAdmin.usersPage.statuses.suspended',
    Banned: 'superAdmin.usersPage.statuses.banned',
    FinanciallyLocked: 'superAdmin.usersPage.statuses.financiallyLocked',
}

const RISK_LABEL_KEYS: Record<string, string> = {
    Low: 'superAdmin.usersPage.riskLevels.low',
    Medium: 'superAdmin.usersPage.riskLevels.medium',
    High: 'superAdmin.usersPage.riskLevels.high',
}

const GOVERNANCE_ACTION_LABEL_KEYS: Record<string, string> = {
    suspend: 'superAdmin.usersPage.actions.suspend',
    ban: 'superAdmin.usersPage.actions.ban',
    activate: 'superAdmin.usersPage.actions.activate',
    'financial-lock': 'superAdmin.usersPage.actions.financialLock',
    'financial-unlock': 'superAdmin.usersPage.actions.financialUnlock',
}

const AUDIT_ACTION_LABEL_KEYS: Record<string, string> = {
    suspend: 'superAdmin.usersPage.auditActions.suspend',
    ban: 'superAdmin.usersPage.auditActions.ban',
    activate: 'superAdmin.usersPage.auditActions.activate',
    financiallock: 'superAdmin.usersPage.auditActions.financialLock',
    financialunlock: 'superAdmin.usersPage.auditActions.financialUnlock',
}

function badgeClasses(value: GovernanceRiskLevel | GovernanceStatus): string {
    if (value === 'High' || value === 'Banned') return 'border-rose-200 bg-rose-50 text-rose-900'
    if (value === 'Medium' || value === 'Suspended') return 'border-amber-200 bg-amber-50 text-amber-900'
    if (value === 'FinanciallyLocked') return 'border-slate-300 bg-slate-100 text-slate-900'
    return 'border-emerald-200 bg-emerald-50 text-emerald-900'
}

function humanizeValue(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[_-]/g, ' ')
}

function formatTimestamp(value: string | null | undefined, locale: string, fallback: string): string {
    if (!value) return fallback
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? fallback : date.toLocaleString(locale)
}

function formatMoney(amount: number, currency: string, locale: string): string {
    return new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: currency || 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(amount)
}

export default function UsersPage() {
    const { t, i18n } = useTranslation()
    const queryClient = useQueryClient()
    const [filters, setFilters] = useState(DEFAULT_FILTERS)
    const [selectedUser, setSelectedUser] = useState<GovernanceUserListItem | null>(null)
    const [actionError, setActionError] = useState<string | null>(null)
    const debouncedSearch = useDebounce(filters.search ?? '', 350)
    const locale = i18n.resolvedLanguage || i18n.language || 'en'

    const effectiveFilters = useMemo(
        () => ({
            ...filters,
            search: debouncedSearch,
        }),
        [debouncedSearch, filters],
    )

    const usersQuery = useQuery({
        queryKey: ['super-admin', 'governance', 'users', effectiveFilters],
        queryFn: () => getGovernedUsers(effectiveFilters),
        placeholderData: (previousData) => previousData,
    })

    const detailsQuery = useQuery({
        queryKey: ['super-admin', 'governance', 'details', selectedUser?.id, selectedUser?.role],
        queryFn: () => getGovernedUserDetails(selectedUser!.id, selectedUser!.role),
        enabled: Boolean(selectedUser),
    })

    const users = usersQuery.data?.users ?? []
    const totalCount = usersQuery.data?.meta?.totalCount ?? 0
    const totalPages = usersQuery.data?.meta?.totalPages ?? 0
    const highRiskCount = users.filter((item) => item.riskLevel === 'High').length

    function updateFilter<K extends keyof GovernanceUsersFilters>(key: K, value: GovernanceUsersFilters[K]) {
        setFilters((current) => ({
            ...current,
            [key]: value,
            page: key === 'page' || key === 'pageSize' ? (value as number) : 1,
        }))
    }

    async function refreshAll() {
        await Promise.all([
            usersQuery.refetch(),
            selectedUser ? detailsQuery.refetch() : Promise.resolve(),
        ])
    }

    async function runAction(action: 'suspend' | 'ban' | 'activate' | 'financial-lock' | 'financial-unlock') {
        if (!detailsQuery.data) return

        const reason = window.prompt(
            t('superAdmin.usersPage.prompts.reasonForAction', {
                action: t(GOVERNANCE_ACTION_LABEL_KEYS[action]),
            }),
        )?.trim()
        if (!reason || reason.length < 3) return

        setActionError(null)
        try {
            const updated = await applyGovernanceAction(
                detailsQuery.data.id,
                detailsQuery.data.role,
                action,
                reason,
            )

            queryClient.setQueryData(
                ['super-admin', 'governance', 'details', updated.id, updated.role],
                updated,
            )
            await usersQuery.refetch()
        } catch (error) {
            setActionError(error instanceof Error ? error.message : t('superAdmin.usersPage.feedback.applyActionFailed'))
        }
    }

    if (usersQuery.isError && !usersQuery.data) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <ShieldAlert className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">{t('superAdmin.usersPage.unavailable.title')}</h2>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white"
                        onClick={() => refreshAll()}
                    >
                        <RefreshCw className="h-4 w-4" />
                        {t('superAdmin.common.actions.retry')}
                    </button>
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.16),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(244,63,94,0.12),_transparent_28%)]" />
                    <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                <ShieldCheck className="h-4 w-4" />
                                {t('superAdmin.usersPage.hero.badge')}
                            </div>
                            <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                {t('superAdmin.usersPage.hero.title')}
                            </h1>
                            <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                {t('superAdmin.usersPage.hero.description')}
                            </p>
                        </div>
                        <button
                            type="button"
                            className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white"
                            onClick={() => refreshAll()}
                        >
                            <RefreshCw className={`h-4 w-4 ${usersQuery.isFetching ? 'animate-spin' : ''}`} />
                            {usersQuery.isFetching ? t('superAdmin.common.actions.refreshing') : t('superAdmin.common.actions.refresh')}
                        </button>
                    </div>
                    <div className="mt-6 grid gap-3 lg:grid-cols-3">
                        <div className="rounded-3xl border border-slate-200 bg-white px-5 py-4">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.usersPage.stats.visibleUsers')}</p>
                            <p className="mt-2 text-2xl font-semibold text-slate-950">{totalCount}</p>
                        </div>
                        <div className="rounded-3xl border border-rose-200 bg-rose-50 px-5 py-4 text-rose-950">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-75">{t('superAdmin.usersPage.stats.highRisk')}</p>
                            <p className="mt-2 text-2xl font-semibold">{highRiskCount}</p>
                        </div>
                        <div className="rounded-3xl border border-slate-200 bg-slate-100 px-5 py-4 text-slate-950">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-75">{t('superAdmin.usersPage.stats.currentPage')}</p>
                            <p className="mt-2 text-2xl font-semibold">{users.length}</p>
                        </div>
                    </div>
                </div>
            </section>

            {actionError ? (
                <section className="rounded-[28px] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900 shadow-sm">
                    {actionError}
                </section>
            ) : null}

            {highRiskCount > 0 ? (
                <section className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-amber-950 shadow-sm">
                    <div className="flex items-start gap-3">
                        <AlertTriangle className="mt-0.5 h-5 w-5" />
                        <div>
                            <p className="text-sm font-semibold">{t('superAdmin.usersPage.alerts.highRiskTitle')}</p>
                            <p className="mt-1 text-sm leading-6 opacity-85">
                                {t('superAdmin.usersPage.alerts.highRiskDescription', { count: highRiskCount })}
                            </p>
                        </div>
                    </div>
                </section>
            ) : null}

            <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                <div className="grid gap-3 lg:grid-cols-5">
                    <input
                        value={filters.search}
                        onChange={(event) => updateFilter('search', event.target.value)}
                        placeholder={t('superAdmin.usersPage.filters.searchPlaceholder')}
                        className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900 lg:col-span-2"
                    />
                    <select value={filters.role} onChange={(event) => updateFilter('role', event.target.value as GovernanceRole | '')} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900">
                        <option value="">{t('superAdmin.usersPage.filters.allRoles')}</option>
                        <option value="Customer">{t('superAdmin.usersPage.roles.customer')}</option>
                        <option value="Vendor">{t('superAdmin.usersPage.roles.vendor')}</option>
                        <option value="Admin">{t('superAdmin.usersPage.roles.admin')}</option>
                    </select>
                    <select value={filters.status} onChange={(event) => updateFilter('status', event.target.value as GovernanceStatus | '')} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900">
                        <option value="">{t('superAdmin.usersPage.filters.allStatuses')}</option>
                        <option value="Active">{t('superAdmin.usersPage.statuses.active')}</option>
                        <option value="Suspended">{t('superAdmin.usersPage.statuses.suspended')}</option>
                        <option value="Banned">{t('superAdmin.usersPage.statuses.banned')}</option>
                        <option value="FinanciallyLocked">{t('superAdmin.usersPage.statuses.financiallyLocked')}</option>
                    </select>
                    <select value={filters.riskLevel} onChange={(event) => updateFilter('riskLevel', event.target.value as GovernanceRiskLevel | '')} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm text-slate-900">
                        <option value="">{t('superAdmin.usersPage.filters.allRisk')}</option>
                        <option value="Low">{t('superAdmin.usersPage.riskLevels.low')}</option>
                        <option value="Medium">{t('superAdmin.usersPage.riskLevels.medium')}</option>
                        <option value="High">{t('superAdmin.usersPage.riskLevels.high')}</option>
                    </select>
                </div>
            </section>

            <div className="grid gap-6 xl:grid-cols-[minmax(0,1.4fr)_minmax(360px,0.9fr)]">
                <section className="overflow-hidden rounded-[30px] border border-slate-200 bg-white shadow-sm">
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-slate-200">
                            <thead className="bg-slate-50">
                                <tr className="text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <th className="px-5 py-4">{t('superAdmin.usersPage.table.user')}</th>
                                    <th className="px-5 py-4">{t('superAdmin.usersPage.table.role')}</th>
                                    <th className="px-5 py-4">{t('superAdmin.usersPage.table.status')}</th>
                                    <th className="px-5 py-4">{t('superAdmin.usersPage.table.risk')}</th>
                                    <th className="px-5 py-4">{t('superAdmin.usersPage.table.debt')}</th>
                                    <th className="px-5 py-4">{t('superAdmin.usersPage.table.lastLogin')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100 text-sm text-slate-700">
                                {users.map((user) => (
                                    <tr
                                        key={`${user.role}-${user.id}`}
                                        className={`cursor-pointer hover:bg-slate-50 ${selectedUser?.id === user.id ? 'bg-emerald-50/60' : ''}`}
                                        onClick={() => setSelectedUser(user)}
                                    >
                                        <td className="px-5 py-4">
                                            <p className="font-semibold text-slate-950">{user.name}</p>
                                            <p className="mt-1 text-xs text-slate-500">{user.email ?? user.phoneNumber ?? user.vendorSlug ?? user.id}</p>
                                        </td>
                                        <td className="px-5 py-4">{t(ROLE_LABEL_KEYS[user.role] ?? ROLE_LABEL_KEYS.Customer)}</td>
                                        <td className="px-5 py-4"><span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(user.status)}`}>{t(STATUS_LABEL_KEYS[user.status] ?? STATUS_LABEL_KEYS.Active)}</span></td>
                                        <td className="px-5 py-4">
                                            <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(user.riskLevel)}`}>{t(RISK_LABEL_KEYS[user.riskLevel] ?? RISK_LABEL_KEYS.Low)}</span>
                                            <p className="mt-2 text-xs text-slate-500">{t('superAdmin.usersPage.table.flags', { count: user.suspiciousFlags })}</p>
                                        </td>
                                        <td className="px-5 py-4">{user.hasDebt ? formatMoney(user.vendorDebt, 'EGP', locale) : t('superAdmin.usersPage.table.noDebt')}</td>
                                        <td className="px-5 py-4">{formatTimestamp(user.lastLoginAtUtc, locale, t('superAdmin.usersPage.labels.noActivityRecorded'))}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                    <div className="flex items-center justify-between border-t border-slate-100 px-5 py-4 text-sm text-slate-600">
                        <p>{t('superAdmin.usersPage.pagination.summary', { page: filters.page, totalPages: totalPages || 1 })}</p>
                        <div className="flex items-center gap-3">
                            <button type="button" disabled={filters.page <= 1} className="rounded-2xl border border-slate-200 px-4 py-3 disabled:cursor-not-allowed disabled:opacity-60" onClick={() => updateFilter('page', filters.page - 1)}>{t('superAdmin.common.actions.previous')}</button>
                            <button type="button" disabled={!totalPages || filters.page >= totalPages} className="rounded-2xl bg-slate-950 px-4 py-3 font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60" onClick={() => updateFilter('page', filters.page + 1)}>{t('superAdmin.common.actions.next')}</button>
                        </div>
                    </div>
                </section>

                <section className="rounded-[30px] border border-slate-200 bg-white p-5 shadow-sm">
                    {!selectedUser ? (
                        <div className="grid min-h-[420px] place-items-center text-center">
                            <div>
                                <p className="text-sm font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.usersPage.detail.profileDrawer')}</p>
                                <p className="mt-3 text-sm leading-6 text-slate-600">{t('superAdmin.usersPage.detail.profileDrawerDescription')}</p>
                            </div>
                        </div>
                    ) : detailsQuery.isLoading || !detailsQuery.data ? (
                        <div className="space-y-4">
                            <div className="h-28 animate-pulse rounded-3xl bg-slate-100" />
                            <div className="h-36 animate-pulse rounded-3xl bg-slate-100" />
                            <div className="h-52 animate-pulse rounded-3xl bg-slate-100" />
                        </div>
                    ) : (
                        <div className="space-y-5">
                            <div className="rounded-[26px] border border-slate-200 bg-slate-50 p-5">
                                <div className="flex flex-wrap items-center gap-3">
                                    <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(detailsQuery.data.status)}`}>{t(STATUS_LABEL_KEYS[detailsQuery.data.status] ?? STATUS_LABEL_KEYS.Active)}</span>
                                    <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${badgeClasses(detailsQuery.data.riskLevel)}`}>{t('superAdmin.usersPage.detail.riskBadge', { level: t(RISK_LABEL_KEYS[detailsQuery.data.riskLevel] ?? RISK_LABEL_KEYS.Low) })}</span>
                                </div>
                                <h2 className="mt-4 text-2xl font-semibold text-slate-950">{detailsQuery.data.basicInfo.name}</h2>
                                <p className="mt-2 text-sm text-slate-600">{detailsQuery.data.basicInfo.email ?? detailsQuery.data.basicInfo.phoneNumber ?? detailsQuery.data.basicInfo.vendorSlug ?? selectedUser.id}</p>
                                {detailsQuery.data.statusReason ? (
                                    <div className="mt-4 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-700">{detailsQuery.data.statusReason}</div>
                                ) : null}
                            </div>

                            <div className="grid gap-3 sm:grid-cols-2">
                                <div className="rounded-3xl border border-slate-200 bg-white p-4">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.usersPage.detail.financialSnapshot')}</p>
                                    <div className="mt-3 space-y-2 text-sm text-slate-700">
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.vendorDebt')}</span><span className="font-semibold text-slate-950">{formatMoney(detailsQuery.data.financialSnapshot.vendorDebt, detailsQuery.data.financialSnapshot.currency, locale)}</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.vendorPayable')}</span><span className="font-semibold text-slate-950">{formatMoney(detailsQuery.data.financialSnapshot.vendorPayable, detailsQuery.data.financialSnapshot.currency, locale)}</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.totalRevenue')}</span><span className="font-semibold text-slate-950">{formatMoney(detailsQuery.data.financialSnapshot.totalRevenue, detailsQuery.data.financialSnapshot.currency, locale)}</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.totalRefunds')}</span><span className="font-semibold text-slate-950">{formatMoney(detailsQuery.data.financialSnapshot.totalRefunds, detailsQuery.data.financialSnapshot.currency, locale)}</span></div>
                                    </div>
                                </div>
                                <div className="rounded-3xl border border-slate-200 bg-white p-4">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.usersPage.detail.activityAccess')}</p>
                                    <div className="mt-3 space-y-2 text-sm text-slate-700">
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.orders')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.activity.ordersCount}</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.refunds')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.activity.refundCount}</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.returns')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.activity.returnCount}</span></div>
                                        <div><span className="text-xs uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.usersPage.detail.lastIp')}</span><p className="mt-1 text-slate-900">{detailsQuery.data.accessInfo.lastIp ?? t('superAdmin.usersPage.detail.notCollectedYet')}</p></div>
                                    </div>
                                </div>
                            </div>

                            <div className="rounded-3xl border border-slate-200 bg-white p-4">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.usersPage.detail.riskIndicators')}</p>
                                <div className="mt-3 grid gap-3 sm:grid-cols-2">
                                    <div className="space-y-2 text-sm text-slate-700">
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.refundRate')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.risk.refundRate}%</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.disputeRate')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.risk.disputeRate}%</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.failedPayments')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.risk.failedPayments}</span></div>
                                        <div className="flex justify-between"><span>{t('superAdmin.usersPage.detail.suspiciousFlags')}</span><span className="font-semibold text-slate-950">{detailsQuery.data.risk.suspiciousFlags}</span></div>
                                    </div>
                                    <div className="space-y-2">
                                        {detailsQuery.data.risk.signals.length === 0 ? (
                                            <p className="text-sm text-slate-500">{t('superAdmin.usersPage.detail.noActiveRiskSignals')}</p>
                                        ) : detailsQuery.data.risk.signals.map((signal) => (
                                            <div key={signal} className="rounded-2xl border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">{signal}</div>
                                        ))}
                                    </div>
                                </div>
                            </div>

                            <div className="flex flex-wrap gap-3">
                                <button type="button" className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm font-semibold text-amber-900" onClick={() => runAction('suspend')}>{t('superAdmin.usersPage.actions.suspend')}</button>
                                <button type="button" className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-900" onClick={() => runAction('ban')}>{t('superAdmin.usersPage.actions.ban')}</button>
                                <button type="button" className="rounded-2xl border border-slate-200 bg-slate-100 px-4 py-3 text-sm font-semibold text-slate-900" onClick={() => runAction('financial-lock')}>{t('superAdmin.usersPage.actions.financialLock')}</button>
                                <button type="button" className="rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-semibold text-emerald-900" onClick={() => runAction('activate')}>{t('superAdmin.usersPage.actions.activate')}</button>
                                <button type="button" className="rounded-2xl border border-cyan-200 bg-cyan-50 px-4 py-3 text-sm font-semibold text-cyan-900" onClick={() => runAction('financial-unlock')}>{t('superAdmin.usersPage.actions.financialUnlock')}</button>
                            </div>

                            <div className="rounded-3xl border border-slate-200 bg-white p-4">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.usersPage.detail.auditTimeline')}</p>
                                <div className="mt-3 space-y-3">
                                    {detailsQuery.data.auditTimeline.length === 0 ? (
                                        <p className="text-sm text-slate-500">{t('superAdmin.usersPage.detail.noAuditEntries')}</p>
                                    ) : detailsQuery.data.auditTimeline.map((entry) => (
                                        <div key={entry.id} className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                                            <div className="flex items-center justify-between gap-3">
                                                <p className="text-sm font-semibold text-slate-950">{t(AUDIT_ACTION_LABEL_KEYS[entry.actionType.replace(/[-_\s]/g, '').toLowerCase()] ?? 'superAdmin.usersPage.auditActions.default', { action: humanizeValue(entry.actionType) })}</p>
                                                <p className="text-xs font-medium text-slate-500">{formatTimestamp(entry.timestampUtc, locale, t('superAdmin.usersPage.labels.noActivityRecorded'))}</p>
                                            </div>
                                            <p className="mt-2 text-sm text-slate-700">{entry.reason}</p>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        </div>
                    )}
                </section>
            </div>
        </div>
    )
}
