import React, { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../contexts/SettingsContext'
import api from '../services/api'

const SESSION_STATUSES = [
    { value: 'Abandoned', label: 'Abandoned', color: 'bg-red-100 text-red-800' },
    { value: 'Active', label: 'Active', color: 'bg-yellow-100 text-yellow-800' },
    { value: 'ConvertedToOrder', label: 'Converted', color: 'bg-green-100 text-green-800' },
]

function getStatusInfo(value) {
    return SESSION_STATUSES.find((status) => status.value === value) || SESSION_STATUSES[0]
}

function formatRelativeTime(dateString) {
    if (!dateString) return '-'

    const date = new Date(dateString)
    const now = new Date()
    const diffMs = now - date
    const diffMins = Math.floor(diffMs / 60000)
    const diffHours = Math.floor(diffMs / 3600000)
    const diffDays = Math.floor(diffMs / 86400000)

    if (diffMins < 1) return 'just now'
    if (diffMins < 60) return `${diffMins}m ago`
    if (diffHours < 24) return `${diffHours}h ago`
    if (diffDays < 7) return `${diffDays}d ago`

    return date.toLocaleDateString()
}

function formatTimeSpent(seconds) {
    if (!seconds && seconds !== 0) return '-'

    const totalSeconds = Math.round(seconds)
    if (totalSeconds < 60) return `${totalSeconds}s`

    const minutes = Math.floor(totalSeconds / 60)
    const remainingSeconds = totalSeconds % 60
    if (minutes < 60) return `${minutes}m ${remainingSeconds}s`

    const hours = Math.floor(minutes / 60)
    return `${hours}h ${minutes % 60}m`
}

const DEVICE_LABELS = {
    Mobile: 'Mobile',
    Desktop: 'Desktop',
    Tablet: 'Tablet',
}

export default function AbandonedCheckouts() {
    const { t } = useTranslation()
    const { formatCurrency } = useSettings()

    const [checkouts, setCheckouts] = useState([])
    const [loading, setLoading] = useState(true)
    const [listError, setListError] = useState('')
    const [detailError, setDetailError] = useState('')
    const [pagination, setPagination] = useState({
        page: 1,
        pageSize: 20,
        totalCount: 0,
        totalPages: 0,
        hasMore: false,
    })
    const [statusFilter, setStatusFilter] = useState('Abandoned')
    const [search, setSearch] = useState('')
    const [selectedCheckout, setSelectedCheckout] = useState(null)
    const [detailLoading, setDetailLoading] = useState(false)
    const [stats, setStats] = useState(null)

    const fetchCheckouts = useCallback(async () => {
        try {
            setLoading(true)
            setListError('')

            const params = new URLSearchParams()
            params.append('page', pagination.page.toString())
            params.append('pageSize', pagination.pageSize.toString())
            if (statusFilter) params.append('status', statusFilter)
            if (search.trim()) params.append('search', search.trim())

            const response = await api.get(`/admin/abandoned-checkouts?${params.toString()}`)
            const data = response.data
            setCheckouts(data.items || [])
            setPagination((previous) => ({
                ...previous,
                totalCount: data.totalCount || 0,
                totalPages: data.totalPages || Math.max(1, Math.ceil((data.totalCount || 0) / previous.pageSize)),
                hasMore: Boolean(data.hasMore),
            }))
        } catch (error) {
            console.error('Failed to fetch abandoned checkouts:', error)
            setCheckouts([])
            setListError('Abandoned checkouts could not be loaded. Please retry.')
        } finally {
            setLoading(false)
        }
    }, [pagination.page, pagination.pageSize, search, statusFilter])

    const fetchStats = useCallback(async () => {
        try {
            const response = await api.get('/admin/abandoned-checkouts/stats')
            setStats(response.data)
        } catch (error) {
            console.error('Failed to fetch abandoned checkout stats:', error)
        }
    }, [])

    const fetchDetail = useCallback(async (sessionId) => {
        try {
            setDetailLoading(true)
            setDetailError('')
            const response = await api.get(`/admin/abandoned-checkouts/${sessionId}`)
            setSelectedCheckout(response.data)
        } catch (error) {
            console.error('Failed to fetch detail:', error)
            setDetailError('Checkout details could not be loaded. Please retry.')
        } finally {
            setDetailLoading(false)
        }
    }, [])

    useEffect(() => {
        void fetchCheckouts()
    }, [fetchCheckouts])

    useEffect(() => {
        void fetchStats()
    }, [fetchStats])

    const conversionRate = stats?.totalSessions
        ? ((stats.convertedSessions / stats.totalSessions) * 100).toFixed(1)
        : '0.0'

    const abandonmentRate = stats?.totalSessions
        ? ((stats.abandonedSessions / stats.totalSessions) * 100).toFixed(1)
        : '0.0'

    const detailItems = selectedCheckout?.items || []

    return (
        <div className="p-6 space-y-6">
            <div className="flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">
                        {t('abandonedCheckouts.title', 'Abandoned Checkouts')}
                    </h1>
                    <p className="text-sm text-gray-500 mt-1">
                        {t('abandonedCheckouts.subtitle', 'Track checkout sessions that were started but not completed')}
                    </p>
                </div>
                <button
                    onClick={() => {
                        void fetchCheckouts()
                        void fetchStats()
                    }}
                    className="inline-flex items-center p-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                    Refresh
                </button>
            </div>

            {stats && (
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <p className="text-sm text-gray-500">{t('abandonedCheckouts.totalSessions', 'Total Sessions')}</p>
                        <p className="text-2xl font-bold text-gray-900">{stats.totalSessions || 0}</p>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <p className="text-sm text-gray-500">{t('abandonedCheckouts.abandoned', 'Abandoned')}</p>
                        <p className="text-2xl font-bold text-gray-900">
                            {stats.abandonedSessions || 0}
                            <span className="text-sm font-normal text-gray-500 ml-1">({abandonmentRate}%)</span>
                        </p>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <p className="text-sm text-gray-500">{t('abandonedCheckouts.converted', 'Converted')}</p>
                        <p className="text-2xl font-bold text-gray-900">
                            {stats.convertedSessions || 0}
                            <span className="text-sm font-normal text-gray-500 ml-1">({conversionRate}%)</span>
                        </p>
                    </div>
                    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5">
                        <p className="text-sm text-gray-500">{t('abandonedCheckouts.lostRevenue', 'Lost Revenue')}</p>
                        <p className="text-2xl font-bold text-gray-900">{formatCurrency(stats.totalAbandonedRevenue || 0)}</p>
                    </div>
                </div>
            )}

            <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-4">
                <div className="flex flex-col md:flex-row gap-4">
                    <div className="flex gap-2 flex-wrap">
                        <button
                            onClick={() => {
                                setStatusFilter('')
                                setPagination((previous) => ({ ...previous, page: 1 }))
                            }}
                            className={`px-4 py-2 text-sm font-medium rounded-lg transition-colors ${
                                !statusFilter ? 'bg-gray-900 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                            }`}
                        >
                            All
                        </button>
                        {SESSION_STATUSES.map((status) => (
                            <button
                                key={status.value}
                                onClick={() => {
                                    setStatusFilter(status.value)
                                    setPagination((previous) => ({ ...previous, page: 1 }))
                                }}
                                className={`px-4 py-2 text-sm font-medium rounded-lg transition-colors ${
                                    statusFilter === status.value ? 'bg-gray-900 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                                }`}
                            >
                                {status.label}
                            </button>
                        ))}
                    </div>

                    <div className="flex-1">
                        <input
                            type="text"
                            placeholder={t('abandonedCheckouts.searchPlaceholder', 'Search by name, phone, or email...')}
                            value={search}
                            onChange={(event) => setSearch(event.target.value)}
                            onKeyDown={(event) => {
                                if (event.key === 'Enter') {
                                    setPagination((previous) => ({ ...previous, page: 1 }))
                                }
                            }}
                            className="w-full rounded-lg border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500"
                        />
                    </div>
                </div>
            </div>

            {listError && (
                <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700" role="alert">
                    {listError}
                </div>
            )}

            <div className="bg-white rounded-xl shadow-sm border border-gray-100 overflow-hidden">
                {loading ? (
                    <div className="flex items-center justify-center py-20">
                        <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-indigo-500" />
                    </div>
                ) : checkouts.length === 0 ? (
                    <div className="text-center py-20 text-gray-500">
                        <p className="text-lg font-medium">{t('abandonedCheckouts.empty', 'No abandoned checkouts found')}</p>
                        <p className="text-sm mt-1">{t('abandonedCheckouts.emptyHint', 'Abandoned checkout sessions will appear here when customers start but do not complete checkout')}</p>
                    </div>
                ) : (
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.customer', 'Customer')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.status', 'Status')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.total', 'Cart Total')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('abandonedCheckouts.device', 'Device')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('abandonedCheckouts.source', 'Source')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('abandonedCheckouts.timeSpent', 'Time Spent')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.lastUpdate', 'Last Activity')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('common.actions', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="bg-white divide-y divide-gray-200">
                            {checkouts.map((checkout) => {
                                const statusInfo = getStatusInfo(checkout.status)

                                return (
                                    <tr
                                        key={checkout.checkoutSessionId || checkout.mongoId}
                                        className="hover:bg-gray-50 transition-colors cursor-pointer"
                                        onClick={() => void fetchDetail(checkout.checkoutSessionId)}
                                    >
                                        <td className="px-4 py-3">
                                            <div className="flex flex-col">
                                                <span className="text-sm font-medium text-gray-900">{checkout.customerName || '-'}</span>
                                                <span className="text-xs text-gray-500">{checkout.phone || checkout.email || '-'}</span>
                                            </div>
                                        </td>
                                        <td className="px-4 py-3">
                                            <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium ${statusInfo.color}`}>
                                                {statusInfo.label}
                                            </span>
                                        </td>
                                        <td className="px-4 py-3 text-sm font-medium text-gray-900">
                                            {checkout.cartTotal ? formatCurrency(checkout.cartTotal) : '-'}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600">
                                            {checkout.deviceType ? DEVICE_LABELS[checkout.deviceType] || checkout.deviceType : '-'}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600">
                                            {checkout.source || '-'}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-600">
                                            {formatTimeSpent(checkout.timeSpentSeconds)}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-gray-500">
                                            {formatRelativeTime(checkout.lastActivityAtUtc)}
                                        </td>
                                        <td className="px-4 py-3">
                                            <button
                                                onClick={(event) => {
                                                    event.stopPropagation()
                                                    void fetchDetail(checkout.checkoutSessionId)
                                                }}
                                                className="text-indigo-600 hover:text-indigo-800 text-sm font-medium"
                                            >
                                                {t('common.view', 'View')}
                                            </button>
                                        </td>
                                    </tr>
                                )
                            })}
                        </tbody>
                    </table>
                )}

                {pagination.totalPages > 1 && (
                    <div className="flex items-center justify-between px-4 py-3 bg-gray-50 border-t border-gray-200">
                        <p className="text-sm text-gray-700">
                            {t('common.showing', 'Showing')} <span className="font-medium">{((pagination.page - 1) * pagination.pageSize) + 1}</span> {t('common.to', 'to')}{' '}
                            <span className="font-medium">{Math.min(pagination.page * pagination.pageSize, pagination.totalCount)}</span> {t('common.of', 'of')}{' '}
                            <span className="font-medium">{pagination.totalCount}</span>
                        </p>
                        <div className="flex gap-2">
                            <button
                                disabled={pagination.page <= 1}
                                onClick={() => setPagination((previous) => ({ ...previous, page: previous.page - 1 }))}
                                className="px-3 py-1 text-sm rounded-md border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {t('common.previous', 'Previous')}
                            </button>
                            <button
                                disabled={pagination.page >= pagination.totalPages || !pagination.hasMore}
                                onClick={() => setPagination((previous) => ({ ...previous, page: previous.page + 1 }))}
                                className="px-3 py-1 text-sm rounded-md border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {t('common.next', 'Next')}
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {selectedCheckout && (
                <div className="fixed inset-0 z-50 flex justify-end">
                    <div className="absolute inset-0 bg-black/30" onClick={() => setSelectedCheckout(null)} />

                    <div className="relative w-full max-w-lg bg-white shadow-xl overflow-y-auto">
                        {detailLoading ? (
                            <div className="flex items-center justify-center h-full">
                                <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-indigo-500" />
                            </div>
                        ) : (
                            <div className="p-6 space-y-6">
                                <div className="flex items-center justify-between">
                                    <h2 className="text-lg font-bold text-gray-900">{t('abandonedCheckouts.details', 'Checkout Details')}</h2>
                                    <button
                                        onClick={() => setSelectedCheckout(null)}
                                        className="p-1 rounded-md hover:bg-gray-100"
                                    >
                                        Close
                                    </button>
                                </div>

                                {detailError && (
                                    <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700" role="alert">
                                        {detailError}
                                    </div>
                                )}

                                {(() => {
                                    const statusInfo = getStatusInfo(selectedCheckout.status)
                                    return (
                                        <div className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium ${statusInfo.color}`}>
                                            {statusInfo.label}
                                        </div>
                                    )
                                })()}

                                {selectedCheckout.status === 'ConvertedToOrder' && selectedCheckout.convertedOrderNumber && (
                                    <div className="bg-green-50 border border-green-200 rounded-lg p-4 text-sm text-green-800">
                                        Converted to order <strong>#{selectedCheckout.convertedOrderNumber}</strong>
                                    </div>
                                )}

                                <div className="bg-gray-50 rounded-lg p-4 space-y-2">
                                    <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">{t('common.customer', 'Customer')}</h3>
                                    <div className="grid grid-cols-2 gap-3 text-sm">
                                        <div>
                                            <span className="text-gray-500">{t('common.name', 'Name')}:</span>
                                            <p className="font-medium text-gray-900">{selectedCheckout.customerName || '-'}</p>
                                        </div>
                                        <div>
                                            <span className="text-gray-500">{t('common.phone', 'Phone')}:</span>
                                            <p className="font-medium text-gray-900">{selectedCheckout.phone || '-'}</p>
                                        </div>
                                        <div>
                                            <span className="text-gray-500">{t('common.email', 'Email')}:</span>
                                            <p className="font-medium text-gray-900">{selectedCheckout.email || '-'}</p>
                                        </div>
                                        <div>
                                            <span className="text-gray-500">{t('common.city', 'City')}:</span>
                                            <p className="font-medium text-gray-900">{selectedCheckout.city || '-'}</p>
                                        </div>
                                    </div>
                                    {selectedCheckout.address && (
                                        <div className="text-sm">
                                            <span className="text-gray-500">{t('common.address', 'Address')}:</span>
                                            <p className="font-medium text-gray-900">{selectedCheckout.address}</p>
                                        </div>
                                    )}
                                    {selectedCheckout.notes && (
                                        <div className="text-sm">
                                            <span className="text-gray-500">{t('common.notes', 'Notes')}:</span>
                                            <p className="text-gray-700 italic">{selectedCheckout.notes}</p>
                                        </div>
                                    )}
                                </div>

                                <div className="bg-gray-50 rounded-lg p-4 space-y-2">
                                    <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">{t('abandonedCheckouts.checkoutInfo', 'Checkout Info')}</h3>
                                    <div className="grid grid-cols-2 gap-3 text-sm">
                                        <div>
                                            <span className="text-gray-500">{t('common.total', 'Cart Total')}:</span>
                                            <p className="font-bold text-lg text-gray-900">
                                                {selectedCheckout.cartTotal ? formatCurrency(selectedCheckout.cartTotal) : '-'}
                                            </p>
                                        </div>
                                        <div>
                                            <span className="text-gray-500">{t('abandonedCheckouts.paymentMethod', 'Payment')}:</span>
                                            <p className="font-medium text-gray-900">{selectedCheckout.paymentMethodCode || '-'}</p>
                                        </div>
                                    </div>
                                </div>

                                {selectedCheckout.recoveryUrl && selectedCheckout.status === 'Abandoned' && (
                                    <div className="bg-indigo-50 border border-indigo-200 rounded-lg p-4">
                                        <h3 className="text-sm font-semibold text-indigo-800 mb-2">{t('abandonedCheckouts.recoveryLink', 'Recovery Link')}</h3>
                                        <div className="flex gap-2">
                                            <input
                                                type="text"
                                                readOnly
                                                value={selectedCheckout.recoveryUrl}
                                                className="flex-1 text-xs bg-white border border-indigo-200 rounded px-3 py-2 text-gray-700 font-mono"
                                            />
                                            <button
                                                onClick={() => navigator.clipboard.writeText(selectedCheckout.recoveryUrl)}
                                                className="px-3 py-2 bg-indigo-600 text-white rounded text-xs font-medium hover:bg-indigo-700 transition-colors"
                                            >
                                                {t('common.copy', 'Copy')}
                                            </button>
                                        </div>
                                    </div>
                                )}

                                <div className="bg-gray-50 rounded-lg p-4 space-y-2">
                                    <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">{t('common.timeline', 'Timeline')}</h3>
                                    <div className="text-sm space-y-1">
                                        <div className="flex justify-between">
                                            <span className="text-gray-500">{t('abandonedCheckouts.started', 'Started')}:</span>
                                            <span className="text-gray-900">{selectedCheckout.createdAtUtc ? new Date(selectedCheckout.createdAtUtc).toLocaleString() : '-'}</span>
                                        </div>
                                        <div className="flex justify-between">
                                            <span className="text-gray-500">{t('abandonedCheckouts.lastActivity', 'Last Activity')}:</span>
                                            <span className="text-gray-900">{selectedCheckout.lastActivityAtUtc ? new Date(selectedCheckout.lastActivityAtUtc).toLocaleString() : '-'}</span>
                                        </div>
                                        {selectedCheckout.timeSpentSeconds != null && (
                                            <div className="flex justify-between">
                                                <span className="text-gray-500">{t('abandonedCheckouts.duration', 'Checkout Duration')}:</span>
                                                <span className="text-indigo-600 font-medium">{formatTimeSpent(selectedCheckout.timeSpentSeconds)}</span>
                                            </div>
                                        )}
                                    </div>
                                </div>

                                {detailItems.length > 0 && (
                                    <div className="bg-gray-50 rounded-lg p-4 space-y-3">
                                        <h3 className="text-sm font-semibold text-gray-700 uppercase tracking-wider">
                                            {t('abandonedCheckouts.cartItems', 'Cart Items')} ({detailItems.length})
                                        </h3>
                                        <div className="space-y-2">
                                            {detailItems.map((item, index) => (
                                                <div key={`${item.variantId || item.productName}-${index}`} className="flex items-center gap-3 bg-white rounded-lg p-3 border border-gray-100">
                                                    {item.imageUrl ? (
                                                        <img src={item.imageUrl} alt="" className="w-12 h-12 rounded-md object-cover bg-gray-100" />
                                                    ) : (
                                                        <div className="w-12 h-12 rounded-md bg-gray-100 flex items-center justify-center text-gray-400 text-xs">
                                                            ITEM
                                                        </div>
                                                    )}
                                                    <div className="flex-1 min-w-0">
                                                        <p className="text-sm font-medium text-gray-900 truncate">{item.productName}</p>
                                                        {item.variantName && (
                                                            <p className="text-xs text-gray-500">{item.variantName}</p>
                                                        )}
                                                    </div>
                                                    <div className="text-right flex-shrink-0">
                                                        <p className="text-sm font-medium text-gray-900">{formatCurrency((item.price ?? 0) * (item.quantity ?? 0))}</p>
                                                        <p className="text-xs text-gray-500">x{item.quantity ?? 0}</p>
                                                    </div>
                                                </div>
                                            ))}
                                        </div>
                                    </div>
                                )}
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    )
}
