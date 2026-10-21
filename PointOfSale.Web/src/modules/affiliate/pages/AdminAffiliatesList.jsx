/**
 * AF-4B + AF-23: Admin Affiliates List Page
 *
 * Table of all affiliates in the tenant with:
 * - DisplayName, ReferralCode, Status, Financial Metrics, Actions
 * - Filters: Status, Search
 * - Click row → navigate to affiliate detail
 *
 * AF-23: Shows readiness gate if prerequisites not met
 *
 * RULES:
 * - All money values are pre-computed from MongoDB projections
 * - UI never computes totals
 * - Role: Owner, Admin only
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useSettings } from '../../../contexts/SettingsContext'
import adminAffiliateApi from '../../../services/adminAffiliateApi'
import { useAffiliateReadiness } from '../../../hooks/useAffiliateReadiness'
import AffiliateReadinessWarning from '../../../components/AffiliateReadinessWarning'

// ═══════════════════════════════════════════════════════════════════
// STATUS BADGE
// ═══════════════════════════════════════════════════════════════════

const statusStyles = {
    Active: 'bg-emerald-100 text-emerald-800 border-emerald-200',
    Pending: 'bg-yellow-100 text-yellow-800 border-yellow-200',
    Suspended: 'bg-red-100 text-red-800 border-red-200',
}

function StatusBadge({ status }) {
    return (
        <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${statusStyles[status] || 'bg-gray-100 text-gray-800 border-gray-200'}`}>
            {status}
        </span>
    )
}

function formatEarningsByCurrency(affiliate, amountKey, formatCurrency) {
    const summaries = Array.isArray(affiliate.earningsByCurrency) ? affiliate.earningsByCurrency : []
    if (summaries.length === 0) {
        return '—'
    }

    return summaries.map(summary => (
        <div key={`${affiliate.affiliateAccountId}-${amountKey}-${summary.currency}`}>
            {formatCurrency(summary[amountKey] || 0, summary.currency)}
        </div>
    ))
}

// ═══════════════════════════════════════════════════════════════════
// SKELETON LOADER
// ═══════════════════════════════════════════════════════════════════

function SkeletonRow() {
    return (
        <tr className="animate-pulse">
            <td className="px-4 py-3"><div className="h-4 w-28 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-20 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-5 w-16 bg-gray-200 rounded-full"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-24 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-20 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-20 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-20 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-16 bg-gray-200 rounded"></div></td>
            <td className="px-4 py-3"><div className="h-4 w-12 bg-gray-200 rounded"></div></td>
        </tr>
    )
}

// ═══════════════════════════════════════════════════════════════════
// EMPTY STATE
// ═══════════════════════════════════════════════════════════════════

function EmptyState({ search, status }) {
    return (
        <div className="flex flex-col items-center justify-center py-16 text-center">
            <div className="text-5xl mb-4">👥</div>
            <h3 className="text-lg font-semibold text-gray-700 mb-2">
                {search || status ? 'No Matching Affiliates' : 'No Affiliate Earnings for This Store Yet'}
            </h3>
            <p className="text-gray-500 text-sm max-w-sm">
                {search || status
                    ? 'Try adjusting your filters to find the affiliate you\'re looking for.'
                    : 'This merchant view lists global affiliates only after they earn commission for this store. Enrollment and promotions in other stores do not add an affiliate here.'
                }
            </p>
        </div>
    )
}

// ═══════════════════════════════════════════════════════════════════
// MAIN COMPONENT
// ═══════════════════════════════════════════════════════════════════

const STATUS_OPTIONS = [
    { value: '', label: 'All Statuses' },
    { value: 'Active', label: 'Active' },
    { value: 'Pending', label: 'Pending' },
    { value: 'Suspended', label: 'Suspended' },
]

export default function AdminAffiliatesList() {
    const navigate = useNavigate()
    const { formatCurrency } = useSettings()
    const [data, setData] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState(false)
    const [page, setPage] = useState(1)
    const [statusFilter, setStatusFilter] = useState('')
    const [search, setSearch] = useState('')
    const [searchInput, setSearchInput] = useState('')
    const requestSeq = useRef(0)
    const pageSize = 20

    // AF-23: Readiness check
    const readiness = useAffiliateReadiness()

    const fetchAffiliates = useCallback(({ showLoading = true } = {}) => {
        const currentRequest = ++requestSeq.current

        // AF-23: Don't fetch if not ready
        if (!readiness.isReady) {
            setLoading(false)
            setData(null)
            return
        }

        if (showLoading) {
            setLoading(true)
        }
        setError(false)

        adminAffiliateApi.listAffiliates(page, pageSize, statusFilter, search)
            .then(d => {
                if (currentRequest === requestSeq.current) {
                    setData(d)
                }
            })
            .catch(() => {
                if (currentRequest === requestSeq.current) {
                    setData(null)
                    setError(true)
                }
            })
            .finally(() => {
                if (currentRequest === requestSeq.current) {
                    setLoading(false)
                }
            })
    }, [page, statusFilter, search, readiness.isReady])

    useEffect(() => { 
        if (!readiness.loading) {
            fetchAffiliates() 
        }
    }, [fetchAffiliates, readiness.loading])

    useEffect(() => {
        const refetchCurrentAuthority = () => {
            if (!readiness.loading) {
                fetchAffiliates({ showLoading: false })
            }
        }

        const handleVisibility = () => {
            if (document.visibilityState === 'visible') {
                refetchCurrentAuthority()
            }
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
    }, [fetchAffiliates, readiness.loading])

    const handleSearch = (e) => {
        e.preventDefault()
        setSearch(searchInput.trim())
        setPage(1)
    }

    const handleClearSearch = () => {
        setSearchInput('')
        setSearch('')
        setPage(1)
    }

    // AF-23: Show readiness gate if not ready
    if (readiness.loading) {
        return (
            <div className="space-y-6">
                <div>
                    <h2 className="text-3xl font-bold text-gray-900">Affiliate Management</h2>
                    <p className="text-gray-600 mt-1">Checking affiliate system readiness...</p>
                </div>
                <div className="card">
                    <div className="animate-pulse space-y-4">
                        <div className="h-4 bg-gray-200 rounded w-3/4"></div>
                        <div className="h-4 bg-gray-200 rounded w-1/2"></div>
                    </div>
                </div>
            </div>
        )
    }

    if (!readiness.isReady) {
        return (
            <div className="space-y-6">
                <div>
                    <h2 className="text-3xl font-bold text-gray-900">Affiliate Management</h2>
                    <p className="text-gray-600 mt-1">Manage affiliate accounts, review commissions, and process settlements</p>
                </div>
                <div className="card">
                    <AffiliateReadinessWarning
                        storefrontLive={readiness.storefrontLive}
                        hasPaymentMethod={readiness.hasPaymentMethod}
                        hasShippingMethod={readiness.hasShippingMethod}
                        missingRequirements={readiness.missingRequirements}
                        variant="fullpage"
                    />
                </div>
            </div>
        )
    }

    return (
        <div className="space-y-6">
            {/* Header */}
            <div>
                <h2 className="text-3xl font-bold text-gray-900">Affiliate Management</h2>
                <p className="text-gray-600 mt-1">Global affiliates with earned commissions for this store only. This is not the global enrollment registry.</p>
            </div>

            {/* Filters Bar */}
            <div className="card">
                <div className="flex flex-col sm:flex-row items-start sm:items-center gap-4">
                    {/* Search */}
                    <form onSubmit={handleSearch} className="flex-1 flex items-center gap-2 w-full sm:w-auto">
                        <div className="relative flex-1">
                            <input
                                id="admin-affiliate-search"
                                type="text"
                                value={searchInput}
                                onChange={(e) => setSearchInput(e.target.value)}
                                placeholder="Search by name or referral code..."
                                className="w-full pl-10 pr-4 py-2.5 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 outline-none"
                            />
                            <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-600 dark:text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                            </svg>
                        </div>
                        <button type="submit" className="px-4 py-2.5 bg-primary-600 text-gray-900 dark:text-white rounded-lg text-sm font-medium hover:bg-primary-700 transition-colors">
                            Search
                        </button>
                        {search && (
                            <button
                                type="button"
                                onClick={handleClearSearch}
                                className="px-3 py-2.5 text-gray-500 hover:text-gray-700 text-sm"
                            >
                                Clear
                            </button>
                        )}
                    </form>

                    {/* Status Filter */}
                    <select
                        id="admin-affiliate-status-filter"
                        value={statusFilter}
                        onChange={(e) => { setStatusFilter(e.target.value); setPage(1) }}
                        className="px-3 py-2.5 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 outline-none bg-white"
                    >
                        {STATUS_OPTIONS.map(opt => (
                            <option key={opt.value} value={opt.value}>{opt.label}</option>
                        ))}
                    </select>
                </div>
            </div>

            {/* Summary Stats */}
            {data && !loading && data.affiliates.length > 0 && (
                <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200">
                        <p className="text-xs text-blue-600 font-medium uppercase">Total Affiliates</p>
                        <p className="text-2xl font-bold text-blue-900 mt-1">{data.totalCount}</p>
                    </div>
                    <div className="card bg-gradient-to-br from-emerald-50 to-emerald-100 border-emerald-200">
                        <p className="text-xs text-emerald-600 font-medium uppercase">Active</p>
                        <p className="text-2xl font-bold text-emerald-900 mt-1">
                            {data.affiliates.filter(a => a.status === 'Active').length}
                        </p>
                    </div>
                    <div className="card bg-gradient-to-br from-yellow-50 to-yellow-100 border-yellow-200">
                        <p className="text-xs text-yellow-600 font-medium uppercase">Pending</p>
                        <p className="text-2xl font-bold text-yellow-900 mt-1">
                            {data.affiliates.filter(a => a.status === 'Pending').length}
                        </p>
                    </div>
                    <div className="card bg-gradient-to-br from-red-50 to-red-100 border-red-200">
                        <p className="text-xs text-red-600 font-medium uppercase">Suspended</p>
                        <p className="text-2xl font-bold text-red-900 mt-1">
                            {data.affiliates.filter(a => a.status === 'Suspended').length}
                        </p>
                    </div>
                </div>
            )}

            {/* Table */}
            <div className="card overflow-hidden">
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-gray-200">
                        <thead className="bg-gray-50">
                            <tr>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Affiliate</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Referral Code</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Payout Wallet</th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Earned</th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Approved</th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Paid</th>
                                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Net</th>
                                <th className="px-4 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">Orders</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200 bg-white">
                            {loading ? (
                                [...Array(5)].map((_, i) => <SkeletonRow key={i} />)
                            ) : error ? (
                                <tr><td colSpan={9} className="p-8 text-center" role="alert">
                                    Unable to load this store's affiliates.{' '}
                                    <button type="button" onClick={() => fetchAffiliates()} className="underline">Retry</button>
                                </td></tr>
                            ) : !data?.affiliates?.length ? (
                                <tr>
                                    <td colSpan={9}>
                                        <EmptyState search={search} status={statusFilter} />
                                    </td>
                                </tr>
                            ) : (
                                data.affiliates.map(affiliate => (
                                    <tr
                                        key={affiliate.affiliateAccountId}
                                        onClick={() => navigate(`/admin/affiliates/${affiliate.affiliateAccountId}`)}
                                        className="hover:bg-gray-50 cursor-pointer transition-colors"
                                    >
                                        <td className="px-4 py-3">
                                            <div className="flex items-center gap-3">
                                                <div className="w-9 h-9 bg-gradient-to-br from-primary-500 to-primary-700 rounded-full flex items-center justify-center text-gray-900 dark:text-white text-sm font-bold flex-shrink-0">
                                                    {affiliate.displayName?.charAt(0)?.toUpperCase() || 'A'}
                                                </div>
                                                <span className="text-sm font-medium text-gray-900">{affiliate.displayName}</span>
                                            </div>
                                        </td>
                                        <td className="px-4 py-3">
                                            <code className="px-2 py-1 bg-gray-100 rounded text-xs text-gray-700 font-mono">
                                                {affiliate.referralCode}
                                            </code>
                                        </td>
                                        <td className="px-4 py-3">
                                            <StatusBadge status={affiliate.status} />
                                        </td>
                                        <td className="px-4 py-3">
                                            {affiliate.payoutWalletNumber ? (
                                                <div className="text-xs">
                                                    <div className="font-medium text-indigo-700">{affiliate.payoutWalletType}</div>
                                                    <code className="text-gray-600 font-mono">{affiliate.payoutWalletNumber}</code>
                                                </div>
                                            ) : (
                                                <span className="text-xs text-gray-600 dark:text-gray-400 italic">Not set</span>
                                            )}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-right font-medium text-gray-900">
                                            {formatEarningsByCurrency(affiliate, 'totalEarned', formatCurrency)}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-right text-emerald-700">
                                            {formatEarningsByCurrency(affiliate, 'totalApproved', formatCurrency)}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-right text-green-700">
                                            {formatEarningsByCurrency(affiliate, 'totalPaid', formatCurrency)}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-right font-semibold text-purple-700">
                                            {formatEarningsByCurrency(affiliate, 'netEarnings', formatCurrency)}
                                        </td>
                                        <td className="px-4 py-3 text-sm text-center text-gray-600">
                                            {affiliate.totalOrders}
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Pagination */}
            {data && data.totalPages > 1 && (
                <div className="flex items-center justify-between px-2">
                    <p className="text-sm text-gray-600">
                        Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, data.totalCount)} of {data.totalCount}
                    </p>
                    <div className="flex gap-2">
                        <button
                            onClick={() => setPage(p => Math.max(1, p - 1))}
                            disabled={page === 1}
                            className="btn-secondary text-sm disabled:opacity-50"
                        >
                            ← Previous
                        </button>
                        <span className="flex items-center px-3 text-sm text-gray-600">
                            Page {page} of {data.totalPages}
                        </span>
                        <button
                            onClick={() => setPage(p => Math.min(data.totalPages, p + 1))}
                            disabled={page === data.totalPages}
                            className="btn-secondary text-sm disabled:opacity-50"
                        >
                            Next →
                        </button>
                    </div>
                </div>
            )}
        </div>
    )
}
