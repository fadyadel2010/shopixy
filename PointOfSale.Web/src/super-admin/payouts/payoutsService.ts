import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'
import {
    requireSuperAdminPayoutExecutionPresentation,
    requireSuperAdminPayoutQueueFinancialAuthority,
} from './payoutFinancialAuthority'

export interface SuperAdminVendorPayoutMethodChoice {
    id: string
    methodType: string
    destinationValue: string
    isDefault: boolean
    isVerified: boolean
}

export interface SuperAdminVendorPayoutQueueItem {
    vendorId: string
    vendorName: string
    currency: string
    totalPayable: number
    eligibleAmount: number
    holdbackAmount: number
    completedPayoutsTotal: number
    pendingPayoutsTotal: number
    lastOrderAt?: string | null
    defaultPayoutMethodId?: string | null
    defaultPayoutMethodType?: string | null
    defaultPayoutDestinationValue?: string | null
    payoutMethods: SuperAdminVendorPayoutMethodChoice[]
    riskFlags: string[]
    canRecordCompletedPayout: boolean
    executionBlockedCode?: string | null
    executionBlockedReason?: string | null
}

export interface SuperAdminVendorPayoutQueueCurrencySummary {
    currency: string
    totalPayableAmount: number
    totalEligibleAmount: number
    totalHeldAmount: number
}

export interface SuperAdminExecuteVendorPayoutRequest {
    amount: number
    payoutMethodId: string
    transferReference?: string
    externalReferenceId?: string
    bankTransactionId?: string
    idempotencyKey: string
}

export interface SuperAdminVendorPayoutQueueMeta extends SuperAdminApiMeta {
    page?: number
    pageSize?: number
    totalCount?: number
    totalPages?: number
}

export interface SuperAdminPayoutDto {
    payoutId: string
    tenantId: string
    payoutMethodId?: string | null
    amount: number
    currency: string
    status: string
    method: string
    destinationValueSnapshot?: string | null
    reference?: string | null
    processedBy?: string | null
    idempotencyKey: string
    createdAtUtc: string
    processedAtUtc?: string | null
}

export interface SuperAdminVendorPayoutQueueResult {
    payouts: SuperAdminVendorPayoutQueueItem[]
    currencySummaries: SuperAdminVendorPayoutQueueCurrencySummary[]
    hasCurrencyMix: boolean
    aggregateCurrency?: string | null
    totalPayableAmount: number
    totalEligibleAmount: number
    totalHeldAmount: number
    meta?: SuperAdminVendorPayoutQueueMeta
}

export function createVendorPayoutIdempotencyKey(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID()
    }

    return `vendor-payout-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

export async function getSuperAdminPayoutQueue(page: number, pageSize: number): Promise<SuperAdminVendorPayoutQueueResult> {
    const params = new URLSearchParams({
        page: String(page),
        pageSize: String(pageSize),
    })
    const response = await superAdminApiClient.get<{
        payouts: SuperAdminVendorPayoutQueueItem[]
        currencySummaries?: SuperAdminVendorPayoutQueueCurrencySummary[]
        hasCurrencyMix?: boolean
        aggregateCurrency?: string | null
        totalPayableAmount?: number
        totalEligibleAmount?: number
        totalHeldAmount?: number
    }>(`/payouts?${params.toString()}`)

    return requireSuperAdminPayoutQueueFinancialAuthority({
        payouts: response.data.payouts,
        currencySummaries: response.data.currencySummaries,
        hasCurrencyMix: response.data.hasCurrencyMix,
        aggregateCurrency: response.data.aggregateCurrency,
        totalPayableAmount: response.data.totalPayableAmount,
        totalEligibleAmount: response.data.totalEligibleAmount,
        totalHeldAmount: response.data.totalHeldAmount,
        meta: response.meta,
    })
}

export async function executeSuperAdminVendorPayout(
    vendorId: string,
    payload: SuperAdminExecuteVendorPayoutRequest,
): Promise<SuperAdminPayoutDto> {
    const response = await superAdminApiClient.post<SuperAdminPayoutDto>(`/payouts/${vendorId}/execute`, payload)
    return requireSuperAdminPayoutExecutionPresentation(response.data, vendorId, payload.amount)
}
