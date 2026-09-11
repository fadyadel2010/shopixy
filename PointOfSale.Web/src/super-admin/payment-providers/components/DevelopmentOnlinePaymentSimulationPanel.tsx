import { CheckCircle2, Loader2, ShieldAlert } from 'lucide-react'
import { useState } from 'react'
import type {
    DevelopmentOnlinePaymentSuccessRequest,
    DevelopmentOnlinePaymentSuccessResponse,
} from '../paymentProviderTypes'

interface Props {
    submitting: boolean
    result?: DevelopmentOnlinePaymentSuccessResponse | null
    onSubmit: (paymentId: string, request: DevelopmentOnlinePaymentSuccessRequest) => void
}

export function DevelopmentOnlinePaymentSimulationPanel({ submitting, result, onSubmit }: Props) {
    const [paymentId, setPaymentId] = useState('')
    const [idempotencyKey, setIdempotencyKey] = useState('')
    const [reason, setReason] = useState('Gate 3R Development online success-path certification')
    const [confirmed, setConfirmed] = useState(false)

    const canSubmit =
        paymentId.trim().length > 0 &&
        idempotencyKey.trim().length >= 8 &&
        reason.trim().length > 0 &&
        confirmed &&
        !submitting

    return (
        <section className="space-y-4 rounded-2xl border border-violet-200 bg-violet-50 p-5 shadow-sm">
            <div className="flex items-start gap-3">
                <div className="rounded-xl bg-violet-100 p-2 text-violet-700">
                    <ShieldAlert className="h-5 w-5" />
                </div>
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-violet-700">
                        Development only
                    </p>
                    <h3 className="mt-1 text-lg font-semibold text-violet-950">Online success-path simulation</h3>
                    <p className="mt-2 text-sm leading-6 text-violet-900/80">
                        Enters the canonical platform payment-success lifecycle for an existing pending payment. This
                        records Development evidence only; it does not claim a Paymob charge, card movement, callback,
                        or external money movement.
                    </p>
                </div>
            </div>

            <div className="grid gap-3 md:grid-cols-2">
                <input
                    value={paymentId}
                    onChange={(event) => setPaymentId(event.target.value)}
                    placeholder="CommerceHub payment ID"
                    className="rounded-xl border border-violet-200 bg-white px-4 py-3 text-sm outline-none focus:border-violet-400 focus:ring-4 focus:ring-violet-100"
                />
                <input
                    value={idempotencyKey}
                    onChange={(event) => setIdempotencyKey(event.target.value)}
                    placeholder="Stable idempotency key (8–128 chars)"
                    className="rounded-xl border border-violet-200 bg-white px-4 py-3 text-sm outline-none focus:border-violet-400 focus:ring-4 focus:ring-violet-100"
                />
            </div>
            <textarea
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                rows={3}
                placeholder="Certification reason"
                className="w-full rounded-xl border border-violet-200 bg-white px-4 py-3 text-sm outline-none focus:border-violet-400 focus:ring-4 focus:ring-violet-100"
            />
            <label className="flex items-start gap-3 rounded-xl border border-violet-200 bg-white px-4 py-3 text-sm text-violet-950">
                <input
                    type="checkbox"
                    checked={confirmed}
                    onChange={(event) => setConfirmed(event.target.checked)}
                    className="mt-1 h-4 w-4 rounded border-violet-300"
                />
                <span>
                    I confirm this is a Development simulation with no external Paymob charge or money movement.
                </span>
            </label>
            <button
                type="button"
                disabled={!canSubmit}
                onClick={() => onSubmit(paymentId.trim(), {
                    idempotencyKey: idempotencyKey.trim(),
                    reason: reason.trim(),
                    administratorConfirmation: true,
                })}
                className="inline-flex items-center gap-2 rounded-xl bg-violet-700 px-5 py-3 text-sm font-semibold text-white transition hover:bg-violet-800 disabled:cursor-not-allowed disabled:opacity-50"
            >
                {submitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}
                {submitting ? 'Executing canonical lifecycle…' : 'Simulate accepted online payment'}
            </button>

            {result ? (
                <div className="rounded-xl border border-violet-200 bg-white px-4 py-3 text-sm text-violet-950">
                    <p className="font-semibold">{result.message}</p>
                    <p className="mt-1 break-all text-xs text-violet-700">{result.transactionId}</p>
                    <p className="mt-2 text-xs font-semibold uppercase tracking-wide text-violet-700">
                        Development simulation · No external money movement
                    </p>
                </div>
            ) : null}
        </section>
    )
}
