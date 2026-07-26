/**
 * AF-4B: Admin Affiliate Detail Page
 *
 * Header: DisplayName, ReferralCode, Status, Activate/Suspend buttons
 * Tabs: Orders | Earnings | Settlement
 *
 * RULES:
 * - All money values are pre-computed from MongoDB projections
 * - Write operations (Approve/Pay/Suspend/Activate) call IAffiliateService via API
 * - UI never computes totals or writes ledger rows
 * - Role: Owner, Admin only
 */
import { useState, useEffect, useCallback, useRef, Fragment } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../../../contexts/SettingsContext'
import adminAffiliateApi from '../../../services/adminAffiliateApi'

// ═══════════════════════════════════════════════════════════════════
// SHARED COMPONENTS
// ═══════════════════════════════════════════════════════════════════

const statusStyles = {
    Active: 'bg-emerald-100 text-emerald-800 border-emerald-200',
    Pending: 'bg-yellow-100 text-yellow-800 border-yellow-200',
    Suspended: 'bg-red-100 text-red-800 border-red-200',
    Earned: 'bg-blue-100 text-blue-800 border-blue-200',
    Approved: 'bg-emerald-100 text-emerald-800 border-emerald-200',
    Paid: 'bg-green-100 text-green-800 border-green-200',
    Reversed: 'bg-red-100 text-red-800 border-red-200',
    PartiallyReversed: 'bg-orange-100 text-orange-800 border-orange-200',
}

const STATUS_LABEL_KEYS = {
    Active: 'adminAffiliateDetail.statuses.active',
    Pending: 'adminAffiliateDetail.statuses.pending',
    Suspended: 'adminAffiliateDetail.statuses.suspended',
    Earned: 'adminAffiliateDetail.statuses.earned',
    Approved: 'adminAffiliateDetail.statuses.approved',
    Paid: 'adminAffiliateDetail.statuses.paid',
    Reversed: 'adminAffiliateDetail.statuses.reversed',
    PartiallyReversed: 'adminAffiliateDetail.statuses.partiallyReversed',
    Rejected: 'adminAffiliateDetail.statuses.rejected',
}

const ORDER_STATUS_VALUES = ['', 'Earned', 'Approved', 'Paid', 'Reversed', 'PartiallyReversed']
const PAYOUT_STATUS_VALUES = ['', 'Pending', 'Approved', 'Rejected', 'Paid']
const SETTLEMENT_ORDERS_PAGE_SIZE = 100
const AFFILIATE_DETAIL_TABS = [
    { id: 'orders', labelKey: 'adminAffiliateDetail.tabs.orders', icon: '📦' },
    { id: 'earnings', labelKey: 'adminAffiliateDetail.tabs.earnings', icon: '💰' },
    { id: 'settlement', labelKey: 'adminAffiliateDetail.tabs.settlement', icon: '⚡' },
    { id: 'withdrawals', labelKey: 'adminAffiliateDetail.tabs.withdrawals', icon: '💸' },
]

function formatDate(value, locale, fallback) {
    if (!value) return fallback
    const date = new Date(value)
    if (Number.isNaN(date.getTime())) return fallback
    return date.toLocaleDateString(locale)
}

function keepSelectedOrders(selectedOrderIds, orders) {
    const validOrderIds = new Set(orders.map(order => order.orderId))
    return new Set([...selectedOrderIds].filter(orderId => validOrderIds.has(orderId)))
}

async function fetchAllSettlementOrders(affiliateId, status) {
    const firstPage = await adminAffiliateApi.getAffiliateOrders(affiliateId, 1, SETTLEMENT_ORDERS_PAGE_SIZE, status)
    const initialOrders = firstPage?.orders || []
    const totalPages = Math.max(
        firstPage?.totalPages || Math.ceil((firstPage?.totalCount || initialOrders.length) / SETTLEMENT_ORDERS_PAGE_SIZE),
        1
    )

    if (totalPages === 1) {
        return initialOrders
    }

    const remainingPages = await Promise.all(
        Array.from({ length: totalPages - 1 }, (_, index) =>
            adminAffiliateApi.getAffiliateOrders(affiliateId, index + 2, SETTLEMENT_ORDERS_PAGE_SIZE, status)
        )
    )

    return [
        ...initialOrders,
        ...remainingPages.flatMap(response => response?.orders || []),
    ]
}

function StatusBadge({ status }) {
    const { t } = useTranslation()

    const labelKey = STATUS_LABEL_KEYS[status]
    const label = labelKey ? t(labelKey) : status

    return (
        <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${statusStyles[status] || 'bg-gray-100 text-gray-800 border-gray-200'}`}>
            {label}
        </span>
    )
}

function normalizeCurrency(currency) {
    return typeof currency === 'string' && currency.trim()
        ? currency.trim().toUpperCase()
        : undefined
}

function formatEarningsByCurrency(detail, amountKey, formatCurrency) {
    const summaries = Array.isArray(detail?.earningsByCurrency) ? detail.earningsByCurrency : []
    if (summaries.length === 0) {
        return '—'
    }

    return summaries.map(summary => (
        <div key={`${amountKey}-${summary.currency}`}>
            {formatCurrency(summary[amountKey] || 0, summary.currency)}
        </div>
    ))
}

function SkeletonCard() {
    return (
        <div className="card animate-pulse">
            <div className="h-3 w-24 bg-gray-200 rounded mb-3"></div>
            <div className="h-8 w-32 bg-gray-200 rounded mb-2"></div>
            <div className="h-3 w-16 bg-gray-100 rounded"></div>
        </div>
    )
}

function SkeletonRow() {
    return (
        <tr className="animate-pulse">
            <td className="px-4 py-3"><div className="h-4 w-20 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-16 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-20 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-12 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-24 bg-gray-200 rounded"></div></td>
        </tr>
    )
}

// ═══════════════════════════════════════════════════════════════════
// CONFIRMATION MODAL
// ═══════════════════════════════════════════════════════════════════

function ConfirmModal({ open, title, message, confirmLabel, confirmColor, onConfirm, onCancel, loading, children }) {
    const { t } = useTranslation()

    if (!open) return null
    const colors = {
        red: 'bg-red-600 hover:bg-red-700',
        green: 'bg-green-600 hover:bg-green-700',
        blue: 'bg-blue-600 hover:bg-blue-700',
        primary: 'bg-primary-600 hover:bg-primary-700',
    }
    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={onCancel}>
            <div className="fixed inset-0 bg-gray-900/40 dark:bg-black/50" />
            <div className="relative bg-white rounded-xl shadow-xl max-w-md w-full mx-4 p-6" onClick={e => e.stopPropagation()}>
                <h3 className="text-lg font-semibold text-gray-900 mb-2">{title}</h3>
                <p className="text-sm text-gray-600 mb-4">{message}</p>
                {children}
                <div className="flex justify-end gap-3 mt-6">
                    <button
                        onClick={onCancel}
                        disabled={loading}
                        className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors disabled:opacity-50"
                    >
                        {t('adminAffiliateDetail.actions.cancel')}
                    </button>
                    <button
                        onClick={onConfirm}
                        disabled={loading}
                        className={`px-4 py-2 text-sm font-medium text-gray-900 dark:text-white rounded-lg transition-colors disabled:opacity-50 ${colors[confirmColor] || colors.primary}`}
                    >
                        {loading ? (
                            <span className="flex items-center gap-2">
                                <span className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></span>
                                {t('adminAffiliateDetail.actions.processing')}
                            </span>
                        ) : confirmLabel}
                    </button>
                </div>
            </div>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// TOAST
// ═══════════════════════════════════════════════════════════════════

function Toast({ message, type, onClose }) {
    useEffect(() => {
        const t = setTimeout(onClose, 4000)
        return () => clearTimeout(t)
    }, [onClose])

    const bg = type === 'success' ? 'bg-green-600' : 'bg-red-600'
    return (
        <div className={`fixed bottom-6 right-6 z-50 ${bg} text-gray-900 dark:text-white px-6 py-3 rounded-lg shadow-xl text-sm font-medium flex items-center gap-3 animate-slide-up`}>
            <span>{type === 'success' ? '✓' : '✕'}</span>
            {message}
            <button onClick={onClose} className="ml-2 hover:opacity-75">✕</button>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// ORDERS TAB
// ═══════════════════════════════════════════════════════════════════

function OrdersTab({ affiliateId, formatCurrency }) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [page, setPage] = useState(1)
    const [statusFilter, setStatusFilter] = useState('')
    const [expandedOrder, setExpandedOrder] = useState(null)
    const requestSeq = useRef(0)
    const pageSize = 20

    const fetchOrders = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current
        if (showLoading) {
            setLoading(true)
        }
        adminAffiliateApi.getAffiliateOrders(affiliateId, page, pageSize, statusFilter)
            .then(d => {
                if (currentRequest === requestSeq.current) setData(d)
            })
            .catch(() => {
                if (currentRequest === requestSeq.current) setData(null)
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) setLoading(false)
            })
    }, [affiliateId, page, statusFilter])

    useEffect(() => { fetchOrders() }, [fetchOrders])

    useEffect(() => {
        const refetchCurrentAuthority = () => fetchOrders({ showLoading: false })
        const handleVisibility = () => {
            if (document.visibilityState === 'visible') refetchCurrentAuthority()
        }

        window.addEventListener('focus', refetchCurrentAuthority)
        window.addEventListener('online', refetchCurrentAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            requestSeq.current += 1
            window.removeEventListener('focus', refetchCurrentAuthority)
            window.removeEventListener('online', refetchCurrentAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [fetchOrders])

    if (loading) {
        return (
            <div className="card">
                <table className="min-w-full">
                    <thead><tr>
                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.order')}</th>
                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.status')}</th>
                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.commission')}</th>
                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.currency')}</th>
                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.date')}</th>
                    </tr></thead>
                    <tbody>{[...Array(5)].map((_, i) => <SkeletonRow key={i} />)}</tbody>
                </table>
            </div>
        )
    }

    if (!data?.orders?.length) {
        return (
            <div className="flex flex-col items-center justify-center py-16 text-center">
                <div className="text-5xl mb-4">📋</div>
                <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('adminAffiliateDetail.orders.empty.title')}</h3>
                <p className="text-gray-500 text-sm max-w-sm">
                    {statusFilter
                        ? t('adminAffiliateDetail.orders.empty.filteredDescription')
                        : t('adminAffiliateDetail.orders.empty.description')}
                </p>
            </div>
        )
    }

    return (
        <div className="space-y-4">
            {/* Filter */}
            <div className="flex items-center justify-between">
                <p className="text-sm text-gray-600">
                    {t('adminAffiliateDetail.orders.totalCount', { count: data.totalCount })}
                </p>
                <select
                    id="admin-affiliate-orders-status-filter"
                    value={statusFilter}
                    onChange={(e) => { setStatusFilter(e.target.value); setPage(1) }}
                    className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 outline-none bg-white"
                >
                    <option value="">{t('adminAffiliateDetail.filters.allStatuses')}</option>
                    {ORDER_STATUS_VALUES.filter(Boolean).map(value => (
                        <option key={value} value={value}>{t(STATUS_LABEL_KEYS[value])}</option>
                    ))}
                </select>
            </div>

            <div className="card overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('adminAffiliateDetail.orders.table.order')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('adminAffiliateDetail.orders.table.status')}</th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">{t('adminAffiliateDetail.orders.table.commission')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('adminAffiliateDetail.orders.table.currency')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">{t('adminAffiliateDetail.orders.table.date')}</th>
                                <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">{t('adminAffiliateDetail.orders.table.details')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200 bg-white">
                            {data.orders.map(order => (
                                <Fragment key={order.orderId}>
                                    <tr className="hover:bg-gray-50 cursor-pointer" onClick={() => setExpandedOrder(expandedOrder === order.orderId ? null : order.orderId)}>
                                        <td className="px-4 py-3 text-sm font-medium text-gray-900">{order.orderNumber}</td>
                                        <td className="px-4 py-3"><StatusBadge status={order.status} /></td>
                                        <td className="px-4 py-3 text-sm text-right font-semibold text-gray-900">{formatCurrency(order.totalCommission, order.currency)}</td>
                                        <td className="px-4 py-3 text-sm text-gray-600">{order.currency}</td>
                                        <td className="px-4 py-3 text-sm text-gray-600">{formatDate(order.createdAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable'))}</td>
                                        <td className="px-4 py-3 text-center">
                                            <button className="text-gray-600 dark:text-gray-400 hover:text-gray-600">
                                                <svg className={`w-5 h-5 transition-transform ${expandedOrder === order.orderId ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
                                                </svg>
                                            </button>
                                        </td>
                                    </tr>
                                    {expandedOrder === order.orderId && (
                                        <tr>
                                            <td colSpan={6} className="px-4 py-0">
                                                <div className="py-4 bg-gray-50 -mx-4 px-8 border-y border-gray-100">
                                                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-3">{t('adminAffiliateDetail.orders.items.title')}</h4>
                                                    <div className="space-y-2">
                                                        {order.items.map(item => (
                                                            <div key={item.orderItemId} className={`flex items-center justify-between p-3 rounded-lg ${item.isReversed ? 'bg-red-50 border border-red-100' : 'bg-white border border-gray-100'}`}>
                                                                <div className="flex-1">
                                                                    <p className={`text-sm font-medium ${item.isReversed ? 'text-red-700 line-through' : 'text-gray-900'}`}>
                                                                        {item.productName}
                                                                    </p>
                                                                    {item.variantName && <p className="text-xs text-gray-500">{item.variantName}</p>}
                                                                </div>
                                                                <div className="flex items-center gap-4 text-sm">
                                                                    <span className="text-gray-500">{t('adminAffiliateDetail.orders.items.paid', { amount: formatCurrency(item.paidAmount, order.currency) })}</span>
                                                                    <span className="font-semibold text-gray-900">{t('adminAffiliateDetail.orders.items.commission', { amount: formatCurrency(item.commissionAmount, order.currency) })}</span>
                                                                    <StatusBadge status={item.isReversed ? 'Reversed' : item.settlementStatus} />
                                                                </div>
                                                            </div>
                                                        ))}
                                                    </div>
                                                </div>
                                            </td>
                                        </tr>
                                    )}
                                </Fragment>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Pagination */}
            {data.totalPages > 1 && (
                <div className="flex items-center justify-between px-2">
                    <p className="text-sm text-gray-600">
                        {t('adminAffiliateDetail.orders.pagination.showing', {
                            from: (page - 1) * pageSize + 1,
                            to: Math.min(page * pageSize, data.totalCount),
                            total: data.totalCount,
                        })}
                    </p>
                    <div className="flex gap-2">
                        <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} className="btn-secondary text-sm disabled:opacity-50">
                            {t('adminAffiliateDetail.actions.previous')}
                        </button>
                        <span className="flex items-center px-3 text-sm text-gray-600">{t('adminAffiliateDetail.orders.pagination.page', { page, totalPages: data.totalPages })}</span>
                        <button onClick={() => setPage(p => Math.min(data.totalPages, p + 1))} disabled={page === data.totalPages} className="btn-secondary text-sm disabled:opacity-50">
                            {t('adminAffiliateDetail.actions.next')}
                        </button>
                    </div>
                </div>
            )}
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// EARNINGS TAB
// ═══════════════════════════════════════════════════════════════════

function EarningsTab({ affiliateId, formatCurrency }) {
    const { t } = useTranslation()
    const [data, setData] = useState([])
    const [loading, setLoading] = useState(true)
    const requestSeq = useRef(0)

    const fetchEarnings = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current
        if (showLoading) {
            setLoading(true)
        }
        adminAffiliateApi.getAffiliateEarnings(affiliateId)
            .then(d => {
                if (currentRequest === requestSeq.current) setData(d)
            })
            .catch(() => {
                if (currentRequest === requestSeq.current) setData([])
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) setLoading(false)
            })
    }, [affiliateId])

    useEffect(() => { fetchEarnings() }, [fetchEarnings])

    useEffect(() => {
        const refetchCurrentAuthority = () => fetchEarnings({ showLoading: false })
        const handleVisibility = () => {
            if (document.visibilityState === 'visible') refetchCurrentAuthority()
        }

        window.addEventListener('focus', refetchCurrentAuthority)
        window.addEventListener('online', refetchCurrentAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            requestSeq.current += 1
            window.removeEventListener('focus', refetchCurrentAuthority)
            window.removeEventListener('online', refetchCurrentAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [fetchEarnings])

    if (loading) {
        return <div className="grid grid-cols-1 gap-6">{[...Array(2)].map((_, i) => <SkeletonCard key={i} />)}</div>
    }

    if (!data.length) {
        return (
            <div className="flex flex-col items-center justify-center py-16 text-center">
                <div className="text-5xl mb-4">💹</div>
                <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('adminAffiliateDetail.earnings.empty.title')}</h3>
                <p className="text-gray-500 text-sm max-w-sm">{t('adminAffiliateDetail.earnings.empty.description')}</p>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {data.map(summary => (
                <div key={summary.currency} className="card">
                    <div className="flex items-center justify-between mb-6">
                        <h3 className="text-lg font-semibold text-gray-900">{summary.currency}</h3>
                        <span className="px-3 py-1 bg-gray-100 rounded-full text-sm font-medium text-gray-600">{summary.currency}</span>
                    </div>
                    <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
                        <div className="p-4 bg-blue-50 rounded-lg">
                            <p className="text-xs text-blue-600 font-medium uppercase">{t('adminAffiliateDetail.earnings.cards.totalEarned')}</p>
                            <p className="text-xl font-bold text-blue-900 mt-1">{formatCurrency(summary.totalEarned, summary.currency)}</p>
                        </div>
                        <div className="p-4 bg-emerald-50 rounded-lg">
                            <p className="text-xs text-emerald-600 font-medium uppercase">{t('adminAffiliateDetail.earnings.cards.approved')}</p>
                            <p className="text-xl font-bold text-emerald-900 mt-1">{formatCurrency(summary.totalApproved, summary.currency)}</p>
                        </div>
                        <div className="p-4 bg-green-50 rounded-lg">
                            <p className="text-xs text-green-600 font-medium uppercase">{t('adminAffiliateDetail.earnings.cards.paid')}</p>
                            <p className="text-xl font-bold text-green-900 mt-1">{formatCurrency(summary.totalPaid, summary.currency)}</p>
                        </div>
                        <div className="p-4 bg-red-50 rounded-lg">
                            <p className="text-xs text-red-600 font-medium uppercase">{t('adminAffiliateDetail.earnings.cards.reversed')}</p>
                            <p className="text-xl font-bold text-red-900 mt-1">{formatCurrency(summary.totalReversed, summary.currency)}</p>
                        </div>
                        <div className="p-4 bg-purple-50 rounded-lg">
                            <p className="text-xs text-purple-600 font-medium uppercase">{t('adminAffiliateDetail.earnings.cards.netEarnings')}</p>
                            <p className="text-xl font-bold text-purple-900 mt-1">{formatCurrency(summary.netEarnings, summary.currency)}</p>
                        </div>
                    </div>

                    {/* Progress Bar */}
                    <div className="mt-6">
                        <div className="flex items-center justify-between text-xs text-gray-500 mb-2">
                            <span>{t('adminAffiliateDetail.earnings.progress.title')}</span>
                            <span>{t('adminAffiliateDetail.earnings.progress.paidPercent', { percent: summary.totalEarned > 0 ? Math.round((summary.totalPaid / summary.totalEarned) * 100) : 0 })}</span>
                        </div>
                        <div className="w-full bg-gray-100 rounded-full h-3 overflow-hidden">
                            <div className="h-full flex">
                                {summary.totalEarned > 0 && (
                                    <>
                                        <div className="bg-green-500 transition-all" style={{ width: `${(summary.totalPaid / summary.totalEarned) * 100}%` }} />
                                        <div className="bg-emerald-300 transition-all" style={{ width: `${((summary.totalApproved - summary.totalPaid) / summary.totalEarned) * 100}%` }} />
                                    </>
                                )}
                            </div>
                        </div>
                        <div className="flex items-center gap-4 mt-2 text-xs text-gray-500">
                            <span className="flex items-center gap-1"><span className="w-3 h-3 bg-green-500 rounded-full inline-block"></span> {t('adminAffiliateDetail.earnings.progress.legendPaid')}</span>
                            <span className="flex items-center gap-1"><span className="w-3 h-3 bg-emerald-300 rounded-full inline-block"></span> {t('adminAffiliateDetail.earnings.progress.legendApproved')}</span>
                            <span className="flex items-center gap-1"><span className="w-3 h-3 bg-gray-100 rounded-full inline-block"></span> {t('adminAffiliateDetail.earnings.progress.legendPending')}</span>
                        </div>
                    </div>
                </div>
            ))}
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// SETTLEMENT TAB
// ═══════════════════════════════════════════════════════════════════

function SettlementTab({ affiliateId, formatCurrency, onAction, cashboxBalance = 0 }) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const [earnedOrders, setEarnedOrders] = useState([])
    const [approvedOrders, setApprovedOrders] = useState([])
    const [loading, setLoading] = useState(true)
    const [selectedEarned, setSelectedEarned] = useState(new Set())
    const [selectedApproved, setSelectedApproved] = useState(new Set())
    const requestSeq = useRef(0)

    const fetchOrders = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current
        if (showLoading) {
            setLoading(true)
        }
        Promise.all([
            fetchAllSettlementOrders(affiliateId, 'Earned'),
            fetchAllSettlementOrders(affiliateId, 'Approved'),
        ])
            .then(([earned, approved]) => {
                if (currentRequest === requestSeq.current) {
                    setEarnedOrders(earned)
                    setApprovedOrders(approved)
                    setSelectedEarned(prev => keepSelectedOrders(prev, earned))
                    setSelectedApproved(prev => keepSelectedOrders(prev, approved))
                }
            })
            .catch(() => {
                if (currentRequest === requestSeq.current) {
                    setEarnedOrders([])
                    setApprovedOrders([])
                    setSelectedEarned(new Set())
                    setSelectedApproved(new Set())
                }
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) setLoading(false)
            })
    }, [affiliateId])

    useEffect(() => { fetchOrders() }, [fetchOrders])

    useEffect(() => {
        const refetchCurrentAuthority = () => fetchOrders({ showLoading: false })
        const handleVisibility = () => {
            if (document.visibilityState === 'visible') refetchCurrentAuthority()
        }

        window.addEventListener('focus', refetchCurrentAuthority)
        window.addEventListener('online', refetchCurrentAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            requestSeq.current += 1
            window.removeEventListener('focus', refetchCurrentAuthority)
            window.removeEventListener('online', refetchCurrentAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [fetchOrders])

    const toggleEarnedSelection = (orderId) => {
        setSelectedEarned(prev => {
            const next = new Set(prev)
            next.has(orderId) ? next.delete(orderId) : next.add(orderId)
            return next
        })
    }

    const toggleApprovedSelection = (orderId) => {
        setSelectedApproved(prev => {
            const next = new Set(prev)
            next.has(orderId) ? next.delete(orderId) : next.add(orderId)
            return next
        })
    }

    const handleApproveSelected = () => {
        onAction('approve', [...selectedEarned], false, () => {
            setSelectedEarned(new Set())
            fetchOrders()
        })
    }

    const handleApproveAll = () => {
        onAction('approve', [], true, () => {
            setSelectedEarned(new Set())
            fetchOrders()
        })
    }

    const handlePaySelected = () => {
        onAction('pay', [...selectedApproved], false, () => {
            setSelectedApproved(new Set())
            fetchOrders()
        })
    }

    const handlePayAll = () => {
        onAction('pay', [], true, () => {
            setSelectedApproved(new Set())
            fetchOrders()
        })
    }

    if (loading) {
        return <div className="space-y-6">{[...Array(2)].map((_, i) => <SkeletonCard key={i} />)}</div>
    }

    return (
        <div className="space-y-8">
            {/* Earned → Approve Section */}
            <div className="card">
                <div className="flex items-center justify-between mb-4">
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">{t('adminAffiliateDetail.settlement.pendingApproval.title')}</h3>
                        <p className="text-sm text-gray-500 mt-1">{t('adminAffiliateDetail.settlement.pendingApproval.description')}</p>
                    </div>
                    <div className="flex gap-2">
                        {selectedEarned.size > 0 && (
                            <button
                                onClick={handleApproveSelected}
                                className="px-4 py-2 bg-emerald-600 text-gray-900 dark:text-white rounded-lg text-sm font-medium hover:bg-emerald-700 transition-colors"
                            >
                                {t('adminAffiliateDetail.settlement.pendingApproval.approveSelected', { count: selectedEarned.size })}
                            </button>
                        )}
                        {earnedOrders.length > 0 && (
                            <button
                                onClick={handleApproveAll}
                                className="px-4 py-2 bg-emerald-100 text-emerald-700 rounded-lg text-sm font-medium hover:bg-emerald-200 transition-colors"
                            >
                                {t('adminAffiliateDetail.settlement.pendingApproval.approveAll', { count: earnedOrders.length })}
                            </button>
                        )}
                    </div>
                </div>

                {earnedOrders.length === 0 ? (
                    <div className="text-center py-8 text-gray-600 dark:text-gray-400">
                        <p className="text-sm">{t('adminAffiliateDetail.settlement.pendingApproval.empty')}</p>
                    </div>
                ) : (
                    <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                            <thead className="bg-gray-50">
                                <tr>
                                    <th className="px-4 py-3 text-center w-12">
                                        <input
                                            type="checkbox"
                                            checked={selectedEarned.size === earnedOrders.length && earnedOrders.length > 0}
                                            onChange={() => {
                                                if (selectedEarned.size === earnedOrders.length) {
                                                    setSelectedEarned(new Set())
                                                } else {
                                                    setSelectedEarned(new Set(earnedOrders.map(o => o.orderId)))
                                                }
                                            }}
                                            className="rounded border-gray-300"
                                        />
                                    </th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.order')}</th>
                                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.commission')}</th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.currency')}</th>
                                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.date')}</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-200 bg-white">
                                {earnedOrders.map(order => (
                                    <tr key={order.orderId} className="hover:bg-blue-50/50">
                                        <td className="px-4 py-3 text-center">
                                            <input
                                                type="checkbox"
                                                checked={selectedEarned.has(order.orderId)}
                                                onChange={() => toggleEarnedSelection(order.orderId)}
                                                className="rounded border-gray-300"
                                            />
                                        </td>
                                        <td className="px-4 py-3 text-sm font-medium text-gray-900">{order.orderNumber}</td>
                                        <td className="px-4 py-3 text-sm text-right font-semibold text-blue-700">{formatCurrency(order.totalCommission, order.currency)}</td>
                                        <td className="px-4 py-3 text-sm text-gray-600">{order.currency}</td>
                                        <td className="px-4 py-3 text-sm text-gray-600">{formatDate(order.createdAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable'))}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>

            {/* Approved → Pay Section */}
            <div className="card">
                <div className="flex items-center justify-between mb-4">
                    <div>
                        <h3 className="text-lg font-semibold text-gray-900">{t('adminAffiliateDetail.settlement.readyForPayment.title')}</h3>
                        <p className="text-sm text-gray-500 mt-1">{t('adminAffiliateDetail.settlement.readyForPayment.description')}</p>
                    </div>
                    <div className="flex items-center gap-3">
                        <div className="text-right">
                            <p className="text-xs text-gray-500 uppercase font-medium">{t('adminAffiliateDetail.settlement.readyForPayment.cashboxBalance')}</p>
                            <p className={`text-sm font-bold ${cashboxBalance > 0 ? 'text-green-700' : 'text-red-600'}`}>
                                {formatCurrency(cashboxBalance, 'EGP')}
                            </p>
                        </div>
                        <div className="flex gap-2">
                            {selectedApproved.size > 0 && (
                                <button
                                    onClick={handlePaySelected}
                                    disabled={cashboxBalance <= 0}
                                    className="px-4 py-2 bg-green-600 text-gray-900 dark:text-white rounded-lg text-sm font-medium hover:bg-green-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                                    title={cashboxBalance <= 0 ? t('adminAffiliateDetail.settlement.readyForPayment.insufficientCashbox') : ''}
                                >
                                    {t('adminAffiliateDetail.settlement.readyForPayment.paySelected', { count: selectedApproved.size })}
                                </button>
                            )}
                            {approvedOrders.length > 0 && (
                                <button
                                    onClick={handlePayAll}
                                    disabled={cashboxBalance <= 0}
                                    className="px-4 py-2 bg-green-100 text-green-700 rounded-lg text-sm font-medium hover:bg-green-200 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                                    title={cashboxBalance <= 0 ? t('adminAffiliateDetail.settlement.readyForPayment.insufficientCashbox') : ''}
                                >
                                    {t('adminAffiliateDetail.settlement.readyForPayment.payAll', { count: approvedOrders.length })}
                                </button>
                            )}
                        </div>
                    </div>
                </div>

                {approvedOrders.length === 0 ? (
                    <div className="text-center py-8 text-gray-600 dark:text-gray-400">
                        <p className="text-sm">{t('adminAffiliateDetail.settlement.readyForPayment.empty')}</p>
                    </div>
                ) : (
                    <>
                        {cashboxBalance <= 0 && (
                            <div className="mb-4 p-3 bg-amber-50 border border-amber-200 rounded-lg flex items-center gap-2 text-sm text-amber-800">
                                <span className="text-lg">⚠️</span>
                                <span>{t('adminAffiliateDetail.settlement.readyForPayment.warning')}</span>
                            </div>
                        )}
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th className="px-4 py-3 text-center w-12">
                                            <input
                                                type="checkbox"
                                                checked={selectedApproved.size === approvedOrders.length && approvedOrders.length > 0}
                                                onChange={() => {
                                                    if (selectedApproved.size === approvedOrders.length) {
                                                        setSelectedApproved(new Set())
                                                    } else {
                                                        setSelectedApproved(new Set(approvedOrders.map(o => o.orderId)))
                                                    }
                                                }}
                                                className="rounded border-gray-300"
                                            />
                                        </th>
                                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.order')}</th>
                                        <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.commission')}</th>
                                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.currency')}</th>
                                        <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.orders.table.date')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-gray-200 bg-white">
                                    {approvedOrders.map(order => (
                                        <tr key={order.orderId} className="hover:bg-green-50/50">
                                            <td className="px-4 py-3 text-center">
                                                <input
                                                    type="checkbox"
                                                    checked={selectedApproved.has(order.orderId)}
                                                    onChange={() => toggleApprovedSelection(order.orderId)}
                                                    className="rounded border-gray-300"
                                                />
                                            </td>
                                            <td className="px-4 py-3 text-sm font-medium text-gray-900">{order.orderNumber}</td>
                                            <td className="px-4 py-3 text-sm text-right font-semibold text-green-700">{formatCurrency(order.totalCommission, order.currency)}</td>
                                            <td className="px-4 py-3 text-sm text-gray-600">{order.currency}</td>
                                            <td className="px-4 py-3 text-sm text-gray-600">{formatDate(order.createdAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable'))}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </>
                )}
            </div>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// AF-38: WITHDRAWALS TAB (Admin)
// ═══════════════════════════════════════════════════════════════════

function WithdrawalsTab({ affiliateId, formatCurrency, onToast }) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [page, setPage] = useState(1)
    const [statusFilter, setStatusFilter] = useState('')
    const [actionLoading, setActionLoading] = useState(null) // requestId being acted upon
    const [actionModal, setActionModal] = useState(null) // { type, requestId, affiliateName }
    const [actionNotes, setActionNotes] = useState('')
    const [paymentRef, setPaymentRef] = useState('')
    const requestSeq = useRef(0)

    const fetchData = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current
        if (showLoading) {
            setLoading(true)
        }
        // AF-38C: FIX - Use affiliate-scoped endpoint
        adminAffiliateApi.getAffiliateWithdrawals(affiliateId, statusFilter, page, 20)
            .then(d => {
                if (currentRequest === requestSeq.current) setData(d)
            })
            .catch(() => {
                if (currentRequest === requestSeq.current) setData(null)
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) setLoading(false)
            })
    }, [affiliateId, statusFilter, page])

    useEffect(() => { fetchData() }, [fetchData])

    useEffect(() => {
        const refetchCurrentAuthority = () => fetchData({ showLoading: false })
        const handleVisibility = () => {
            if (document.visibilityState === 'visible') refetchCurrentAuthority()
        }

        window.addEventListener('focus', refetchCurrentAuthority)
        window.addEventListener('online', refetchCurrentAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            requestSeq.current += 1
            window.removeEventListener('focus', refetchCurrentAuthority)
            window.removeEventListener('online', refetchCurrentAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [fetchData])

    const handleAction = async () => {
        if (!actionModal) return
        setActionLoading(actionModal.requestId)
        try {
            if (actionModal.type === 'approve') {
                await adminAffiliateApi.approvePayoutRequest(actionModal.requestId, actionNotes)
                onToast?.(t('adminAffiliateDetail.withdrawals.toast.approved'), 'success')
            } else if (actionModal.type === 'reject') {
                await adminAffiliateApi.rejectPayoutRequest(actionModal.requestId, actionNotes)
                onToast?.(t('adminAffiliateDetail.withdrawals.toast.rejected'), 'success')
            } else if (actionModal.type === 'mark-paid') {
                await adminAffiliateApi.markPayoutPaid(actionModal.requestId, paymentRef, actionNotes)
                onToast?.(t('adminAffiliateDetail.withdrawals.toast.markedPaid'), 'success')
            }
            setActionModal(null)
            setActionNotes('')
            setPaymentRef('')
            fetchData()
        } catch (err) {
            onToast?.(err?.response?.data?.message || t('adminAffiliateDetail.toast.actionFailed'), 'error')
        } finally {
            setActionLoading(null)
        }
    }

    if (loading) {
        return <div className="space-y-4">{[...Array(3)].map((_, i) => <SkeletonRow key={i} />)}</div>
    }

    if (!data?.items?.length) {
        return (
            <div className="flex flex-col items-center justify-center py-16 text-center">
                <div className="text-5xl mb-4">💸</div>
                <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('adminAffiliateDetail.withdrawals.empty.title')}</h3>
                <p className="text-gray-500 text-sm max-w-sm">
                    {statusFilter
                        ? t('adminAffiliateDetail.withdrawals.empty.filteredDescription')
                        : t('adminAffiliateDetail.withdrawals.empty.description')}
                </p>
            </div>
        )
    }

    return (
        <div className="space-y-4">
            {/* Filter */}
            <div className="flex items-center justify-between">
                <p className="text-sm text-gray-600">{t('adminAffiliateDetail.withdrawals.totalCount', { count: data.totalCount })}</p>
                <select
                    value={statusFilter}
                    onChange={(e) => { setStatusFilter(e.target.value); setPage(1) }}
                    className="px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 outline-none bg-white"
                >
                    <option value="">{t('adminAffiliateDetail.filters.allStatuses')}</option>
                    {PAYOUT_STATUS_VALUES.filter(Boolean).map(value => (
                        <option key={value} value={value}>{t(STATUS_LABEL_KEYS[value])}</option>
                    ))}
                </select>
            </div>

            <div className="card overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.withdrawals.table.affiliate')}</th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.withdrawals.table.amount')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.withdrawals.table.status')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.withdrawals.table.date')}</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.withdrawals.table.reference')}</th>
                                <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase">{t('adminAffiliateDetail.withdrawals.table.actions')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200 bg-white">
                            {data.items.map(req => (
                                <tr key={req.id} className="hover:bg-gray-50">
                                    <td className="px-4 py-3 text-sm font-medium text-gray-900">{req.affiliateName}</td>
                                    <td className="px-4 py-3 text-sm text-right font-semibold text-gray-900">
                                        {formatCurrency(req.requestedAmount, normalizeCurrency(req.currency))}
                                        {req.approvedAmount != null && req.approvedAmount !== req.requestedAmount && (
                                            <span className="text-xs text-gray-500 block">{t('adminAffiliateDetail.withdrawals.table.approvedAmount', { amount: formatCurrency(req.approvedAmount, normalizeCurrency(req.currency)) })}</span>
                                        )}
                                        {req.remainingAmount != null && req.remainingAmount < req.requestedAmount && req.remainingAmount > 0 && req.status !== 'Rejected' && (
                                            <span className="text-xs text-orange-500 block">{t('adminAffiliateDetail.withdrawals.table.remainingAmount', { amount: formatCurrency(req.remainingAmount, normalizeCurrency(req.currency)) })}</span>
                                        )}
                                    </td>
                                    <td className="px-4 py-3"><StatusBadge status={req.status} /></td>
                                    <td className="px-4 py-3 text-sm text-gray-600">{formatDate(req.requestedAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable'))}</td>
                                    <td className="px-4 py-3 text-sm text-gray-500 font-mono truncate max-w-[150px]">{req.paymentReference || t('adminAffiliateDetail.labels.emDash')}</td>
                                    <td className="px-4 py-3 text-center">
                                        <div className="flex items-center justify-center gap-1">
                                            {req.status === 'Pending' && (
                                                <button
                                                    onClick={() => setActionModal({ type: 'approve', requestId: req.id, affiliateName: req.affiliateName })}
                                                    disabled={actionLoading === req.id}
                                                    className="px-2.5 py-1 bg-emerald-100 text-emerald-700 rounded text-xs font-medium hover:bg-emerald-200 transition-colors disabled:opacity-50"
                                                >
                                                    {t('adminAffiliateDetail.withdrawals.actions.approve')}
                                                </button>
                                            )}
                                            {(req.status === 'Pending' || req.status === 'Approved') && (
                                                <button
                                                    onClick={() => setActionModal({ type: 'reject', requestId: req.id, affiliateName: req.affiliateName })}
                                                    disabled={actionLoading === req.id}
                                                    className="px-2.5 py-1 bg-red-100 text-red-700 rounded text-xs font-medium hover:bg-red-200 transition-colors disabled:opacity-50"
                                                >
                                                    {t('adminAffiliateDetail.withdrawals.actions.reject')}
                                                </button>
                                            )}
                                            {req.status === 'Approved' && (
                                                <button
                                                    onClick={() => setActionModal({ type: 'mark-paid', requestId: req.id, affiliateName: req.affiliateName })}
                                                    disabled={actionLoading === req.id}
                                                    className="px-2.5 py-1 bg-green-100 text-green-700 rounded text-xs font-medium hover:bg-green-200 transition-colors disabled:opacity-50"
                                                >
                                                    {t('adminAffiliateDetail.withdrawals.actions.markPaid')}
                                                </button>
                                            )}
                                            {(req.status === 'Rejected' || req.status === 'Paid') && (
                                                <span className="text-xs text-gray-400">{t('adminAffiliateDetail.labels.emDash')}</span>
                                            )}
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Pagination */}
            {data.totalPages > 1 && (
                <div className="flex items-center justify-between px-2">
                    <p className="text-sm text-gray-600">{t('adminAffiliateDetail.withdrawals.pagination.page', { page, totalPages: data.totalPages })}</p>
                    <div className="flex gap-2">
                        <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} className="btn-secondary text-sm disabled:opacity-50">{t('adminAffiliateDetail.actions.previous')}</button>
                        <button onClick={() => setPage(p => Math.min(data.totalPages, p + 1))} disabled={page === data.totalPages} className="btn-secondary text-sm disabled:opacity-50">{t('adminAffiliateDetail.actions.next')}</button>
                    </div>
                </div>
            )}

            {/* Action Modal */}
            <ConfirmModal
                open={!!actionModal}
                title={
                    actionModal?.type === 'approve' ? t('adminAffiliateDetail.withdrawals.modal.approveTitle') :
                        actionModal?.type === 'reject' ? t('adminAffiliateDetail.withdrawals.modal.rejectTitle') :
                            t('adminAffiliateDetail.withdrawals.modal.markPaidTitle')
                }
                message={t('adminAffiliateDetail.withdrawals.modal.message', {
                    action: actionModal?.type === 'approve'
                        ? t('adminAffiliateDetail.withdrawals.modal.actions.approve')
                        : actionModal?.type === 'reject'
                            ? t('adminAffiliateDetail.withdrawals.modal.actions.reject')
                            : t('adminAffiliateDetail.withdrawals.modal.actions.markPaid'),
                    affiliateName: actionModal?.affiliateName,
                })}
                confirmLabel={actionModal?.type === 'approve' ? t('adminAffiliateDetail.withdrawals.actions.approve') : actionModal?.type === 'reject' ? t('adminAffiliateDetail.withdrawals.actions.reject') : t('adminAffiliateDetail.withdrawals.actions.markPaid')}
                confirmColor={actionModal?.type === 'reject' ? 'red' : 'green'}
                onConfirm={handleAction}
                onCancel={() => { setActionModal(null); setActionNotes(''); setPaymentRef('') }}
                loading={!!actionLoading}
            >
                {actionModal?.type === 'mark-paid' && (
                    <div className="mt-3">
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('adminAffiliateDetail.withdrawals.modal.paymentReference')}</label>
                        <input
                            type="text"
                            value={paymentRef}
                            onChange={e => setPaymentRef(e.target.value)}
                            placeholder={t('adminAffiliateDetail.withdrawals.modal.paymentReferencePlaceholder')}
                            className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-green-500 focus:border-green-500 outline-none"
                        />
                    </div>
                )}
                <div className="mt-3">
                    <label className="block text-sm font-medium text-gray-700 mb-1">{t('adminAffiliateDetail.withdrawals.modal.adminNotes')}</label>
                    <textarea
                        value={actionNotes}
                        onChange={e => setActionNotes(e.target.value)}
                        placeholder={t('adminAffiliateDetail.withdrawals.modal.adminNotesPlaceholder')}
                        rows={2}
                        className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 outline-none"
                    />
                </div>
            </ConfirmModal>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// MAIN DETAIL PAGE
// ═══════════════════════════════════════════════════════════════════

export default function AdminAffiliateDetail() {
    const { id } = useParams()
    const navigate = useNavigate()
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const { formatCurrency } = useSettings()
    const [activeTab, setActiveTab] = useState('orders')
    const [detail, setDetail] = useState(null)
    const [loading, setLoading] = useState(true)
    const [toast, setToast] = useState(null)

    // Modal state
    const [modal, setModal] = useState({ open: false, title: '', message: '', confirmLabel: '', confirmColor: 'primary', action: null })
    const [modalLoading, setModalLoading] = useState(false)
    const [suspendReason, setSuspendReason] = useState('')
    const [suspendReasonError, setSuspendReasonError] = useState('')
    const requestSeq = useRef(0)

    const fetchDetail = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current
        if (showLoading) {
            setLoading(true)
        }
        adminAffiliateApi.getAffiliateDetail(id)
            .then(d => {
                if (currentRequest === requestSeq.current) setDetail(d)
            })
            .catch(() => {
                if (currentRequest === requestSeq.current) setDetail(null)
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) setLoading(false)
            })
    }, [id])

    useEffect(() => { fetchDetail() }, [fetchDetail])

    useEffect(() => {
        const refetchCurrentAuthority = () => fetchDetail({ showLoading: false })
        const handleVisibility = () => {
            if (document.visibilityState === 'visible') refetchCurrentAuthority()
        }

        window.addEventListener('focus', refetchCurrentAuthority)
        window.addEventListener('online', refetchCurrentAuthority)
        document.addEventListener('visibilitychange', handleVisibility)

        return () => {
            requestSeq.current += 1
            window.removeEventListener('focus', refetchCurrentAuthority)
            window.removeEventListener('online', refetchCurrentAuthority)
            document.removeEventListener('visibilitychange', handleVisibility)
        }
    }, [fetchDetail])

    const showToast = (message, type = 'success') => {
        setToast({ message, type })
    }

    // ─── Suspend ────────────────────────────────────────────────────
    const handleSuspendClick = () => {
        setSuspendReason('')
        setSuspendReasonError('')
        setModal({
            open: true,
            title: t('adminAffiliateDetail.modals.suspend.title'),
            message: t('adminAffiliateDetail.modals.suspend.message', { name: detail?.displayName }),
            confirmLabel: t('adminAffiliateDetail.actions.suspend'),
            confirmColor: 'red',
            action: 'suspend',
        })
    }

    // ─── Activate ────────────────────────────────────────────────────
    const handleActivateClick = () => {
        setModal({
            open: true,
            title: t('adminAffiliateDetail.modals.activate.title'),
            message: t('adminAffiliateDetail.modals.activate.message', { name: detail?.displayName }),
            confirmLabel: t('adminAffiliateDetail.actions.activate'),
            confirmColor: 'green',
            action: 'activate',
        })
    }

    // ─── Settlement Action (from SettlementTab) ────────────────────
    const handleSettlementAction = (type, orderIds, applyAll, onSuccess) => {
        const isApprove = type === 'approve'
        setModal({
            open: true,
            title: isApprove ? t('adminAffiliateDetail.modals.approveCommissions.title') : t('adminAffiliateDetail.modals.recordPayment.title'),
            message: applyAll
                ? isApprove
                    ? t('adminAffiliateDetail.modals.approveCommissions.allMessage')
                    : t('adminAffiliateDetail.modals.recordPayment.allMessage')
                : isApprove
                    ? t('adminAffiliateDetail.modals.approveCommissions.selectedMessage', { count: orderIds.length })
                    : t('adminAffiliateDetail.modals.recordPayment.selectedMessage', { count: orderIds.length }),
            confirmLabel: isApprove ? t('adminAffiliateDetail.actions.approve') : t('adminAffiliateDetail.actions.recordPayment'),
            confirmColor: isApprove ? 'green' : 'blue',
            action: () => performSettlement(type, orderIds, applyAll, onSuccess),
        })
    }

    const performSettlement = async (type, orderIds, applyAll, onSuccess) => {
        setModalLoading(true)
        try {
            const fn = type === 'approve'
                ? adminAffiliateApi.approveCommissions
                : adminAffiliateApi.recordPayment

            const result = await fn(id, orderIds, applyAll)

            setModal(m => ({ ...m, open: false }))
            showToast(
                result.failedCount > 0
                    ? t('adminAffiliateDetail.toast.settlementProcessedWithFailures', {
                        processedCount: result.processedCount,
                        failedCount: result.failedCount,
                    })
                    : t('adminAffiliateDetail.toast.settlementProcessed', {
                        processedCount: result.processedCount,
                    }),
                result.failedCount > 0 ? 'error' : 'success'
            )
            fetchDetail() // refresh header metrics
            onSuccess?.()
        } catch (err) {
            showToast(
                err?.response?.data?.message || (
                    type === 'approve'
                        ? t('adminAffiliateDetail.toast.approveFailed')
                        : t('adminAffiliateDetail.toast.paymentFailed')
                ),
                'error'
            )
        } finally {
            setModalLoading(false)
        }
    }

    // ─── Modal Confirm Handler ───────────────────────────────────────
    const handleModalConfirm = async () => {
        const trimmedSuspendReason = suspendReason.trim()

        if (modal.action === 'suspend') {
            if (!trimmedSuspendReason) {
                setSuspendReasonError(t('adminAffiliateDetail.modals.suspend.reasonRequired'))
                return
            }

            setSuspendReasonError('')
        }

        setModalLoading(true)
        try {
            if (modal.action === 'suspend') {
                await adminAffiliateApi.suspendAffiliate(id, trimmedSuspendReason)
                showToast(t('adminAffiliateDetail.toast.suspended'))
            } else if (modal.action === 'activate') {
                await adminAffiliateApi.activateAffiliate(id)
                showToast(t('adminAffiliateDetail.toast.activated'))
            } else if (typeof modal.action === 'function') {
                await modal.action()
                return // action handles its own UI
            }
            setModal(m => ({ ...m, open: false }))
            fetchDetail()
        } catch (err) {
            showToast(err?.response?.data?.message || t('adminAffiliateDetail.toast.actionFailed'), 'error')
        } finally {
            setModalLoading(false)
        }
    }

    // ─── Loading State ───────────────────────────────────────────────
    if (loading) {
        return (
            <div className="space-y-6">
                <div className="h-8 w-48 bg-gray-200 rounded animate-pulse"></div>
                <div className="card animate-pulse">
                    <div className="flex items-center gap-4">
                        <div className="w-16 h-16 bg-gray-200 rounded-full"></div>
                        <div className="space-y-2">
                            <div className="h-6 w-40 bg-gray-200 rounded"></div>
                            <div className="h-4 w-24 bg-gray-200 rounded"></div>
                        </div>
                    </div>
                </div>
                <div className="grid grid-cols-5 gap-4">{[...Array(5)].map((_, i) => <SkeletonCard key={i} />)}</div>
            </div>
        )
    }

    if (!detail) {
        return (
            <div className="space-y-6">
                <button onClick={() => navigate('/admin/affiliates')} className="text-sm text-primary-600 hover:text-primary-700 font-medium">
                    {t('adminAffiliateDetail.navigation.backToAffiliates')}
                </button>
                <div className="flex flex-col items-center justify-center py-16 text-center">
                    <div className="text-5xl mb-4">❌</div>
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('adminAffiliateDetail.empty.title')}</h3>
                    <p className="text-gray-500 text-sm max-w-sm">{t('adminAffiliateDetail.empty.description')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {/* Toast */}
            {toast && <Toast message={toast.message} type={toast.type} onClose={() => setToast(null)} />}

            {/* Back Link */}
            <button onClick={() => navigate('/admin/affiliates')} className="text-sm text-primary-600 hover:text-primary-700 font-medium">
                {t('adminAffiliateDetail.navigation.backToAffiliates')}
            </button>

            {/* Header Card */}
            <div className="card">
                <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4">
                    <div className="flex items-center gap-4">
                        <div className="w-16 h-16 bg-gradient-to-br from-primary-500 to-primary-700 rounded-full flex items-center justify-center text-gray-900 dark:text-white text-2xl font-bold">
                            {detail.displayName?.charAt(0)?.toUpperCase() || 'A'}
                        </div>
                        <div>
                            <h2 className="text-2xl font-bold text-gray-900">{detail.displayName}</h2>
                            <div className="flex items-center gap-3 mt-1">
                                <code className="px-2 py-1 bg-gray-100 rounded text-xs text-gray-700 font-mono">{detail.referralCode}</code>
                                <StatusBadge status={detail.status} />
                            </div>
                        </div>
                    </div>
                    <div className="flex gap-2">
                        {detail.status !== 'Active' && (
                            <button
                                id="admin-affiliate-activate-btn"
                                onClick={handleActivateClick}
                                className="px-4 py-2 bg-green-600 text-gray-900 dark:text-white rounded-lg text-sm font-medium hover:bg-green-700 transition-colors"
                            >
                                {t('adminAffiliateDetail.actions.activate')}
                            </button>
                        )}
                        {detail.status !== 'Suspended' && (
                            <button
                                id="admin-affiliate-suspend-btn"
                                onClick={handleSuspendClick}
                                className="px-4 py-2 bg-red-100 text-red-700 rounded-lg text-sm font-medium hover:bg-red-200 transition-colors"
                            >
                                {t('adminAffiliateDetail.actions.suspend')}
                            </button>
                        )}
                    </div>
                </div>

                {/* Suspension info */}
                {detail.status === 'Suspended' && detail.suspensionReason && (
                    <div className="mt-4 p-3 bg-red-50 border border-red-100 rounded-lg text-sm text-red-700">
                        {t('adminAffiliateDetail.header.suspendedPrefix')} {detail.suspensionReason}
                        {detail.suspendedAtUtc && (
                            <span className="ml-2 text-red-500">({new Date(detail.suspendedAtUtc).toLocaleDateString()})</span>
                        )}
                    </div>
                )}

                {/* Dates */}
                <div className="mt-4 flex gap-6 text-xs text-gray-500">
                    <span>{t('adminAffiliateDetail.header.createdAt', { date: formatDate(detail.createdAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable')) })}</span>
                    {detail.activatedAtUtc && <span>{t('adminAffiliateDetail.header.activatedAt', { date: formatDate(detail.activatedAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable')) })}</span>}
                    {detail.lastCommissionAtUtc && <span>{t('adminAffiliateDetail.header.lastCommissionAt', { date: formatDate(detail.lastCommissionAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable')) })}</span>}
                </div>

                {/* AF-16: Payout Wallet (Admin Read-Only) */}
                {(detail.payoutWalletNumber || detail.payoutWalletType) && (
                    <div className="mt-4 p-4 bg-gradient-to-r from-indigo-50 to-purple-50 border border-indigo-200 rounded-lg">
                        <div className="flex items-center gap-2 mb-2">
                            <span className="text-lg">💰</span>
                            <h4 className="text-sm font-semibold text-indigo-900">{t('adminAffiliateDetail.wallet.title')}</h4>
                        </div>
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-sm">
                            <div>
                                <span className="text-indigo-600 font-medium">{t('adminAffiliateDetail.wallet.type')}</span>
                                <span className="ml-2 text-indigo-900">{detail.payoutWalletType || t('adminAffiliateDetail.labels.notSet')}</span>
                            </div>
                            <div>
                                <span className="text-indigo-600 font-medium">{t('adminAffiliateDetail.wallet.number')}</span>
                                <code className="ml-2 px-2 py-0.5 bg-white rounded text-indigo-900 font-mono text-xs">
                                    {detail.payoutWalletNumber || t('adminAffiliateDetail.labels.notSet')}
                                </code>
                            </div>
                            {detail.payoutWalletUpdatedAtUtc && (
                                <div>
                                    <span className="text-indigo-600 font-medium">{t('adminAffiliateDetail.wallet.lastUpdated')}</span>
                                    <span className="ml-2 text-indigo-900">
                                        {formatDate(detail.payoutWalletUpdatedAtUtc, locale, t('adminAffiliateDetail.labels.notAvailable'))}
                                    </span>
                                </div>
                            )}
                        </div>
                    </div>
                )}
            </div>

            {/* Financial Summary Cards */}
            <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
                {[
                    { label: t('adminAffiliateDetail.summary.totalEarned'), value: formatEarningsByCurrency(detail, 'totalEarned', formatCurrency), gradient: 'from-blue-50 to-blue-100', border: 'border-blue-200', labelColor: 'text-blue-600', valueColor: 'text-blue-900' },
                    { label: t('adminAffiliateDetail.summary.approved'), value: formatEarningsByCurrency(detail, 'totalApproved', formatCurrency), gradient: 'from-emerald-50 to-emerald-100', border: 'border-emerald-200', labelColor: 'text-emerald-600', valueColor: 'text-emerald-900' },
                    { label: t('adminAffiliateDetail.summary.paid'), value: formatEarningsByCurrency(detail, 'totalPaid', formatCurrency), gradient: 'from-green-50 to-green-100', border: 'border-green-200', labelColor: 'text-green-600', valueColor: 'text-green-900' },
                    { label: t('adminAffiliateDetail.summary.reversed'), value: formatEarningsByCurrency(detail, 'totalReversed', formatCurrency), gradient: 'from-red-50 to-red-100', border: 'border-red-200', labelColor: 'text-red-600', valueColor: 'text-red-900' },
                    { label: t('adminAffiliateDetail.summary.netEarnings'), value: formatEarningsByCurrency(detail, 'netEarnings', formatCurrency), gradient: 'from-purple-50 to-purple-100', border: 'border-purple-200', labelColor: 'text-purple-600', valueColor: 'text-purple-900' },
                    { label: t('adminAffiliateDetail.summary.totalOrders'), value: detail.totalOrders, gradient: 'from-orange-50 to-orange-100', border: 'border-orange-200', labelColor: 'text-orange-600', valueColor: 'text-orange-900' },
                ].map((card, i) => (
                    <div key={i} className={`card bg-gradient-to-br ${card.gradient} ${card.border}`}>
                        <p className={`text-xs font-medium ${card.labelColor} uppercase`}>{card.label}</p>
                        <p className={`text-xl font-bold ${card.valueColor} mt-2`}>{card.value}</p>
                    </div>
                ))}
            </div>

            {/* Tab Navigation */}
            <div className="border-b border-gray-200">
                <nav className="flex gap-0 -mb-px overflow-x-auto">
                    {AFFILIATE_DETAIL_TABS.map(tab => (
                        <button
                            key={tab.id}
                            id={`admin-affiliate-tab-${tab.id}`}
                            onClick={() => setActiveTab(tab.id)}
                            className={`flex items-center gap-2 px-6 py-3 text-sm font-medium border-b-2 transition-all whitespace-nowrap ${activeTab === tab.id
                                ? 'border-primary-500 text-primary-600'
                                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                }`}
                        >
                            <span>{tab.icon}</span>
                            {t(tab.labelKey)}
                        </button>
                    ))}
                </nav>
            </div>

            {/* Tab Content */}
            <div>
                {activeTab === 'orders' && <OrdersTab affiliateId={id} formatCurrency={formatCurrency} />}
                {activeTab === 'earnings' && <EarningsTab affiliateId={id} formatCurrency={formatCurrency} />}
                {activeTab === 'settlement' && <SettlementTab affiliateId={id} formatCurrency={formatCurrency} onAction={handleSettlementAction} cashboxBalance={detail?.cashboxBalance ?? 0} />}
                {activeTab === 'withdrawals' && <WithdrawalsTab affiliateId={id} formatCurrency={formatCurrency} onToast={showToast} />}
            </div>

            {/* Confirmation Modal */}
            <ConfirmModal
                open={modal.open}
                title={modal.title}
                message={modal.message}
                confirmLabel={modal.confirmLabel}
                confirmColor={modal.confirmColor}
                onConfirm={handleModalConfirm}
                onCancel={() => {
                    setSuspendReasonError('')
                    setModal(m => ({ ...m, open: false }))
                }}
                loading={modalLoading}
            >
                {modal.action === 'suspend' && (
                    <div className="mt-4">
                        <label className="block text-sm font-medium text-gray-700 mb-1">{t('adminAffiliateDetail.modals.suspend.reasonLabel')}</label>
                        <textarea
                            id="admin-affiliate-suspend-reason"
                            value={suspendReason}
                            onChange={e => {
                                setSuspendReason(e.target.value)
                                if (suspendReasonError) setSuspendReasonError('')
                            }}
                            placeholder={t('adminAffiliateDetail.modals.suspend.reasonPlaceholder')}
                            rows={3}
                            aria-invalid={Boolean(suspendReasonError)}
                            aria-describedby={suspendReasonError ? 'admin-affiliate-suspend-reason-error' : undefined}
                            className={`w-full px-3 py-2 border rounded-lg text-sm focus:ring-2 focus:ring-red-500 focus:border-red-500 outline-none ${suspendReasonError ? 'border-red-500' : 'border-gray-300'}`}
                            required
                        />
                        {suspendReasonError && (
                            <p id="admin-affiliate-suspend-reason-error" className="mt-2 text-sm text-red-600">
                                {suspendReasonError}
                            </p>
                        )}
                    </div>
                )}
            </ConfirmModal>
        </div>
    )
}
