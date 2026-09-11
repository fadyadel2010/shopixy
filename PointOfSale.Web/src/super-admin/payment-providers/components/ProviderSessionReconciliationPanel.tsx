import { useState } from 'react'
import { AlertTriangle, ShieldAlert } from 'lucide-react'
import type { PaymentProviderSessionReconciliationRequest } from '../paymentProviderTypes'

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

export function ProviderSessionReconciliationPanel({
    submitting,
    disabled,
    onSubmit,
}: {
    submitting: boolean
    disabled: boolean
    onSubmit: (payload: PaymentProviderSessionReconciliationRequest) => void
}) {
    const [paymentId, setPaymentId] = useState('')
    const [providerReference, setProviderReference] = useState('')
    const [evidenceNote, setEvidenceNote] = useState('')
    const [confirmed, setConfirmed] = useState(false)

    const normalizedPaymentId = paymentId.trim()
    const normalizedProviderReference = providerReference.trim()
    const normalizedEvidenceNote = evidenceNote.trim()
    const valid =
        guidPattern.test(normalizedPaymentId) &&
        normalizedProviderReference.length > 0 &&
        normalizedEvidenceNote.length >= 10 &&
        normalizedEvidenceNote.length <= 500 &&
        confirmed

    function submit() {
        if (!valid || submitting || disabled) {
            return
        }

        onSubmit({
            paymentId: normalizedPaymentId,
            providerReference: normalizedProviderReference,
            evidenceCode: 'PAYMOB_DASHBOARD_NO_ORDER',
            evidenceNote: normalizedEvidenceNote,
            administratorConfirmation: true,
        })
    }

    return (
        <section className="rounded-2xl border border-amber-300 bg-amber-50 p-5 shadow-sm">
            <div className="flex items-start gap-3">
                <span className="rounded-xl bg-amber-100 p-2 text-amber-800">
                    <ShieldAlert className="h-5 w-5" />
                </span>
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.2em] text-amber-800">
                        Manual provider reconciliation
                    </p>
                    <h3 className="mt-2 text-lg font-semibold text-slate-950">Confirm no Paymob session was created</h3>
                    <p className="mt-2 text-sm leading-6 text-slate-700">
                        Use this only for a pending outcome-unknown payment after checking the matching Paymob environment,
                        merchant, Orders page, and Transactions page. This action records operator evidence and closes only
                        an evidence-free session gap; it cannot override a provider transaction or paid payment.
                    </p>
                </div>
            </div>

            <div className="mt-5 grid gap-4">
                <label className="grid gap-2 text-sm font-medium text-slate-800">
                    Payment ID
                    <input
                        value={paymentId}
                        onChange={(event) => setPaymentId(event.target.value)}
                        placeholder="00000000-0000-0000-0000-000000000000"
                        autoComplete="off"
                        className="rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-sm text-slate-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200"
                    />
                </label>
                <label className="grid gap-2 text-sm font-medium text-slate-800">
                    Exact provider reference
                    <input
                        value={providerReference}
                        onChange={(event) => setProviderReference(event.target.value)}
                        autoComplete="off"
                        className="rounded-lg border border-slate-300 bg-white px-3 py-2 font-mono text-sm text-slate-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200"
                    />
                </label>
                <label className="grid gap-2 text-sm font-medium text-slate-800">
                    Dashboard evidence note
                    <textarea
                        value={evidenceNote}
                        onChange={(event) => setEvidenceNote(event.target.value)}
                        rows={3}
                        maxLength={500}
                        placeholder="Record merchant, environment, pages checked, and the observed result."
                        className="resize-y rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-950 outline-none focus:border-amber-500 focus:ring-2 focus:ring-amber-200"
                    />
                    <span className="text-xs font-normal text-slate-500">{normalizedEvidenceNote.length}/500 characters</span>
                </label>
                <label className="flex items-start gap-3 rounded-lg border border-amber-200 bg-white p-3 text-sm text-slate-800">
                    <input
                        type="checkbox"
                        checked={confirmed}
                        onChange={(event) => setConfirmed(event.target.checked)}
                        className="mt-1 h-4 w-4 rounded border-slate-300 text-amber-700 focus:ring-amber-500"
                    />
                    <span>
                        I checked the correct Paymob Test-mode merchant and found no matching order or transaction for
                        this exact provider reference.
                    </span>
                </label>
            </div>

            {disabled ? (
                <div className="mt-4 flex items-center gap-2 text-sm font-medium text-amber-900">
                    <AlertTriangle className="h-4 w-4" />
                    Save or discard provider configuration changes before reconciling a session.
                </div>
            ) : null}

            <div className="mt-5 flex justify-end">
                <button
                    type="button"
                    onClick={submit}
                    disabled={!valid || submitting || disabled}
                    className="rounded-lg bg-amber-700 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-amber-800 disabled:cursor-not-allowed disabled:bg-slate-300"
                >
                    {submitting ? 'Reconciling…' : 'Reconcile as not created'}
                </button>
            </div>
        </section>
    )
}
