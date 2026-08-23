import { useCallback, useEffect, useReducer, useRef } from 'react'
import {
    bulkActivate,
    bulkDeactivate,
    bulkDelete,
    bulkPublish,
    bulkUnpublish,
} from '../services/bulkProductApi'

// ---- Phase 6G.2: Products bulk mutation orchestration ----
// The hook owns the client-side lifecycle of exactly one bulk product
// operation at a time on behalf of the Products page. It performs no transport
// of its own: every action is delegated to the matching Phase 6G.1 service
// function, which stays the sole Idempotency-Key generator. What this layer
// adds is synchronous selection/rowVersion snapshotting, a duplicate-start
// lock, aggregate outcome classification, and awaitable canonical-refresh
// coordination.

export const BULK_MUTATION_STATUSES = Object.freeze([
    'idle',
    'preparing',
    'submitting',
    'success',
    'partial_success',
    'failed',
])

export const BULK_MUTATION_ACTIONS = Object.freeze([
    'activate',
    'deactivate',
    'publish',
    'unpublish',
    'delete',
])

// Local (pre-request) failure codes. They describe why zero API requests were
// made and must stay machine-readable; they never replace server error data.
export const BULK_MUTATION_LOCAL_ERROR_CODES = Object.freeze({
    INVALID_ACTION: 'BULK_INVALID_ACTION',
    EMPTY_SELECTION: 'BULK_EMPTY_SELECTION',
    MISSING_ROW_VERSION: 'BULK_MISSING_ROW_VERSION',
    SELECTED_ROW_STALE: 'BULK_SELECTED_ROW_STALE',
    REQUEST_ID_UNAVAILABLE: 'BULK_REQUEST_ID_UNAVAILABLE',
    REFRESH_FAILED: 'PRODUCTS_REFRESH_FAILED',
})

const ACTION_SERVICE_MAP = Object.freeze({
    activate: bulkActivate,
    deactivate: bulkDeactivate,
    publish: bulkPublish,
    unpublish: bulkUnpublish,
    delete: bulkDelete,
})

export class BulkMutationValidationError extends Error {
    constructor(code, message, { details = null } = {}) {
        super(message)
        this.name = 'BulkMutationValidationError'
        this.code = code
        this.details = details
    }
}

const createInitialBulkMutationState = () => ({
    status: 'idle',
    action: null,
    requestId: null,
    selectedItems: [],
    result: null,
    error: null,
    refreshError: null,
})

// Single focused state machine. Guards refuse impossible combinations (e.g.
// SETTLE outside 'submitting'), and terminal states retain action/requestId/
// selectedItems/result-or-error until an explicit RESET.
export const bulkMutationReducer = (state, action) => {
    switch (action.type) {
        case 'PREPARE':
            if (state.status === 'preparing' || state.status === 'submitting') return state
            return {
                status: 'preparing',
                action: action.action,
                requestId: action.requestId,
                selectedItems: action.selectedItems,
                result: null,
                error: null,
                refreshError: null,
            }
        case 'BEGIN_SUBMIT':
            if (state.status !== 'preparing') return state
            return { ...state, status: 'submitting' }
        case 'SETTLE':
            if (state.status !== 'submitting') return state
            return {
                status: action.outcome,
                action: state.action,
                requestId: state.requestId,
                selectedItems: state.selectedItems,
                result: action.result,
                error: null,
                refreshError: null,
            }
        case 'MUTATION_FAILED':
            if (state.status !== 'submitting') return state
            return {
                status: 'failed',
                action: state.action,
                requestId: state.requestId,
                selectedItems: state.selectedItems,
                result: null,
                error: action.error,
                refreshError: null,
            }
        case 'LOCAL_FAILURE':
            return {
                status: 'failed',
                action: action.action ?? null,
                requestId: null,
                selectedItems: [],
                result: null,
                error: action.error,
                refreshError: null,
            }
        case 'REFRESH_FAILED':
            return { ...state, refreshError: action.error }
        case 'RESET':
            return createInitialBulkMutationState()
        default:
            return state
    }
}

const toCountOrZero = (value) => (
    typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : 0
)

// Aggregate classification uses the server counts as a whole, never a single
// per-item entry:
//   - failures alongside processed/unchanged items -> partial_success
//   - every requested item failed (nothing processed/unchanged) -> failed
//   - otherwise -> success
const classifyAggregateOutcome = (result) => {
    const failedCount = toCountOrZero(result?.failed)
    const processedCount = toCountOrZero(result?.processed)
    const unchangedCount = toCountOrZero(result?.unchanged)

    if (failedCount > 0 && processedCount === 0 && unchangedCount === 0) {
        return 'failed'
    }
    if (failedCount > 0) {
        return 'partial_success'
    }
    return 'success'
}

// Frontend lifecycle identifier only. It is distinct from the backend
// Idempotency-Key (owned by the Phase 6G.1 service) and is never persisted or
// logged anywhere by this hook.
const createLifecycleRequestId = () => {
    const cryptoRef = typeof globalThis !== 'undefined' ? globalThis.crypto : null

    if (cryptoRef && typeof cryptoRef.randomUUID === 'function') {
        return cryptoRef.randomUUID()
    }

    return null
}

const freezeSnapshot = (items) => (
    Object.freeze(items.map((item) => Object.freeze({
        productId: item.productId,
        expectedVersion: item.expectedVersion,
    })))
)

export function useBulkProductMutation({ products = [], selectedProductIds = [], refreshProducts } = {}) {
    const [state, dispatch] = useReducer(bulkMutationReducer, undefined, createInitialBulkMutationState)

    // Synchronous duplicate-start lock: two starts in the same tick must yield
    // exactly one API call, so React state or disabled UI alone cannot guard
    // this — it has to be a ref checked and set synchronously inside start.
    const inFlightRef = useRef(false)
    const mountedRef = useRef(true)
    const lifecycleGenerationRef = useRef(0)
    const refreshProductsRef = useRef(refreshProducts)
    refreshProductsRef.current = refreshProducts

    useEffect(() => {
        mountedRef.current = true
        return () => {
            mountedRef.current = false
            lifecycleGenerationRef.current += 1
            // Release safely even if settlement callbacks never run.
            inFlightRef.current = false
        }
    }, [])

    // Canonical Products refresh wrapper. fetchProducts clears page-scoped
    // selection at request start, so invoking it satisfies both "refetch" and
    // "clear selection" through the established mechanism; rows are never
    // patched manually. A failed refresh is reported separately via
    // refreshError and never reclassifies or retries the mutation. After
    // unmount it neither refetches nor touches state.
    const requestCanonicalRefresh = useCallback(async (lifecycleGeneration) => {
        if (!mountedRef.current || lifecycleGeneration !== lifecycleGenerationRef.current) {
            return
        }

        let outcome
        try {
            outcome = await refreshProductsRef.current?.()
        } catch (err) {
            if (mountedRef.current && lifecycleGeneration === lifecycleGenerationRef.current) {
                dispatch({ type: 'REFRESH_FAILED', error: err })
            }
            return
        }

        if (!mountedRef.current || lifecycleGeneration !== lifecycleGenerationRef.current) {
            return
        }

        if (outcome === false) {
            dispatch({
                type: 'REFRESH_FAILED',
                error: new BulkMutationValidationError(
                    BULK_MUTATION_LOCAL_ERROR_CODES.REFRESH_FAILED,
                    'The products list could not be refreshed after the bulk operation.',
                ),
            })
        }
    }, [])

    const startBulkMutation = useCallback((requestedAction) => {
        // 1. Reject a second start while another operation is
        // preparing/submitting (synchronous ref lock).
        if (inFlightRef.current) {
            return false
        }
        inFlightRef.current = true
        const lifecycleGeneration = ++lifecycleGenerationRef.current

        // 2. Validate the action against the exact internal function map.
        const serviceFn = Object.prototype.hasOwnProperty.call(ACTION_SERVICE_MAP, requestedAction)
            ? ACTION_SERVICE_MAP[requestedAction]
            : null

        if (!serviceFn) {
            dispatch({
                type: 'LOCAL_FAILURE',
                action: null,
                error: new BulkMutationValidationError(
                    BULK_MUTATION_LOCAL_ERROR_CODES.INVALID_ACTION,
                    `Unsupported bulk product action: ${String(requestedAction)}.`,
                ),
            })
            inFlightRef.current = false
            return false
        }

        // 3.-4. Resolve every selected ID against the actual loaded rows and
        // capture each row's current rowVersion immediately. Live selection or
        // products are never read again after start.
        const rowByProductId = new Map(products.map((row) => [row?.id, row]))
        const items = []
        let validationCode = null
        let validationProductId = null

        for (const productId of selectedProductIds) {
            const row = rowByProductId.get(productId)

            if (!row) {
                validationCode = BULK_MUTATION_LOCAL_ERROR_CODES.SELECTED_ROW_STALE
                validationProductId = productId
                break
            }

            const rawVersion = row.rowVersion
            const versionText = typeof rawVersion === 'string'
                ? rawVersion
                : (rawVersion === null || rawVersion === undefined ? '' : String(rawVersion))

            if (versionText.trim().length === 0) {
                validationCode = BULK_MUTATION_LOCAL_ERROR_CODES.MISSING_ROW_VERSION
                validationProductId = productId
                break
            }

            items.push({ productId: row.id, expectedVersion: versionText })
        }

        // 5. Local validation failures make zero API requests and keep their
        // machine-readable local codes.
        if (selectedProductIds.length === 0) {
            dispatch({
                type: 'LOCAL_FAILURE',
                action: requestedAction,
                error: new BulkMutationValidationError(
                    BULK_MUTATION_LOCAL_ERROR_CODES.EMPTY_SELECTION,
                    'No products are selected for the bulk operation.',
                ),
            })
            inFlightRef.current = false
            return false
        }

        if (validationCode === BULK_MUTATION_LOCAL_ERROR_CODES.SELECTED_ROW_STALE) {
            dispatch({
                type: 'LOCAL_FAILURE',
                action: requestedAction,
                error: new BulkMutationValidationError(
                    validationCode,
                    'A selected product is no longer present among the loaded rows.',
                    { details: { productId: validationProductId } },
                ),
            })
            inFlightRef.current = false
            // Stale selection recovers through the established Products
            // semantics: the canonical refetch clears selection at request
            // start. No row remapping, no fabricated versions.
            void requestCanonicalRefresh(lifecycleGeneration)
            return false
        }

        if (validationCode === BULK_MUTATION_LOCAL_ERROR_CODES.MISSING_ROW_VERSION) {
            dispatch({
                type: 'LOCAL_FAILURE',
                action: requestedAction,
                error: new BulkMutationValidationError(
                    validationCode,
                    'A selected product row is missing its rowVersion.',
                    { details: { productId: validationProductId } },
                ),
            })
            inFlightRef.current = false
            // Selection is preserved here: nothing suggests the rows moved.
            return false
        }

        // 6. Frontend lifecycle request id — never sent as the service
        // Idempotency-Key, never persisted, never logged.
        const requestId = createLifecycleRequestId()

        if (!requestId) {
            dispatch({
                type: 'LOCAL_FAILURE',
                action: requestedAction,
                error: new BulkMutationValidationError(
                    BULK_MUTATION_LOCAL_ERROR_CODES.REQUEST_ID_UNAVAILABLE,
                    'A secure request id could not be generated for the bulk operation.',
                ),
            })
            inFlightRef.current = false
            return false
        }

        const snapshotItems = freezeSnapshot(items)

        dispatch({ type: 'PREPARE', action: requestedAction, requestId, selectedItems: snapshotItems })
        dispatch({ type: 'BEGIN_SUBMIT' })

        // Exactly one service call with only the immutable snapshot (no
        // explicit Idempotency-Key), issued synchronously at start. There is no
        // retry path here, unmount does not abort the request or imply
        // rollback, and late settlements simply stop touching React state
        // after unmount.
        let requestPromise
        try {
            requestPromise = serviceFn(snapshotItems)
        } catch (err) {
            requestPromise = Promise.reject(err)
        }

        requestPromise
            .then((result) => {
                if (!mountedRef.current) {
                    return
                }
                dispatch({ type: 'SETTLE', outcome: classifyAggregateOutcome(result), result })
                void requestCanonicalRefresh(lifecycleGeneration)
            })
            .catch((error) => {
                if (!mountedRef.current) {
                    return
                }
                dispatch({ type: 'MUTATION_FAILED', error })
                // Transport failure is commit-ambiguous: the backend may have
                // committed one or more items before the response was lost or
                // timed out. Refresh canonical rows and clear the stale
                // selection, but never resubmit the mutation automatically.
                void requestCanonicalRefresh(lifecycleGeneration)
            })
            .finally(() => {
                inFlightRef.current = false
            })

        return true
    }, [products, selectedProductIds, requestCanonicalRefresh])

    // Reset is refused while an operation is preparing/submitting so a late
    // settlement cannot resurrect state after an explicit reset. Terminal
    // states reset into a coherent idle snapshot.
    const resetBulkMutation = useCallback(() => {
        if (inFlightRef.current) {
            return false
        }
        inFlightRef.current = false
        lifecycleGenerationRef.current += 1
        dispatch({ type: 'RESET' })
        return true
    }, [])

    const isBulkMutating = state.status === 'preparing' || state.status === 'submitting'

    return {
        status: state.status,
        action: state.action,
        requestId: state.requestId,
        selectedItems: state.selectedItems,
        result: state.result,
        error: state.error,
        refreshError: state.refreshError,
        isBulkMutating,
        startBulkMutation,
        resetBulkMutation,
    }
}
