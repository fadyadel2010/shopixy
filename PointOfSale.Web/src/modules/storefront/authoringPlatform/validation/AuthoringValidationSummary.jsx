import { useId } from 'react'

export function AuthoringValidationSummary({ errors = [], title = 'Review the highlighted fields' }) {
    const titleId = useId()
    if (!errors.length) return null

    return (
        <section className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900" role="alert" aria-live="assertive" aria-labelledby={titleId}>
            <p id={titleId} className="font-semibold">{title}</p>
            <ul className="mt-2 list-disc space-y-1 pl-5">
                {errors.map((error, index) => <li key={`${error}-${index}`}>{error}</li>)}
            </ul>
        </section>
    )
}
