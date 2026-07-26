import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'
import { requireSuperAdminOrderDetailAuthority, requireSuperAdminOrderListAuthority } from './orderIdentityAuthority'

export type SuperAdminOrderStatus =
    | 'Pending'
    | 'Awaiting Payment'
    | 'AwaitingPayment'
    | 'Payment Pending'
    | 'PaymentPending'
    | 'Confirmed'
    | 'Processing'
    | 'Packed'
    | 'Ready For Shipment'
    | 'ReadyForShipment'
    | 'Shipped'
    | 'Delivered'
    | 'Completed'
    | 'Cancelled'
    | 'Returned'
    | 'Refunded'
    | 'Failed'
    | 'OnHold'

export type SuperAdminOrderPaymentFilter = 'Paid' | 'Unpaid' | 'Failed'

export type SuperAdminPaymentStatus =
    | 'Pending'
    | 'Authorized'
    | 'Collected'
    | 'Paid'
    | 'PartiallyPaid'
    | 'Failed'
    | 'Refunded'
    | 'PartiallyRefunded'
    | 'AwaitingVerification'
    | 'Rejected'
    | 'Settled'
    | 'CashOnDelivery'
    | 'Expired'
    | 'AmountMismatch'

export type SuperAdminPaymentRecordStatus = 'Pending' | 'Paid' | 'Failed' | 'Ignored'

export type SuperAdminOrdersSortField =
    | 'CreatedAt'
    | 'OrderNumber'
    | 'Customer'
    | 'Tenant'
    | 'TotalAmount'
    | 'Status'
    | 'PaymentStatus'
    | 'VendorCount'

export type SuperAdminSortDirection = 'Asc' | 'Desc'

export interface SuperAdminTenantOption {
    id: string
    name: string
    slug?: string | null
    isActive: boolean
    contactEmail?: string | null
    createdAtUtc: string
}

export interface SuperAdminOrderSubOrderSummary {
    subOrderId: string
    vendorId: string
    vendorName: string
    amount: number
    currency: string
    status: SuperAdminOrderStatus
    paymentStatus: SuperAdminPaymentStatus
}

export interface SuperAdminOrderListItem {
    orderId: string
    orderNumber: string
    customerName: string
    totalAmount: number
    currency: string
    status: SuperAdminOrderStatus
    paymentStatus: SuperAdminPaymentStatus
    tenantName: string
    vendorCount: number
    createdAt: string
    subOrders: SuperAdminOrderSubOrderSummary[]
}

export interface SuperAdminOrderPaymentDetails {
    paymentId: string
    amount: number
    currency: string
    status: SuperAdminPaymentRecordStatus
    provider: string
    providerReference: string
    providerIntentId?: string | null
    providerTransactionId?: string | null
    failureReason?: string | null
    isProcessed: boolean
    createdAt: string
    processedAtUtc?: string | null
    paidAtUtc?: string | null
}

export interface SuperAdminOrderSubOrderDetails {
    subOrderId: string
    orderNumber: string
    vendorId: string
    vendorName: string
    amount: number
    currency: string
    status: SuperAdminOrderStatus
    paymentStatus: SuperAdminPaymentStatus
    platformCommissionAmount: number
    platformCommissionRate: number
    vendorEarningAmount: number
    shippingMethodName?: string | null
    paymentMethodName: string
    paymentReferenceId?: string | null
    paidAt?: string | null
    itemCount: number
    commissionSnapshotJson?: string | null
}

export interface SuperAdminOrderItem {
    itemId: string
    subOrderId: string
    vendorName: string
    productName: string
    variantName?: string | null
    imageUrl?: string | null
    quantity: number
    unitPrice: number
    totalPrice: number
}

export interface SuperAdminOrderDetails {
    orderId: string
    orderNumber: string
    customerName: string
    customerPhone?: string | null
    addressLine: string
    city?: string | null
    area?: string | null
    notes?: string | null
    currency: string
    subtotal: number
    shippingTotal: number
    totalAmount: number
    status: SuperAdminOrderStatus
    paymentStatus: SuperAdminPaymentStatus
    paymentMethodCode: string
    paymentMethodName: string
    paymentReference?: string | null
    paymentReferenceId?: string | null
    paymentGateway?: string | null
    createdAt: string
    paidAt?: string | null
    vendorCount: number
    tenantName: string
    platformCommissionTotal: number
    vendorEarningsTotal: number
    eventStatus?: string | null
    payments: SuperAdminOrderPaymentDetails[]
    subOrders: SuperAdminOrderSubOrderDetails[]
    orderItems: SuperAdminOrderItem[]
}

export interface SuperAdminOrdersStats {
    windowStartUtc: string
    windowEndUtc: string
    asOfUtc: string
    currency: string
    totalOrdersToday: number
    pendingOrders: number
    failedPayments: number
    revenueToday: number
}

export interface SuperAdminOrdersFilters {
    page: number
    pageSize: number
    search?: string
    status?: SuperAdminOrderStatus | ''
    paymentStatus?: SuperAdminOrderPaymentFilter | ''
    tenantId?: string
    fromDate?: string
    toDate?: string
    sortBy: SuperAdminOrdersSortField
    sortDirection: SuperAdminSortDirection
}

export interface SuperAdminOrdersMeta extends SuperAdminApiMeta {
    totalPages?: number
    sortBy?: string
    sortDirection?: string
}

export interface SuperAdminOrdersResult {
    orders: SuperAdminOrderListItem[]
    meta?: SuperAdminOrdersMeta
}

export const SUPER_ADMIN_ORDER_STATUS_OPTIONS: SuperAdminOrderStatus[] = [
    'Pending',
    'Awaiting Payment',
    'Payment Pending',
    'Confirmed',
    'Processing',
    'Packed',
    'Ready For Shipment',
    'Shipped',
    'Delivered',
    'Completed',
    'Cancelled',
    'Returned',
    'Refunded',
    'Failed',
    'OnHold',
]

export const SUPER_ADMIN_PAYMENT_FILTER_OPTIONS: SuperAdminOrderPaymentFilter[] = [
    'Paid',
    'Unpaid',
    'Failed',
]

export const PAID_PAYMENT_STATUSES: readonly SuperAdminPaymentStatus[] = [
    'Collected',
    'Paid',
    'Settled',
    'Refunded',
    'PartiallyRefunded',
]

export const FAILED_PAYMENT_STATUSES: readonly SuperAdminPaymentStatus[] = ['Failed', 'Rejected', 'Expired', 'AmountMismatch']

export const UNPAID_PAYMENT_STATUSES: readonly SuperAdminPaymentStatus[] = [
    'Pending',
    'Authorized',
    'PartiallyPaid',
    'AwaitingVerification',
    'CashOnDelivery',
]

export function isFailedPaymentStatus(status: SuperAdminPaymentStatus | null | undefined): boolean {
    return status ? FAILED_PAYMENT_STATUSES.includes(status) : false
}

export function isPendingOrderStatus(status: SuperAdminOrderStatus | null | undefined): boolean {
    return status === 'Pending' || status === 'Awaiting Payment' || status === 'AwaitingPayment' || status === 'Payment Pending' || status === 'PaymentPending'
}

export function isPaidPaymentStatus(status: SuperAdminPaymentStatus | null | undefined): boolean {
    return status ? PAID_PAYMENT_STATUSES.includes(status) : false
}

function buildOrdersQueryParams(
    filters: Partial<SuperAdminOrdersFilters>,
    options?: { includePagination?: boolean },
): URLSearchParams {
    const params = new URLSearchParams()

    if (options?.includePagination !== false) {
        if (typeof filters.page === 'number') {
            params.set('page', String(filters.page))
        }

        if (typeof filters.pageSize === 'number') {
            params.set('pageSize', String(filters.pageSize))
        }
    }

    if (filters.sortBy) {
        params.set('sortBy', filters.sortBy)
    }

    if (filters.sortDirection) {
        params.set('sortDirection', filters.sortDirection)
    }

    if (filters.search?.trim()) {
        params.set('search', filters.search.trim())
    }

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (filters.paymentStatus) {
        params.set('paymentStatus', filters.paymentStatus)
    }

    if (filters.tenantId) {
        params.set('tenantId', filters.tenantId)
    }

    if (filters.fromDate) {
        params.set('fromDate', filters.fromDate)
    }

    if (filters.toDate) {
        params.set('toDate', filters.toDate)
    }

    return params
}

export async function getSuperAdminOrders(
    filters: SuperAdminOrdersFilters,
): Promise<SuperAdminOrdersResult> {
    const params = buildOrdersQueryParams(filters)
    const response = await superAdminApiClient.get<{ orders: SuperAdminOrderListItem[] }>(
        `/orders?${params.toString()}`,
    )

    return {
        orders: requireSuperAdminOrderListAuthority(response.data.orders ?? []),
        meta: response.meta as SuperAdminOrdersMeta | undefined,
    }
}

export async function getSuperAdminOrderStats(
    tenantId?: string,
): Promise<SuperAdminOrdersStats> {
    const params = new URLSearchParams()

    if (tenantId) {
        params.set('tenantId', tenantId)
    }

    const response = await superAdminApiClient.get<SuperAdminOrdersStats>(
        `/orders/stats${params.size ? `?${params.toString()}` : ''}`,
    )

    return response.data
}

export async function getSuperAdminOrderDetails(orderId: string): Promise<SuperAdminOrderDetails> {
    const response = await superAdminApiClient.get<SuperAdminOrderDetails>(`/orders/${orderId}`)
    return requireSuperAdminOrderDetailAuthority(response.data, orderId)
}

export async function downloadSuperAdminOrdersCsv(
    filters: SuperAdminOrdersFilters,
): Promise<Blob> {
    const params = buildOrdersQueryParams(filters, { includePagination: false })
    return superAdminApiClient.getBlob(`/orders/export?${params.toString()}`)
}

export async function getSuperAdminOrderTenants(): Promise<SuperAdminTenantOption[]> {
    return superAdminApiClient.getRaw<SuperAdminTenantOption[]>('/api/tenants')
}
