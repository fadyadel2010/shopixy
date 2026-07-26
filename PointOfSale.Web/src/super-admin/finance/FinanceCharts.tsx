import {
    CalendarRange,
    LineChart as LineChartIcon,
    RotateCcw,
    ShieldAlert,
} from 'lucide-react'
import {
    CartesianGrid,
    Legend,
    Line,
    LineChart,
    ResponsiveContainer,
    Tooltip,
    XAxis,
    YAxis,
} from 'recharts'
import type {
    SuperAdminFinancialOverview,
    SuperAdminFinancialTrendGroupBy,
    SuperAdminFinancialTrends,
    SuperAdminRefundAnalysis,
} from './financeService'

interface FinanceChartsProps {
    overview?: SuperAdminFinancialOverview
    trends?: SuperAdminFinancialTrends
    refunds?: SuperAdminRefundAnalysis
    isTrendsLoading?: boolean
    isRefundsLoading?: boolean
    trendsError?: string | null
    refundsError?: string | null
    fromDate: string
    toDate: string
    vendorSearch: string
    promoCode: string
    groupBy: SuperAdminFinancialTrendGroupBy
    onFromDateChange: (value: string) => void
    onToDateChange: (value: string) => void
    onVendorSearchChange: (value: string) => void
    onPromoCodeChange: (value: string) => void
    onGroupByChange: (value: SuperAdminFinancialTrendGroupBy) => void
    onResetFilters: () => void
}

const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(value: number, currency = 'EGP'): string {
    if ((currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(value ?? 0))
}

function formatCompactCurrency(value: number, currency = 'EGP'): string {
    if ((currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        notation: 'compact',
        minimumFractionDigits: 0,
        maximumFractionDigits: 1,
    }).format(Number(value ?? 0))
}

function formatDateLabel(value?: string): string {
    if (!value) {
        return 'Unknown'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'Unknown'
    }

    return date.toLocaleDateString('en-US', {
        month: 'short',
        day: 'numeric',
    })
}

function ChartSkeleton() {
    return (
        <div className="h-[320px] rounded-[28px] border border-slate-200 bg-slate-50 p-5">
            <div className="h-full animate-pulse rounded-[20px] bg-gradient-to-br from-slate-100 via-white to-slate-100" />
        </div>
    )
}

function TrendTooltip({
    active,
    payload,
    label,
    currency,
}: {
    active?: boolean
    payload?: Array<{ dataKey?: string; value?: number }>
    label?: string
    currency: string
}) {
    if (!active || !payload?.length) {
        return null
    }

    const rowByKey = new Map(payload.map((item) => [String(item.dataKey), Number(item.value ?? 0)]))

    return (
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-xl shadow-slate-900/10">
            <p className="text-sm font-semibold text-slate-950">{formatDateLabel(label)}</p>
            <div className="mt-3 space-y-2 text-sm text-slate-600">
                <div className="flex items-center justify-between gap-4">
                    <span>Gross Commission</span>
                    <span className="font-semibold text-sky-700">
                        {formatCurrency(rowByKey.get('grossCommission') ?? 0, currency)}
                    </span>
                </div>
                <div className="flex items-center justify-between gap-4">
                    <span>Promo Cost</span>
                    <span className="font-semibold text-amber-700">
                        {formatCurrency(rowByKey.get('promoCost') ?? 0, currency)}
                    </span>
                </div>
                <div className="flex items-center justify-between gap-4">
                    <span>Net Revenue</span>
                    <span className="font-semibold text-emerald-700">
                        {formatCurrency(rowByKey.get('netRevenue') ?? 0, currency)}
                    </span>
                </div>
                <div className="flex items-center justify-between gap-4">
                    <span>Refund Amount</span>
                    <span className="font-semibold text-rose-700">
                        {formatCurrency(rowByKey.get('refundAmount') ?? 0, currency)}
                    </span>
                </div>
            </div>
        </div>
    )
}

function FilterPill({
    label,
    value,
}: {
    label: string
    value: string
}) {
    return (
        <div className="rounded-full border border-slate-200 bg-white px-4 py-2 text-sm text-slate-600">
            <span className="font-semibold text-slate-900">{label}:</span> {value}
        </div>
    )
}

export default function FinanceCharts({
    overview,
    trends,
    refunds,
    isTrendsLoading,
    isRefundsLoading,
    trendsError,
    refundsError,
    fromDate,
    toDate,
    vendorSearch,
    promoCode,
    groupBy,
    onFromDateChange,
    onToDateChange,
    onVendorSearchChange,
    onPromoCodeChange,
    onGroupByChange,
    onResetFilters,
}: FinanceChartsProps) {
    const currency = trends?.currency || refunds?.currency || overview?.currency || 'EGP'
    const trendRows = trends?.points ?? []
    const refundRows = refunds?.points ?? []

    return (
        <section className="space-y-6">
            <article className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="border-b border-slate-100 px-6 py-5">
                    <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                                <CalendarRange className="h-4 w-4" />
                                Financial Filters
                            </div>
                            <h2 className="mt-4 text-2xl font-semibold text-slate-950">
                                Revenue, promo funding, and refund drag from the CommerceHub ledger
                            </h2>
                            <p className="mt-3 text-sm leading-7 text-slate-500">
                                Overview cards and charts are scoped by financial recognition date, vendor, and promo code without re-implementing checkout or refund math.
                            </p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                            <label className="space-y-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                                    From
                                </span>
                                <input
                                    type="date"
                                    value={fromDate}
                                    onChange={(event) => onFromDateChange(event.target.value)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                            </label>

                            <label className="space-y-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                                    To
                                </span>
                                <input
                                    type="date"
                                    value={toDate}
                                    onChange={(event) => onToDateChange(event.target.value)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                            </label>

                            <label className="space-y-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                                    Bucket
                                </span>
                                <select
                                    value={groupBy}
                                    onChange={(event) => onGroupByChange(event.target.value as SuperAdminFinancialTrendGroupBy)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                >
                                    <option value="Day">Daily</option>
                                    <option value="Month">Monthly</option>
                                </select>
                            </label>

                            <label className="space-y-2 sm:col-span-2 xl:col-span-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                                    Vendor Filter
                                </span>
                                <input
                                    type="text"
                                    value={vendorSearch}
                                    onChange={(event) => onVendorSearchChange(event.target.value)}
                                    placeholder="Search vendor name, slug, email, or id"
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                            </label>

                            <label className="space-y-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                                    Promo Code
                                </span>
                                <input
                                    type="text"
                                    value={promoCode}
                                    onChange={(event) => onPromoCodeChange(event.target.value)}
                                    placeholder="PROMO2026"
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm uppercase shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                            </label>

                            <div className="flex items-end">
                                <button
                                    type="button"
                                    className="inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                    onClick={onResetFilters}
                                >
                                    <RotateCcw className="h-4 w-4" />
                                    Reset
                                </button>
                            </div>
                        </div>
                    </div>
                </div>

                <div className="flex flex-wrap gap-3 border-b border-slate-100 bg-slate-50/70 px-6 py-4">
                    <FilterPill label="Scope" value={overview?.orderScope || 'SubOrder'} />
                    <FilterPill label="Date" value={`${fromDate || 'Open'} to ${toDate || 'Open'}`} />
                    <FilterPill label="Vendor" value={vendorSearch.trim() || 'All vendors'} />
                    <FilterPill label="Promo" value={promoCode.trim().toUpperCase() || 'All promos'} />
                </div>

                <div className="grid gap-6 px-6 py-6 xl:grid-cols-2">
                    <div className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                        <div className="flex items-center gap-2 text-sm font-semibold text-slate-900">
                            <LineChartIcon className="h-4 w-4" />
                            Revenue vs Promo
                        </div>
                        <p className="mt-2 text-sm text-slate-500">
                            Gross commission, promo cost, and net revenue over time.
                        </p>

                        {isTrendsLoading && !trends ? (
                            <div className="mt-4">
                                <ChartSkeleton />
                            </div>
                        ) : trendsError ? (
                            <div className="mt-4 rounded-[24px] border border-rose-200 bg-rose-50 px-5 py-6 text-sm text-rose-800">
                                {trendsError}
                            </div>
                        ) : trendRows.length === 0 ? (
                            <div className="mt-4 rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-5 py-10 text-center text-sm text-slate-500">
                                No financial trend rows matched the selected scope.
                            </div>
                        ) : (
                            <div className="mt-4 h-[320px]">
                                <ResponsiveContainer width="100%" height="100%">
                                    <LineChart data={trendRows} margin={{ top: 12, right: 16, left: 0, bottom: 0 }}>
                                        <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                                        <XAxis
                                            dataKey="bucketStartUtc"
                                            tickFormatter={formatDateLabel}
                                            stroke="#64748b"
                                            tickLine={false}
                                            axisLine={false}
                                        />
                                        <YAxis
                                            stroke="#64748b"
                                            tickLine={false}
                                            axisLine={false}
                                            tickFormatter={(value) => formatCompactCurrency(value, currency)}
                                        />
                                        <Tooltip content={<TrendTooltip currency={currency} />} />
                                        <Legend />
                                        <Line type="monotone" dataKey="grossCommission" name="Gross Commission" stroke="#0284c7" strokeWidth={3} dot={false} />
                                        <Line type="monotone" dataKey="promoCost" name="Promo Cost" stroke="#d97706" strokeWidth={3} dot={false} />
                                        <Line type="monotone" dataKey="netRevenue" name="Net Revenue" stroke="#059669" strokeWidth={3} dot={false} />
                                    </LineChart>
                                </ResponsiveContainer>
                            </div>
                        )}
                    </div>

                    <div className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
                        <div className="flex items-center gap-2 text-sm font-semibold text-slate-900">
                            <ShieldAlert className="h-4 w-4" />
                            Refund Trend
                        </div>
                        <p className="mt-2 text-sm text-slate-500">
                            Refund value and promo reversals across the same reporting window.
                        </p>

                        {isRefundsLoading && !refunds ? (
                            <div className="mt-4">
                                <ChartSkeleton />
                            </div>
                        ) : refundsError ? (
                            <div className="mt-4 rounded-[24px] border border-rose-200 bg-rose-50 px-5 py-6 text-sm text-rose-800">
                                {refundsError}
                            </div>
                        ) : refundRows.length === 0 ? (
                            <div className="mt-4 rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-5 py-10 text-center text-sm text-slate-500">
                                No refunds were completed in the selected scope.
                            </div>
                        ) : (
                            <div className="mt-4 h-[320px]">
                                <ResponsiveContainer width="100%" height="100%">
                                    <LineChart data={refundRows} margin={{ top: 12, right: 16, left: 0, bottom: 0 }}>
                                        <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                                        <XAxis
                                            dataKey="bucketStartUtc"
                                            tickFormatter={formatDateLabel}
                                            stroke="#64748b"
                                            tickLine={false}
                                            axisLine={false}
                                        />
                                        <YAxis
                                            stroke="#64748b"
                                            tickLine={false}
                                            axisLine={false}
                                            tickFormatter={(value) => formatCompactCurrency(value, currency)}
                                        />
                                        <Legend />
                                        <Line type="monotone" dataKey="refundAmount" name="Refund Amount" stroke="#dc2626" strokeWidth={3} dot={false} />
                                        <Line type="monotone" dataKey="promoReversalAmount" name="Promo Reversal" stroke="#7c3aed" strokeWidth={3} dot={false} />
                                    </LineChart>
                                </ResponsiveContainer>
                            </div>
                        )}
                    </div>
                </div>
            </article>
        </section>
    )
}
