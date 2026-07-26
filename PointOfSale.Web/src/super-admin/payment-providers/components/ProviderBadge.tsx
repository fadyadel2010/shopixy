export function ProviderBadge({ enabled, status }: { enabled: boolean; status: string }) {
    const tone = enabled
        ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
        : 'border-slate-200 bg-slate-50 text-slate-600'

    return (
        <span className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold ${tone}`}>
            {enabled ? 'Enabled' : status || 'Not configured'}
        </span>
    )
}
