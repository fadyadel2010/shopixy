import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import * as DropdownMenu from '@radix-ui/react-dropdown-menu'
import api, { isRequestCanceled } from '../services/api'
import {
    getPosUserAudit,
    getPosUserRoles,
    listPosUsers,
    resendPosUserInvitation,
    revokePosUserInvitation,
} from '../services/userManagementApi'
import { useAuthStore } from '../store/authStore'
import { useDebounce } from '../hooks/useDebounce'
import { useRTL } from '../hooks/useRTL'
import { formatDateTime } from '../utils/dateFormat'
import { hasPosPermission } from '../auth/posPermissions'
import { PermissionGate } from '../components/PermissionGuard'
import { isAccessDenied, userErrorKey } from '../components/users/userErrors'
import UserInviteDialog from '../components/users/UserInviteDialog'
import UserProfileDialog from '../components/users/UserProfileDialog'
import UserRoleDialog from '../components/users/UserRoleDialog'
import UserStatusDialog from '../components/users/UserStatusDialog'
import UserResetDialog from '../components/users/UserResetDialog'
import UserRevokeSessionDialog from '../components/users/UserRevokeSessionDialog'
import UserOwnershipDialog from '../components/users/UserOwnershipDialog'
import UserDialog from '../components/users/UserDialog'

const PAGE_SIZE_OPTIONS = [10, 20, 50, 100]
const SORT_OPTIONS = ['username', 'email', 'role', 'status', 'lastLogin', 'invitedAt']
const STATUS_OPTIONS = ['Active', 'Pending', 'InvitationExpired', 'Disabled', 'Removed']
const ROLE_OPTIONS = ['Owner', 'Admin', 'Manager', 'Cashier', 'Warehouse']

const statusBadgeClass = (status) => {
    switch (status) {
        case 'Active': return 'bg-green-100 text-green-800'
        case 'Pending': return 'bg-yellow-100 text-yellow-800'
        case 'InvitationExpired': return 'bg-orange-100 text-orange-800'
        case 'Disabled': return 'bg-red-100 text-red-800'
        default: return 'bg-gray-100 text-gray-800'
    }
}

const inputClass = 'w-full rounded-lg border px-3 py-2 text-sm focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500'

export default function UsersManagementPage() {
    const { t } = useTranslation()
    const { dir, isRTL, locale } = useRTL()
    const user = useAuthStore((state) => state.user)
    const tableTextAlignClass = isRTL ? 'text-right' : 'text-left'
    const actionAlignClass = isRTL ? 'text-left' : 'text-right'

    const [filters, setFilters] = useState({
        search: '', role: '', status: '', invitedFrom: '', invitedTo: '',
        lastLoginFrom: '', lastLoginTo: '', sortBy: 'username', sortDir: 'asc',
        page: 1, pageSize: 20,
    })
    const [data, setData] = useState({ items: [], totalCount: 0, totalPages: 0, activeCount: 0, pendingCount: 0, disabledCount: 0, ownerAdminCount: 0 })
    const [roles, setRoles] = useState([])
    const [loading, setLoading] = useState(true)
    const [banner, setBanner] = useState('')
    const [dialog, setDialog] = useState(null)
    const [ownershipUsers, setOwnershipUsers] = useState([])
    const abortRef = useRef(null)
    const requestGenerationRef = useRef(0)

    const debouncedSearch = useDebounce(filters.search, 500)
    const isOwner = user?.role === 'Owner'

    const setFilter = (key, value) => setFilters((current) => ({ ...current, [key]: value, page: 1 }))
    const setPage = (page) => setFilters((current) => ({ ...current, page }))

    const refetchSession = useCallback(async () => {
        try {
            const response = await api.get('/auth/session')
            if (response.data?.user) {
                useAuthStore.getState().hydrateSession(response.data.user)
            }
        } catch {
            // Session-loss handling stays centralized in the api client.
        }
    }, [])

    const fetchList = useCallback(async (overrides) => {
        abortRef.current?.abort()
        const controller = new AbortController()
        abortRef.current = controller
        const generation = ++requestGenerationRef.current
        setLoading(true)
        try {
            const active = overrides || filters
            const params = {
                page: active.page,
                pageSize: active.pageSize,
                ...(debouncedSearch.trim() && { search: debouncedSearch.trim() }),
                ...(active.role && { role: active.role }),
                ...(active.status && { status: active.status }),
                ...(active.invitedFrom && { invitedFrom: active.invitedFrom }),
                ...(active.invitedTo && { invitedTo: active.invitedTo }),
                ...(active.lastLoginFrom && { lastLoginFrom: active.lastLoginFrom }),
                ...(active.lastLoginTo && { lastLoginTo: active.lastLoginTo }),
                ...(active.sortBy && { sortBy: active.sortBy }),
                ...(active.sortDir && { sortDir: active.sortDir }),
            }
            const response = await listPosUsers(params, { signal: controller.signal })
            setData(response.data)
            setBanner('')
        } catch (error) {
            if (isRequestCanceled(error)) return
            if (isAccessDenied(error)) {
                setBanner(t('userManagement.errors.accessDenied'))
                refetchSession()
            } else {
                setBanner(t('userManagement.errors.loadFailed'))
            }
        } finally {
            if (generation === requestGenerationRef.current) {
                setLoading(false)
            }
        }
    }, [
        filters.page,
        filters.pageSize,
        filters.role,
        filters.status,
        filters.invitedFrom,
        filters.invitedTo,
        filters.lastLoginFrom,
        filters.lastLoginTo,
        filters.sortBy,
        filters.sortDir,
        debouncedSearch,
        t,
        refetchSession,
    ])

    useEffect(() => {
        fetchList()
        return () => abortRef.current?.abort()
    }, [fetchList])

    useEffect(() => {
        getPosUserRoles()
            .then((response) => setRoles(response.data || []))
            .catch(() => setRoles([]))
    }, [])

    const refresh = () => fetchList()

    const canManageTarget = (target) => {
        if (!target || target.id === user?.id) return false
        if (target.role === 'Owner') return false
        if (isOwner) return true
        return user?.role === 'Admin' && target.role !== 'Admin'
    }

    const openOwnership = async () => {
        try {
            const response = await listPosUsers({ page: 1, pageSize: 100, status: 'Active' })
            setOwnershipUsers(response.data.items || [])
        } catch {
            setOwnershipUsers(data.items.filter((item) => item.status === 'Active'))
        }
        setDialog({ type: 'ownership' })
    }

    const handleResend = async (target) => {
        try {
            await resendPosUserInvitation(target.id, target.version)
            refresh()
        } catch (error) {
            setBanner(t(`userManagement.${userErrorKey(error)}`))
            if (isAccessDenied(error)) refetchSession()
        }
    }

    const handleRevokeInvitation = async (target) => {
        try {
            await revokePosUserInvitation(target.id, undefined, target.version)
            refresh()
        } catch (error) {
            setBanner(t(`userManagement.${userErrorKey(error)}`))
            if (isAccessDenied(error)) refetchSession()
        }
    }

    const currentOwner = [...(ownershipUsers.length ? ownershipUsers : data.items)].find((item) => item.role === 'Owner')
        || data.items.find((item) => item.id === user?.id && item.role === 'Owner')

    const from = data.totalCount === 0 ? 0 : (filters.page - 1) * filters.pageSize + 1
    const to = Math.min(filters.page * filters.pageSize, data.totalCount)

    const renderMenu = (target) => {
        const manageable = canManageTarget(target)
        const pending = target.status === 'Pending' || target.status === 'InvitationExpired'
        const items = []
        if (manageable && !pending) {
            if (hasPosPermission(user, 'users.profile.manage')) items.push({ key: 'profile', label: t('userManagement.actions.editProfile') })
            if (hasPosPermission(user, 'users.role.manage')) items.push({ key: 'role', label: t('userManagement.actions.changeRole') })
            if (hasPosPermission(user, 'users.status.manage')) {
                items.push({ key: 'status', label: t(target.status === 'Disabled' ? 'userManagement.actions.enable' : 'userManagement.actions.disable') })
            }
            if (hasPosPermission(user, 'users.password-reset.initiate') && target.status !== 'Disabled') {
                items.push({ key: 'reset', label: t('userManagement.actions.resetPassword') })
            }
            if (hasPosPermission(user, 'users.session.revoke')) items.push({ key: 'revoke', label: t('userManagement.actions.revokeSession') })
        }
        if (manageable && pending && hasPosPermission(user, 'users.invite')) {
            items.push({ key: 'resend', label: t('userManagement.actions.resendInvitation') })
            items.push({ key: 'revokeInvitation', label: t('userManagement.actions.revokeInvitation') })
        }
        if (hasPosPermission(user, 'audit.view')) items.push({ key: 'audit', label: t('userManagement.actions.viewAudit') })

        if (items.length === 0) return null

        return (
            <DropdownMenu.Root dir={dir}>
                <DropdownMenu.Trigger asChild>
                    <button
                        type="button"
                        aria-label={`${t('userManagement.actions.menu')}: ${target.username}`}
                        className="rounded-lg border px-3 py-1 text-sm text-gray-700 hover:bg-gray-50"
                    >
                        ⋯
                    </button>
                </DropdownMenu.Trigger>
                <DropdownMenu.Portal>
                    <DropdownMenu.Content
                        sideOffset={4}
                        align={isRTL ? 'start' : 'end'}
                        className="z-50 min-w-48 rounded-lg border bg-white py-1 shadow-lg"
                    >
                        {items.map((item) => (
                            <DropdownMenu.Item
                                key={item.key}
                                onSelect={() => {
                                    if (item.key === 'resend') handleResend(target)
                                    else if (item.key === 'revokeInvitation') handleRevokeInvitation(target)
                                    else if (item.key === 'audit') setDialog({ type: 'audit', user: target })
                                    else setDialog({ type: item.key, user: target })
                                }}
                                className={`cursor-pointer px-4 py-2 text-sm text-gray-700 outline-none focus:bg-gray-100 ${tableTextAlignClass}`}
                            >
                                {item.label}
                            </DropdownMenu.Item>
                        ))}
                    </DropdownMenu.Content>
                </DropdownMenu.Portal>
            </DropdownMenu.Root>
        )
    }

    return (
        <div dir={dir} className="space-y-6 p-6">
            <div className="flex flex-col items-start justify-between gap-4 md:flex-row md:items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800">{t('userManagement.title')}</h1>
                    <p className="max-w-2xl text-sm text-gray-500">{t('userManagement.subtitle')}</p>
                </div>
                <div className={`flex gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <PermissionGate permission="users.ownership.transfer">
                        <button
                            type="button"
                            onClick={openOwnership}
                            className="rounded-lg border border-red-300 px-4 py-2 text-sm text-red-700 hover:bg-red-50"
                        >
                            {t('userManagement.actions.transferOwnership')}
                        </button>
                    </PermissionGate>
                    <PermissionGate permission="users.invite">
                        <button
                            type="button"
                            onClick={() => setDialog({ type: 'invite' })}
                            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm text-white hover:bg-indigo-700"
                        >
                            {t('userManagement.actions.invite')}
                        </button>
                    </PermissionGate>
                </div>
            </div>

            {banner && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-700">{banner}</p>}

            <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
                {[
                    { label: t('userManagement.summary.active'), value: data.activeCount, tone: 'bg-green-50 text-green-800' },
                    { label: t('userManagement.summary.pending'), value: data.pendingCount, tone: 'bg-yellow-50 text-yellow-800' },
                    { label: t('userManagement.summary.disabled'), value: data.disabledCount, tone: 'bg-red-50 text-red-800' },
                    { label: t('userManagement.summary.ownerAdmin'), value: data.ownerAdminCount, tone: 'bg-indigo-50 text-indigo-800' },
                ].map((card) => (
                    <div key={card.label} className={`rounded-xl p-4 shadow-sm ${card.tone}`}>
                        <p className="text-2xl font-bold">{card.value}</p>
                        <p className="text-sm font-medium">{card.label}</p>
                    </div>
                ))}
            </div>

            <div className="grid grid-cols-1 gap-3 rounded-xl border border-gray-200 bg-white p-4 shadow-sm md:grid-cols-3 lg:grid-cols-4">
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.search')}
                    <input value={filters.search} onChange={(event) => setFilter('search', event.target.value)} placeholder={t('userManagement.searchPlaceholder')} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.role')}
                    <select value={filters.role} onChange={(event) => setFilter('role', event.target.value)} className={inputClass}>
                        <option value="">{t('userManagement.filters.allRoles')}</option>
                        {ROLE_OPTIONS.map((role) => <option key={role} value={role}>{t(`userManagement.roles.${role}`)}</option>)}
                    </select>
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.status')}
                    <select value={filters.status} onChange={(event) => setFilter('status', event.target.value)} className={inputClass}>
                        <option value="">{t('userManagement.filters.allStatuses')}</option>
                        {STATUS_OPTIONS.map((status) => <option key={status} value={status}>{t(`userManagement.status.${status}`)}</option>)}
                    </select>
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.sortBy')}
                    <select value={filters.sortBy} onChange={(event) => setFilters((current) => ({ ...current, sortBy: event.target.value, page: 1 }))} className={inputClass}>
                        {SORT_OPTIONS.map((option) => <option key={option} value={option}>{t(`userManagement.sort.${option}`)}</option>)}
                    </select>
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.invitedFrom')}
                    <input type="date" value={filters.invitedFrom} onChange={(event) => setFilter('invitedFrom', event.target.value)} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.invitedTo')}
                    <input type="date" value={filters.invitedTo} onChange={(event) => setFilter('invitedTo', event.target.value)} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.lastLoginFrom')}
                    <input type="date" value={filters.lastLoginFrom} onChange={(event) => setFilter('lastLoginFrom', event.target.value)} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.lastLoginTo')}
                    <input type="date" value={filters.lastLoginTo} onChange={(event) => setFilter('lastLoginTo', event.target.value)} className={inputClass} />
                </label>
                <label className="block text-sm font-medium text-gray-700">
                    {t('userManagement.filters.sortDir')}
                    <select value={filters.sortDir} onChange={(event) => setFilters((current) => ({ ...current, sortDir: event.target.value, page: 1 }))} className={inputClass}>
                        <option value="asc">{t('userManagement.filters.asc')}</option>
                        <option value="desc">{t('userManagement.filters.desc')}</option>
                    </select>
                </label>
                <div className="flex items-end">
                    <button
                        type="button"
                        onClick={() => setFilters({ search: '', role: '', status: '', invitedFrom: '', invitedTo: '', lastLoginFrom: '', lastLoginTo: '', sortBy: 'username', sortDir: 'asc', page: 1, pageSize: filters.pageSize })}
                        className="rounded-lg border px-4 py-2 text-sm text-gray-600 hover:bg-gray-50"
                    >
                        {t('userManagement.filters.clear')}
                    </button>
                </div>
            </div>

            {loading ? (
                <div className="flex items-center justify-center py-12">
                    <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-indigo-600" />
                </div>
            ) : data.items.length === 0 ? (
                <p className="rounded-xl border border-gray-200 bg-white p-12 text-center text-gray-500">{t('userManagement.empty.noUsers')}</p>
            ) : (
                <>
                    <div className="hidden overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm md:block">
                        <div className="overflow-x-auto">
                            <table className="w-full">
                                <thead className="border-b border-gray-200 bg-gray-50">
                                    <tr>
                                        {[t('userManagement.table.user'), t('userManagement.table.role'), t('userManagement.table.status'), t('userManagement.table.lastLogin'), t('userManagement.table.invited')].map((header) => (
                                            <th key={header} className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${tableTextAlignClass}`}>{header}</th>
                                        ))}
                                        <th className={`px-6 py-3 text-xs font-semibold uppercase tracking-wider text-gray-500 ${actionAlignClass}`}>{t('userManagement.table.actions')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200">
                                    {data.items.map((item) => (
                                        <tr key={item.id} className="hover:bg-gray-50">
                                            <td className={`px-6 py-4 ${tableTextAlignClass}`}>
                                                <p className="text-sm font-medium text-gray-900">{item.firstName} {item.lastName}</p>
                                                <p className="text-xs text-gray-500">{item.username} · {item.email}</p>
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-700 ${tableTextAlignClass}`}>{t(`userManagement.roles.${item.role}`)}</td>
                                            <td className={`px-6 py-4 ${tableTextAlignClass}`}>
                                                <span className={`rounded-full px-2 py-1 text-xs font-semibold ${statusBadgeClass(item.status)}`}>
                                                    {t(`userManagement.status.${item.status}`)}
                                                </span>
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-600 ${tableTextAlignClass}`}>
                                                {item.lastLoginAt ? formatDateTime(item.lastLoginAt, locale) : t('userManagement.table.never')}
                                            </td>
                                            <td className={`px-6 py-4 text-sm text-gray-600 ${tableTextAlignClass}`}>
                                                {item.invitedAtUtc ? formatDateTime(item.invitedAtUtc, locale) : t('userManagement.table.never')}
                                            </td>
                                            <td className={`px-6 py-4 ${actionAlignClass}`}>{renderMenu(item)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </div>

                    <div className="space-y-3 md:hidden">
                        {data.items.map((item) => (
                            <div key={item.id} className="rounded-xl border border-gray-200 bg-white p-4 shadow-sm">
                                <div className="mb-2 flex items-start justify-between gap-3">
                                    <div>
                                        <p className="text-sm font-semibold text-gray-900">{item.firstName} {item.lastName}</p>
                                        <p className="break-all text-xs text-gray-500">{item.username}</p>
                                        <p className="break-all text-xs text-gray-500">{item.email}</p>
                                    </div>
                                    {renderMenu(item)}
                                </div>
                                <div className="flex flex-wrap items-center gap-2 text-xs">
                                    <span className="rounded-full bg-gray-100 px-2 py-1 font-semibold text-gray-700">{t(`userManagement.roles.${item.role}`)}</span>
                                    <span className={`rounded-full px-2 py-1 font-semibold ${statusBadgeClass(item.status)}`}>
                                        {t(`userManagement.status.${item.status}`)}
                                    </span>
                                </div>
                                <p className="mt-2 text-xs text-gray-500">
                                    {t('userManagement.table.lastLogin')}: {item.lastLoginAt ? formatDateTime(item.lastLoginAt, locale) : t('userManagement.table.never')}
                                </p>
                            </div>
                        ))}
                    </div>

                    <div className={`flex flex-col items-center justify-between gap-3 rounded-xl border border-gray-200 bg-white p-4 shadow-sm sm:flex-row ${isRTL ? 'sm:flex-row-reverse' : ''}`}>
                        <p className="text-sm text-gray-600">
                            {t('userManagement.pagination.showing', { from, to, total: data.totalCount })}
                        </p>
                        <div className={`flex items-center gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                            <label className="text-sm text-gray-600">
                                {t('userManagement.pagination.pageSize')}
                                <select
                                    value={filters.pageSize}
                                    onChange={(event) => setFilters((current) => ({ ...current, pageSize: Number(event.target.value), page: 1 }))}
                                    className="mx-2 rounded-lg border px-2 py-1 text-sm"
                                >
                                    {PAGE_SIZE_OPTIONS.map((size) => <option key={size} value={size}>{size}</option>)}
                                </select>
                            </label>
                            <button
                                type="button"
                                disabled={filters.page <= 1}
                                onClick={() => setPage(filters.page - 1)}
                                className="rounded-lg border px-3 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                            >
                                {t('userManagement.pagination.prev')}
                            </button>
                            <button
                                type="button"
                                disabled={filters.page >= data.totalPages}
                                onClick={() => setPage(filters.page + 1)}
                                className="rounded-lg border px-3 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                            >
                                {t('userManagement.pagination.next')}
                            </button>
                        </div>
                    </div>
                </>
            )}

            {dialog?.type === 'invite' && (
                <UserInviteDialog roles={roles} onClose={() => setDialog(null)} onSaved={refresh} />
            )}
            {dialog?.type === 'profile' && (
                <UserProfileDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={refresh} />
            )}
            {dialog?.type === 'role' && (
                <UserRoleDialog user={dialog.user} roles={roles} onClose={() => setDialog(null)} onSaved={refresh} />
            )}
            {dialog?.type === 'status' && (
                <UserStatusDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={refresh} />
            )}
            {dialog?.type === 'reset' && (
                <UserResetDialog user={dialog.user} onClose={() => setDialog(null)} />
            )}
            {dialog?.type === 'revoke' && (
                <UserRevokeSessionDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={refresh} />
            )}
            {dialog?.type === 'ownership' && (
                <UserOwnershipDialog
                    users={ownershipUsers}
                    currentOwner={currentOwner}
                    onClose={() => setDialog(null)}
                    onSaved={() => { refresh(); refetchSession() }}
                />
            )}
            {dialog?.type === 'audit' && (
                <UserAuditDialog user={dialog.user} onClose={() => setDialog(null)} />
            )}
        </div>
    )
}

function UserAuditDialog({ user, onClose }) {
    const { t } = useTranslation()
    const { locale } = useRTL()
    const [rows, setRows] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')

    useEffect(() => {
        let cancelled = false
        setLoading(true)
        getPosUserAudit(user.id, { page: 1, pageSize: 20 })
            .then((response) => {
                if (!cancelled) {
                    setRows(response.data.items || [])
                    setError('')
                }
            })
            .catch((err) => {
                if (!cancelled) setError(t(`userManagement.${userErrorKey(err)}`))
            })
            .finally(() => {
                if (!cancelled) setLoading(false)
            })
        return () => { cancelled = true }
    }, [user.id, t])

    return (
        <UserDialog title={`${t('userManagement.actions.viewAudit')}: ${user.username}`} onClose={onClose} wide>
                {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
                {loading ? (
                    <div className="flex items-center justify-center py-8">
                        <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-indigo-600" />
                    </div>
                ) : rows.length === 0 ? (
                    <p className="py-8 text-center text-sm text-gray-500">{t('userManagement.empty.noUsers')}</p>
                ) : (
                    <ul className="space-y-2">
                        {rows.map((row, index) => (
                            <li key={`${row.timestampUtc}-${index}`} className="rounded-lg border border-gray-200 p-3 text-sm">
                                <p className="font-medium text-gray-800">
                                    {t(`auditLog.actions.${String(row.actionType || '').toLowerCase()}`, {
                                        defaultValue: t('userManagement.audit.unknownAction'),
                                    })}
                                </p>
                                <p className="text-xs text-gray-400">{row.timestampUtc ? formatDateTime(row.timestampUtc, locale) : ''}</p>
                            </li>
                        ))}
                    </ul>
                )}
                <div className="mt-4 flex justify-end">
                    <button type="button" onClick={onClose} className="rounded-lg border px-4 py-2 text-gray-600 hover:bg-gray-50">
                        {t('common.close')}
                    </button>
                </div>
        </UserDialog>
    )
}
