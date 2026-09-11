import { describe, expect, it } from 'vitest'
import {
    deriveCurrentPaymentMethodSelection,
    deriveVendorEarningsDashboardPresentation,
    deriveVendorFinanceStatementPresentation,
    deriveVendorFinancialDashboardPresentation,
    deriveVendorPaymentProofPresentation,
    deriveVendorPaymentsDashboardPresentation,
    deriveVendorPayoutMethodsPresentation,
} from './commerceHubVendorFinancialParticipationAuthority'

const currentUser = {
    id: '11111111-1111-4111-8111-111111111111',
    tenantId: '22222222-2222-4222-8222-222222222222',
}

const foreignVendorId = '33333333-3333-4333-8333-333333333333'

const financeDashboard = {
    vendorId: currentUser.tenantId,
    currentBalance: 1200,
    amountOwedByVendor: 100,
    debtStatus: 'ACTIVE',
    isAtRisk: false,
    isSuspendedFromNewOrders: false,
    isGracePeriodActive: false,
    isNearThreshold: false,
    hardLimitAmount: 5000,
    gracePeriodDays: 7,
    thresholdCrossedAtUtc: null,
    graceDeadlineUtc: null,
    minimumPaymentToRestoreOrdering: 0,
    totalSettlementPaymentsToPlatform: 20,
    totalEarnings: 200,
    totalCommissions: 300,
    grossCommissions: 320,
    commissionReversals: 20,
    totalRefunds: 50,
    totalPayouts: 200,
    currency: 'EGP',
    transactionsCount: 4,
    lastTransactionAtUtc: '2026-06-16T10:00:00Z',
    recentRefundImpacts: [
        {
            vendorId: currentUser.tenantId,
            refundId: '44444444-4444-4444-8444-444444444444',
            impactType: 'DebtIncrease',
            amount: 50,
            signedDebtAmount: 50,
            currency: 'EGP',
            description: 'Refund debt',
            correlationId: 'corr-1',
            createdAtUtc: '2026-06-16T10:00:00Z',
        },
    ],
    recentSettlements: [
        {
            settlementId: '45454545-4545-4454-8454-454545454545',
            vendorId: currentUser.tenantId,
            settlementType: 'SettlementPayment',
            direction: 'Credit',
            amount: 20,
            signedAmount: 20,
            currency: 'EGP',
            idempotencyKey: 'backend-settlement-1',
            externalReference: 'backend-reference-1',
            createdAtUtc: '2026-06-16T10:00:00Z',
        },
    ],
}

const earningsDashboard = {
    vendorId: currentUser.tenantId,
    pendingTotal: 0,
    availableTotal: 0,
    paidOutTotal: 200,
    inPayoutTotal: 0,
    reversedTotal: 0,
    totalEarnings: 200,
    totalPayable: 700,
    eligiblePayoutAmount: 400,
    holdbackAmount: 300,
    completedPayoutsTotal: 200,
    pendingPayoutsTotal: 100,
    currency: 'EGP',
    lastPayoutAtUtc: '2026-06-16T10:00:00Z',
    riskFlags: ['NoDefaultPayoutMethod'],
    payoutHistory: [
        {
            payoutId: '55555555-5555-4555-8555-555555555555',
            tenantId: currentUser.tenantId,
            amount: 200,
            currency: 'EGP',
            status: 'Completed',
            method: 'BankTransfer',
            createdAtUtc: '2026-06-16T10:00:00Z',
            processedAtUtc: '2026-06-16T11:00:00Z',
            items: [
                {
                    tenantId: currentUser.tenantId,
                    payoutId: '55555555-5555-4555-8555-555555555555',
                    payoutItemId: '5a5a5a5a-5a5a-45a5-85a5-5a5a5a5a5a5a',
                    earningId: '12121212-1212-4121-8121-121212121212',
                    subOrderId: '23232323-2323-4232-8232-232323232323',
                    payoutId: '55555555-5555-4555-8555-555555555555',
                    orderNumber: 'CH-1001',
                    amount: 200,
                },
            ],
        },
    ],
    items: [
        {
            tenantId: currentUser.tenantId,
            earningId: '12121212-1212-4121-8121-121212121212',
            subOrderId: '23232323-2323-4232-8232-232323232323',
            originalEarningId: null,
            refundId: null,
            payoutId: null,
            orderNumber: 'CH-1001',
            amount: 200,
            currency: 'EGP',
            status: 'PaidOut',
            createdAt: '2026-06-16T10:00:00Z',
            availableAtUtc: '2026-06-16T10:30:00Z',
            paidOutAtUtc: '2026-06-16T11:00:00Z',
            reversedAtUtc: null,
        },
    ],
}

const paymentsDashboard = {
    vendorId: currentUser.tenantId,
    amountOwedByVendor: 100,
    currency: 'EGP',
    debtStatus: 'ACTIVE',
    canSubmitPayment: true,
    paymentMethods: [
        {
            id: '66666666-6666-4666-8666-666666666666',
            name: 'Platform bank',
            destinationValue: 'EG123',
            instructions: 'Transfer with proof',
            isActive: true,
            createdAtUtc: '2026-06-16T10:00:00Z',
        },
    ],
    paymentHistory: [
        {
            paymentRequestId: '77777777-7777-4777-8777-777777777777',
            paymentMethodId: '66666666-6666-4666-8666-666666666666',
            amount: 100,
            currency: 'EGP',
            status: 'PendingVerification',
            methodName: 'Platform bank',
            destinationValue: 'EG123',
            instructions: 'Transfer with proof',
            createdAtUtc: '2026-06-16T10:00:00Z',
            hasProof: true,
        },
    ],
}

const statement = {
    entityType: 'Vendor',
    entityId: currentUser.tenantId,
    currency: 'EGP',
    appliedTransactionTypes: ['Commission'],
    currentBalance: 1200,
    filteredCredits: 300,
    filteredDebits: 0,
    openingBalance: 900,
    closingBalance: 1200,
    page: 1,
    pageSize: 25,
    totalCount: 1,
    hasMore: false,
    transactions: [
        {
            transactionId: '88888888-8888-4888-8888-888888888888',
            entityType: 'Vendor',
            entityId: currentUser.tenantId,
            financialBucket: 'Receivable',
            transactionType: 'Commission',
            direction: 'Credit',
            amount: 300,
            signedAmount: 300,
            runningBalance: 1200,
            currency: 'EGP',
            referenceType: 'Order',
            referenceId: '99999999-9999-4999-8999-999999999999',
            description: 'Commission',
            correlationId: 'corr-2',
            createdAtUtc: '2026-06-16T10:00:00Z',
        },
    ],
}

describe('CommerceHub vendor financial participation authority', () => {
    it('accepts current backend financial dashboards and rejects fabricated balances', () => {
        expect(deriveVendorFinancialDashboardPresentation(financeDashboard, currentUser)).toMatchObject({
            vendorId: currentUser.tenantId,
            currentBalance: 1200,
        })

        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            currentBalance: 'fabricated',
        }, currentUser)).toBeNull()
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            vendorId: foreignVendorId,
        }, currentUser)).toBeNull()
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            recentRefundImpacts: [{ ...financeDashboard.recentRefundImpacts[0], vendorId: foreignVendorId }],
        }, currentUser)).toBeNull()
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            recentSettlements: [{ ...financeDashboard.recentSettlements[0], vendorId: foreignVendorId }],
        }, currentUser)).toBeNull()
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            currency: 'USD',
            recentRefundImpacts: [{ ...financeDashboard.recentRefundImpacts[0], currency: 'USD' }],
            recentSettlements: [{ ...financeDashboard.recentSettlements[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            recentSettlements: [{ ...financeDashboard.recentSettlements[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
    })

    it('rejects debt and governance contradictions instead of showing healthy financial state', () => {
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            debtStatus: 'HEALTHY_BY_FRONTEND',
        }, currentUser)).toBeNull()
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            isSuspendedFromNewOrders: 'false',
        }, currentUser)).toBeNull()
    })

    it('accepts the canonical at-risk debt state on finance and repayment dashboards', () => {
        expect(deriveVendorFinancialDashboardPresentation({
            ...financeDashboard,
            debtStatus: 'AT_RISK',
            isAtRisk: true,
            amountOwedByVendor: 132,
        }, currentUser)).toMatchObject({
            debtStatus: 'AT_RISK',
            amountOwedByVendor: 132,
        })
        expect(deriveVendorPaymentsDashboardPresentation({
            ...paymentsDashboard,
            debtStatus: 'AT_RISK',
            amountOwedByVendor: 132,
        }, currentUser)).toMatchObject({
            debtStatus: 'AT_RISK',
            amountOwedByVendor: 132,
        })
    })

    it('derives earnings, payout methods, and payout history only from current backend authority', () => {
        expect(deriveVendorEarningsDashboardPresentation(earningsDashboard, currentUser)).toMatchObject({
            vendorId: currentUser.tenantId,
            eligiblePayoutAmount: 400,
        })
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            riskFlags: ['FrontendHealthy'],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            payoutHistory: [{ ...earningsDashboard.payoutHistory[0], tenantId: foreignVendorId }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            payoutHistory: [{
                ...earningsDashboard.payoutHistory[0],
                items: [{ ...earningsDashboard.payoutHistory[0].items[0], earningId: 'frontend-created' }],
            }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            items: [{ ...earningsDashboard.items[0], tenantId: foreignVendorId }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            payoutHistory: [{
                ...earningsDashboard.payoutHistory[0],
                items: [{
                    ...earningsDashboard.payoutHistory[0].items[0],
                    payoutId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
                }],
            }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            payoutHistory: [{
                ...earningsDashboard.payoutHistory[0],
                items: [{
                    ...earningsDashboard.payoutHistory[0].items[0],
                    earningId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
                }],
            }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            items: [{ ...earningsDashboard.items[0], status: 'FrontendAvailable' }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            paidOutTotal: 0,
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            eligiblePayoutAmount: 701,
            holdbackAmount: -1,
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            holdbackAmount: 299,
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            payoutHistory: [{ ...earningsDashboard.payoutHistory[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            currency: 'USD',
            payoutHistory: [{
                ...earningsDashboard.payoutHistory[0],
                currency: 'USD',
            }],
            items: [{
                ...earningsDashboard.items[0],
                currency: 'USD',
            }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            items: [{ ...earningsDashboard.items[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            items: [{ ...earningsDashboard.items[0], status: 'InPayout', payoutId: null }],
        }, currentUser)).toBeNull()
        expect(deriveVendorEarningsDashboardPresentation({
            ...earningsDashboard,
            items: [{ ...earningsDashboard.items[0], status: 'PaidOut', paidOutAtUtc: null }],
        }, currentUser)).toBeNull()

        const methods = deriveVendorPayoutMethodsPresentation([
            {
                id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
                vendorId: currentUser.tenantId,
                methodType: 'BankTransfer',
                destinationValue: 'EG123',
                isDefault: true,
                isVerified: false,
                createdAtUtc: '2026-06-16T10:00:00Z',
            },
        ], currentUser)

        expect(methods).toHaveLength(1)
        expect(deriveVendorPayoutMethodsPresentation([
            { ...methods[0], vendorId: foreignVendorId },
        ], currentUser)).toEqual([])
    })

    it('keeps payment submission and selected methods bound to accepted backend rows', () => {
        const accepted = deriveVendorPaymentsDashboardPresentation(paymentsDashboard, currentUser)
        expect(accepted?.canSubmitPayment).toBe(true)
        expect(deriveCurrentPaymentMethodSelection(
            '66666666-6666-4666-8666-666666666666',
            accepted?.paymentMethods,
        )).toMatchObject({ name: 'Platform bank' })
        expect(deriveCurrentPaymentMethodSelection(
            'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            accepted?.paymentMethods,
        )).toBeNull()
        expect(deriveVendorPaymentsDashboardPresentation({
            ...paymentsDashboard,
            canSubmitPayment: 'true',
        }, currentUser)).toBeNull()
        expect(deriveVendorPaymentsDashboardPresentation({
            ...paymentsDashboard,
            currency: 'USD',
            paymentHistory: [{ ...paymentsDashboard.paymentHistory[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
    })

    it('rejects foreign or malformed statement authority and transaction replay', () => {
        expect(deriveVendorFinanceStatementPresentation(statement, currentUser)).toMatchObject({
            entityId: currentUser.tenantId,
            totalCount: 1,
        })
        expect(deriveVendorFinanceStatementPresentation({
            ...statement,
            entityId: foreignVendorId,
        }, currentUser)).toBeNull()
        expect(deriveVendorFinanceStatementPresentation({
            ...statement,
            transactions: [{ ...statement.transactions[0], direction: 'RepairedCredit' }],
        }, currentUser)).toBeNull()
        expect(deriveVendorFinanceStatementPresentation({
            ...statement,
            totalCount: 26,
            hasMore: false,
        }, currentUser)).toBeNull()
        expect(deriveVendorFinanceStatementPresentation({
            ...statement,
            transactions: Array.from({ length: 26 }, () => statement.transactions[0]),
            totalCount: 26,
            hasMore: true,
        }, currentUser)).toBeNull()
        expect(deriveVendorFinanceStatementPresentation({
            ...statement,
            transactions: [{ ...statement.transactions[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
        expect(deriveVendorFinanceStatementPresentation({
            ...statement,
            currency: 'USD',
            transactions: [{ ...statement.transactions[0], currency: 'USD' }],
        }, currentUser)).toBeNull()
    })

    it('requires backend proof DTO shape before opening payment proof presentation', () => {
        expect(deriveVendorPaymentProofPresentation({
            fileUrl: 'https://example.test/proof',
            contentType: 'image/png',
            fileName: 'proof.png',
            sizeBytes: 100,
            uploadedAtUtc: '2026-06-16T10:00:00Z',
        })).toMatchObject({ fileName: 'proof.png' })

        expect(deriveVendorPaymentProofPresentation({
            fileUrl: 'https://example.test/proof',
            contentType: 'image/png',
            fileName: 'proof.png',
            sizeBytes: -1,
            uploadedAtUtc: '2026-06-16T10:00:00Z',
        })).toBeNull()
    })
})
