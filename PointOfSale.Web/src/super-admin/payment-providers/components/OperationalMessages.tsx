import { AlertCircle, CheckCircle2 } from 'lucide-react'
import type { PaymentProviderHealth } from '../paymentProviderTypes'

export function OperationalMessages({ health }: { health?: PaymentProviderHealth | null }) {
    const messages = health?.operationalMessages ?? []

    return (
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex items-center gap-2">
                <AlertCircle className="h-4 w-4 text-slate-600" />
                <h3 className="text-lg font-semibold text-slate-950">Operational messages</h3>
            </div>
            {messages.length > 0 ? (
                <ul className="mt-4 space-y-2 text-sm text-slate-700">
                    {messages.map((message) => (
                        <li key={message} className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3">
                            {message}
                        </li>
                    ))}
                </ul>
            ) : (
                <div className="mt-4 flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-medium text-emerald-800">
                    <CheckCircle2 className="h-4 w-4" />
                    No operational messages.
                </div>
            )}
        </section>
    )
}
