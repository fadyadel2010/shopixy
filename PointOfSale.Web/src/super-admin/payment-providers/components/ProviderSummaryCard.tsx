import { ChevronDown, ChevronUp, ShieldCheck } from 'lucide-react'
import type { PaymentProviderDashboardProvider } from '../paymentProviderTypes'
import { HealthBadge } from './HealthBadge'
import { ProviderBadge } from './ProviderBadge'

function formatDate(value?: string | null) {
    if (!value) {
        return 'Never'
    }

    const parsed = new Date(value)
    return Number.isNaN(parsed.getTime()) ? 'Unavailable' : parsed.toLocaleString()
}

export function ProviderSummaryCard({
    provider,
    expanded,
    onToggle,
}: {
    provider: PaymentProviderDashboardProvider
    expanded: boolean
    onToggle: () => void
}) {
    return (
        <article className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                    <div className="flex flex-wrap items-center gap-2">
                        <h3 className="text-lg font-semibold text-slate-950">{provider.displayName}</h3>
                        <ProviderBadge enabled={provider.enabled} status={provider.status} />
                        <HealthBadge status={provider.health.status} />
                    </div>
                    <p className="mt-2 max-w-3xl text-sm leading-6 text-slate-600">{provider.description}</p>
                </div>
                <button
                    type="button"
                    onClick={onToggle}
                    className="inline-flex h-9 items-center gap-2 rounded-md border border-slate-300 px-3 text-sm font-semibold text-slate-700 hover:bg-slate-50"
                >
                    {expanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                    Details
                </button>
            </div>

            <dl className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
                <Metric label="Provider" value={provider.providerCode} />
                <Metric label="Environment" value={provider.environment} />
                <Metric label="Enabled" value={provider.enabled ? 'Yes' : 'No'} />
                <Metric label="Last Check" value={formatDate(provider.lastCheckUtc)} />
                <Metric label="Secrets" value={`${provider.secrets.length}`} />
                <Metric label="Version" value={provider.version || 'Unavailable'} />
            </dl>

            {expanded ? (
                <div className="mt-5 space-y-4 border-t border-slate-200 pt-5">
                    <section className="bg-slate-50 p-4">
                        <div className="flex items-center gap-2">
                            <ShieldCheck className="h-4 w-4 text-slate-600" />
                            <h4 className="font-semibold text-slate-950">Administrative Permissions</h4>
                        </div>
                        <div className="mt-3 flex flex-wrap gap-2">
                            {provider.permissions.map((permission) => (
                                <span key={permission} className="rounded-md border border-slate-200 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700">
                                    {permission}
                                </span>
                            ))}
                        </div>
                    </section>
                    <section className="bg-slate-50 p-4">
                        <h4 className="font-semibold text-slate-950">Provider Metadata</h4>
                        <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
                            {Object.entries(provider.metadata).slice(0, 8).map(([key, value]) => (
                                <Metric key={key} label={key} value={String(value ?? 'Unavailable')} />
                            ))}
                        </div>
                    </section>
                </div>
            ) : null}
        </article>
    )
}

function Metric({ label, value }: { label: string; value: string }) {
    return (
        <div className="rounded-md border border-slate-200 bg-slate-50 px-3 py-2">
            <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</dt>
            <dd className="mt-1 truncate font-semibold text-slate-950">{value}</dd>
        </div>
    )
}
