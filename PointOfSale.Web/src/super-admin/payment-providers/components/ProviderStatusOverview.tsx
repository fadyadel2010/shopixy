import { Activity, KeyRound, Layers3, Power } from 'lucide-react'
import type { PaymentProviderDashboardSummary } from '../paymentProviderTypes'

export function ProviderStatusOverview({ summary }: { summary: PaymentProviderDashboardSummary }) {
    const tiles = [
        { label: 'Providers', value: summary.providerCount, icon: Layers3 },
        { label: 'Enabled', value: summary.enabledCount, icon: Power },
        { label: 'Healthy', value: summary.healthyCount, icon: Activity },
        { label: 'Secrets', value: summary.configuredSecretCount, icon: KeyRound },
    ]

    return (
        <section className="grid gap-3 md:grid-cols-4">
            {tiles.map(({ label, value, icon: Icon }) => (
                <div key={label} className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
                    <div className="flex items-center justify-between gap-3">
                        <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
                        <Icon className="h-4 w-4 text-slate-500" />
                    </div>
                    <p className="mt-3 text-2xl font-semibold text-slate-950">{value}</p>
                </div>
            ))}
        </section>
    )
}
