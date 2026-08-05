import { AuthoringButton } from '../components/AuthoringButton'

export function AuthoringFeedback({ kind = 'info', title, message, action }) {
    if (!title && !message) return null
    const isError = kind === 'error'
    const styles = isError
        ? 'border-red-200 bg-red-50 text-red-900'
        : kind === 'success'
            ? 'border-emerald-200 bg-emerald-50 text-emerald-900'
            : kind === 'warning'
                ? 'border-amber-200 bg-amber-50 text-amber-900'
                : 'border-sky-200 bg-sky-50 text-sky-900'

    return (
        <div className={`rounded-lg border px-4 py-3 text-sm ${styles}`} role={isError ? 'alert' : 'status'} aria-live={isError ? 'assertive' : 'polite'}>
            <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                    {title ? <p className="font-semibold">{title}</p> : null}
                    {message ? <p className={title ? 'mt-1' : ''}>{message}</p> : null}
                </div>
                {action ? <AuthoringButton action={action} defaultLabel={action.label} /> : null}
            </div>
        </div>
    )
}

export function getAuthoringErrorFeedback(error) {
    const status = error?.status ?? error?.response?.status
    const code = error?.response?.data?.code ?? error?.code ?? 'UNEXPECTED_FAILURE'
    const backendMessage = error?.response?.data?.message || error?.message
    const message = backendMessage && !/^(AxiosError:\s*)?Request failed with status code/i.test(backendMessage)
        ? backendMessage
        : null

    if (status === 409 || code === 'FOOTER_CONCURRENCY_CONFLICT' || /CONCURRENCY/.test(code)) {
        return { kind: 'conflict', code, title: 'Another author saved first', message: message || 'Review the latest authoring revision, then retry your draft.' }
    }
    if (/ALREADY_PUBLISHED/.test(code)) return { kind: 'already-published', code, title: 'Already published', message: message || 'This content is already published.' }
    if (/ALREADY_EXISTS/.test(code)) return { kind: 'already-exists', code, title: 'Already exists', message: message || 'A matching resource already exists.' }
    if (/DUPLICATE/.test(code)) return { kind: 'duplicate', code, title: 'Duplicate content', message: message || 'Remove the duplicate value and try again.' }
    if (status === 404 || /NOT_FOUND|MISSING/.test(code)) return { kind: 'missing', code, title: 'Resource unavailable', message: message || 'A referenced resource no longer exists.' }
    if (status === 401 || status === 403 || /UNAUTHORIZED|FORBIDDEN/.test(code)) return { kind: 'forbidden', code, title: 'Permission required', message: message || 'You do not have permission to perform this action.' }
    if (status === 428 || /PRECONDITION/.test(code)) return { kind: 'precondition', code, title: 'Latest version required', message: message || 'Load the latest accepted version, then retry your draft.' }
    if (status === 400 || status === 422 || /VALID|REQUIRED|INVALID/.test(code)) return { kind: 'validation', code, title: 'Review the authoring fields', message: message || 'Correct the highlighted authoring fields and try again.' }
    return { kind: 'unexpected', code, title: 'Authoring request failed', message: message || 'An unexpected failure occurred. Your draft is still available; retry when ready.' }
}
