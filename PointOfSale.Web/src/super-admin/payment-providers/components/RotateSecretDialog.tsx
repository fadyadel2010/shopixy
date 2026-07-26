import { useEffect, useState } from 'react'
import { KeyRound, X } from 'lucide-react'
import type { RotatePaymentProviderSecretRequest } from '../paymentProviderTypes'

export function RotateSecretDialog({
    open,
    secretType,
    rotating,
    onClose,
    onRotate,
}: {
    open: boolean
    secretType: string
    rotating: boolean
    onClose: () => void
    onRotate: (payload: RotatePaymentProviderSecretRequest) => void
}) {
    const [newSecret, setNewSecret] = useState('')
    const [confirmNewSecret, setConfirmNewSecret] = useState('')
    const [reason, setReason] = useState('')
    const [administratorConfirmation, setAdministratorConfirmation] = useState(false)

    useEffect(() => {
        if (!open) {
            setNewSecret('')
            setConfirmNewSecret('')
            setReason('')
            setAdministratorConfirmation(false)
        }
    }, [open])

    if (!open) {
        return null
    }

    const validationErrors = [
        !newSecret.trim() ? 'New Secret is required.' : null,
        newSecret.trim() !== confirmNewSecret.trim() ? 'Confirm New Secret must match.' : null,
        !administratorConfirmation ? 'Administrator confirmation is required.' : null,
    ].filter((error): error is string => Boolean(error))

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/40 p-4">
            <div className="w-full max-w-lg rounded-lg border border-slate-200 bg-white shadow-xl">
                <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
                    <div className="flex items-center gap-3">
                        <div className="flex h-9 w-9 items-center justify-center rounded-md bg-emerald-50 text-emerald-700">
                            <KeyRound className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                                Rotate Secret
                            </p>
                            <h3 className="text-lg font-semibold text-slate-950">{secretType}</h3>
                        </div>
                    </div>
                    <button
                        type="button"
                        onClick={onClose}
                        className="rounded-md p-2 text-slate-500 hover:bg-slate-100"
                        aria-label="Close rotate secret dialog"
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <div className="space-y-4 px-5 py-4">
                    <SecretInput label="New Secret" value={newSecret} onChange={setNewSecret} />
                    <SecretInput label="Confirm New Secret" value={confirmNewSecret} onChange={setConfirmNewSecret} />
                    <label className="space-y-2">
                        <span className="text-sm font-semibold text-slate-700">Reason</span>
                        <textarea
                            value={reason}
                            onChange={(event) => setReason(event.target.value)}
                            rows={3}
                            className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-100"
                        />
                    </label>
                    <label className="flex items-start gap-3 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700">
                        <input
                            type="checkbox"
                            checked={administratorConfirmation}
                            onChange={(event) => setAdministratorConfirmation(event.target.checked)}
                            className="mt-0.5 h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500"
                        />
                        <span>I confirm this rotation is intentional and authorized.</span>
                    </label>
                    {validationErrors.length > 0 ? (
                        <ul className="space-y-1 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                            {validationErrors.map((error) => (
                                <li key={error}>{error}</li>
                            ))}
                        </ul>
                    ) : null}
                </div>

                <div className="flex justify-end gap-3 border-t border-slate-200 px-5 py-4">
                    <button
                        type="button"
                        onClick={onClose}
                        className="rounded-md border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50"
                    >
                        Cancel
                    </button>
                    <button
                        type="button"
                        disabled={rotating || validationErrors.length > 0}
                        onClick={() =>
                            onRotate({
                                secretType,
                                newSecret,
                                confirmNewSecret,
                                reason,
                                administratorConfirmation,
                            })
                        }
                        className="rounded-md bg-emerald-600 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-slate-300"
                    >
                        {rotating ? 'Rotating...' : 'Rotate'}
                    </button>
                </div>
            </div>
        </div>
    )
}

function SecretInput({
    label,
    value,
    onChange,
}: {
    label: string
    value: string
    onChange: (value: string) => void
}) {
    return (
        <label className="space-y-2">
            <span className="text-sm font-semibold text-slate-700">{label}</span>
            <input
                type="password"
                value={value}
                autoComplete="new-password"
                onChange={(event) => onChange(event.target.value)}
                className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-emerald-500 focus:outline-none focus:ring-2 focus:ring-emerald-100"
            />
        </label>
    )
}
