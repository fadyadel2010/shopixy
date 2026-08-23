// Phase 6G.6: shared presentation model for bulk product mutation outcomes.
// Pure mapping only: normalized aggregates and machine codes become safe
// localized strings. Raw backend messages, stack traces, transport internals
// and idempotency keys are never read here. Lifecycle ownership (dispatch,
// retry, refresh, reset) stays with useBulkProductMutation and the page.

const toCountOrZero = (value) => (
    typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : 0
)

const TONE_CLASSES = Object.freeze({
    success: 'bg-green-50 border-green-200 text-green-800',
    info: 'bg-blue-50 border-blue-200 text-blue-800',
    warning: 'bg-amber-50 border-amber-200 text-amber-800',
    error: 'bg-red-100 border-red-400 text-red-700',
})

export const BULK_MUTATION_TONES = Object.freeze({
    SUCCESS: 'success',
    INFO: 'info',
    WARNING: 'warning',
    ERROR: 'error',
})

// Localized presentation per bulk action. Machine codes map only to safe
// localized strings; raw backend messages are never rendered.
const BULK_ACTION_PRESENTATION = Object.freeze({
    activate: Object.freeze({
        actionLabelKey: 'products.bulkActionNameActivate',
        processingKey: 'products.bulkProcessingActivate',
        successTitleKey: 'products.bulkSuccessTitleActivate',
        processedCountKey: 'products.bulkCountProcessedActivate',
        unchangedCountKey: 'products.bulkCountUnchangedActivate',
        refreshErrorKey: 'products.bulkRefreshErrorNotice',
        unchangedCodes: Object.freeze({
            ALREADY_ACTIVE: 'products.bulkItemAlreadyActive',
        }),
    }),
    deactivate: Object.freeze({
        actionLabelKey: 'products.bulkActionNameDeactivate',
        processingKey: 'products.bulkProcessingDeactivate',
        successTitleKey: 'products.bulkSuccessTitleDeactivate',
        processedCountKey: 'products.bulkCountProcessedDeactivate',
        unchangedCountKey: 'products.bulkCountUnchangedDeactivate',
        refreshErrorKey: 'products.bulkRefreshErrorNotice',
        unchangedCodes: Object.freeze({
            ALREADY_INACTIVE: 'products.bulkItemAlreadyInactive',
        }),
    }),
    publish: Object.freeze({
        actionLabelKey: 'products.bulkActionNamePublish',
        processingKey: 'products.bulkProcessingPublish',
        successTitleKey: 'products.bulkSuccessTitlePublish',
        processedCountKey: 'products.bulkCountProcessedPublish',
        unchangedCountKey: 'products.bulkCountUnchangedPublish',
        refreshErrorKey: 'products.bulkRefreshErrorNoticePublish',
        unchangedCodes: Object.freeze({
            ALREADY_PUBLISHED: 'products.bulkItemAlreadyPublished',
        }),
    }),
    unpublish: Object.freeze({
        actionLabelKey: 'products.bulkActionNameUnpublish',
        processingKey: 'products.bulkProcessingUnpublish',
        successTitleKey: 'products.bulkSuccessTitleUnpublish',
        processedCountKey: 'products.bulkCountProcessedUnpublish',
        unchangedCountKey: 'products.bulkCountUnchangedUnpublish',
        refreshErrorKey: 'products.bulkRefreshErrorNoticeUnpublish',
        unchangedCodes: Object.freeze({
            ALREADY_UNPUBLISHED: 'products.bulkItemAlreadyUnpublished',
        }),
    }),
    delete: Object.freeze({
        actionLabelKey: 'products.bulkActionNameDelete',
        processingKey: 'products.bulkProcessingDelete',
        successTitleKey: 'products.bulkSuccessTitleDelete',
        processedCountKey: 'products.bulkCountProcessedDelete',
        unchangedCountKey: 'products.bulkCountUnchangedDelete',
        refreshErrorKey: 'products.bulkRefreshErrorNoticeDelete',
        unchangedCodes: Object.freeze({}),
    }),
})

export const resolveBulkActionPresentation = (bulkAction) => (
    BULK_ACTION_PRESENTATION[bulkAction] || BULK_ACTION_PRESENTATION.activate
)

const BULK_SHARED_FAILURE_CODES = Object.freeze({
    CONCURRENCY_CONFLICT: 'products.bulkItemConcurrencyConflict',
    NOT_FOUND: 'products.bulkItemNotFound',
    REQUEST_CANCELLED: 'products.bulkItemRequestCancelled',
    VALIDATION_FAILED: 'products.bulkItemValidationFailed',
})

// Delete-specific safe localized reasons. They take precedence over the
// shared map so Delete can use its own conservative wording (e.g. a
// concurrency conflict must tell the user to refresh before retrying, never
// to resubmit with a substituted version).
const BULK_DELETE_FAILURE_CODES = Object.freeze({
    DEPENDENCY_BLOCKED: 'products.bulkItemDependencyBlocked',
    CONCURRENCY_CONFLICT: 'products.bulkItemDeleteConcurrencyConflict',
    NOT_FOUND: 'products.bulkItemNotFound',
    REQUEST_CANCELLED: 'products.bulkItemRequestCancelled',
    IDEMPOTENCY_CONFLICT: 'products.bulkItemIdempotencyConflict',
    IDEMPOTENCY_IN_PROGRESS: 'products.bulkItemIdempotencyInProgress',
})

const BULK_DELETE_REQUEST_ERROR_CODES = Object.freeze({
    IDEMPOTENCY_CONFLICT: 'products.bulkDeleteIdempotencyConflictMessage',
    IDEMPOTENCY_IN_PROGRESS: 'products.bulkDeleteIdempotencyInProgressMessage',
})

// Local/pre-request failure codes thrown by the API layer before any HTTP
// request is dispatched. Zero requests were sent, so they are definite start
// failures for every action and must never be presented as uncertain.
const BULK_PRE_REQUEST_ERROR_CODES = new Set([
    'INVALID_BULK_ITEMS',
    'INVALID_IDEMPOTENCY_KEY',
    'IDEMPOTENCY_KEY_GENERATION_UNAVAILABLE',
])

// Request-level idempotency refusals are definitive about this request only:
// it was rejected before execution. Shared conservative wording covers every
// action without exposing the key; Delete keeps its more conservative variant.
const BULK_REQUEST_IDEMPOTENCY_ERROR_CODES = Object.freeze({
    IDEMPOTENCY_CONFLICT: 'products.bulkRequestIdempotencyConflictMessage',
    IDEMPOTENCY_IN_PROGRESS: 'products.bulkRequestIdempotencyInProgressMessage',
})

const BULK_PUBLISH_FAILURE_CODES = Object.freeze({
    PRODUCT_INACTIVE: 'products.bulkItemInactiveCannotPublish',
    PUBLICATION_NOT_READY: 'products.bulkItemPublicationNotReady',
})

// Centralized action-aware machine-code mapping with a safe unknown fallback.
// Action-inapplicable codes (e.g. PRODUCT_INACTIVE on Activate) intentionally
// miss the specialized maps and land on the generic fallback so no misleading
// action-specific text can be produced.
export const resolveBulkItemReasonKey = ({ action, code }) => {
    if (action === 'publish' && BULK_PUBLISH_FAILURE_CODES[code]) {
        return BULK_PUBLISH_FAILURE_CODES[code]
    }

    if (action === 'delete' && BULK_DELETE_FAILURE_CODES[code]) {
        return BULK_DELETE_FAILURE_CODES[code]
    }

    return BULK_SHARED_FAILURE_CODES[code] ?? null
}

const buildResultModel = ({ action, result, refreshError, resolveProductName, t }) => {
    const presentation = resolveBulkActionPresentation(action)
    const requested = toCountOrZero(result.requested)
    const accepted = toCountOrZero(result.accepted)
    const processed = toCountOrZero(result.processed)
    const unchanged = toCountOrZero(result.unchanged)
    const failed = toCountOrZero(result.failed)
    const results = Array.isArray(result.results) ? result.results : []

    let heading
    if (failed > 0 && processed === 0 && unchanged === 0) {
        heading = t('products.bulkFailedTitle')
    } else if (failed > 0) {
        heading = t('products.bulkPartialTitle', {
            action: t(presentation.actionLabelKey),
        })
    } else if (processed === 0 && unchanged > 0) {
        // All-unchanged is a shared neutral "no products were changed"
        // summary, never an action-specific success-like headline. The
        // action-aware counts and per-item reasons below still explain it.
        heading = t('products.bulkNoChangesTitle')
    } else {
        heading = t(presentation.successTitleKey)
    }

    const items = []
    results.forEach((item, index) => {
        if (!item || typeof item !== 'object') {
            return
        }
        const resolvedName = typeof resolveProductName === 'function'
            ? resolveProductName(item.productId)
            : ''
        const name = typeof resolvedName === 'string' && resolvedName.trim().length > 0
            ? resolvedName
            : t('products.bulkUnknownProduct')
        const detailKey = `${item.productId ?? 'item'}-${index}`
        if (item.status === 'failed') {
            // Publish-specific business failures carry their reason without an
            // inline product name; the name is rendered as a separate prefix.
            const isPublishBusinessFailure = action === 'publish'
                && Boolean(BULK_PUBLISH_FAILURE_CODES[item.code])
            const reasonKey = resolveBulkItemReasonKey({ action, code: item.code })
            items.push({
                key: `failed-${detailKey}`,
                namePrefix: isPublishBusinessFailure ? name : null,
                text: reasonKey
                    ? t(reasonKey, { name })
                    : t('products.bulkItemUnknownReason', { name }),
                statusLabel: t('products.bulkItemStatusFailed'),
                statusClass: 'bg-red-100 text-red-800',
            })
        } else if (item.status === 'unchanged') {
            // Unchanged rows are neutral, never failures. For Delete they may
            // still explain themselves through its conservative code map.
            const unchangedReasonKey = (action === 'delete' && BULK_DELETE_FAILURE_CODES[item.code])
                ? BULK_DELETE_FAILURE_CODES[item.code]
                : presentation.unchangedCodes[item.code] ?? null
            items.push({
                key: `unchanged-${detailKey}`,
                namePrefix: null,
                text: unchangedReasonKey
                    ? t(unchangedReasonKey, { name })
                    : t('products.bulkItemUnknownUnchangedReason', { name }),
                statusLabel: t('products.bulkItemStatusUnchanged'),
                statusClass: 'bg-gray-100 text-gray-700',
            })
        }
    })

    const tone = failed > 0
        ? (processed > 0 || unchanged > 0 ? 'warning' : 'error')
        : (processed === 0 && unchanged > 0 ? 'info' : 'success')

    return {
        kind: 'result',
        tone,
        toneClass: TONE_CLASSES[tone],
        heading,
        counts: { requested, accepted, processed, unchanged, failed },
        countLines: [
            { key: 'requested', text: t('products.bulkCountRequested', { count: requested }) },
            { key: 'accepted', text: t('products.bulkCountAccepted', { count: accepted }) },
            { key: 'processed', text: t(presentation.processedCountKey, { count: processed }) },
            { key: 'unchanged', text: t(presentation.unchangedCountKey, { count: unchanged }) },
            { key: 'failed', text: t('products.bulkCountFailed', { count: failed }) },
        ],
        items,
        requestMessage: null,
        refreshWarning: refreshError ? t(presentation.refreshErrorKey) : null,
    }
}

const buildRequestErrorModel = ({ action, error, refreshError, t }) => {
    const errorCode = typeof error?.code === 'string' ? error.code : null
    const status = typeof error?.status === 'number' ? error.status : null

    let tone = 'error'
    let heading = t('products.bulkRequestFailedTitle')
    let messageKey
    let isUncertain = false

    if (errorCode !== null && errorCode.startsWith('BULK_')) {
        // Local pre-request failures dispatched zero API requests, so a
        // definite "could not be started" is accurate for every action.
        messageKey = 'products.bulkStartFailed'
    } else if (BULK_PRE_REQUEST_ERROR_CODES.has(errorCode)) {
        // API-layer pre-dispatch refusals are equally definite for every
        // action: no request left the client, so uncertainty never applies.
        messageKey = 'products.bulkStartFailed'
    } else if (action === 'delete' && BULK_DELETE_REQUEST_ERROR_CODES[errorCode]) {
        tone = 'warning'
        heading = t('products.bulkRequestAttentionTitle')
        messageKey = BULK_DELETE_REQUEST_ERROR_CODES[errorCode]
    } else if (BULK_REQUEST_IDEMPOTENCY_ERROR_CODES[errorCode]) {
        tone = 'warning'
        heading = t('products.bulkRequestAttentionTitle')
        messageKey = BULK_REQUEST_IDEMPOTENCY_ERROR_CODES[errorCode]
    } else if (status === null || status === 408 || status >= 500) {
        // Transport-ambiguous outcome for every action: the request may still
        // have been committed after dispatch, so neither definite success nor
        // a definite failure may be claimed. The user must refresh before any
        // retry; nothing resubmits automatically.
        tone = 'warning'
        isUncertain = true
        heading = t(action === 'delete'
            ? 'products.bulkDeleteUncertainTitle'
            : 'products.bulkRequestUncertainTitle')
        messageKey = action === 'delete'
            ? 'products.bulkDeleteUncertainMessage'
            : 'products.bulkRequestUncertainMessage'
    } else if (status === 400) {
        messageKey = 'products.bulkRequestValidationError'
    } else if (status === 403) {
        messageKey = 'products.bulkRequestPermissionError'
    } else if (status === 409) {
        messageKey = 'products.bulkRequestConflictError'
    } else {
        messageKey = 'products.bulkRequestServiceError'
    }

    return {
        kind: 'requestError',
        tone,
        toneClass: TONE_CLASSES[tone],
        heading,
        counts: null,
        countLines: [],
        items: [],
        requestMessage: t(messageKey),
        refreshWarning: refreshError
            ? t(isUncertain
                ? 'products.bulkRequestRefreshErrorNotice'
                : 'products.bulkRequestRefreshWarning')
            : null,
    }
}

// Builds the complete shared presentation model consumed by
// BulkMutationResult. Returns null when there is nothing to present.
export function buildBulkMutationPresentation({
    action,
    isMutating = false,
    processingCount = 0,
    result = null,
    error = null,
    refreshError = null,
    resolveProductName,
    t,
}) {
    if (isMutating) {
        const presentation = resolveBulkActionPresentation(action)
        return {
            kind: 'processing',
            processingText: t(presentation.processingKey, { count: toCountOrZero(processingCount) }),
        }
    }

    if (!result && !error) {
        return null
    }

    if (result) {
        return buildResultModel({ action, result, refreshError, resolveProductName, t })
    }

    return buildRequestErrorModel({ action, error, refreshError, t })
}
