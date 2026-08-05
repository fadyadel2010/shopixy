import { AlertCircle, CheckCircle2, Clock3 } from 'lucide-react'

import { AUTHORING_STATUS_LABELS } from '../contracts/authoringContracts'

const statusStyles = {
    dirty: 'bg-amber-100 text-amber-800',
    draft: 'bg-slate-100 text-slate-700',
    published: 'bg-emerald-100 text-emerald-800',
    live: 'bg-emerald-100 text-emerald-800',
    saving: 'bg-sky-100 text-sky-800',
    publishing: 'bg-violet-100 text-violet-800',
    error: 'bg-red-100 text-red-800',
    disabled: 'bg-gray-100 text-gray-600',
}

export function AuthoringStatusBadge({ state = 'draft', children }) {
    const Icon = state === 'dirty' || state === 'error'
        ? AlertCircle
        : state === 'published' || state === 'live'
            ? CheckCircle2
            : Clock3

    return (
        <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold ${statusStyles[state] || statusStyles.draft}`}>
            <Icon size={13} aria-hidden="true" />
            {children || AUTHORING_STATUS_LABELS[state] || AUTHORING_STATUS_LABELS.draft}
        </span>
    )
}
