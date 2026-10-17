import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import api from '../services/api'
import { useAuthStore } from '../store/authStore'

export default function BlockedPhones() {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage?.startsWith('ar') ? 'ar-EG' : 'en-US'
    const queryClient = useQueryClient()
    const user = useAuthStore(state => state.user)
    const [searchParams] = useSearchParams()
    const { confirm } = useConfirmDialog()
    const [items, setItems] = useState([])
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const [search, setSearch] = useState(() => searchParams.get('search') || '')
    const [pagination, setPagination] = useState({
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0,
    })
    const [actionLoadingPhone, setActionLoadingPhone] = useState('')
    const requestSequence = useRef(0)
    const mutation = useRef(false)

    const fetchBlockedPhones = useCallback(async () => {
        const sequence = ++requestSequence.current
        try {
            setLoading(true)
            setError('')

            const params = new URLSearchParams()
            params.append('page', String(pagination.page))
            params.append('pageSize', String(pagination.pageSize))
            if (search.trim()) params.append('search', search.trim())

            const response = await api.get(`/admin/phones?${params}`)
            if (sequence !== requestSequence.current) return false
            setItems(response.data.items || [])
            setPagination(prev => ({
                ...prev,
                page: Math.max(1, Math.min(prev.page, response.data.totalPages || 1)),
                totalCount: response.data.totalCount || 0,
                totalPages: response.data.totalPages || 0,
            }))
            return true
        } catch {
            if (sequence !== requestSequence.current) return false
            setError(t('blockedPhones.loadError'))
            return false
        } finally {
            if (sequence === requestSequence.current) setLoading(false)
        }
    }, [pagination.page, pagination.pageSize, search, t])

    useEffect(() => {
        fetchBlockedPhones()
        return () => { requestSequence.current += 1 }
    }, [fetchBlockedPhones])

    const handleUnblock = async (phoneNumber) => {
        if (mutation.current) return
        mutation.current = true
        const confirmed = await confirm({
            confirmLabel: t('blockedPhones.unblock', 'Unblock'),
            description: t('blockedPhones.unblockConfirm', 'Unblock this phone number and allow new storefront orders?'),
            title: t('common.confirm'),
            variant: 'warning',
        })

        if (!confirmed) { mutation.current = false; return }

        try {
            setActionLoadingPhone(phoneNumber)
            await api.post('/admin/phones/unblock', { phoneNumber })
            await queryClient.invalidateQueries({ queryKey: ['pos-admin-orders', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous', 'customer-trust'] })
            const loaded = await fetchBlockedPhones()
            if (!loaded) setError(t('blockedPhones.savedReloadFailed'))
        } catch {
            setError(t('blockedPhones.unblockError'))
        } finally {
            mutation.current = false
            setActionLoadingPhone('')
        }
    }

    return (
        <div className="p-6 space-y-6" dir={i18n.dir()}>
            <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">
                        {t('blockedPhones.title', 'Blocked Phones')}
                    </h1>
                    <p className="mt-1 text-sm text-gray-500">
                        {t('blockedPhones.subtitle', 'Manage phone numbers that are not allowed to place new storefront orders.')}
                    </p>
                </div>

                <div className="flex gap-3">
                    <input
                        type="text"
                        dir={/^[+\d]/.test(search.trim()) ? 'ltr' : 'auto'}
                        value={search}
                        onChange={(e) => {
                            setSearch(e.target.value)
                            setPagination(prev => ({ ...prev, page: 1 }))
                        }}
                        onKeyDown={(e) => {
                            if (e.key === 'Enter') {
                                fetchBlockedPhones()
                            }
                        }}
                        placeholder={t('blockedPhones.searchPlaceholder', 'Search by phone or reason')}
                        aria-label={t('blockedPhones.searchPlaceholder', 'Search by phone or reason')}
                        className="min-w-0 w-full md:w-80 rounded-lg border border-gray-300 px-3 py-2 shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                    />
                    <button
                        onClick={fetchBlockedPhones}
                        className="rounded-lg bg-gray-900 px-4 py-2 text-sm font-medium text-white hover:bg-gray-800"
                    >
                        {t('common.search', 'Search')}
                    </button>
                </div>
            </div>

            {error && (
                <div role="alert" className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                    {error}
                </div>
            )}

            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                <div className="rounded-xl border border-gray-100 bg-white p-5 shadow-sm">
                    <p className="text-sm text-gray-500">{t('blockedPhones.totalBlocked', 'Blocked Phones')}</p>
                    <p className="mt-2 text-3xl font-bold text-gray-900">{error ? '—' : pagination.totalCount}</p>
                </div>
            </div>

            <div className="overflow-x-auto rounded-xl border border-gray-100 bg-white shadow-sm">
                {loading ? (
                    <div className="flex justify-center py-16">
                        <div className="h-10 w-10 animate-spin rounded-full border-b-2 border-indigo-600" />
                    </div>
                ) : items.length === 0 && !error ? (
                    <div className="py-16 text-center">
                        <div className="text-4xl">📵</div>
                        <p className="mt-4 text-lg font-medium text-gray-900">
                            {t('blockedPhones.empty', 'No blocked phone numbers found')}
                        </p>
                        <p className="mt-1 text-sm text-gray-500">
                            {t('blockedPhones.emptyHint', 'Blocked phones from the order page will appear here.')}
                        </p>
                    </div>
                ) : (
                    <table className="min-w-[640px] w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                    <th className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">
                                    {t('blockedPhones.phoneNumber', 'Phone Number')}
                                </th>
                                    <th className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">
                                    {t('blockedPhones.blockedAt', 'Blocked At')}
                                </th>
                                    <th className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">
                                    {t('blockedPhones.reason', 'Reason')}
                                </th>
                                    <th className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">
                                    {t('blockedPhones.blockedBy', 'Blocked By')}
                                </th>
                                    <th className="px-4 py-3 text-start text-xs font-medium uppercase tracking-wider text-gray-500">
                                    {t('common.actions', 'Actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200 bg-white">
                            {items.map((item) => (
                                <tr key={item.id} className="hover:bg-gray-50">
                                    <td className="px-4 py-4 text-sm font-medium text-gray-900">
                                        <bdi dir="ltr">{item.phoneNumber}</bdi>
                                    </td>
                                    <td className="px-4 py-4 text-sm text-gray-600">
                                        {item.blockedAtUtc ? new Date(item.blockedAtUtc).toLocaleString(locale) : '—'}
                                    </td>
                                    <td className="px-4 py-4 text-sm text-gray-600">
                                        {item.blockedReason || '—'}
                                    </td>
                                    <td className="px-4 py-4 text-sm text-gray-600">
                                        {item.blockedByUser || '—'}
                                    </td>
                                    <td className="px-4 py-4">
                                        <button
                                            onClick={() => handleUnblock(item.phoneNumber)}
                                            disabled={actionLoadingPhone === item.phoneNumber}
                                            className="rounded-lg bg-green-100 px-3 py-1.5 text-sm font-medium text-green-800 hover:bg-green-200 disabled:opacity-50"
                                        >
                                            {actionLoadingPhone === item.phoneNumber
                                                ? '...'
                                                : t('blockedPhones.unblock', 'Unblock')}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                )}

                {pagination.totalPages > 1 && (
                    <div className="flex items-center justify-between border-t border-gray-200 bg-gray-50 px-4 py-3">
                        <p className="text-sm text-gray-600">
                            {t('common.showing', 'Showing')} {((pagination.page - 1) * pagination.pageSize) + 1} {t('common.to', 'to')}{' '}
                            {Math.min(pagination.page * pagination.pageSize, pagination.totalCount)} {t('common.of', 'of')}{' '}
                            {pagination.totalCount}
                        </p>
                        <div className="flex gap-2">
                            <button
                                onClick={() => setPagination(prev => ({ ...prev, page: prev.page - 1 }))}
                                disabled={pagination.page <= 1}
                                className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-100 disabled:opacity-50"
                            >
                                {t('common.previous', 'Previous')}
                            </button>
                            <button
                                onClick={() => setPagination(prev => ({ ...prev, page: prev.page + 1 }))}
                                disabled={pagination.page >= pagination.totalPages}
                                className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-100 disabled:opacity-50"
                            >
                                {t('common.next', 'Next')}
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    )
}
