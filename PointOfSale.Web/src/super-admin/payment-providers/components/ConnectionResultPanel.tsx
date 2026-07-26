import { Timer } from 'lucide-react'
import type { PaymentProviderActionResult } from '../paymentProviderTypes'
import { HealthBadge } from './HealthBadge'

function messageList(result: PaymentProviderActionResult) {
    return result.errors.length > 0 ? result.errors : result.warnings
}

export function ConnectionResultPanel({ result }: { result: PaymentProviderActionResult | null }) {
    if (!result) {
        return null
    }

    const messages = messageList(result)

    return (
        <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Latest result</p>
                    <h3 className="mt-2 text-lg font-semibold text-slate-950">{result.message}</h3>
                </div>
                <HealthBadge status={result.status} />
            </div>
            <div className="mt-4 flex flex-wrap gap-3 text-sm text-slate-700">
                <span className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-1">
                    <Timer className="h-4 w-4" />
                    {result.latencyMs} ms
                </span>
                <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1">{result.environment}</span>
                {result.providerVersion ? (
                    <span className="rounded-full border border-slate-200 bg-slate-50 px-3 py-1">{result.providerVersion}</span>
                ) : null}
                {result.errorCategory ? (
                    <span className="rounded-full border border-rose-200 bg-rose-50 px-3 py-1 text-rose-700">{result.errorCategory}</span>
                ) : null}
            </div>
            {messages.length > 0 ? (
                <ul className="mt-4 list-disc space-y-1 pl-5 text-sm text-slate-700">
                    {messages.map((message) => (
                        <li key={message}>{message}</li>
                    ))}
                </ul>
            ) : null}
        </section>
    )
}
