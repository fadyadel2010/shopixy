import {
    ArrowLeft,
    Eye,
    RotateCcw,
    Save,
    Upload,
} from 'lucide-react'

import { AuthoringHistoryLauncher } from '../history/AuthoringHistoryLauncher'
import { AuthoringButton } from './AuthoringButton'

/** Constitutional order: Back/title/status, Preview, History, Discard, Save, final commit. */
export function AuthoringActionBar({
    title,
    subtitle,
    backAction,
    status,
    previewAction,
    historyAction,
    discardAction,
    saveAction,
    publishAction,
    dangerActions = [],
    leading,
}) {
    return (
        <header className="rounded-xl border border-gray-200 bg-white px-4 py-3 shadow-sm" aria-label={`${title || 'Storefront'} authoring actions`}>
            <div className="flex flex-wrap items-center gap-3">
                {backAction ? <AuthoringButton action={backAction} defaultLabel="Back" defaultIcon={ArrowLeft} /> : null}
                <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                        <h1 className="truncate text-xl font-semibold text-gray-900">{title}</h1>
                        {status}
                    </div>
                    {subtitle ? <p className="mt-0.5 text-sm text-gray-500">{subtitle}</p> : null}
                </div>
                {leading}
                <div className="flex flex-wrap items-center gap-2" role="toolbar" aria-label="Authoring actions">
                    <AuthoringButton action={previewAction} defaultLabel="Preview" defaultIcon={Eye} />
                    <AuthoringHistoryLauncher action={historyAction} />
                    <AuthoringButton action={discardAction} defaultLabel="Discard" defaultIcon={RotateCcw} />
                    <AuthoringButton action={saveAction} defaultLabel="Save Draft" defaultIcon={Save} tone="primary" />
                    <AuthoringButton action={publishAction} defaultLabel="Publish" defaultIcon={Upload} tone="publish" />
                    {dangerActions.map((action) => (
                        <AuthoringButton key={action.label} action={{ ...action, tone: 'danger' }} defaultLabel={action.label} />
                    ))}
                </div>
            </div>
        </header>
    )
}
