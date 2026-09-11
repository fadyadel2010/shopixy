import { describe, expect, it } from 'vitest'
import {
    requireCurrentPayoutExecutionAmount,
    requireCurrentPayoutMethodSelection,
    requireSuperAdminPayoutQueueFinancialAuthority,
} from './payoutFinancialAuthority'
import type { SuperAdminVendorPayoutQueueItem, SuperAdminVendorPayoutQueueResult } from './payoutsService'

const vendorId = '11111111-1111-4111-8111-111111111111'
const methodId = '22222222-2222-4222-8222-222222222222'

const queueItem: SuperAdminVendorPayoutQueueItem = {
    vendorId,
    vendorName: 'Vendor A',
    currency: 'EGP',
    totalPayable: 125,
    eligibleAmount: 100,
    holdbackAmount: 25,
    completedPayoutsTotal: 10,
    pendingPayoutsTotal: 5,
    lastOrderAt: '2026-06-21T12:00:00Z',
    defaultPayoutMethodId: methodId,
    defaultPayoutMethodType: 'BankTransfer',
    defaultPayoutDestinationValue: '****1234',
    payoutMethods: [{
        id: methodId,
        methodType: 'BankTransfer',
        destinationValue: '****1234',
        isDefault: true,
        isVerified: true,
    }],
    riskFlags: [],
    activePayout: null,
    canPreparePayout: true,
    canCompletePayout: false,
    executionBlockedCode: null,
    executionBlockedReason: null,
}

const queueResult: SuperAdminVendorPayoutQueueResult = {
    payouts: [queueItem],
    currencySummaries: [{
        currency: 'EGP',
        totalPayableAmount: 125,
        totalEligibleAmount: 100,
        totalHeldAmount: 25,
    }],
    hasCurrencyMix: false,
    aggregateCurrency: 'EGP',
    totalPayableAmount: 125,
    totalEligibleAmount: 100,
    totalHeldAmount: 25,
}

describe('payoutFinancialAuthority', () => {
    it('accepts coherent backend payout queue money', () => {
        expect(requireSuperAdminPayoutQueueFinancialAuthority(queueResult).payouts[0]?.eligibleAmount).toBe(100)
    })

    it('rejects fabricated missing queue totals instead of substituting zeroes', () => {
        expect(() => requireSuperAdminPayoutQueueFinancialAuthority({
            ...queueResult,
            totalPayableAmount: undefined,
        })).toThrow(/malformed queue response evidence/)
    })

    it('rejects payable breakdown contradictions', () => {
        expect(() => requireSuperAdminPayoutQueueFinancialAuthority({
            ...queueResult,
            payouts: [{ ...queueItem, holdbackAmount: 20 }],
        })).toThrow(/payable breakdown contradiction/)
    })

    it('rejects non-EGP payout queue currency evidence', () => {
        expect(() => requireSuperAdminPayoutQueueFinancialAuthority({
            ...queueResult,
            payouts: [{ ...queueItem, currency: 'USD' }],
        })).toThrow(/malformed queue evidence/)

        expect(() => requireSuperAdminPayoutQueueFinancialAuthority({
            ...queueResult,
            currencySummaries: [{ ...queueResult.currencySummaries[0]!, currency: 'EUR' }],
        })).toThrow(/malformed currency summary evidence/)

        expect(() => requireSuperAdminPayoutQueueFinancialAuthority({
            ...queueResult,
            aggregateCurrency: 'SAR',
        })).toThrow(/malformed queue response evidence/)
    })

    it('rejects execution amounts outside current eligible authority', () => {
        expect(() => requireCurrentPayoutExecutionAmount(100.01, queueItem)).toThrow(/execution amount outside current backend eligible authority/)
        expect(requireCurrentPayoutExecutionAmount(100, queueItem)).toBe(100)
    })

    it('rejects stale payout-method selection absent from current backend queue evidence', () => {
        expect(() => requireCurrentPayoutMethodSelection('33333333-3333-4333-8333-333333333333', queueItem)).toThrow(/stale payout-method selection/)
        expect(requireCurrentPayoutMethodSelection(methodId, queueItem).id).toBe(methodId)
    })

    it('accepts a committed processing payout only as completion authority', () => {
        const activeQueueResult: SuperAdminVendorPayoutQueueResult = {
            ...queueResult,
            payouts: [{
                ...queueItem,
                totalPayable: 0,
                eligibleAmount: 0,
                holdbackAmount: 0,
                activePayout: {
                    payoutId: '33333333-3333-4333-8333-333333333333',
                    amount: 100,
                    currency: 'EGP',
                    status: 'Processing',
                    payoutMethodId: methodId,
                    method: 'BankTransfer',
                    destinationValueSnapshot: '****1234',
                    idempotencyKey: 'prepare-idempotency',
                    externalTransferIdempotencyKey: 'transfer-intent',
                    externalTransferHandoffToken: 'handoff-token',
                    externalTransferHandoffCommittedAtUtc: '2026-06-21T12:00:00Z',
                },
                canPreparePayout: false,
                canCompletePayout: true,
            }],
            currencySummaries: [{
                currency: 'EGP',
                totalPayableAmount: 0,
                totalEligibleAmount: 0,
                totalHeldAmount: 0,
            }],
            totalPayableAmount: 0,
            totalEligibleAmount: 0,
            totalHeldAmount: 0,
        }

        const accepted = requireSuperAdminPayoutQueueFinancialAuthority(activeQueueResult).payouts[0]!
        expect(accepted.canCompletePayout).toBe(true)
        expect(accepted.canPreparePayout).toBe(false)
    })

    it('accepts backend-blocked preparation despite otherwise eligible queue evidence', () => {
        const blockedQueueResult: SuperAdminVendorPayoutQueueResult = {
            ...queueResult,
            payouts: [{
                ...queueItem,
                canPreparePayout: false,
                executionBlockedCode: 'PAYOUT_AUTHORITY_CONFLICT',
                executionBlockedReason: 'Financial authority checks blocked payout preparation.',
            }],
        }

        const accepted = requireSuperAdminPayoutQueueFinancialAuthority(blockedQueueResult).payouts[0]!
        expect(accepted.canPreparePayout).toBe(false)
        expect(accepted.executionBlockedCode).toBe('PAYOUT_AUTHORITY_CONFLICT')
    })
})
