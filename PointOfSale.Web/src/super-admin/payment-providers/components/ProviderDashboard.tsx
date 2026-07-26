import { useMemo, useState } from 'react'
import type {
    PaymentProviderDashboard as PaymentProviderDashboardModel,
    PaymentProviderSecretMetadata,
    ProviderDashboardFiltersState,
} from '../paymentProviderTypes'
import { defaultProviderDashboardFilters, filterDashboardProviders } from '../providerDashboardFilters'
import { AdministrationTimeline } from './AdministrationTimeline'
import { CapabilityGrid } from './CapabilityGrid'
import { DashboardFilters } from './DashboardFilters'
import { ProviderHealthCard } from './ProviderHealthCard'
import { ProviderStatusOverview } from './ProviderStatusOverview'
import { ProviderSummaryCard } from './ProviderSummaryCard'
import { SecretCard } from './SecretCard'

export function ProviderDashboard({
    dashboard,
    loading,
    refreshing,
    onRefresh,
}: {
    dashboard?: PaymentProviderDashboardModel
    loading: boolean
    refreshing: boolean
    onRefresh: () => void
}) {
    const [filters, setFilters] = useState<ProviderDashboardFiltersState>(defaultProviderDashboardFilters)
    const [selectedProviderCode, setSelectedProviderCode] = useState<string | null>(null)
    const [expandedProviderCodes, setExpandedProviderCodes] = useState<Set<string>>(new Set())
    const providers = dashboard?.providers ?? []
    const filteredProviders = useMemo(
        () => filterDashboardProviders(providers, filters),
        [providers, filters],
    )
    const selectedProvider = providers.find((provider) => provider.providerCode === selectedProviderCode) ?? filteredProviders[0]
    const secretGroups = selectedProvider?.secrets.reduce<Record<string, PaymentProviderSecretMetadata[]>>((groups, secret) => {
        groups[secret.secretType] = [...(groups[secret.secretType] ?? []), secret]
        return groups
    }, {}) ?? {}

    function toggleProvider(providerCode: string) {
        setSelectedProviderCode(providerCode)
        setExpandedProviderCodes((current) => {
            const next = new Set(current)
            if (next.has(providerCode)) {
                next.delete(providerCode)
            } else {
                next.add(providerCode)
            }
            return next
        })
    }

    if (loading && !dashboard) {
        return (
            <section className="rounded-lg border border-slate-200 bg-white p-8 text-sm font-semibold text-slate-600 shadow-sm">
                Loading provider dashboard.
            </section>
        )
    }

    if (!dashboard) {
        return (
            <section className="rounded-lg border border-slate-200 bg-white p-8 text-sm font-semibold text-slate-600 shadow-sm">
                Dashboard unavailable.
            </section>
        )
    }

    return (
        <section className="space-y-5">
            <ProviderStatusOverview summary={dashboard.summary} />
            <DashboardFilters
                providers={providers}
                capabilities={dashboard.capabilities}
                filters={filters}
                refreshing={refreshing}
                onChange={setFilters}
                onRefresh={onRefresh}
            />
            <div className="space-y-3">
                {filteredProviders.length === 0 ? (
                    <div className="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-5 text-sm text-slate-600">
                        No providers match the current filters.
                    </div>
                ) : filteredProviders.map((provider) => (
                    <ProviderSummaryCard
                        key={provider.providerCode}
                        provider={provider}
                        expanded={expandedProviderCodes.has(provider.providerCode)}
                        onToggle={() => toggleProvider(provider.providerCode)}
                    />
                ))}
            </div>
            {selectedProvider ? (
                <div className="grid gap-5 xl:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
                    <ProviderHealthCard health={selectedProvider.health} />
                    <section className="space-y-3">
                        {Object.entries(secretGroups).length === 0 ? (
                            <div className="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-5 text-sm text-slate-600">
                                No secret metadata available.
                            </div>
                        ) : Object.entries(secretGroups).map(([secretType, versions]) => (
                            <SecretCard
                                key={secretType}
                                secretType={secretType}
                                current={versions.find((secret) => secret.status === 'Active')}
                                versions={versions}
                                canRotate={false}
                            />
                        ))}
                    </section>
                </div>
            ) : null}
            <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_420px]">
                <CapabilityGrid capabilities={dashboard.capabilities} />
                <AdministrationTimeline activity={dashboard.recentActivity} />
            </div>
        </section>
    )
}
