import { Loader2 } from 'lucide-react'
import type { PaymentProviderSummary } from '../paymentProviderTypes'
import { PaymentProviderCard } from './PaymentProviderCard'

export function PaymentProviderList({
    providers,
    loading,
    selectedProviderCode,
    onSelect,
}: {
    providers: PaymentProviderSummary[]
    loading: boolean
    selectedProviderCode: string | null
    onSelect: (providerCode: string) => void
}) {
    if (loading) {
        return (
            <div className="flex items-center gap-3 rounded-2xl border border-slate-200 bg-white px-4 py-5 text-sm text-slate-600">
                <Loader2 className="h-4 w-4 animate-spin" />
                Loading providers
            </div>
        )
    }

    if (providers.length === 0) {
        return (
            <div className="rounded-2xl border border-dashed border-slate-300 bg-slate-50 px-4 py-6 text-sm text-slate-600">
                No payment providers are registered.
            </div>
        )
    }

    return (
        <div className="space-y-3">
            {providers.map((provider) => (
                <PaymentProviderCard
                    key={provider.providerCode}
                    provider={provider}
                    selected={provider.providerCode === selectedProviderCode}
                    onSelect={onSelect}
                />
            ))}
        </div>
    )
}
