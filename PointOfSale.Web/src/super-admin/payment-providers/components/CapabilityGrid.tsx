import { CheckCircle2, CircleDashed } from 'lucide-react'
import type { PaymentProviderCapability } from '../paymentProviderTypes'

export function CapabilityGrid({ capabilities }: { capabilities: PaymentProviderCapability[] }) {
    return (
        <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex items-center justify-between gap-3">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Capabilities</p>
                    <h3 className="mt-1 text-lg font-semibold text-slate-950">Canonical model</h3>
                </div>
                <span className="text-sm font-semibold text-slate-600">{capabilities.length}</span>
            </div>
            <div className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                {capabilities.map((capability) => {
                    const Icon = capability.supported ? CheckCircle2 : CircleDashed
                    return (
                        <div key={capability.code} className="rounded-md border border-slate-200 bg-slate-50 p-3">
                            <div className="flex items-center gap-2">
                                <Icon className={capability.supported ? 'h-4 w-4 text-emerald-600' : 'h-4 w-4 text-slate-400'} />
                                <p className="font-semibold text-slate-950">{capability.displayName}</p>
                            </div>
                            <p className="mt-2 text-xs leading-5 text-slate-600">{capability.description}</p>
                        </div>
                    )
                })}
            </div>
        </section>
    )
}
