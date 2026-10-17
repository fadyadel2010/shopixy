import { useCallback, useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
    Activity,
    AlertTriangle,
    Download,
    PackageSearch,
    RefreshCw,
    ShieldAlert,
} from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { useDebounce } from '../../hooks/useDebounce'
import OrderDetailsDrawer from './OrderDetailsDrawer'
import OrdersFilters from './OrdersFilters'
import OrdersTable from './OrdersTable'
import {
    SUPER_ADMIN_ORDER_STATUS_OPTIONS,
    SUPER_ADMIN_PAYMENT_FILTER_OPTIONS,
    downloadSuperAdminOrdersCsv,
    getSuperAdminOrderDetails,
    getSuperAdminOrderStats,
    getSuperAdminOrderTenants,
    getSuperAdminOrders,
    isFailedPaymentStatus,
    isPendingOrderStatus,
    type SuperAdminOrderPaymentFilter,
    type SuperAdminOrderStatus,
    type SuperAdminOrdersFilters,
    type SuperAdminOrdersSortField,
    type SuperAdminSortDirection,
} from './ordersService'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20
const DEFAULT_SORT_BY: SuperAdminOrdersSortField = 'CreatedAt'
const DEFAULT_SORT_DIRECTION: SuperAdminSortDirection = 'Desc'
const PENDING_ALERT_THRESHOLD = 10
const pageSizeOptions = [20, 50, 100]
const validSortFields: SuperAdminOrdersSortField[] = [
    'CreatedAt',
    'OrderNumber',
    'Customer',
    'Tenant',
    'TotalAmount',
    'Status',
    'PaymentStatus',
    'VendorCount',
]
const validSortDirections: SuperAdminSortDirection[] = ['Asc', 'Desc']

function parsePositiveInt(value: string | null, fallback: number): number {
    const parsed = Number.parseInt(value ?? '', 10)
    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function parseSortField(value: string | null): SuperAdminOrdersSortField {
    return validSortFields.includes(value as SuperAdminOrdersSortField)
        ? (value as SuperAdminOrdersSortField)
        : DEFAULT_SORT_BY
}

function parseSortDirection(value: string | null): SuperAdminSortDirection {
    return validSortDirections.includes(value as SuperAdminSortDirection)
        ? (value as SuperAdminSortDirection)
        : DEFAULT_SORT_DIRECTION
}

function parseStatus(value: string | null): SuperAdminOrderStatus | '' {
    return SUPER_ADMIN_ORDER_STATUS_OPTIONS.includes(value as SuperAdminOrderStatus)
        ? (value as SuperAdminOrderStatus)
        : ''
}

function parsePaymentStatus(value: string | null): SuperAdminOrderPaymentFilter | '' {
    return SUPER_ADMIN_PAYMENT_FILTER_OPTIONS.includes(value as SuperAdminOrderPaymentFilter)
        ? (value as SuperAdminOrderPaymentFilter)
        : ''
}

function buildFilters(searchParams: URLSearchParams): SuperAdminOrdersFilters {
    return {
        page: parsePositiveInt(searchParams.get('page'), DEFAULT_PAGE),
        pageSize: parsePositiveInt(searchParams.get('pageSize'), DEFAULT_PAGE_SIZE),
        search: searchParams.get('search') ?? '',
        status: parseStatus(searchParams.get('status')),
        paymentStatus: parsePaymentStatus(searchParams.get('paymentStatus')),
        tenantId: searchParams.get('tenantId') ?? '',
        fromDate: searchParams.get('fromDate') ?? '',
        toDate: searchParams.get('toDate') ?? '',
        sortBy: parseSortField(searchParams.get('sortBy')),
        sortDirection: parseSortDirection(searchParams.get('sortDirection')),
    }
}

function formatTimestamp(value?: string): string {
    if (!value) {
        return 'Awaiting sync'
    }

    const timestamp = new Date(value)
    if (Number.isNaN(timestamp.getTime())) {
        return 'Awaiting sync'
    }

    return timestamp.toLocaleString()
}

function formatCurrency(amount: number, currency: string): string {
    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: currency || 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(amount)
}

function getErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof Error && error.message) {
        return error.message
    }

    return fallback
}

function StatCard({
    label,
    value,
    caption,
    tone,
    isLoading,
}: {
    label: string
    value: string
    caption: string
    tone: string
    isLoading: boolean
}) {
    return (
        <div className={`rounded-3xl border px-5 py-4 shadow-sm ${tone}`}>
            <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-75">{label}</p>
            {isLoading ? (
                <div className="mt-3 animate-pulse">
                    <div className="h-7 w-24 rounded-full bg-white/60" />
                    <div className="mt-2 h-4 w-28 rounded-full bg-white/40" />
                </div>
            ) : (
                <>
                    <p className="mt-2 text-2xl font-semibold">{value}</p>
                    <p className="mt-2 text-xs font-medium opacity-80">{caption}</p>
                </>
            )}
        </div>
    )
}

export default function OrdersPage() {
    const [searchParams, setSearchParams] = useSearchParams()
    const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null)
    const [isExporting, setIsExporting] = useState(false)
    const [exportError, setExportError] = useState<string | null>(null)
    const filters = useMemo(() => buildFilters(searchParams), [searchParams])
    const [searchInput, setSearchInput] = useState(filters.search ?? '')
    const debouncedSearch = useDebounce(searchInput, 400)

    useEffect(() => {
        setSearchInput(filters.search ?? '')
    }, [filters.search])

    const updateParams = useCallback(
        (updates: Record<string, string | null | undefined>) => {
            setSearchParams((previous) => {
                const next = new URLSearchParams(previous)

                Object.entries(updates).forEach(([key, value]) => {
                    if (!value) {
                        next.delete(key)
                        return
                    }

                    next.set(key, value)
                })

                return next
            })
        },
        [setSearchParams],
    )

    useEffect(() => {
        if (debouncedSearch !== searchInput) {
            return
        }

        const currentSearch = filters.search ?? ''
        if (debouncedSearch === currentSearch) {
            return
        }

        updateParams({
            search: debouncedSearch || null,
            page: String(DEFAULT_PAGE),
        })
    }, [debouncedSearch, filters.search, searchInput, updateParams])

    const ordersQuery = useQuery({
        queryKey: ['super-admin', 'orders', filters],
        queryFn: () => getSuperAdminOrders(filters),
        staleTime: 30_000,
        refetchOnWindowFocus: true,
    })

    const statsQuery = useQuery({
        queryKey: ['super-admin', 'orders', 'stats', filters.tenantId || 'all'],
        queryFn: () => getSuperAdminOrderStats(filters.tenantId || undefined),
        staleTime: 15_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: false,
    })

    const tenantsQuery = useQuery({
        queryKey: ['super-admin', 'orders', 'tenants'],
        queryFn: getSuperAdminOrderTenants,
        staleTime: 5 * 60_000,
    })

    const detailsQuery = useQuery({
        queryKey: ['super-admin', 'orders', 'details', selectedOrderId],
        queryFn: () => getSuperAdminOrderDetails(selectedOrderId as string),
        enabled: Boolean(selectedOrderId),
        staleTime: 30_000,
    })

    const orders = useMemo(() => ordersQuery.isFetching || ordersQuery.isError ? [] : (ordersQuery.data?.orders ?? []),
        [ordersQuery.isFetching, ordersQuery.isError, ordersQuery.data?.orders])
    const meta = ordersQuery.data?.meta
    const totalCount = meta?.totalCount ?? 0
    const totalPages = meta?.totalPages ?? 0
    const currentPageFailedPayments = orders.filter((order) => isFailedPaymentStatus(order.paymentStatus)).length
    const currentPagePendingOrders = orders.filter((order) => isPendingOrderStatus(order.status)).length
    const currentPageMultiVendorOrders = orders.filter((order) => order.vendorCount > 1).length
    const activeFilterCount = [
        filters.search,
        filters.status,
        filters.paymentStatus,
        filters.tenantId,
        filters.fromDate,
        filters.toDate,
    ].filter(Boolean).length

    const stats = statsQuery.data
    const showIssueAlert =
        (stats?.failedPayments ?? 0) > 0 ||
        (stats?.pendingOrders ?? 0) >= PENDING_ALERT_THRESHOLD

    const issueAlertMessage = useMemo(() => {
        if (!stats) {
            return null
        }

        const parts: string[] = []

        if (stats.failedPayments > 0) {
            parts.push(
                `${stats.failedPayments} failed payment${stats.failedPayments === 1 ? '' : 's'} require attention`,
            )
        }

        if (stats.pendingOrders >= PENDING_ALERT_THRESHOLD) {
            parts.push(
                `${stats.pendingOrders} pending order${stats.pendingOrders === 1 ? '' : 's'} are building up`,
            )
        }

        return parts.join(' | ')
    }, [stats])

    function handleResetFilters() {
        setSearchInput('')
        setExportError(null)
        setSearchParams({
            page: String(DEFAULT_PAGE),
            pageSize: String(DEFAULT_PAGE_SIZE),
            sortBy: DEFAULT_SORT_BY,
            sortDirection: DEFAULT_SORT_DIRECTION,
        })
    }

    function setFilterParam(
        key: 'status' | 'paymentStatus' | 'tenantId' | 'fromDate' | 'toDate',
        value: string,
    ) {
        setExportError(null)
        updateParams({
            [key]: value || null,
            page: String(DEFAULT_PAGE),
        })
    }

    function handleSortChange(field: SuperAdminOrdersSortField) {
        const nextDirection: SuperAdminSortDirection =
            filters.sortBy === field && filters.sortDirection === 'Desc' ? 'Asc' : 'Desc'

        updateParams({
            sortBy: field,
            sortDirection: nextDirection,
            page: String(DEFAULT_PAGE),
        })
    }

    function handlePageChange(page: number) {
        updateParams({
            page: String(page),
        })
    }

    function handlePageSizeChange(pageSize: number) {
        updateParams({
            pageSize: String(pageSize),
            page: String(DEFAULT_PAGE),
        })
    }

    useEffect(() => {
        if (!selectedOrderId || ordersQuery.isFetching) {
            return
        }

        if (!orders.some((order) => order.orderId === selectedOrderId)) {
            setSelectedOrderId(null)
        }
    }, [orders, ordersQuery.isFetching, selectedOrderId])

    async function refreshAll() {
        await Promise.all([
            ordersQuery.refetch(),
            statsQuery.refetch(),
            tenantsQuery.refetch(),
            selectedOrderId ? detailsQuery.refetch() : Promise.resolve(),
        ])
    }

    async function handleExport() {
        setIsExporting(true)
        setExportError(null)

        try {
            const blob = await downloadSuperAdminOrdersCsv(filters)
            const downloadUrl = window.URL.createObjectURL(blob)
            const link = document.createElement('a')
            link.href = downloadUrl
            link.download = `commercehub-orders-${new Date().toISOString().slice(0, 10)}.csv`
            document.body.appendChild(link)
            link.click()
            link.remove()
            window.URL.revokeObjectURL(downloadUrl)
        } catch (error) {
            setExportError(
                getErrorMessage(error, 'Unable to export the filtered orders right now.'),
            )
        } finally {
            setIsExporting(false)
        }
    }

    if (ordersQuery.isError && !ordersQuery.data) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <ShieldAlert className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                        Orders monitoring unavailable
                    </h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {getErrorMessage(ordersQuery.error, 'Unable to load the Super Admin orders dashboard.')}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        onClick={() => refreshAll()}
                    >
                        <RefreshCw className="h-4 w-4" />
                        Retry request
                    </button>
                </div>
            </div>
        )
    }

    return (
        <>
            <div className="space-y-6">
                <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                    <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(249,115,22,0.12),_transparent_26%)]" />
                        <div className="relative flex flex-col gap-6">
                            <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                                <div className="max-w-3xl">
                                    <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                        <Activity className="h-4 w-4" />
                                        Orders Monitoring
                                    </div>
                                    <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                        Monitor every CommerceHub order with live issue visibility, payment health, and full order inspection
                                    </h1>
                                    <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                        Scan the full platform order stream, surface failed payments and aging pending orders quickly, then drill into vendor splits, purchased items, and financial snapshots without leaving the page.
                                    </p>
                                </div>

                                <div className="flex flex-col gap-3 sm:flex-row">
                                    <button
                                        type="button"
                                        className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                        disabled={ordersQuery.isFetching || statsQuery.isFetching}
                                        onClick={() => refreshAll()}
                                    >
                                        <RefreshCw
                                            className={`h-4 w-4 ${ordersQuery.isFetching || statsQuery.isFetching ? 'animate-spin' : ''}`}
                                        />
                                        {ordersQuery.isFetching || statsQuery.isFetching ? 'Refreshing' : 'Refresh'}
                                    </button>
                                    <button
                                        type="button"
                                        className="inline-flex items-center justify-center gap-2 rounded-3xl border border-slate-200 bg-white px-5 py-4 text-sm font-semibold text-slate-700 shadow-sm transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-70"
                                        disabled={isExporting}
                                        onClick={() => handleExport()}
                                    >
                                        <Download className={`h-4 w-4 ${isExporting ? 'animate-bounce' : ''}`} />
                                        {isExporting ? 'Exporting CSV' : 'Export CSV'}
                                    </button>
                                </div>
                            </div>

                            <div className="grid gap-3 lg:grid-cols-4">
                                <StatCard
                                    label="Total Orders Today"
                                    value={String(stats?.totalOrdersToday ?? 0)}
                                    caption="UTC day window"
                                    tone="border-slate-200 bg-white text-slate-950"
                                    isLoading={statsQuery.isLoading && !stats}
                                />
                                <StatCard
                                    label="Pending Orders"
                                    value={String(stats?.pendingOrders ?? 0)}
                                    caption="Current pending backlog"
                                    tone="border-amber-200 bg-amber-50 text-amber-950"
                                    isLoading={statsQuery.isLoading && !stats}
                                />
                                <StatCard
                                    label="Failed Payments"
                                    value={String(stats?.failedPayments ?? 0)}
                                    caption="Requires payment follow-up"
                                    tone="border-rose-200 bg-rose-50 text-rose-950"
                                    isLoading={statsQuery.isLoading && !stats}
                                />
                                <StatCard
                                    label="Revenue Today"
                                    value={formatCurrency(stats?.revenueToday ?? 0, stats?.currency ?? 'EGP')}
                                    caption="UTC day window"
                                    tone="border-emerald-200 bg-emerald-50 text-emerald-950"
                                    isLoading={statsQuery.isLoading && !stats}
                                />
                            </div>
                        </div>
                    </div>

                    <div className="grid gap-4 border-t border-slate-100 bg-slate-50/80 px-6 py-5 text-sm text-slate-600 sm:grid-cols-4 sm:px-8">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                API Timestamp
                            </p>
                            <p className="mt-2 font-medium text-slate-900">
                                {formatTimestamp(meta?.timestamp)}
                            </p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                Live Stats As Of
                            </p>
                            <p className="mt-2 font-medium text-slate-900">
                                {formatTimestamp(stats?.asOfUtc)}
                            </p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                Active Filters
                            </p>
                            <p className="mt-2 font-medium text-slate-900">{activeFilterCount}</p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                Matching Orders
                            </p>
                            <p className="mt-2 font-medium text-slate-900">{totalCount}</p>
                        </div>
                    </div>
                </section>

                {showIssueAlert && issueAlertMessage ? (
                    <section className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-amber-950 shadow-sm">
                        <div className="flex items-start gap-3">
                            <AlertTriangle className="mt-0.5 h-5 w-5" />
                            <div>
                                <p className="text-sm font-semibold">Operational attention needed</p>
                                <p className="mt-1 text-sm leading-6 opacity-85">{issueAlertMessage}</p>
                            </div>
                        </div>
                    </section>
                ) : null}

                {statsQuery.isError ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        Operational summary cards are temporarily unavailable. The detailed orders table is still live.
                    </div>
                ) : null}

                {exportError ? (
                    <div className="rounded-[28px] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900 shadow-sm">
                        {exportError}
                    </div>
                ) : null}

                <OrdersFilters
                    filters={filters}
                    searchValue={searchInput}
                    tenants={tenantsQuery.data ?? []}
                    isLoadingTenants={tenantsQuery.isLoading}
                    isRefreshing={ordersQuery.isFetching}
                    onSearchChange={setSearchInput}
                    onStatusChange={(value) => setFilterParam('status', value)}
                    onPaymentStatusChange={(value) => setFilterParam('paymentStatus', value)}
                    onTenantChange={(value) => setFilterParam('tenantId', value)}
                    onFromDateChange={(value) => setFilterParam('fromDate', value)}
                    onToDateChange={(value) => setFilterParam('toDate', value)}
                    onReset={handleResetFilters}
                />

                {ordersQuery.isError && ordersQuery.data ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        Current order authority refresh failed, so cached order rows are withheld until the next accepted backend response.
                    </div>
                ) : null}

                {tenantsQuery.isError ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        Tenant lookup is temporarily unavailable, so the tenant filter falls back to "All tenants" until the next successful refresh.
                    </div>
                ) : null}

                <OrdersTable
                    orders={orders}
                    isLoading={ordersQuery.isLoading || ordersQuery.isFetching}
                    isFetching={ordersQuery.isFetching}
                    selectedOrderId={selectedOrderId}
                    sortBy={filters.sortBy}
                    sortDirection={filters.sortDirection}
                    onSortChange={handleSortChange}
                    onSelectOrder={setSelectedOrderId}
                />

                <section className="flex flex-col gap-4 rounded-[32px] border border-slate-200 bg-white px-6 py-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)] lg:flex-row lg:items-center lg:justify-between">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-600">
                            <PackageSearch className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                                Pagination
                            </p>
                            <p className="mt-1 text-sm text-slate-700">
                                Page {filters.page} of {totalPages || 1} with {totalCount} matching orders. Current page includes {currentPageFailedPayments} failed payment issue{currentPageFailedPayments === 1 ? '' : 's'}, {currentPagePendingOrders} pending order{currentPagePendingOrders === 1 ? '' : 's'}, and {currentPageMultiVendorOrders} multi-vendor order{currentPageMultiVendorOrders === 1 ? '' : 's'}.
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                        <label className="flex items-center gap-3 text-sm text-slate-600">
                            <span>Page size</span>
                            <select
                                value={filters.pageSize}
                                onChange={(event) => handlePageSizeChange(Number(event.target.value))}
                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                {pageSizeOptions.map((size) => (
                                    <option key={size} value={size}>
                                        {size}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <div className="flex items-center gap-3">
                            <button
                                type="button"
                                className="inline-flex items-center justify-center rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={filters.page <= 1}
                                onClick={() => handlePageChange(filters.page - 1)}
                            >
                                Previous
                            </button>
                            <button
                                type="button"
                                className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={!totalPages || filters.page >= totalPages}
                                onClick={() => handlePageChange(filters.page + 1)}
                            >
                                Next
                            </button>
                        </div>
                    </div>
                </section>
            </div>

            <OrderDetailsDrawer
                isOpen={Boolean(selectedOrderId)}
                order={detailsQuery.data}
                isLoading={detailsQuery.isLoading || detailsQuery.isFetching}
                isError={detailsQuery.isError}
                errorMessage={
                    detailsQuery.isError
                        ? getErrorMessage(detailsQuery.error, 'Unable to load the requested order details.')
                        : null
                }
                onClose={() => setSelectedOrderId(null)}
                onRetry={() => detailsQuery.refetch()}
            />
        </>
    )
}
