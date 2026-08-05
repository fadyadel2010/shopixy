import { Loader2 } from 'lucide-react'

import { AUTHORING_BUTTON_TONES } from '../contracts/authoringContracts'

export function AuthoringButton({ action, defaultLabel, defaultIcon: DefaultIcon, tone = 'secondary' }) {
    if (!action) return null
    const Icon = action.icon || DefaultIcon
    const busy = action.busy === true

    return (
        <button
            type="button"
            onClick={action.onClick}
            disabled={action.disabled || busy}
            aria-busy={busy || undefined}
            aria-label={action.ariaLabel}
            title={action.title}
            className={`inline-flex min-h-10 items-center justify-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 ${AUTHORING_BUTTON_TONES[action.tone || tone]}`}
        >
            {busy ? <Loader2 size={16} className="animate-spin" aria-hidden="true" /> : (Icon ? <Icon size={16} aria-hidden="true" /> : null)}
            <span>{busy ? (action.busyLabel || defaultLabel) : (action.label || defaultLabel)}</span>
        </button>
    )
}
