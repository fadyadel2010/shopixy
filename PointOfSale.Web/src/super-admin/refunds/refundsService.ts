import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'
import {
    requireSuperAdminRefundDetailsForCurrentSelection,
    requireSuperAdminRefundDetailsPresentation,
    requireSuperAdminRefundsPresentation,
    requireSuperAdminReturnPresentation,
} from './refundLifecycleAuthority'

export const GATE3R_ACCOUNTING_OUTBOX_MESSAGE_ID = '01a08680-3cff-7ef1-a6ff-58fa723f30f1'

export interface DevelopmentAccountingOutboxRequeueResult {
    messageId: string
    wasRequeued: boolean
    timestamp: string
}

export type SuperAdminRefundStatus =
    | 'Requested'
    | 'UnderReview'
    | 'Approved'
    | 'WaitingForReturn'
    | 'AwaitingPayout'
    | 'ReadyForExecution'
    | 'Rejected'
    | 'Cancelled'
    | 'Processing'
    | 'Paid'
    | 'Executed'
    | 'Completed'
    | 'Failed'

export type RefundReasonCode =
    | 'DamagedItem'
    | 'NotAsDescribed'
    | 'WrongItem'
    | 'MissingParts'
    | 'QualityIssue'
    | 'Other'

export type RefundPayoutMethod = 'VodafoneCash' | 'InstaPay' | 'BankTransfer'
export type SuperAdminReturnStatus =
    | 'Pending'
    | 'PickupScheduled'
    | 'PickedUp'
    | 'InTransit'
    | 'DeliveredToVendor'
    | 'Confirmed'
    | 'Failed'

export type RefundExecutionTrigger = 'OnPickup' | 'OnDelivery' | 'OnVendorConfirmation'

export interface RefundEvidenceMedia {
    mediaId: string
    url: string
    mediaType: 'image' | 'video' | string
    mimeType: string
    sizeBytes: number
    fileName: string
}

export interface SuperAdminRefundListItem {
    refundId: string
    orderId: string
    orderNumber: string
    vendorId: string
    vendorName: string
    customerId: string
    customerName: string
    productName: string
    quantity: number
    amount: number
    currency: string
    status: SuperAdminRefundStatus
    createdAt: string
}

export interface SuperAdminRefundOrderInfo {
    orderId: string
    orderNumber: string
    paymentMethodName: string
    paymentStatus: string
    totalAmount: number
    shippingTotal: number
    currency: string
    createdAt: string
}

export interface SuperAdminRefundCustomerInfo {
    customerId: string
    name: string
    email: string
    phoneNumber?: string | null
}

export interface SuperAdminRefundItemInfo {
    orderItemId: string
    subOrderId: string
    vendorId: string
    vendorName: string
    productName: string
    variantName?: string | null
    imageUrl?: string | null
    purchasedQuantity: number
    unitPrice: number
    totalPrice: number
    platformCommissionAmount: number
    vendorEarningAmount: number
    status: string
    deliveredAt?: string | null
}

export interface SuperAdminRefundPayoutInfo {
    method: RefundPayoutMethod
    maskedAccountIdentifier: string
    accountHolderName?: string | null
    isVerified: boolean
}

export interface SuperAdminRefundFinancialPreview {
    paymentType: string
    hasPayoutOccurred: boolean
    commissionReversalAmount: number
    vendorEarningChange: number
    gatewayRefundRequired: boolean
    paymentId?: string | null
    paymentProvider?: string | null
    paymentRecordStatus?: string | null
    paymentRefundedAmountBefore: number
    paymentRefundedAmountAfter: number
    vendorNetEarningsBefore: number
    vendorNetEarningsAfter: number
    hasPaidOutVendorEarnings: boolean
    hasPendingPayoutReservation: boolean
    executionPath: string
    platformCashDelta: number
    platformPayableDelta: number
    vendorDebtDelta: number
    commissionReceivableDelta: number
    vendorPayableBefore: number
    vendorPayableAfter: number
    vendorDebtBefore: number
    vendorDebtAfter: number
    impactSummary: string
}

export interface SuperAdminReturnDetails {
    returnId: string
    refundId: string
    orderItemId: string
    customerId: string
    vendorId: string
    status: SuperAdminReturnStatus
    refundExecutionTrigger: RefundExecutionTrigger
    pickupScheduledAt?: string | null
    pickedUpAt?: string | null
    inTransitAt?: string | null
    deliveredAt?: string | null
    confirmedAt?: string | null
    trackingNumber?: string | null
    courierName?: string | null
    notes?: string | null
    createdAt: string
    updatedAt?: string | null
    canSchedulePickup: boolean
    canMarkPickedUp: boolean
    canMarkInTransit: boolean
    canMarkDelivered: boolean
    canMarkFailed: boolean
    canConfirmReceipt: boolean
    isExecutionTriggerReached: boolean
}

export interface SuperAdminRefundExecution {
    executionId: string
    method: RefundPayoutMethod
    maskedAccountIdentifier: string
    transactionReference: string
    notes?: string | null
    executedBy: string
    executedAt: string
    isSuccessful: boolean
    failureReason?: string | null
    financialRefundId: string
    status: 'Pending' | 'Processing' | 'Completed' | 'Failed'
    paymentId?: string | null
    gatewayStatus?: string | null
    gatewayReference?: string | null
    isDevelopmentSimulation: boolean
    externalMoneyMovementConfirmed: boolean
    commissionReversalAmount: number
    vendorEarningReversalAmount: number
    isPostPayoutAdjustment: boolean
    processingStartedAtUtc?: string | null
    processedAtUtc?: string | null
}

export interface SuperAdminRefundDetails {
    refundId: string
    status: SuperAdminRefundStatus
    reason: string
    reasonCode: RefundReasonCode
    reasonText?: string | null
    payout?: SuperAdminRefundPayoutInfo | null
    mediaUrls: string[]
    media: RefundEvidenceMedia[]
    adminNote?: string | null
    failureReason?: string | null
    quantity: number
    unitPriceSnapshot: number
    amount: number
    currency: string
    createdAt: string
    updatedAt?: string | null
    reviewedAtUtc?: string | null
    approvedAtUtc?: string | null
    rejectedAtUtc?: string | null
    processingStartedAtUtc?: string | null
    completedAtUtc?: string | null
    failedAtUtc?: string | null
    executionAttempts: number
    canApprove: boolean
    canReject: boolean
    canExecute: boolean
    canRetry: boolean
    order: SuperAdminRefundOrderInfo
    customer: SuperAdminRefundCustomerInfo
    item: SuperAdminRefundItemInfo
    financialPreview: SuperAdminRefundFinancialPreview
    returnRequest?: SuperAdminReturnDetails | null
    execution?: SuperAdminRefundExecution | null
}

export interface SuperAdminRefundsFilters {
    page: number
    pageSize: number
    status?: SuperAdminRefundStatus | ''
    vendorId?: string
    fromDate?: string
    toDate?: string
}

export interface SuperAdminRefundsMeta extends SuperAdminApiMeta {
    totalPages?: number
}

export interface SuperAdminRefundsResult {
    refunds: SuperAdminRefundListItem[]
    meta?: SuperAdminRefundsMeta
}

export const SUPER_ADMIN_REFUND_STATUS_OPTIONS: SuperAdminRefundStatus[] = [
    'Requested',
    'UnderReview',
    'Approved',
    'WaitingForReturn',
    'AwaitingPayout',
    'Rejected',
    'Cancelled',
    'Processing',
    'Paid',
    'Failed',
]

function buildRefundsQueryParams(filters: SuperAdminRefundsFilters): URLSearchParams {
    const params = new URLSearchParams()
    params.set('page', String(filters.page))
    params.set('pageSize', String(filters.pageSize))

    if (filters.status) {
        params.set('status', filters.status)
    }

    if (filters.vendorId) {
        params.set('vendorId', filters.vendorId)
    }

    if (filters.fromDate) {
        params.set('fromDate', filters.fromDate)
    }

    if (filters.toDate) {
        params.set('toDate', filters.toDate)
    }

    return params
}

export async function getSuperAdminRefunds(filters: SuperAdminRefundsFilters): Promise<SuperAdminRefundsResult> {
    const params = buildRefundsQueryParams(filters)
    const response = await superAdminApiClient.get<{ refunds: SuperAdminRefundListItem[] }>(
        `/refunds?${params.toString()}`,
    )

    return requireSuperAdminRefundsPresentation({
        refunds: response.data.refunds,
        meta: response.meta as SuperAdminRefundsMeta | undefined,
    })
}

export async function getSuperAdminRefundDetails(refundId: string, currentSelection?: SuperAdminRefundListItem): Promise<SuperAdminRefundDetails> {
    const response = await superAdminApiClient.get<SuperAdminRefundDetails>(`/refunds/${refundId}`)
    if (currentSelection) {
        return requireSuperAdminRefundDetailsForCurrentSelection(response.data, currentSelection)
    }

    return requireSuperAdminRefundDetailsPresentation(response.data, refundId)
}

export async function getSuperAdminReturnDetails(returnId: string): Promise<SuperAdminReturnDetails> {
    const response = await superAdminApiClient.get<SuperAdminReturnDetails>(`/returns/${returnId}`)
    return requireSuperAdminReturnPresentation(response.data, returnId)
}

export async function approveSuperAdminRefund(refundId: string, note?: string): Promise<SuperAdminRefundDetails> {
    const response = await superAdminApiClient.post<SuperAdminRefundDetails, { note?: string }>(
        `/refunds/${refundId}/approve`,
        { note },
    )

    return requireSuperAdminRefundDetailsPresentation(response.data, refundId)
}

export async function rejectSuperAdminRefund(refundId: string, reason: string): Promise<SuperAdminRefundDetails> {
    const response = await superAdminApiClient.post<SuperAdminRefundDetails, { reason: string }>(
        `/refunds/${refundId}/reject`,
        { reason },
    )

    return requireSuperAdminRefundDetailsPresentation(response.data, refundId)
}

export async function retrySuperAdminRefund(refundId: string, note?: string): Promise<SuperAdminRefundDetails> {
    const response = await superAdminApiClient.post<SuperAdminRefundDetails, { note?: string }>(
        `/refunds/${refundId}/retry`,
        { note },
    )

    return requireSuperAdminRefundDetailsPresentation(response.data, refundId)
}

export async function executeSuperAdminRefund(
    refundId: string,
    payload: {
        method: RefundPayoutMethod
        transactionReference: string
        notes?: string
        confirmMoneySent?: boolean
        confirmMethodOverride?: boolean
    },
): Promise<SuperAdminRefundDetails> {
    const response = await superAdminApiClient.post<SuperAdminRefundDetails, typeof payload>(
        `/refunds/${refundId}/execute`,
        payload,
    )

    return requireSuperAdminRefundDetailsPresentation(response.data, refundId)
}

export async function executeDevelopmentCodRefundSimulation(refundId: string): Promise<SuperAdminRefundDetails> {
    if (!import.meta.env.DEV) {
        throw new Error('Development refund simulation is unavailable outside Development.')
    }

    const idempotencyKey = `M1D-${refundId.replace(/-/g, '')}`
    const result = await superAdminApiClient.postRaw<{
        refundId: string
        idempotencyKey: string
        executionPath: string
        evidenceType: string
        isDevelopmentSimulation: boolean
        externalMoneyMovementConfirmed: boolean
        transactionReference: string
    }, { idempotencyKey: string }>(
        `/api/dev/commercehub/m1d/refunds/${refundId}/execute-cod-debt-reduction`,
        { idempotencyKey },
    )

    if (
        result.refundId.toLowerCase() !== refundId.toLowerCase()
        || result.idempotencyKey !== idempotencyKey
        || result.executionPath !== 'CashOnDeliveryDebtReduction'
        || result.evidenceType !== 'DevelopmentSimulatedCodCustomerRefund'
        || result.isDevelopmentSimulation !== true
        || result.externalMoneyMovementConfirmed !== false
    ) {
        throw new Error('Development refund simulation evidence was rejected.')
    }

    return getSuperAdminRefundDetails(refundId)
}

export async function executeDevelopmentOnlineRefundSimulation(refundId: string): Promise<SuperAdminRefundDetails> {
    if (!import.meta.env.DEV) {
        throw new Error('Development refund simulation is unavailable outside Development.')
    }

    const idempotencyKey = `GATE3R-${refundId.replace(/-/g, '')}`
    const result = await superAdminApiClient.postRaw<{
        refundId: string
        idempotencyKey: string
        executionPath: string
        evidenceType: string
        isDevelopmentSimulation: boolean
        externalMoneyMovementConfirmed: boolean
        transactionReference: string
    }, { idempotencyKey: string; reason: string; administratorConfirmation: boolean }>(
        `/api/dev/commercehub/m1d/refunds/${refundId}/execute-online-before-payout`,
        {
            idempotencyKey,
            reason: 'Gate 3R online before-payout refund certification',
            administratorConfirmation: true,
        },
    )

    if (
        result.refundId.toLowerCase() !== refundId.toLowerCase()
        || result.idempotencyKey !== idempotencyKey
        || result.executionPath !== 'OnlineBeforePayout'
        || result.evidenceType !== 'DevelopmentSimulatedOnlineCustomerRefund'
        || result.isDevelopmentSimulation !== true
        || result.externalMoneyMovementConfirmed !== false
        || result.transactionReference !== `DEV-SIM:ONLINE-REFUND:${idempotencyKey}`
    ) {
        throw new Error('Development online refund simulation evidence was rejected.')
    }

    return getSuperAdminRefundDetails(refundId)
}

export async function requeueGate3RAccountingOutbox(): Promise<DevelopmentAccountingOutboxRequeueResult> {
    if (!import.meta.env.DEV) {
        throw new Error('Development accounting outbox requeue is unavailable outside Development.')
    }

    const result = await superAdminApiClient.postRaw<DevelopmentAccountingOutboxRequeueResult, undefined>(
        `/api/debug/outbox/requeue/${GATE3R_ACCOUNTING_OUTBOX_MESSAGE_ID}`,
    )

    if (
        result.messageId?.toLowerCase() !== GATE3R_ACCOUNTING_OUTBOX_MESSAGE_ID
        || typeof result.wasRequeued !== 'boolean'
        || !result.timestamp
        || Number.isNaN(Date.parse(result.timestamp))
    ) {
        throw new Error('Development accounting outbox requeue response was rejected.')
    }

    return {
        messageId: result.messageId,
        wasRequeued: result.wasRequeued,
        timestamp: result.timestamp,
    }
}
