import { CheckCircle2, Clock3, Archive, XCircle } from 'lucide-react'

export function SecretStatusBadge({ status }: { status: string }) {
    const normalized = status.toLowerCase()
    const style = normalized === 'active'
        ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
        : normalized === 'pendingactivation'
            ? 'border-amber-200 bg-amber-50 text-amber-800'
            : normalized === 'retired'
                ? 'border-slate-200 bg-slate-50 text-slate-700'
                : 'border-zinc-200 bg-zinc-50 text-zinc-700'
    const Icon = normalized === 'active'
        ? CheckCircle2
        : normalized === 'pendingactivation'
            ? Clock3
            : normalized === 'retired'
                ? XCircle
                : Archive

    return (
        <span className={`inline-flex items-center gap-1 rounded-md border px-2 py-1 text-xs font-semibold ${style}`}>
            <Icon className="h-3.5 w-3.5" />
            {status}
        </span>
    )
}
