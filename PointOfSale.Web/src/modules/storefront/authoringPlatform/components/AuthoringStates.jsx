import { useId } from 'react'
import { Loader2 } from 'lucide-react'

import { AuthoringButton } from './AuthoringButton'
import { AuthoringFeedback } from '../feedback/AuthoringFeedback'

export function AuthoringLoadingState({ label = 'Loading authoring data...' }) {
    return (
        <div className="flex min-h-48 items-center justify-center gap-3 rounded-xl border border-gray-200 bg-white text-gray-600" role="status" aria-live="polite">
            <Loader2 className="animate-spin" size={20} aria-hidden="true" />
            <span>{label}</span>
        </div>
    )
}

export function AuthoringEmptyState({ title, description, action }) {
    const titleId = useId()
    return (
        <section className="rounded-xl border border-dashed border-gray-300 bg-white px-6 py-12 text-center" aria-labelledby={titleId}>
            <h2 id={titleId} className="text-lg font-semibold text-gray-900">{title}</h2>
            {description ? <p className="mx-auto mt-2 max-w-xl text-sm text-gray-600">{description}</p> : null}
            {action ? <div className="mt-5"><AuthoringButton action={action} defaultLabel={action.label} tone="primary" /></div> : null}
        </section>
    )
}

export function AuthoringPermissionState({ message = 'You do not have permission to change this Storefront surface.' }) {
    return <AuthoringFeedback kind="warning" title="View only" message={message} />
}
