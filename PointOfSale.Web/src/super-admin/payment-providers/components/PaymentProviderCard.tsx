import { CreditCard } from 'lucide-react'
import type { PaymentProviderSummary } from '../paymentProviderTypes'
import { ProviderBadge } from './ProviderBadge'

export function PaymentProviderCard({
    provider,
    selected,
    onSelect,
}: {
    provider: PaymentProviderSummary
    selected: boolean
    onSelect: (providerCode: string) => void
}) {
    return (
        <button
            type="button"
            onClick={() => onSelect(provider.providerCode)}
            className={[
                'w-full rounded-2xl border px-4 py-4 text-left transition',
                selected
                    ? 'border-emerald-300 bg-emerald-50 shadow-sm'
                    : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
            ].join(' ')}
        >
            <div className="flex items-start gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-slate-100 text-slate-700">
                    <CreditCard className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                        <p className="font-semibold text-slate-950">{provider.displayName}</p>
                        <ProviderBadge enabled={provider.enabled} status={provider.status} />
                    </div>
                    <p className="mt-1 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                        {provider.providerCode}
                    </p>
                    <p className="mt-2 text-sm leading-6 text-slate-600">{provider.description}</p>
                </div>
            </div>
        </button>
    )
}
