import type { PaymentProviderSecretMetadata } from '../paymentProviderTypes'
import { SecretStatusBadge } from './SecretStatusBadge'

function formatDate(value?: string | null) {
    if (!value) {
        return 'Future'
    }

    return new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
    }).format(new Date(value))
}

export function SecretMetadataPanel({ secret }: { secret: PaymentProviderSecretMetadata }) {
    return (
        <div className="grid gap-3 rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm md:grid-cols-2">
            <MetadataItem label="Version" value={`v${secret.version}`} />
            <div>
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Status</p>
                <div className="mt-1">
                    <SecretStatusBadge status={secret.status} />
                </div>
            </div>
            <MetadataItem label="Created" value={formatDate(secret.createdAtUtc)} />
            <MetadataItem label="Last Rotated" value={formatDate(secret.lastRotatedAtUtc)} />
            <MetadataItem label="Last Used" value={formatDate(secret.lastUsedAtUtc)} />
            <MetadataItem label="Created By" value={secret.createdBy || 'Unknown'} />
            <MetadataItem label="Fingerprint" value={secret.fingerprint} />
        </div>
    )
}

function MetadataItem({ label, value }: { label: string; value: string }) {
    return (
        <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{label}</p>
            <p className="mt-1 break-words font-medium text-slate-900">{value}</p>
        </div>
    )
}
