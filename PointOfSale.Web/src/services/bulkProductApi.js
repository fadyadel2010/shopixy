import api, { getSafeApiErrorMessage } from './api'

const BULK_PRODUCT_ROUTES = Object.freeze({
    activate: '/products/bulk/activate',
    deactivate: '/products/bulk/deactivate',
    publish: '/products/bulk/publish',
    unpublish: '/products/bulk/unpublish',
    delete: '/products/bulk/delete',
})

const SAFE_BACKEND_MESSAGE_STATUSES = new Set([400, 403, 409])

const BACKEND_DETAIL_LEAK_PATTERNS = [
    /exception/i,
    /stack trace/i,
    /\bat\s+\S+\(/i,
    /sql/i,
    /select\s+.+\s+from/i,
    /system\./i,
]

const MAX_DETAILS_DEPTH = 4
const MAX_SAFE_DETAIL_STRING_LENGTH = 500
const BULK_RESPONSE_FIELDS = Object.freeze([
    'requested',
    'accepted',
    'processed',
    'unchanged',
    'failed',
    'correlationId',
    'results',
])
const BULK_ITEM_STATUSES = new Set(['processed', 'unchanged', 'failed'])

export class BulkProductApiError extends Error {
    constructor(message, { status = null, code = null, correlationId = null, details = null, cause = null } = {}) {
        super(message)
        this.name = 'BulkProductApiError'
        this.status = status
        this.code = code
        this.correlationId = correlationId
        this.details = details
        Object.defineProperty(this, 'cause', {
            value: cause,
            writable: true,
            enumerable: false,
            configurable: true,
        })
    }
}

const isPlainObject = (value) => (
    value !== null
    && typeof value === 'object'
    && !Array.isArray(value)
)

const isNonBlankString = (value) => (
    typeof value === 'string' && value.trim().length > 0
)

const assertValidBulkItems = (items) => {
    if (!Array.isArray(items) || items.length === 0) {
        throw new BulkProductApiError(
            'Bulk operation requires a non-empty array of items.',
            { code: 'INVALID_BULK_ITEMS' },
        )
    }

    return items.map((item, index) => {
        if (!isPlainObject(item)) {
            throw new BulkProductApiError(
                `Bulk item at index ${index} must be an object with productId and expectedVersion.`,
                { code: 'INVALID_BULK_ITEMS' },
            )
        }

        if (!isNonBlankString(item.productId)) {
            throw new BulkProductApiError(
                `Bulk item at index ${index} is missing a valid productId.`,
                { code: 'INVALID_BULK_ITEMS' },
            )
        }

        if (!isNonBlankString(item.expectedVersion)) {
            throw new BulkProductApiError(
                `Bulk item at index ${index} is missing a valid expectedVersion.`,
                { code: 'INVALID_BULK_ITEMS' },
            )
        }

        return {
            productId: item.productId,
            expectedVersion: item.expectedVersion,
        }
    })
}

export const toBulkProductItem = (product) => {
    if (!isPlainObject(product)) {
        throw new BulkProductApiError(
            'Product must be an object with id and rowVersion.',
            { code: 'INVALID_PRODUCT_DTO' },
        )
    }

    if (!isNonBlankString(product.id)) {
        throw new BulkProductApiError(
            'Product is missing a valid id.',
            { code: 'INVALID_PRODUCT_DTO' },
        )
    }

    if (!isNonBlankString(product.rowVersion)) {
        throw new BulkProductApiError(
            'Product is missing a valid rowVersion.',
            { code: 'INVALID_PRODUCT_DTO' },
        )
    }

    return {
        productId: product.id,
        expectedVersion: product.rowVersion,
    }
}

const createSecureIdempotencyKey = () => {
    const cryptoRef = typeof globalThis !== 'undefined' ? globalThis.crypto : null

    if (cryptoRef && typeof cryptoRef.randomUUID === 'function') {
        return cryptoRef.randomUUID()
    }

    throw new BulkProductApiError(
        'Secure UUID generation is unavailable; refusing to send a bulk mutation without an Idempotency-Key.',
        { code: 'IDEMPOTENCY_KEY_GENERATION_UNAVAILABLE' },
    )
}

const resolveIdempotencyKey = (explicitKey) => {
    if (explicitKey === undefined) {
        return createSecureIdempotencyKey()
    }

    if (!isNonBlankString(explicitKey)) {
        throw new BulkProductApiError(
            'A supplied Idempotency-Key must be a non-blank string.',
            { code: 'INVALID_IDEMPOTENCY_KEY' },
        )
    }

    return explicitKey
}

const isSafeBackendMessage = (message) => (
    typeof message === 'string'
    && message.trim().length > 0
    && message.length <= 180
    && !BACKEND_DETAIL_LEAK_PATTERNS.some((pattern) => pattern.test(message))
)

const pickMachineCode = (value) => {
    if (typeof value === 'string' && value.trim().length > 0) {
        return value
    }

    if (typeof value === 'number' && Number.isFinite(value)) {
        return value
    }

    return null
}

const pickFirstCode = (...values) => (
    values.find((value) => value !== null && value !== undefined) ?? null
)

const extractErrorCode = (data) => {
    if (!isPlainObject(data)) {
        return null
    }

    const nestedError = isPlainObject(data.error) ? data.error : {}
    const firstValidationError = Array.isArray(data.errors)
        ? (isPlainObject(data.errors[0]) ? data.errors[0] : {})
        : {}

    return pickFirstCode(
        pickMachineCode(data.code),
        pickMachineCode(data.errorCode),
        pickMachineCode(nestedError.code),
        pickMachineCode(firstValidationError.code),
    )
}

const extractCorrelationId = (data) => {
    if (!isPlainObject(data)) {
        return null
    }

    if (typeof data.correlationId === 'string' && data.correlationId.trim().length > 0) {
        return data.correlationId
    }

    if (isPlainObject(data.error)
        && typeof data.error.correlationId === 'string'
        && data.error.correlationId.trim().length > 0) {
        return data.error.correlationId
    }

    return null
}

const isSafeErrorDetails = (value, depth = 0) => {
    if (value === null || typeof value === 'boolean') {
        return true
    }

    if (typeof value === 'number') {
        return Number.isFinite(value)
    }

    if (typeof value === 'string') {
        return value.length <= MAX_SAFE_DETAIL_STRING_LENGTH
            && !BACKEND_DETAIL_LEAK_PATTERNS.some((pattern) => pattern.test(value))
    }

    if (depth >= MAX_DETAILS_DEPTH) {
        return false
    }

    if (Array.isArray(value)) {
        return value.every((entry) => isSafeErrorDetails(entry, depth + 1))
    }

    if (isPlainObject(value)) {
        return Object.values(value).every((entry) => isSafeErrorDetails(entry, depth + 1))
    }

    return false
}

const extractErrorDetails = (data) => {
    if (!isPlainObject(data)) {
        return null
    }

    for (const candidate of [data.errors, data.details]) {
        if (candidate !== undefined && candidate !== null && isSafeErrorDetails(candidate)) {
            return candidate
        }
    }

    return null
}

const normalizeBulkApiError = (error) => {
    const status = typeof error?.response?.status === 'number' ? error.response.status : null
    const responseData = isPlainObject(error?.response?.data) ? error.response.data : null
    const isServerFailure = status !== null && status >= 500

    const backendMessage = responseData?.message
    const message = (status !== null
        && SAFE_BACKEND_MESSAGE_STATUSES.has(status)
        && isSafeBackendMessage(backendMessage))
        ? backendMessage.trim()
        : getSafeApiErrorMessage(error)

    return new BulkProductApiError(message, {
        status,
        code: extractErrorCode(responseData),
        correlationId: extractCorrelationId(responseData),
        details: isServerFailure ? null : extractErrorDetails(responseData),
        cause: error,
    })
}

const normalizeBulkItemResult = (item) => {
    const normalizedStatus = typeof item?.status === 'string'
        ? item.status.trim().toLowerCase()
        : null
    const optionalStringFieldsAreValid = ['code', 'message', 'previousVersion', 'newVersion']
        .every((field) => (
            item?.[field] === undefined
            || item?.[field] === null
            || typeof item[field] === 'string'
        ))

    if (!isPlainObject(item)
        || !isNonBlankString(item.productId)
        || !BULK_ITEM_STATUSES.has(normalizedStatus)
        || !optionalStringFieldsAreValid) {
        throw new BulkProductApiError(
            'Bulk operation returned an invalid per-item result.',
            { code: 'INVALID_BULK_RESPONSE' },
        )
    }

    return {
        productId: item.productId ?? null,
        status: normalizedStatus,
        code: item.code ?? null,
        message: item.message ?? null,
        previousVersion: item.previousVersion ?? null,
        newVersion: item.newVersion ?? null,
    }
}

const normalizeBulkOperationResult = (payload) => {
    const counts = isPlainObject(payload)
        ? [payload.requested, payload.accepted, payload.processed, payload.unchanged, payload.failed]
        : []
    const countsAreValid = counts.length === 5
        && counts.every((value) => Number.isSafeInteger(value) && value >= 0)

    if (!isPlainObject(payload)
        || BULK_RESPONSE_FIELDS.some((field) => !Object.prototype.hasOwnProperty.call(payload, field))
        || !countsAreValid
        || payload.accepted > payload.requested
        || payload.processed + payload.unchanged + payload.failed !== payload.accepted
        || !isNonBlankString(payload.correlationId)
        || !Array.isArray(payload.results)
        || payload.results.length !== payload.accepted) {
        throw new BulkProductApiError(
            'Bulk operation returned an invalid response contract.',
            { code: 'INVALID_BULK_RESPONSE' },
        )
    }

    return {
        requested: payload.requested,
        accepted: payload.accepted,
        processed: payload.processed,
        unchanged: payload.unchanged,
        failed: payload.failed,
        correlationId: payload.correlationId,
        results: payload.results.map(normalizeBulkItemResult),
    }
}

const executeBulkProductOperation = async (route, items, idempotencyKey) => {
    const transportItems = assertValidBulkItems(items)
    const resolvedIdempotencyKey = resolveIdempotencyKey(idempotencyKey)

    let response
    try {
        response = await api.post(route, { items: transportItems }, {
            headers: { 'Idempotency-Key': resolvedIdempotencyKey },
            // The shared API client historically mirrored Idempotency-Key into
            // X-Request-Id. On the server that value becomes the logged
            // correlation id. Bulk keys are mutation credentials, not tracing
            // identifiers, so let the server generate an independent request id.
            suppressIdempotencyRequestIdMirroring: true,
        })
    } catch (error) {
        throw normalizeBulkApiError(error)
    }

    return normalizeBulkOperationResult(response?.data)
}

export const bulkActivate = (items, idempotencyKey) => executeBulkProductOperation(BULK_PRODUCT_ROUTES.activate, items, idempotencyKey)

export const bulkDeactivate = (items, idempotencyKey) => executeBulkProductOperation(BULK_PRODUCT_ROUTES.deactivate, items, idempotencyKey)

export const bulkPublish = (items, idempotencyKey) => executeBulkProductOperation(BULK_PRODUCT_ROUTES.publish, items, idempotencyKey)

export const bulkUnpublish = (items, idempotencyKey) => executeBulkProductOperation(BULK_PRODUCT_ROUTES.unpublish, items, idempotencyKey)

export const bulkDelete = (items, idempotencyKey) => executeBulkProductOperation(BULK_PRODUCT_ROUTES.delete, items, idempotencyKey)

const bulkProductApi = {
    bulkActivate,
    bulkDeactivate,
    bulkPublish,
    bulkUnpublish,
    bulkDelete,
    toBulkProductItem,
}

export default bulkProductApi
