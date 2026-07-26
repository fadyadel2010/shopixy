import type { PaymentProviderSecretMetadata } from '../paymentProviderTypes'
import { SecretStatusBadge } from './SecretStatusBadge'

function formatDate(value?: string | null) {
    return value
        ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
        : 'Future'
}

export function SecretVersionList({ versions }: { versions: PaymentProviderSecretMetadata[] }) {
    if (versions.length === 0) {
        return (
            <div className="rounded-lg border border-dashed border-slate-300 bg-white p-4 text-sm text-slate-600">
                No secret versions have been created yet.
            </div>
        )
    }

    return (
        <div className="overflow-hidden rounded-lg border border-slate-200">
            <table className="min-w-full divide-y divide-slate-200 text-sm">
                <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                    <tr>
                        <th className="px-4 py-3">Version</th>
                        <th className="px-4 py-3">Status</th>
                        <th className="px-4 py-3">Created</th>
                        <th className="px-4 py-3">Activated</th>
                        <th className="px-4 py-3">Retired</th>
                        <th className="px-4 py-3">Fingerprint</th>
                    </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 bg-white">
                    {versions.map((version) => (
                        <tr key={`${version.secretId}-${version.version}`}>
                            <td className="px-4 py-3 font-semibold text-slate-950">v{version.version}</td>
                            <td className="px-4 py-3"><SecretStatusBadge status={version.status} /></td>
                            <td className="px-4 py-3 text-slate-700">{formatDate(version.createdAtUtc)}</td>
                            <td className="px-4 py-3 text-slate-700">{formatDate(version.activatedAtUtc)}</td>
                            <td className="px-4 py-3 text-slate-700">{formatDate(version.retiredAtUtc)}</td>
                            <td className="px-4 py-3 font-mono text-xs text-slate-700">{version.fingerprint}</td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    )
}
