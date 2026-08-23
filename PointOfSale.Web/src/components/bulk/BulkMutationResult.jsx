import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { buildBulkMutationPresentation } from './bulkMutationPresentation'

// Phase 6G.6: one shared, strictly passive result panel for all five bulk
// product actions (Activate/Deactivate/Publish/Unpublish/Delete). It renders
// the already-normalized aggregate, per-item unchanged/failure reasons,
// request-level and transport-uncertain outcomes plus a separate refresh
// warning. It never dispatches, retries, refetches or owns selection; the
// page and useBulkProductMutation keep lifecycle ownership.
//
// Accessibility contract: exactly one polite atomic live region carries the
// concise summary (processing line, heading + counts / request message, and
// the refresh warning). The per-item list is rendered outside the live region
// so screen readers announce a short summary instead of an unbounded item
// list on every terminal swap; it stays fully visible and readable.
export default function BulkMutationResult({
    action,
    isMutating = false,
    processingCount = 0,
    result = null,
    error = null,
    refreshError = null,
    resolveProductName,
    onDismiss,
}) {
    const { t } = useTranslation()
    const headingId = useId()

    const model = buildBulkMutationPresentation({
        action,
        isMutating,
        processingCount,
        result,
        error,
        refreshError,
        resolveProductName,
        t,
    })

    if (!model) {
        return null
    }

    if (model.kind === 'processing') {
        return (
            <div data-testid="bulk-mutation-result" className="mb-4">
                <div className="p-3 bg-blue-50 border border-blue-200 text-blue-800 rounded text-sm">
                    <div
                        role="status"
                        aria-live="polite"
                        aria-atomic="true"
                        aria-label={t('products.bulkResultRegionAria')}
                    >
                        {model.processingText}
                    </div>
                </div>
            </div>
        )
    }

    return (
        <div data-testid="bulk-mutation-result" className="mb-4">
            <div
                role="region"
                aria-labelledby={headingId}
                className={`p-3 border rounded text-sm ${model.toneClass}`}
            >
                <div
                    role="status"
                    aria-live="polite"
                    aria-atomic="true"
                    aria-label={t('products.bulkResultRegionAria')}
                >
                    <h3 id={headingId} className="break-words font-medium">{model.heading}</h3>
                    {model.counts ? (
                        <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
                            {model.countLines.map((line) => (
                                <span key={line.key}>{line.text}</span>
                            ))}
                        </div>
                    ) : (
                        <div className="mt-1">{model.requestMessage}</div>
                    )}
                    {model.refreshWarning && (
                        <div className="mt-2 break-words rounded border border-amber-200 bg-amber-50 px-2 py-1.5 font-medium text-amber-800">
                            {model.refreshWarning}
                        </div>
                    )}
                </div>
                {model.items.length > 0 && (
                    <ul className="mt-2 space-y-1 list-disc ps-5">
                        {model.items.map((item) => (
                            <li key={item.key} className="break-words">
                                {item.namePrefix ? (
                                    <>
                                        <span className="font-medium">
                                            {t('products.bulkItemProductName', { name: item.namePrefix })}{' '}
                                        </span>
                                        <span>{item.text}</span>
                                    </>
                                ) : (
                                    <span>{item.text}</span>
                                )}
                                <span
                                    className={`ms-2 inline-flex rounded-full px-2 py-0.5 text-xs font-semibold ${item.statusClass}`}
                                >
                                    {item.statusLabel}
                                </span>
                            </li>
                        ))}
                    </ul>
                )}
                <button
                    type="button"
                    onClick={onDismiss}
                    aria-label={t('products.bulkResultDismiss')}
                    className="mt-2 text-sm font-medium underline focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                >
                    {t('products.bulkResultDismiss')}
                </button>
            </div>
        </div>
    )
}
