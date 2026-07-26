import { KeyRound, RotateCw } from 'lucide-react'
import type { PaymentProviderSecretMetadata } from '../paymentProviderTypes'
import { SecretMetadataPanel } from './SecretMetadataPanel'
import { SecretVersionList } from './SecretVersionList'

export function SecretCard({
    secretType,
    current,
    versions,
    onRotate,
    canRotate = true,
}: {
    secretType: string
    current?: PaymentProviderSecretMetadata
    versions: PaymentProviderSecretMetadata[]
    onRotate?: (secretType: string) => void
    canRotate?: boolean
}) {
    return (
        <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-3">
                    <div className="flex h-10 w-10 items-center justify-center rounded-md bg-slate-100 text-slate-700">
                        <KeyRound className="h-5 w-5" />
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                            Secret Lifecycle
                        </p>
                        <h3 className="text-lg font-semibold text-slate-950">{secretType}</h3>
                    </div>
                </div>
                {canRotate && onRotate ? (
                    <button
                        type="button"
                        onClick={() => onRotate(secretType)}
                        className="inline-flex items-center gap-2 rounded-md bg-slate-950 px-4 py-2 text-sm font-semibold text-white hover:bg-slate-800"
                    >
                        <RotateCw className="h-4 w-4" />
                        Rotate
                    </button>
                ) : null}
            </div>

            <div className="mt-4 space-y-4">
                {current ? (
                    <SecretMetadataPanel secret={current} />
                ) : (
                    <div className="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-4 text-sm text-slate-600">
                        No active version exists for this secret type.
                    </div>
                )}
                <SecretVersionList versions={versions} />
            </div>
        </section>
    )
}
