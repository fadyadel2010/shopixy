import { AlertTriangle, CheckCircle2, CircleHelp, XCircle } from 'lucide-react'

const statusStyles: Record<string, string> = {
    healthy: 'border-emerald-200 bg-emerald-50 text-emerald-800',
    warning: 'border-amber-200 bg-amber-50 text-amber-800',
    error: 'border-rose-200 bg-rose-50 text-rose-800',
    unknown: 'border-slate-200 bg-slate-50 text-slate-700',
}

export function HealthBadge({ status }: { status: string }) {
    const normalized = status.toLowerCase()
    const Icon =
        normalized === 'healthy'
            ? CheckCircle2
            : normalized === 'warning'
              ? AlertTriangle
              : normalized === 'error'
                ? XCircle
                : CircleHelp

    return (
        <span className={`inline-flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-semibold ${statusStyles[normalized] ?? statusStyles.unknown}`}>
            <Icon className="h-3.5 w-3.5" />
            {status || 'Unknown'}
        </span>
    )
}
