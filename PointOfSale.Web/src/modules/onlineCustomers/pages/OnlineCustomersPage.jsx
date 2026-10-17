/**
 * Online Customers List Page
 * Phase P1: POS Admin - Online Customers Management (Storefront)
 * 
 * This page manages customers who registered via the Storefront.
 * 
 * ❌ NOT POS Customers (different entity)
 * ❌ NOT POS Users (different entity)
 * 
 * Features:
 * - Paginated list with search & filters
 * - View customer details in drawer
 * - Quick status indicators
 * - Session count display
 * 
 * SECURITY:
 * - Tenant isolation enforced by backend
 * - Role-based access (Admin, Owner, Manager)
 * - All actions audited
 */

import { useState, useEffect, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Search,
    Filter,
    Users,
    Mail,
    CheckCircle,
    XCircle,
    ChevronLeft,
    ChevronRight,
    Eye,
    RefreshCw,
    Loader2,
    Smartphone,
    AlertCircle
} from 'lucide-react'
import { onlineCustomersApi } from '../services'
import { OnlineCustomerDetails } from '../components'
import { useToast } from '../../../components/Toast'
import { formatDistanceToNow, format } from 'date-fns'
import { ar, enUS } from 'date-fns/locale'
import { useDebounce } from '../../../hooks/useDebounce'

export function OnlineCustomersPage() {
    const { t, i18n } = useTranslation()
    const [customers, setCustomers] = useState([])
    const [loading, setLoading] = useState(true)
    const [loadError, setLoadError] = useState(false)
    const [totalCount, setTotalCount] = useState(0)
    const [page, setPage] = useState(1)
    const [totalPages, setTotalPages] = useState(1)
    const pageSize = 20

    // Search & Filters
    const [search, setSearch] = useState('')
    const [statusFilter, setStatusFilter] = useState('')
    const [verifiedFilter, setVerifiedFilter] = useState('')
    const [providerFilter, setProviderFilter] = useState('')
    const [showFilters, setShowFilters] = useState(false)

    // Detail drawer
    const [selectedCustomer, setSelectedCustomer] = useState(null)
    const [showDetails, setShowDetails] = useState(false)
    const requestIdRef = useRef(0)
    const lastFetchKeyRef = useRef('')
    const debouncedSearch = useDebounce(search, 300)
    const isSearchSettled = debouncedSearch === search

    const { showError } = useToast()

    // Dynamic Options inside component to use translation hook
    const STATUS_OPTIONS = [
        { value: '', label: t('onlineCustomers.filters.allStatus') },
        { value: 'active', label: t('onlineCustomers.filters.active') },
        { value: 'inactive', label: t('onlineCustomers.filters.inactive') },
    ]

    const VERIFIED_OPTIONS = [
        { value: '', label: t('onlineCustomers.filters.allVerification') },
        { value: 'true', label: t('onlineCustomers.filters.verified') },
        { value: 'false', label: t('onlineCustomers.filters.unverified') },
    ]

    const PROVIDER_OPTIONS = [
        { value: '', label: t('onlineCustomers.filters.allProviders') },
        { value: 'local', label: 'Email & Password' },
        { value: 'google', label: 'Google' },
        { value: 'facebook', label: 'Facebook' },
    ]

    const fetchCustomers = useCallback(async (searchValue, pageNumber, { force = false } = {}) => {
        const normalizedSearch = searchValue.trim()
        const fetchKey = JSON.stringify({
            page: pageNumber,
            providerFilter,
            search: normalizedSearch,
            statusFilter,
            verifiedFilter,
        })

        if (!force && fetchKey === lastFetchKeyRef.current) {
            return
        }

        const requestId = ++requestIdRef.current
        setLoading(true)
        setLoadError(false)
        try {
            const params = {
                page: pageNumber,
                pageSize,
                search: normalizedSearch || undefined,
                isActive: statusFilter === 'active' ? true : statusFilter === 'inactive' ? false : undefined,
                emailVerified: verifiedFilter === 'true' ? true : verifiedFilter === 'false' ? false : undefined,
                provider: providerFilter || undefined,
            }
            const data = await onlineCustomersApi.getOnlineCustomers(params)

            if (requestId !== requestIdRef.current) {
                return
            }

            lastFetchKeyRef.current = fetchKey
            setCustomers(data.items || [])
            setTotalCount(data.totalCount || 0)
            setTotalPages(data.totalPages || 1)
        } catch {
            if (requestId !== requestIdRef.current) {
                return
            }

            setLoadError(true)
            showError(t('common.failedToLoad'))
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [providerFilter, showError, statusFilter, verifiedFilter, t])

    useEffect(() => {
        if (!isSearchSettled) {
            return
        }

        void fetchCustomers(debouncedSearch, page)
    }, [debouncedSearch, fetchCustomers, isSearchSettled, page])

    useEffect(() => {
        return () => {
            requestIdRef.current += 1
        }
    }, [])

    const handleSearch = (e) => {
        e.preventDefault()
        setPage(1)
        void fetchCustomers(search, 1, { force: true })
    }

    const handleViewDetails = (customer) => {
        setSelectedCustomer(customer)
        setShowDetails(true)
    }

    const handleCloseDetails = () => {
        setShowDetails(false)
        setSelectedCustomer(null)
    }

    const handleCustomerUpdate = () => {
        void fetchCustomers(search, page, { force: true })
    }

    const getProviderBadge = (provider) => {
        const p = provider?.toLowerCase()
        if (p === 'google') return { icon: '🔵', label: 'Google', color: 'bg-blue-100 text-blue-700' }
        if (p === 'facebook') return { icon: '🔷', label: 'Facebook', color: 'bg-indigo-100 text-indigo-700' }
        return { icon: '🔑', label: 'Local', color: 'bg-gray-100 text-gray-700' }
    }

    const clearFilters = () => {
        setSearch('')
        setStatusFilter('')
        setVerifiedFilter('')
        setProviderFilter('')
        setPage(1)
    }

    const hasActiveFilters = statusFilter || verifiedFilter || providerFilter || search
    const getDateLocale = () => i18n.language.startsWith('ar') ? ar : enUS

    // Handle RTL input padding
    const isRtl = i18n.dir() === 'rtl'
    const searchIconClass = `absolute ${isRtl ? 'right-3' : 'left-3'} top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400`
    const searchInputClass = `input ${isRtl ? 'pr-10' : 'pl-10'} w-full`

    return (
        <div className="h-full flex flex-col">
            {/* Header */}
            <div className="mb-6 flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-800 flex items-center gap-2">
                        <Users className="w-7 h-7 text-primary-600" />
                        {t('onlineCustomers.title')}
                    </h1>
                    <p className="text-sm text-gray-500 mt-1">
                        {t('onlineCustomers.registeredDescription', 'Manage customers registered via the Storefront. Session counts are sign-ins, not live presence.')} ({loadError ? '—' : totalCount})
                    </p>
                </div>
                <button
                    onClick={() => void fetchCustomers(search, page, { force: true })}
                    className="btn-secondary flex items-center gap-2"
                    disabled={loading}
                >
                    <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
                    {t('onlineCustomers.sessions.refresh')}
                </button>
            </div>

            {/* Search & Filters */}
            <div className="bg-white p-4 rounded-lg shadow mb-6 space-y-4">
                <form onSubmit={handleSearch} className="flex gap-4">
                    <div className="flex-1 relative">
                        <Search className={searchIconClass} />
                        <input
                            type="text"
                            placeholder={t('onlineCustomers.searchPlaceholder')}
                            value={search}
                            onChange={(e) => {
                                setSearch(e.target.value)

                                if (page !== 1) {
                                    setPage(1)
                                }
                            }}
                            className={searchInputClass}
                        />
                    </div>
                    <button type="submit" className="btn-primary">
                        {t('common.search')}
                    </button>
                    <button
                        type="button"
                        onClick={() => setShowFilters(!showFilters)}
                        className={`btn-secondary flex items-center gap-2 ${showFilters ? 'bg-primary-100' : ''}`}
                    >
                        <Filter className="w-4 h-4" />
                        Filters
                        {hasActiveFilters && (
                            <span className="w-2 h-2 bg-primary-500 rounded-full" />
                        )}
                    </button>
                </form>

                {/* Filter Options */}
                {showFilters && (
                    <div className="pt-4 border-t flex flex-wrap gap-4 items-end">
                        <div className="space-y-1">
                            <label className="text-xs font-medium text-gray-500">{t('onlineCustomers.table.accountStatus')}</label>
                            <select
                                value={statusFilter}
                                onChange={(e) => { setStatusFilter(e.target.value); setPage(1) }}
                                className="input text-sm"
                            >
                                {STATUS_OPTIONS.map(opt => (
                                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                                ))}
                            </select>
                        </div>
                        <div className="space-y-1">
                            <label className="text-xs font-medium text-gray-500">{t('onlineCustomers.table.emailVerified')}</label>
                            <select
                                value={verifiedFilter}
                                onChange={(e) => { setVerifiedFilter(e.target.value); setPage(1) }}
                                className="input text-sm"
                            >
                                {VERIFIED_OPTIONS.map(opt => (
                                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                                ))}
                            </select>
                        </div>
                        <div className="space-y-1">
                            <label className="text-xs font-medium text-gray-500">{t('onlineCustomers.table.provider')}</label>
                            <select
                                value={providerFilter}
                                onChange={(e) => { setProviderFilter(e.target.value); setPage(1) }}
                                className="input text-sm"
                            >
                                {PROVIDER_OPTIONS.map(opt => (
                                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                                ))}
                            </select>
                        </div>
                        {hasActiveFilters && (
                            <button
                                onClick={clearFilters}
                                className="text-sm text-primary-600 hover:text-primary-700"
                            >
                                {t('common.close')}
                            </button>
                        )}
                    </div>
                )}
            </div>

            {/* Customers Table */}
            {loadError && <div role="alert" className="mb-4 rounded border border-red-200 bg-red-50 p-4 text-red-700">{t('common.failedToLoad')}</div>}
            <div className="bg-white rounded-lg shadow flex-1 overflow-hidden flex flex-col">
                {loading && customers.length > 0 && (
                    <div className="border-b border-gray-200 bg-gray-50 px-6 py-2 text-xs text-gray-500">
                        {t('common.loading')}
                    </div>
                )}
                <div className="overflow-x-auto flex-1">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('onlineCustomers.table.customer')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('onlineCustomers.table.provider')}
                                </th>
                                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('onlineCustomers.table.emailVerified')}
                                </th>
                                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('onlineCustomers.table.accountStatus')}
                                </th>
                                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('onlineCustomers.table.sessions')}
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('onlineCustomers.table.lastLogin')}
                                </th>
                                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    {t('common.actions')}
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {loading && customers.length === 0 ? (
                                <tr>
                                    <td colSpan="7" className="px-6 py-12 text-center">
                                        <Loader2 className="w-8 h-8 animate-spin text-primary-600 mx-auto" />
                                        <p className="mt-2 text-gray-500">{t('common.loading')}</p>
                                    </td>
                                </tr>
                            ) : customers.length === 0 && !loadError ? (
                                <tr>
                                    <td colSpan="7" className="px-6 py-12 text-center text-gray-500">
                                        <Users className="w-12 h-12 mx-auto mb-3 text-gray-300" />
                                        <p className="font-medium">{t('common.noData')}</p>
                                    </td>
                                </tr>
                            ) : (
                                customers.map((customer) => {
                                    const provider = getProviderBadge(customer.provider)
                                    return (
                                        <tr key={customer.id} className="hover:bg-gray-50 transition-colors">
                                            {/* Customer Info */}
                                            <td className="px-6 py-4">
                                                <div className="flex items-center gap-3">
                                                    <div className="w-10 h-10 bg-primary-100 rounded-full flex items-center justify-center">
                                                        <span className="text-primary-600 font-semibold text-sm">
                                                            {(customer.email || '?')[0].toUpperCase()}
                                                        </span>
                                                    </div>
                                                    <div>
                                                        <div className="text-sm font-medium text-gray-900">
                                                            {customer.displayName || customer.email?.split('@')[0]}
                                                        </div>
                                                        <div className="text-xs text-gray-500 flex items-center gap-1">
                                                            <Mail className="w-3 h-3" />
                                                            {customer.email}
                                                        </div>
                                                    </div>
                                                </div>
                                            </td>

                                            {/* Provider */}
                                            <td className="px-6 py-4">
                                                <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-full ${provider.color}`}>
                                                    {provider.icon} {provider.label}
                                                </span>
                                            </td>

                                            {/* Email Verified */}
                                            <td className="px-6 py-4 text-center">
                                                {customer.isEmailVerified ? (
                                                    <span className="inline-flex items-center gap-1 text-green-600">
                                                        <CheckCircle className="w-4 h-4" />
                                                        <span className="text-xs">{t('onlineCustomers.filters.verified')}</span>
                                                    </span>
                                                ) : (
                                                    <span className="inline-flex items-center gap-1 text-amber-500">
                                                        <AlertCircle className="w-4 h-4" />
                                                        <span className="text-xs">{t('onlineCustomers.filters.unverified')}</span>
                                                    </span>
                                                )}
                                            </td>

                                            {/* Status */}
                                            <td className="px-6 py-4 text-center">
                                                {customer.isActive ? (
                                                    <span className="inline-flex items-center gap-1 px-2.5 py-1 text-xs font-medium rounded-full bg-green-100 text-green-700">
                                                        <CheckCircle className="w-3.5 h-3.5" />
                                                        {t('onlineCustomers.filters.active')}
                                                    </span>
                                                ) : (
                                                    <span className="inline-flex items-center gap-1 px-2.5 py-1 text-xs font-medium rounded-full bg-red-100 text-red-700">
                                                        <XCircle className="w-3.5 h-3.5" />
                                                        {t('onlineCustomers.filters.inactive')}
                                                    </span>
                                                )}
                                            </td>

                                            {/* Sessions */}
                                            <td className="px-6 py-4 text-center">
                                                <span className={`inline-flex items-center gap-1 text-sm ${customer.activeSessions > 0 ? 'text-primary-600' : 'text-gray-400'
                                                    }`}>
                                                    <Smartphone className="w-4 h-4" />
                                                    {customer.activeSessions || 0}
                                                </span>
                                            </td>

                                            {/* Last Login */}
                                            <td className="px-6 py-4">
                                                {customer.lastLoginAtUtc ? (
                                                    <div>
                                                        <div className="text-sm text-gray-900">
                                                            {formatDistanceToNow(new Date(customer.lastLoginAtUtc), { addSuffix: true, locale: getDateLocale() })}
                                                        </div>
                                                        <div className="text-xs text-gray-500">
                                                            {format(new Date(customer.lastLoginAtUtc), 'MMM d, yyyy', { locale: getDateLocale() })}
                                                        </div>
                                                    </div>
                                                ) : (
                                                    <span className="text-sm text-gray-400">Never</span>
                                                )}
                                            </td>

                                            {/* Actions */}
                                            <td className="px-6 py-4 text-right">
                                                <button
                                                    onClick={() => handleViewDetails(customer)}
                                                    className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-primary-600 hover:text-primary-700 hover:bg-primary-50 rounded-lg transition-colors"
                                                >
                                                    <Eye className="w-4 h-4" />
                                                    {t('onlineCustomers.table.view')}
                                                </button>
                                            </td>
                                        </tr>
                                    )
                                })
                            )}
                        </tbody>
                    </table>
                </div>

                {/* Pagination */}
                {totalPages > 1 && (
                    <div className="bg-gray-50 px-4 py-3 border-t border-gray-200 flex items-center justify-between">
                        <div className="text-sm text-gray-500">
                            {/* Showing X to Y of Z customers */}
                            Showing {((page - 1) * pageSize) + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount}
                        </div>
                        <div className="flex items-center gap-2">
                            <button
                                onClick={() => setPage(p => Math.max(1, p - 1))}
                                disabled={page === 1}
                                className="p-2 text-gray-500 hover:text-gray-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-lg hover:bg-gray-100"
                            >
                                <ChevronLeft className="w-5 h-5" />
                            </button>
                            <span className="text-sm text-gray-700 min-w-[80px] text-center">
                                Page {page} / {totalPages}
                            </span>
                            <button
                                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                                disabled={page === totalPages}
                                className="p-2 text-gray-500 hover:text-gray-700 disabled:opacity-50 disabled:cursor-not-allowed rounded-lg hover:bg-gray-100"
                            >
                                <ChevronRight className="w-5 h-5" />
                            </button>
                        </div>
                    </div>
                )}
            </div>

            {/* Customer Details Drawer */}
            <OnlineCustomerDetails
                customer={selectedCustomer}
                isOpen={showDetails}
                onClose={handleCloseDetails}
                onUpdate={handleCustomerUpdate}
            />
        </div>
    )
}

export default OnlineCustomersPage
