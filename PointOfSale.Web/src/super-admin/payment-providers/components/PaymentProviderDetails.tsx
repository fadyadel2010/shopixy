import { CheckCircle2, CircleOff } from 'lucide-react'
import type { PaymentProviderConfiguration } from '../paymentProviderTypes'
import { ProviderBadge } from './ProviderBadge'

export function PaymentProviderDetails({
    provider,
}: {
    provider: PaymentProviderConfiguration | null
}) {
    if (!provider) {
        return (
            <div className="rounded-2xl border border-slate-200 bg-white px-5 py-8 text-sm text-slate-600">
                Select a provider to view its configuration foundation.
            </div>
        )
    }

    return (
        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-emerald-700">
                        Provider Configuration
                    </p>
                    <h2 className="mt-2 text-2xl font-semibold text-slate-950">{provider.displayName}</h2>
                    <p className="mt-2 text-sm leading-6 text-slate-600">{provider.description}</p>
                </div>
                <ProviderBadge enabled={provider.enabled} status={provider.status} />
            </div>

            <dl className="mt-6 grid gap-3 sm:grid-cols-3">
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Code</dt>
                    <dd className="mt-2 font-semibold text-slate-950">{provider.providerCode}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">Environment</dt>
                    <dd className="mt-2 font-semibold text-slate-950">{provider.environment}</dd>
                </div>
                <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                    <dt className="text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">State</dt>
                    <dd className="mt-2 font-semibold text-slate-950">{provider.enabled ? 'Enabled' : 'Disabled'}</dd>
                </div>
            </dl>

            <div className="mt-6">
                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">
                    Supported Capabilities
                </p>
                <div className="mt-3 grid gap-3 md:grid-cols-3">
                    {provider.capabilities.map((capability) => {
                        const Icon = capability.supported ? CheckCircle2 : CircleOff
                        return (
                            <div key={capability.code} className="rounded-xl border border-slate-200 bg-white px-4 py-4">
                                <div className="flex items-center gap-2">
                                    <Icon className={capability.supported ? 'h-4 w-4 text-emerald-600' : 'h-4 w-4 text-slate-400'} />
                                    <p className="font-semibold text-slate-950">{capability.displayName}</p>
                                </div>
                                <p className="mt-2 text-sm leading-6 text-slate-600">{capability.description}</p>
                            </div>
                        )
                    })}
                </div>
            </div>
        </div>
    )
}
