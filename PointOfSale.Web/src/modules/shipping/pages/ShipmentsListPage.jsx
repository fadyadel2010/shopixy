/**
 * Shipments List Page
 * Phase: Shipping Frontend
 * 
 * Route: /admin/shipping/shipments
 * 
 * Features:
 * - Paginated table
 * - Filters: Status, Carrier, COD/Prepaid, Date range
 * - Row click → Shipment Details
 * 
 * API: GET /api/admin/shipments
 * 
 * PURE RENDERER - NO LIFECYCLE LOGIC
 */

import { useState, useMemo, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import {
    Package, Filter, Search, X, RefreshCw,
    ChevronDown, Calendar
} from 'lucide-react'
import { useShipmentsList, SHIPMENT_STATUS_CONFIG } from '../hooks/useShipmentsApi'
import ShipmentTable from '../components/ShipmentTable'
import { useDebounce } from '../../../hooks/useDebounce'

// Available carriers (display only)
const CARRIERS = [
    { code: '', label: 'All Carriers' },
    { code: 'bosta', label: 'Bosta' },
    { code: 'aramex', label: 'Aramex' },
    { code: 'mylerz', label: 'Mylerz' },
    { code: 'dhl', label: 'DHL Express' }
]

export default function ShipmentsListPage() {
    const { t, i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'

    // Filters state
    const [filters, setFilters] = useState({
        status: '',
        carrierCode: '',
        isCod: undefined,
        search: '',
        dateFrom: '',
        dateTo: ''
    })
    const [showFilters, setShowFilters] = useState(false)

    // Debounced search
    const [searchInput, setSearchInput] = useState('')
    const debouncedSearch = useDebounce(searchInput, 500)

    useEffect(() => {
        setFilters((current) => (
            current.search === debouncedSearch
                ? current
                : { ...current, search: debouncedSearch }
        ))
    }, [debouncedSearch])

    // API hook
    const {
        shipments,
        pagination,
        loading,
        error,
        goToPage,
        refetch
    } = useShipmentsList(filters)

    // Status options
    const statusOptions = useMemo(() => [
        { value: '', label: 'All Statuses' },
        ...Object.keys(SHIPMENT_STATUS_CONFIG).map(status => ({
            value: status,
            label: status,
            config: SHIPMENT_STATUS_CONFIG[status]
        }))
    ], [])

    // Clear all filters
    const clearFilters = () => {
        setFilters({
            status: '',
            carrierCode: '',
            isCod: undefined,
            search: '',
            dateFrom: '',
            dateTo: ''
        })
        setSearchInput('')
    }

    // Check if any filters are active
    const hasActiveFilters = filters.status || filters.carrierCode ||
        filters.isCod !== undefined || filters.search ||
        filters.dateFrom || filters.dateTo

    return (
        <div className="max-w-7xl mx-auto">
            {/* Header */}
            <div className="flex items-center justify-between mb-6">
                <div className="flex items-center gap-3">
                    <div className="w-10 h-10 bg-blue-100 rounded-xl flex items-center justify-center">
                        <Package className="text-blue-600" size={22} />
                    </div>
                    <div>
                        <h1 className="text-2xl font-bold text-gray-900">
                            {t('shipping.shipments', 'Shipments')}
                        </h1>
                        <p className="text-gray-500 text-sm">
                            {t('shipping.shipmentsDesc', 'Manage and track all shipments')}
                        </p>
                    </div>
                </div>

                <button
                    onClick={refetch}
                    disabled={loading}
                    className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-gray-600 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                >
                    <RefreshCw size={16} className={loading ? 'animate-spin' : ''} />
                    {t('common.refresh', 'Refresh')}
                </button>
            </div>

            {/* Search & Filters Bar */}
            <div className="bg-white border border-gray-200 rounded-xl p-4 mb-6">
                <div className="flex flex-wrap items-center gap-4">
                    {/* Search */}
                    <div className="flex-1 min-w-[200px] max-w-md relative">
                        <Search
                            size={18}
                            className={`absolute top-1/2 -translate-y-1/2 text-gray-400 ${isRTL ? 'right-3' : 'left-3'}`}
                        />
                        <input
                            type="text"
                            value={searchInput}
                            onChange={(e) => setSearchInput(e.target.value)}
                            placeholder={t('shipping.searchPlaceholder', 'Search by tracking number, order...')}
                            className={`w-full py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent ${isRTL ? 'pr-10 pl-4' : 'pl-10 pr-4'}`}
                        />
                    </div>

                    {/* Quick Filters */}
                    <select
                        value={filters.status}
                        onChange={(e) => setFilters(f => ({ ...f, status: e.target.value }))}
                        className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 bg-white"
                    >
                        {statusOptions.map(opt => (
                            <option key={opt.value} value={opt.value}>
                                {opt.config?.icon || ''} {opt.label}
                            </option>
                        ))}
                    </select>

                    <select
                        value={filters.carrierCode}
                        onChange={(e) => setFilters(f => ({ ...f, carrierCode: e.target.value }))}
                        className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 bg-white"
                    >
                        {CARRIERS.map(c => (
                            <option key={c.code} value={c.code}>{c.label}</option>
                        ))}
                    </select>

                    {/* More Filters Toggle */}
                    <button
                        onClick={() => setShowFilters(!showFilters)}
                        className={`flex items-center gap-2 px-4 py-2 border rounded-lg font-medium transition-colors
                            ${showFilters || hasActiveFilters
                                ? 'border-blue-300 bg-blue-50 text-blue-700'
                                : 'border-gray-300 text-gray-600 hover:bg-gray-50'
                            }
                        `}
                    >
                        <Filter size={16} />
                        More Filters
                        <ChevronDown
                            size={16}
                            className={`transition-transform ${showFilters ? 'rotate-180' : ''}`}
                        />
                    </button>

                    {/* Clear Filters */}
                    {hasActiveFilters && (
                        <button
                            onClick={clearFilters}
                            className="flex items-center gap-1 px-3 py-2 text-sm text-red-600 hover:bg-red-50 rounded-lg"
                        >
                            <X size={14} />
                            Clear All
                        </button>
                    )}
                </div>

                {/* Expanded Filters */}
                {showFilters && (
                    <div className="mt-4 pt-4 border-t border-gray-100 grid grid-cols-1 md:grid-cols-3 gap-4">
                        {/* Payment Type */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                Payment Type
                            </label>
                            <select
                                value={filters.isCod === undefined ? '' : filters.isCod.toString()}
                                onChange={(e) => {
                                    const val = e.target.value
                                    setFilters(f => ({
                                        ...f,
                                        isCod: val === '' ? undefined : val === 'true'
                                    }))
                                }}
                                className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 bg-white"
                            >
                                <option value="">All</option>
                                <option value="true">COD Only</option>
                                <option value="false">Prepaid Only</option>
                            </select>
                        </div>

                        {/* Date From */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                From Date
                            </label>
                            <div className="relative">
                                <Calendar
                                    size={18}
                                    className={`absolute top-1/2 -translate-y-1/2 text-gray-400 ${isRTL ? 'right-3' : 'left-3'}`}
                                />
                                <input
                                    type="date"
                                    value={filters.dateFrom}
                                    onChange={(e) => setFilters(f => ({ ...f, dateFrom: e.target.value }))}
                                    className={`w-full py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'pr-10 pl-4' : 'pl-10 pr-4'}`}
                                />
                            </div>
                        </div>

                        {/* Date To */}
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">
                                To Date
                            </label>
                            <div className="relative">
                                <Calendar
                                    size={18}
                                    className={`absolute top-1/2 -translate-y-1/2 text-gray-400 ${isRTL ? 'right-3' : 'left-3'}`}
                                />
                                <input
                                    type="date"
                                    value={filters.dateTo}
                                    onChange={(e) => setFilters(f => ({ ...f, dateTo: e.target.value }))}
                                    className={`w-full py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 ${isRTL ? 'pr-10 pl-4' : 'pl-10 pr-4'}`}
                                />
                            </div>
                        </div>
                    </div>
                )}
            </div>

            {/* Error State */}
            {error && (
                <div className="bg-red-50 border border-red-200 rounded-xl p-4 mb-6">
                    <p className="text-red-600">{error}</p>
                    <button
                        onClick={refetch}
                        className="mt-2 text-sm text-red-700 hover:underline"
                    >
                        Try again
                    </button>
                </div>
            )}

            {/* Shipments Table */}
            <ShipmentTable
                shipments={shipments}
                pagination={pagination}
                loading={loading}
                onPageChange={goToPage}
            />

            {/* Summary */}
            {!loading && pagination.totalCount > 0 && (
                <div className="mt-4 text-sm text-gray-500 text-center">
                    Total: {pagination.totalCount} shipment{pagination.totalCount !== 1 ? 's' : ''}
                </div>
            )}
        </div>
    )
}
