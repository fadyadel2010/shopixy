import { RefreshCw, Search } from 'lucide-react'
import type {
    PaymentProviderCapability,
    PaymentProviderDashboardProvider,
    ProviderDashboardFiltersState,
} from '../paymentProviderTypes'

export function DashboardFilters({
    providers,
    capabilities,
    filters,
    refreshing,
    onChange,
    onRefresh,
}: {
    providers: PaymentProviderDashboardProvider[]
    capabilities: PaymentProviderCapability[]
    filters: ProviderDashboardFiltersState
    refreshing: boolean
    onChange: (filters: ProviderDashboardFiltersState) => void
    onRefresh: () => void
}) {
    const environments = unique(providers.map((provider) => provider.environment))
    const statuses = unique(providers.map((provider) => provider.status))
    const healthStatuses = unique(providers.map((provider) => provider.health.status))

    function update(next: Partial<ProviderDashboardFiltersState>) {
        onChange({ ...filters, ...next })
    }

    return (
        <section className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
            <div className="grid gap-3 lg:grid-cols-[1.4fr_repeat(6,minmax(0,1fr))_auto]">
                <label className="relative">
                    <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                    <input
                        value={filters.search}
                        onChange={(event) => update({ search: event.target.value })}
                        className="h-10 w-full rounded-md border border-slate-300 pl-9 pr-3 text-sm outline-none focus:border-slate-500"
                        placeholder="Search"
                    />
                </label>
                <select value={filters.provider} onChange={(event) => update({ provider: event.target.value })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="">Provider</option>
                    {providers.map((provider) => <option key={provider.providerCode} value={provider.providerCode}>{provider.displayName}</option>)}
                </select>
                <select value={filters.environment} onChange={(event) => update({ environment: event.target.value })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="">Environment</option>
                    {environments.map((environment) => <option key={environment} value={environment}>{environment}</option>)}
                </select>
                <select value={filters.status} onChange={(event) => update({ status: event.target.value })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="">Status</option>
                    {statuses.map((status) => <option key={status} value={status}>{status}</option>)}
                </select>
                <select value={filters.health} onChange={(event) => update({ health: event.target.value })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="">Health</option>
                    {healthStatuses.map((status) => <option key={status} value={status}>{status}</option>)}
                </select>
                <select value={filters.enabled} onChange={(event) => update({ enabled: event.target.value as ProviderDashboardFiltersState['enabled'] })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="all">Enabled</option>
                    <option value="enabled">Enabled only</option>
                    <option value="disabled">Disabled only</option>
                </select>
                <select value={filters.capability} onChange={(event) => update({ capability: event.target.value })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="">Capability</option>
                    {capabilities.map((capability) => <option key={capability.code} value={capability.code}>{capability.displayName}</option>)}
                </select>
                <select value={filters.sort} onChange={(event) => update({ sort: event.target.value as ProviderDashboardFiltersState['sort'] })} className="h-10 rounded-md border border-slate-300 px-3 text-sm">
                    <option value="provider">Sort</option>
                    <option value="status">Status</option>
                    <option value="environment">Environment</option>
                    <option value="health">Health</option>
                    <option value="lastCheck">Last check</option>
                </select>
                <button
                    type="button"
                    onClick={onRefresh}
                    className="inline-flex h-10 items-center justify-center gap-2 rounded-md bg-slate-950 px-4 text-sm font-semibold text-white hover:bg-slate-800"
                >
                    <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} />
                    Refresh
                </button>
            </div>
        </section>
    )
}

function unique(values: string[]) {
    return Array.from(new Set(values.filter(Boolean))).sort((left, right) => left.localeCompare(right))
}
