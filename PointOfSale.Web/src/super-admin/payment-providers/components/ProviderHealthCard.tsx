import { Activity, Clock3, Gauge } from 'lucide-react'
import type { PaymentProviderHealth } from '../paymentProviderTypes'
import { HealthBadge } from './HealthBadge'

function formatDate(value?: string | null) {
    if (!value) {
        return 'Never'
    }

    const parsed = new Date(value)
    return Number.isNaN(parsed.getTime()) ? 'Unavailable' : parsed.toLocaleString()
}

function formatDuration(value?: number | null) {
    return typeof value === 'number' ? `${value} ms` : 'Unavailable'
}

export function ProviderHealthCard({ health }: { health?: PaymentProviderHealth | null }) {
    const currentHealth = health ?? {
        status: 'Unknown',
        environment: '',
        connectionQuality: 'Unknown',
        operationalMessages: [],
    }

    return (
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Health</p>
                    <h3 className="mt-2 text-lg font-semibold text-slate-950">Provider health</h3>
                </div>
                <HealthBadge status={currentHealth.status} />
            </div>

            <dl className="mt-5 grid gap-3 md:grid-cols-3">
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                        <Activity className="h-3.5 w-3.5" />
                        Environment
                    </dt>
                    <dd className="mt-2 font-semibold text-slate-950">{currentHealth.environment || 'Unknown'}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                        <Clock3 className="h-3.5 w-3.5" />
                        Last success
                    </dt>
                    <dd className="mt-2 font-semibold text-slate-950">{formatDate(currentHealth.lastSuccessfulCheckUtc)}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                        <Clock3 className="h-3.5 w-3.5" />
                        Last failure
                    </dt>
                    <dd className="mt-2 font-semibold text-slate-950">{formatDate(currentHealth.lastFailedCheckUtc)}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Duration</dt>
                    <dd className="mt-2 font-semibold text-slate-950">{formatDuration(currentHealth.lastCheckDurationMs)}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Provider version</dt>
                    <dd className="mt-2 font-semibold text-slate-950">{currentHealth.providerVersion || 'Unavailable'}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                        <Gauge className="h-3.5 w-3.5" />
                        Quality
                    </dt>
                    <dd className="mt-2 font-semibold text-slate-950">{currentHealth.connectionQuality || 'Unknown'}</dd>
                </div>
            </dl>
        </section>
    )
}
