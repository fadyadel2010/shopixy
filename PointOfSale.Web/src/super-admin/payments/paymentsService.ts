import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'
import {
    requireSuperAdminPaymentMethodPresentation,
    requireSuperAdminPaymentMethodsPresentation,
    requireSuperAdminPaymentProofPresentation,
    requireSuperAdminPaymentRequestPresentation,
    requireSuperAdminPaymentsPresentation,
} from './paymentOwnershipAuthority'

export type VendorPaymentRequestStatus = 'PendingVerification' | 'Approved' | 'Rejected'

export interface SuperAdminVendorPaymentMethod {
    id: string
    name: string
    destinationValue: string
    instructions: string
    isActive: boolean
    createdAtUtc: string
    createdBy: string
}

export interface SuperAdminVendorPaymentRequestItem {
    paymentRequestId: string
    vendorId: string
    vendorName: string
    paymentMethodId: string
    methodName: string
    destinationValue: string
    instructions: string
    amount: number
    currency: string
    status: VendorPaymentRequestStatus
    transferReference?: string | null
    createdAtUtc: string
    approvedAtUtc?: string | null
    approvedBy?: string | null
    rejectedAtUtc?: string | null
    rejectedBy?: string | null
    reviewReason?: string | null
    hasProof: boolean
    proofContentType?: string | null
    proofFileName?: string | null
    proofSizeBytes?: number | null
}

export interface SuperAdminVendorPaymentFilters {
    page: number
    pageSize: number
    status?: VendorPaymentRequestStatus | ''
    vendorId?: string
    search?: string
}

export interface SuperAdminVendorPaymentsResult {
    requests: SuperAdminVendorPaymentRequestItem[]
    meta?: SuperAdminApiMeta
}

export interface SuperAdminVendorPaymentProofAccess {
    fileUrl: string
    contentType: string
    fileName: string
    sizeBytes: number
    uploadedAtUtc: string
}

export const SUPER_ADMIN_VENDOR_PAYMENT_STATUSES: VendorPaymentRequestStatus[] = [
    'PendingVerification',
    'Approved',
    'Rejected',
]

function buildQueryString(filters: SuperAdminVendorPaymentFilters): string {
    const params = new URLSearchParams()
    params.set('page', String(filters.page))
    params.set('pageSize', String(filters.pageSize))

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (filters.vendorId) {
        params.set('vendorId', filters.vendorId)
    }

    if (filters.search?.trim()) {
        params.set('search', filters.search.trim())
    }

    return params.toString()
}

export async function getSuperAdminVendorPaymentMethods(): Promise<SuperAdminVendorPaymentMethod[]> {
    const response = await superAdminApiClient.get<{ methods: SuperAdminVendorPaymentMethod[] }>('/payment-methods')
    return requireSuperAdminPaymentMethodsPresentation(response.data.methods ?? [])
}

export async function createSuperAdminVendorPaymentMethod(payload: {
    name: string
    destinationValue: string
    instructions: string
    isActive: boolean
}): Promise<SuperAdminVendorPaymentMethod> {
    const response = await superAdminApiClient.post<SuperAdminVendorPaymentMethod, typeof payload>(
        '/payment-methods',
        payload,
    )

    return requireSuperAdminPaymentMethodPresentation(response.data)
}

export async function getSuperAdminVendorPayments(
    filters: SuperAdminVendorPaymentFilters,
): Promise<SuperAdminVendorPaymentsResult> {
    const response = await superAdminApiClient.get<{ requests: SuperAdminVendorPaymentRequestItem[] }>(
        `/payments?${buildQueryString(filters)}`,
    )

    return requireSuperAdminPaymentsPresentation({
        requests: response.data.requests ?? [],
        meta: response.meta,
    })
}

export async function approveSuperAdminVendorPayment(
    paymentRequestId: string,
    payload: { reason: string; allowOverpayment?: boolean },
): Promise<SuperAdminVendorPaymentRequestItem> {
    const response = await superAdminApiClient.post<
        SuperAdminVendorPaymentRequestItem,
        { reason: string; allowOverpayment?: boolean }
    >(`/payments/${paymentRequestId}/approve`, payload)

    return requireSuperAdminPaymentRequestPresentation(response.data)
}

export async function rejectSuperAdminVendorPayment(
    paymentRequestId: string,
    payload: { reason: string },
): Promise<SuperAdminVendorPaymentRequestItem> {
    const response = await superAdminApiClient.post<
        SuperAdminVendorPaymentRequestItem,
        { reason: string }
    >(`/payments/${paymentRequestId}/reject`, payload)

    return requireSuperAdminPaymentRequestPresentation(response.data)
}

export async function getSuperAdminVendorPaymentProofUrl(
    paymentRequestId: string,
): Promise<SuperAdminVendorPaymentProofAccess> {
    const response = await superAdminApiClient.get<SuperAdminVendorPaymentProofAccess>(
        `/payments/${paymentRequestId}/proof-url`,
    )

    return requireSuperAdminPaymentProofPresentation(response.data)
}
