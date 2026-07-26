import type {
    SuperAdminVendorPaymentMethod,
    SuperAdminVendorPaymentProofAccess,
    SuperAdminVendorPaymentRequestItem,
    SuperAdminVendorPaymentsResult,
    VendorPaymentRequestStatus,
} from './paymentsService'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const CURRENCY = /^[A-Z]{3}$/
const PAYMENT_STATUSES = new Set<VendorPaymentRequestStatus>(['PendingVerification', 'Approved', 'Rejected'])
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const guid = (value: unknown): value is string => text(value) && GUID.test(value)
const date = (value: unknown) => text(value) && Number.isFinite(Date.parse(value))
const money = (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value >= 0
const optionalDate = (value: unknown) => value == null || date(value)
const optionalText = (value: unknown) => value == null || typeof value === 'string'

/** Monitoring rows are backend-issued visibility evidence only; a selected row
 * is discarded when it is absent from the current backend-backed list. */
export function requireSuperAdminPaymentRequestPresentation(value: unknown): SuperAdminVendorPaymentRequestItem {
    const row = value as SuperAdminVendorPaymentRequestItem
    if (!row || typeof row !== 'object' || !guid(row.paymentRequestId) || !guid(row.vendorId) || !guid(row.paymentMethodId) || !text(row.vendorName) || !text(row.methodName) || !text(row.destinationValue) || !text(row.instructions) || !money(row.amount) || !CURRENCY.test(row.currency) || !PAYMENT_STATUSES.has(row.status) || !date(row.createdAtUtc) || !optionalDate(row.approvedAtUtc) || !optionalDate(row.rejectedAtUtc) || !optionalText(row.transferReference) || !optionalText(row.approvedBy) || !optionalText(row.rejectedBy) || !optionalText(row.reviewReason) || typeof row.hasProof !== 'boolean' || !optionalText(row.proofContentType) || !optionalText(row.proofFileName) || (row.proofSizeBytes != null && !money(row.proofSizeBytes))) {
        throw new Error('Super Admin payment ownership authority rejected malformed monitoring evidence.')
    }
    return row
}

export function requireSuperAdminPaymentMethodsPresentation(value: unknown): SuperAdminVendorPaymentMethod[] {
    if (!Array.isArray(value)) throw new Error('Super Admin payment ownership authority rejected malformed payment-method evidence.')
    const ids = new Set<string>()
    return value.map((method) => requireSuperAdminPaymentMethodPresentation(method, ids))
}

export function requireSuperAdminPaymentMethodPresentation(value: unknown, seenIds?: Set<string>): SuperAdminVendorPaymentMethod {
    const item = value as SuperAdminVendorPaymentMethod
    if (!guid(item?.id) || seenIds?.has(item.id) || !text(item.name) || !text(item.destinationValue) || !text(item.instructions) || typeof item.isActive !== 'boolean' || !date(item.createdAtUtc) || !guid(item.createdBy)) throw new Error('Super Admin payment ownership authority rejected malformed payment-method evidence.')
    seenIds?.add(item.id)
    return item
}

export function requireSuperAdminPaymentsPresentation(value: unknown): SuperAdminVendorPaymentsResult {
    const result = value as SuperAdminVendorPaymentsResult
    if (!result || typeof result !== 'object' || !Array.isArray(result.requests)) throw new Error('Super Admin payment ownership authority rejected malformed monitoring evidence.')
    const ids = new Set<string>()
    const requests = result.requests.map((request) => {
        const accepted = requireSuperAdminPaymentRequestPresentation(request)
        if (ids.has(accepted.paymentRequestId)) throw new Error('Super Admin payment ownership authority rejected duplicate payment monitoring evidence.')
        ids.add(accepted.paymentRequestId)
        return accepted
    })
    return { ...result, requests }
}

export function requireCurrentSuperAdminPaymentSelection(paymentRequestId: string | null, requests: SuperAdminVendorPaymentRequestItem[]): SuperAdminVendorPaymentRequestItem | null {
    if (!paymentRequestId || !guid(paymentRequestId)) return null
    return requests.find((request) => request.paymentRequestId.toLowerCase() === paymentRequestId.toLowerCase()) ?? null
}

export function requireSuperAdminPaymentProofPresentation(value: unknown): SuperAdminVendorPaymentProofAccess {
    const proof = value as SuperAdminVendorPaymentProofAccess
    if (!proof || typeof proof !== 'object' || !text(proof.fileUrl) || !text(proof.contentType) || !text(proof.fileName) || !money(proof.sizeBytes) || !date(proof.uploadedAtUtc)) throw new Error('Super Admin payment ownership authority rejected malformed proof evidence.')
    return proof
}
