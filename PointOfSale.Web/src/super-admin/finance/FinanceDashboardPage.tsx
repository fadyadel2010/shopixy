import { useDeferredValue, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { RefreshCw, ShieldCheck, TrendingUp } from 'lucide-react'
import FinanceCharts from './FinanceCharts'
import FinanceKpiCards from './FinanceKpiCards'
import FinancePerformanceTables from './FinancePerformanceTables'
import FinanceReconciliationPanel from './FinanceReconciliationPanel'
import VendorFinanceWorkspace from './VendorFinanceWorkspace'
import {
    getFinancialIntegrityCheck,
    getFinancialOverview,
    getFinancialPromos,
    getFinancialRefunds,
    getFinancialTrends,
    getFinancialVendors,
    type SuperAdminFinancialTrendGroupBy,
    type SuperAdminPromoAnalyticsSortField,
    type SuperAdminSortDirection,
    type SuperAdminVendorPerformanceSortField,
} from './financeService'

const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(value: number, currency = 'EGP'): string {
    if (String(currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(value ?? 0))
}

function formatDateTime(value?: string | null): string {
    if (!value) {
        return 'Pending'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'Pending'
    }

    return date.toLocaleString()
}

function getErrorMessage(error: unknown, fallback: string): string {
    return error instanceof Error && error.message ? error.message : fallback
}

export default function FinanceDashboardPage() {
    const today = new Date()
    const defaultFrom = new Date(today)
    defaultFrom.setDate(today.getDate() - 29)

    const [filters, setFilters] = useState({
        fromDate: defaultFrom.toISOString().slice(0, 10),
        toDate: today.toISOString().slice(0, 10),
        vendorSearch: '',
        promoCode: '',
    })
    const [groupBy, setGroupBy] = useState<SuperAdminFinancialTrendGroupBy>('Day')
    const [promoTable, setPromoTable] = useState<{
        page: number
        pageSize: number
        sortBy: SuperAdminPromoAnalyticsSortField
        sortDirection: SuperAdminSortDirection
    }>({
        page: 1,
        pageSize: 10,
        sortBy: 'NetImpact',
        sortDirection: 'Desc',
    })
    const [vendorTable, setVendorTable] = useState<{
        page: number
        pageSize: number
        sortBy: SuperAdminVendorPerformanceSortField
        sortDirection: SuperAdminSortDirection
    }>({
        page: 1,
        pageSize: 10,
        sortBy: 'NetContribution',
        sortDirection: 'Desc',
    })

    const deferredVendorSearch = useDeferredValue(filters.vendorSearch.trim())
    const deferredPromoCode = useDeferredValue(filters.promoCode.trim().toUpperCase())
    const scopedFilters = {
        fromDate: filters.fromDate,
        toDate: filters.toDate,
        vendorSearch: deferredVendorSearch || undefined,
        promoCode: deferredPromoCode || undefined,
    }

    const overviewQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'dashboard', 'overview', scopedFilters],
        queryFn: () => getFinancialOverview(scopedFilters),
        staleTime: 30_000,
        refetchInterval: 45_000,
        refetchOnWindowFocus: true,
    })
    const trendsQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'dashboard', 'trends', scopedFilters, groupBy],
        queryFn: () => getFinancialTrends({ ...scopedFilters, groupBy }),
        staleTime: 30_000,
        refetchInterval: 45_000,
        refetchOnWindowFocus: true,
    })
    const promosQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'dashboard', 'promos', scopedFilters, promoTable],
        queryFn: () => getFinancialPromos({ ...scopedFilters, ...promoTable }),
        staleTime: 30_000,
        refetchInterval: 45_000,
        refetchOnWindowFocus: true,
    })
    const vendorsQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'dashboard', 'vendors', scopedFilters, vendorTable],
        queryFn: () => getFinancialVendors({ ...scopedFilters, ...vendorTable }),
        staleTime: 30_000,
        refetchInterval: 45_000,
        refetchOnWindowFocus: true,
    })
    const refundsQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'dashboard', 'refunds', scopedFilters, groupBy],
        queryFn: () => getFinancialRefunds({ ...scopedFilters, groupBy }),
        staleTime: 30_000,
        refetchInterval: 45_000,
        refetchOnWindowFocus: true,
    })
    const integrityQuery = useQuery({
        queryKey: ['super-admin', 'finance', 'dashboard', 'integrity', scopedFilters],
        queryFn: () => getFinancialIntegrityCheck({ ...scopedFilters, maxIssues: 10 }),
        staleTime: 30_000,
        refetchInterval: 45_000,
        refetchOnWindowFocus: true,
    })

    const overviewError = getErrorMessage(overviewQuery.error, 'Unable to load the financial overview.')
    const trendsError = trendsQuery.isError ? getErrorMessage(trendsQuery.error, 'Unable to load financial trends.') : null
    const promosError = promosQuery.isError ? getErrorMessage(promosQuery.error, 'Unable to load promo analytics.') : null
    const vendorsError = vendorsQuery.isError ? getErrorMessage(vendorsQuery.error, 'Unable to load vendor performance.') : null
    const refundsError = refundsQuery.isError ? getErrorMessage(refundsQuery.error, 'Unable to load refund analysis.') : null
    const integrityError = integrityQuery.isError ? getErrorMessage(integrityQuery.error, 'Unable to load the integrity check.') : null

    async function refreshAll() {
        await Promise.all([
            overviewQuery.refetch(),
            trendsQuery.refetch(),
            promosQuery.refetch(),
            vendorsQuery.refetch(),
            refundsQuery.refetch(),
            integrityQuery.refetch(),
        ])
    }

    function resetFilters() {
        const resetToday = new Date()
        const resetFrom = new Date(resetToday)
        resetFrom.setDate(resetToday.getDate() - 29)

        setFilters({
            fromDate: resetFrom.toISOString().slice(0, 10),
            toDate: resetToday.toISOString().slice(0, 10),
            vendorSearch: '',
            promoCode: '',
        })
        setGroupBy('Day')
        setPromoTable((current) => ({ ...current, page: 1 }))
        setVendorTable((current) => ({ ...current, page: 1 }))
    }

    if (overviewQuery.isError && !overviewQuery.data) {
        return (
            <div className="space-y-6">
                <div className="grid min-h-[30vh] place-items-center">
                    <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-red-50 text-red-600">
                            <TrendingUp className="h-6 w-6" />
                        </div>
                        <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                            Financial dashboard unavailable
                        </h2>
                        <p className="mt-3 text-sm leading-6 text-slate-500">{overviewError}</p>
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
                <VendorFinanceWorkspace />
            </div>
        )
    }

    const overview = overviewQuery.data
    const refunds = refundsQuery.data
    const integrity = integrityQuery.data
    const isRefreshing =
        overviewQuery.isFetching ||
        trendsQuery.isFetching ||
        promosQuery.isFetching ||
        vendorsQuery.isFetching ||
        refundsQuery.isFetching ||
        integrityQuery.isFetching

    return (
        <div className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(14,165,233,0.16),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(234,179,8,0.14),_transparent_24%)]" />
                    <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <div className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-4 py-2 text-sm font-semibold text-sky-800">
                                <ShieldCheck className="h-4 w-4" />
                                Super Admin financial control
                            </div>

                            <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                Ledger-backed visibility into platform revenue, promo funding, refunds, and vendor contribution
                            </h1>
                            <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                Every headline number on this screen is derived from CommerceHub order, promo usage, refund, and platform ledger records so the Super Admin can audit profitability without duplicating financial logic.
                            </p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2 xl:min-w-[420px]">
                            <div className={`rounded-3xl border px-5 py-4 shadow-sm ${integrity?.status === 'OK' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-rose-200 bg-rose-50 text-rose-900'}`}>
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-70">
                                    Integrity
                                </p>
                                <p className="mt-2 text-lg font-semibold">{integrity?.status ?? 'CHECKING'}</p>
                                <p className="mt-2 text-sm opacity-80">
                                    {integrity?.totalIssueCount ?? 0} issue flag{(integrity?.totalIssueCount ?? 0) === 1 ? '' : 's'}
                                </p>
                            </div>

                            <button
                                type="button"
                                className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                disabled={isRefreshing}
                                onClick={() => refreshAll()}
                            >
                                <RefreshCw className={`h-4 w-4 ${isRefreshing ? 'animate-spin' : ''}`} />
                                {isRefreshing ? 'Refreshing' : 'Refresh'}
                            </button>
                        </div>
                    </div>
                </div>

                <div className="grid gap-4 border-t border-slate-100 bg-slate-50/80 px-6 py-5 text-sm text-slate-600 sm:grid-cols-4 sm:px-8">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                            Reporting Window
                        </p>
                        <p className="mt-2 font-medium text-slate-900">
                            {formatDateTime(overview?.fromDateUtc)} to {formatDateTime(overview?.toDateUtc)}
                        </p>
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                            Net Revenue
                        </p>
                        <p className="mt-2 font-medium text-slate-900">
                            {formatCurrency(overview?.netRevenue ?? 0, overview?.currency)}
                        </p>
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                            Refund Alerts
                        </p>
                        <p className="mt-2 font-medium text-slate-900">
                            {refunds?.alerts.length ?? 0} active signal{(refunds?.alerts.length ?? 0) === 1 ? '' : 's'}
                        </p>
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                            Integrity Run
                        </p>
                        <p className="mt-2 font-medium text-slate-900">
                            {formatDateTime(integrity?.executedAtUtc)}
                        </p>
                    </div>
                </div>
            </section>

            <section className="rounded-[28px] border border-amber-200 bg-amber-50 px-6 py-5 text-amber-950 shadow-[0_20px_70px_-52px_rgba(120,53,15,0.45)]">
                <div className="flex flex-col gap-2 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <p className="text-sm font-semibold">
                            {overview?.dataFreshnessNote ?? 'Financial data may be slightly delayed due to processing'}
                        </p>
                        <p className="mt-1 text-xs leading-6 text-amber-900/80">
                            {overview?.consistencyModel ??
                                'Strong consistency applies to a single order flow. Aggregated dashboard metrics are eventually consistent.'}
                        </p>
                    </div>

                    <div className="text-xs leading-6 text-amber-900/80">
                        Source {overview?.dataSource ?? 'LIVE_LEDGER'} • Aggregated through{' '}
                        {formatDateTime(overview?.aggregatedThroughUtc ?? overview?.generatedAtUtc)}
                    </div>
                </div>
            </section>

            <FinanceKpiCards data={overview} isLoading={overviewQuery.isLoading} />

            <FinanceCharts
                overview={overview}
                trends={trendsQuery.data}
                refunds={refunds}
                isTrendsLoading={trendsQuery.isLoading}
                isRefundsLoading={refundsQuery.isLoading}
                trendsError={trendsError}
                refundsError={refundsError}
                fromDate={filters.fromDate}
                toDate={filters.toDate}
                vendorSearch={filters.vendorSearch}
                promoCode={filters.promoCode}
                groupBy={groupBy}
                onFromDateChange={(value) => {
                    setFilters((current) => ({ ...current, fromDate: value }))
                    setPromoTable((current) => ({ ...current, page: 1 }))
                    setVendorTable((current) => ({ ...current, page: 1 }))
                }}
                onToDateChange={(value) => {
                    setFilters((current) => ({ ...current, toDate: value }))
                    setPromoTable((current) => ({ ...current, page: 1 }))
                    setVendorTable((current) => ({ ...current, page: 1 }))
                }}
                onVendorSearchChange={(value) => {
                    setFilters((current) => ({ ...current, vendorSearch: value }))
                    setPromoTable((current) => ({ ...current, page: 1 }))
                    setVendorTable((current) => ({ ...current, page: 1 }))
                }}
                onPromoCodeChange={(value) => {
                    setFilters((current) => ({ ...current, promoCode: value.toUpperCase() }))
                    setPromoTable((current) => ({ ...current, page: 1 }))
                    setVendorTable((current) => ({ ...current, page: 1 }))
                }}
                onGroupByChange={setGroupBy}
                onResetFilters={resetFilters}
            />

            <section className="grid gap-6 xl:grid-cols-[1.4fr_1fr]">
                <article className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                    <div className="border-b border-slate-100 px-6 py-5">
                        <h2 className="text-xl font-semibold text-slate-950">Refund concentration</h2>
                        <p className="mt-2 text-sm text-slate-500">
                            Top vendors and items with the highest refund value inside the same dashboard scope.
                        </p>
                    </div>

                    {refundsError ? (
                        <div className="px-6 py-8 text-sm text-rose-700">{refundsError}</div>
                    ) : (
                        <div className="grid gap-6 px-6 py-6 lg:grid-cols-2">
                            <div>
                                <h3 className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-500">Top Vendors</h3>
                                <div className="mt-4 space-y-3">
                                    {(refunds?.topRefundedVendors ?? []).map((vendor) => (
                                        <div key={vendor.vendorId} className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-4">
                                            <p className="font-semibold text-slate-950">{vendor.vendorName}</p>
                                            <p className="mt-2 text-sm text-slate-600">
                                                {formatCurrency(vendor.refundAmount, vendor.currency)} across {vendor.refundCount} refund{vendor.refundCount === 1 ? '' : 's'}
                                            </p>
                                            <p className="mt-1 text-xs text-slate-500">
                                                Promo reversal {formatCurrency(vendor.promoReversalAmount, vendor.currency)}
                                            </p>
                                        </div>
                                    ))}
                                    {refunds && refunds.topRefundedVendors.length === 0 ? (
                                        <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-sm text-slate-500">
                                            No refunded vendors in this scope.
                                        </div>
                                    ) : null}
                                </div>
                            </div>

                            <div>
                                <h3 className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-500">Top Items</h3>
                                <div className="mt-4 space-y-3">
                                    {(refunds?.topRefundedItems ?? []).map((item) => (
                                        <div key={`${item.orderItemId ?? item.subOrderId}-${item.itemLabel}`} className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-4">
                                            <p className="font-semibold text-slate-950">{item.itemLabel}</p>
                                            <p className="mt-1 text-xs text-slate-500">{item.vendorName}</p>
                                            <p className="mt-2 text-sm text-slate-600">
                                                {formatCurrency(item.refundAmount, item.currency)} across {item.refundCount} refund{item.refundCount === 1 ? '' : 's'}
                                            </p>
                                            <p className="mt-1 text-xs text-slate-500">
                                                Refunded quantity {item.refundedQuantity}
                                            </p>
                                        </div>
                                    ))}
                                    {refunds && refunds.topRefundedItems.length === 0 ? (
                                        <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-sm text-slate-500">
                                            No refunded items in this scope.
                                        </div>
                                    ) : null}
                                </div>
                            </div>
                        </div>
                    )}
                </article>

                <article className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                    <div className="border-b border-slate-100 px-6 py-5">
                        <h2 className="text-xl font-semibold text-slate-950">Refund Signals</h2>
                        <p className="mt-2 text-sm text-slate-500">
                            Fast anomaly cues based on refund rate, bucket spikes, and promo-heavy reversals.
                        </p>
                    </div>

                    {refundsError ? (
                        <div className="px-6 py-8 text-sm text-rose-700">{refundsError}</div>
                    ) : (
                        <div className="space-y-3 px-6 py-6">
                            {(refunds?.alerts ?? []).map((alert) => (
                                <div
                                    key={`${alert.alertType}-${alert.bucketStartUtc ?? 'none'}-${alert.message}`}
                                    className={`rounded-[24px] border px-4 py-4 ${alert.severity === 'CRITICAL' ? 'border-rose-200 bg-rose-50 text-rose-900' : alert.severity === 'WARNING' ? 'border-amber-200 bg-amber-50 text-amber-900' : 'border-sky-200 bg-sky-50 text-sky-900'}`}
                                >
                                    <p className="font-semibold">{alert.alertType}</p>
                                    <p className="mt-2 text-sm">{alert.message}</p>
                                    {alert.bucketStartUtc ? (
                                        <p className="mt-2 text-xs opacity-80">{formatDateTime(alert.bucketStartUtc)}</p>
                                    ) : null}
                                </div>
                            ))}
                            {refunds && refunds.alerts.length === 0 ? (
                                <div className="rounded-[24px] border border-dashed border-emerald-200 bg-emerald-50 px-4 py-6 text-sm text-emerald-900">
                                    No refund anomaly signals were generated for the selected scope.
                                </div>
                            ) : null}
                        </div>
                    )}
                </article>
            </section>

            <FinancePerformanceTables
                promos={promosQuery.data}
                vendors={vendorsQuery.data}
                promosLoading={promosQuery.isLoading}
                vendorsLoading={vendorsQuery.isLoading}
                promosError={promosError}
                vendorsError={vendorsError}
                promoSortBy={promoTable.sortBy}
                promoSortDirection={promoTable.sortDirection}
                vendorSortBy={vendorTable.sortBy}
                vendorSortDirection={vendorTable.sortDirection}
                onPromoSortByChange={(value) => setPromoTable((current) => ({ ...current, page: 1, sortBy: value }))}
                onPromoSortDirectionChange={(value) => setPromoTable((current) => ({ ...current, page: 1, sortDirection: value }))}
                onVendorSortByChange={(value) => setVendorTable((current) => ({ ...current, page: 1, sortBy: value }))}
                onVendorSortDirectionChange={(value) => setVendorTable((current) => ({ ...current, page: 1, sortDirection: value }))}
                onPromoPreviousPage={() => setPromoTable((current) => ({ ...current, page: Math.max(1, current.page - 1) }))}
                onPromoNextPage={() => setPromoTable((current) => ({ ...current, page: current.page + 1 }))}
                onVendorPreviousPage={() => setVendorTable((current) => ({ ...current, page: Math.max(1, current.page - 1) }))}
                onVendorNextPage={() => setVendorTable((current) => ({ ...current, page: current.page + 1 }))}
            />

            <FinanceReconciliationPanel
                data={integrity}
                isLoading={integrityQuery.isLoading}
                errorMessage={integrityError}
            />

            <VendorFinanceWorkspace />
        </div>
    )
}
