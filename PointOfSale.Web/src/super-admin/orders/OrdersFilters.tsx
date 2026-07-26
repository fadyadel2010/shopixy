import type { ReactNode } from 'react'
import { RotateCcw, Search, SlidersHorizontal } from 'lucide-react'
import {
    SUPER_ADMIN_ORDER_STATUS_OPTIONS,
    SUPER_ADMIN_PAYMENT_FILTER_OPTIONS,
    type SuperAdminOrderPaymentFilter,
    type SuperAdminOrderStatus,
    type SuperAdminOrdersFilters,
    type SuperAdminTenantOption,
} from './ordersService'

interface OrdersFiltersProps {
    filters: SuperAdminOrdersFilters
    searchValue: string
    tenants: SuperAdminTenantOption[]
    isLoadingTenants: boolean
    isRefreshing: boolean
    onSearchChange: (value: string) => void
    onStatusChange: (value: SuperAdminOrderStatus | '') => void
    onPaymentStatusChange: (value: SuperAdminOrderPaymentFilter | '') => void
    onTenantChange: (value: string) => void
    onFromDateChange: (value: string) => void
    onToDateChange: (value: string) => void
    onReset: () => void
}

function FilterLabel({ children }: { children: ReactNode }) {
    return (
        <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
            {children}
        </label>
    )
}

export default function OrdersFilters({
    filters,
    searchValue,
    tenants,
    isLoadingTenants,
    isRefreshing,
    onSearchChange,
    onStatusChange,
    onPaymentStatusChange,
    onTenantChange,
    onFromDateChange,
    onToDateChange,
    onReset,
}: OrdersFiltersProps) {
    return (
        <section className="sticky top-4 z-20 overflow-hidden rounded-[32px] border border-slate-200 bg-white/95 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)] backdrop-blur">
            <div className="border-b border-slate-100 px-6 py-5">
                <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                            <SlidersHorizontal className="h-4 w-4" />
                            Orders Filters
                        </div>
                        <p className="mt-3 text-sm leading-6 text-slate-500">
                            Search, narrow, and share the exact operational view you need. Live search is debounced before it updates the URL and refetches.
                        </p>
                    </div>

                    <button
                        type="button"
                        className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        onClick={onReset}
                    >
                        <RotateCcw className="h-4 w-4" />
                        Reset filters
                    </button>
                </div>
            </div>

            <div className="grid gap-4 px-6 py-6 md:grid-cols-2 xl:grid-cols-6">
                <div className="space-y-2 xl:col-span-2">
                    <FilterLabel>Search</FilterLabel>
                    <div className="relative">
                        <Search className="pointer-events-none absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                        <input
                            value={searchValue}
                            onChange={(event) => onSearchChange(event.target.value)}
                            placeholder="Order ID, order number, or customer"
                            className="w-full rounded-2xl border border-slate-200 bg-white py-3 pl-11 pr-4 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                        />
                    </div>
                </div>

                <div className="space-y-2">
                    <FilterLabel>Status</FilterLabel>
                    <select
                        value={filters.status ?? ''}
                        onChange={(event) => onStatusChange(event.target.value as SuperAdminOrderStatus | '')}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    >
                        <option value="">All statuses</option>
                        {SUPER_ADMIN_ORDER_STATUS_OPTIONS.map((status) => (
                            <option key={status} value={status}>
                                {status}
                            </option>
                        ))}
                    </select>
                </div>

                <div className="space-y-2">
                    <FilterLabel>Payment</FilterLabel>
                    <select
                        value={filters.paymentStatus ?? ''}
                        onChange={(event) => onPaymentStatusChange(event.target.value as SuperAdminOrderPaymentFilter | '')}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    >
                        <option value="">All payments</option>
                        {SUPER_ADMIN_PAYMENT_FILTER_OPTIONS.map((status) => (
                            <option key={status} value={status}>
                                {status}
                            </option>
                        ))}
                    </select>
                </div>

                <div className="space-y-2">
                    <FilterLabel>Tenant</FilterLabel>
                    <select
                        value={filters.tenantId ?? ''}
                        disabled={isLoadingTenants}
                        onChange={(event) => onTenantChange(event.target.value)}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100 disabled:cursor-not-allowed disabled:bg-slate-50"
                    >
                        <option value="">{isLoadingTenants ? 'Loading tenants...' : 'All tenants'}</option>
                        {tenants.map((tenant) => (
                            <option key={tenant.id} value={tenant.id}>
                                {tenant.name}
                            </option>
                        ))}
                    </select>
                </div>

                <div className="space-y-2">
                    <FilterLabel>From</FilterLabel>
                    <input
                        type="date"
                        value={filters.fromDate ?? ''}
                        onChange={(event) => onFromDateChange(event.target.value)}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    />
                </div>

                <div className="space-y-2">
                    <FilterLabel>To</FilterLabel>
                    <input
                        type="date"
                        value={filters.toDate ?? ''}
                        onChange={(event) => onToDateChange(event.target.value)}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    />
                </div>
            </div>

            <div className="border-t border-slate-100 bg-slate-50/80 px-6 py-4 text-xs font-medium tracking-[0.04em] text-slate-500">
                {isRefreshing
                    ? 'Refreshing orders snapshot...'
                    : 'Filters stay in the URL, and the live stats strip refreshes automatically while this tab is active.'}
            </div>
        </section>
    )
}
