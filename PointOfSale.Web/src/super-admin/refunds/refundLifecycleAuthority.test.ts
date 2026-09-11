import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
    requireCurrentSuperAdminRefundSelection,
    requireSuperAdminRefundDetailsForCurrentSelection,
    requireSuperAdminRefundDetailsPresentation,
    requireSuperAdminRefundsPresentation,
    requireSuperAdminReturnPresentation,
} from './refundLifecycleAuthority'

const refundId = '11111111-1111-4111-8111-111111111111'
const orderId = '22222222-2222-4222-8222-222222222222'
const vendorId = '33333333-3333-4333-8333-333333333333'
const customerId = '44444444-4444-4444-8444-444444444444'
const itemId = '55555555-5555-4555-8555-555555555555'
const subOrderId = '66666666-6666-4666-8666-666666666666'
const returnId = '77777777-7777-4777-8777-777777777777'

const listItem = {
    refundId,
    orderId,
    orderNumber: 'ORD-1',
    vendorId,
    vendorName: 'Vendor',
    customerId,
    customerName: 'Customer',
    productName: 'Product',
    quantity: 1,
    amount: 10,
    currency: 'EGP',
    status: 'Requested' as const,
    createdAt: '2030-01-01T00:00:00Z',
}

const detail = {
    ...listItem,
    reason: 'Damaged',
    reasonCode: 'DamagedItem' as const,
    mediaUrls: [],
    media: [],
    unitPriceSnapshot: 10,
    updatedAt: '2030-01-01T00:01:00Z',
    reviewedAtUtc: null,
    approvedAtUtc: null,
    rejectedAtUtc: null,
    processingStartedAtUtc: null,
    completedAtUtc: null,
    failedAtUtc: null,
    executionAttempts: 0,
    canApprove: true,
    canReject: true,
    canExecute: false,
    canRetry: false,
    order: {
        orderId,
        orderNumber: 'ORD-1',
        paymentMethodName: 'Card',
        paymentStatus: 'Collected',
        totalAmount: 20,
        shippingTotal: 0,
        currency: 'EGP',
        createdAt: '2030-01-01T00:00:00Z',
    },
    customer: {
        customerId,
        name: 'Customer',
        email: 'customer@example.test',
    },
    item: {
        orderItemId: itemId,
        subOrderId,
        vendorId,
        vendorName: 'Vendor',
        productName: 'Product',
        purchasedQuantity: 1,
        unitPrice: 10,
        totalPrice: 10,
        platformCommissionAmount: 1,
        vendorEarningAmount: 9,
        status: 'Delivered',
    },
    financialPreview: {
        paymentType: 'Online',
        hasPayoutOccurred: false,
        commissionReversalAmount: 1,
        vendorEarningChange: 9,
        gatewayRefundRequired: true,
        paymentId: null,
        paymentProvider: null,
        paymentRecordStatus: 'Paid',
        paymentRefundedAmountBefore: 0,
        paymentRefundedAmountAfter: 10,
        vendorNetEarningsBefore: 9,
        vendorNetEarningsAfter: 0,
        hasPaidOutVendorEarnings: false,
        hasPendingPayoutReservation: false,
        executionPath: 'OnlineBeforePayout',
        platformCashDelta: -10,
        platformPayableDelta: 0,
        vendorDebtDelta: 0,
        commissionReceivableDelta: -1,
        vendorPayableBefore: 9,
        vendorPayableAfter: 0,
        vendorDebtBefore: 0,
        vendorDebtAfter: 0,
        impactSummary: 'Current backend preview',
    },
}

const returnRequest = {
    returnId,
    refundId,
    orderItemId: itemId,
    customerId,
    vendorId,
    status: 'Pending' as const,
    refundExecutionTrigger: 'OnDelivery' as const,
    pickupScheduledAt: null,
    pickedUpAt: null,
    inTransitAt: null,
    deliveredAt: null,
    confirmedAt: null,
    trackingNumber: null,
    courierName: null,
    notes: null,
    createdAt: '2030-01-01T00:00:00Z',
    updatedAt: null,
    canSchedulePickup: true,
    canMarkPickedUp: false,
    canMarkInTransit: false,
    canMarkDelivered: false,
    canMarkFailed: true,
    canConfirmReceipt: false,
    isExecutionTriggerReached: false,
}

describe('Super Admin refund lifecycle authority', () => {
    it('rejects malformed or unsupported refund monitoring lifecycle evidence', () => {
        expect(() => requireSuperAdminRefundsPresentation({ refunds: [{ ...listItem, status: 'Fabricated' }] })).toThrow(/lifecycle authority rejected/i)
    })

    it('does not preserve a selected refund after current authority removes it', () => {
        expect(requireCurrentSuperAdminRefundSelection(refundId, [])).toBeNull()
    })

    it('rejects detail payment lifecycle contradictions before presentation', () => {
        expect(() => requireSuperAdminRefundDetailsPresentation({ ...detail, order: { ...detail.order, paymentStatus: 'Forged' } }, refundId)).toThrow(/detail evidence/i)
    })

    it('rejects malformed financial preview evidence before it reaches an admin financial surface', () => {
        expect(() => requireSuperAdminRefundDetailsPresentation({
            ...detail,
            financialPreview: { ...detail.financialPreview, platformCashDelta: Number.NaN },
        }, refundId)).toThrow(/financial authority rejected/i)
    })

    it('rejects execution evidence without a backend financial refund identity', () => {
        expect(() => requireSuperAdminRefundDetailsPresentation({
            ...detail,
            status: 'Completed',
            execution: {
                executionId: refundId,
                method: 'BankTransfer',
                maskedAccountIdentifier: '****1234',
                transactionReference: 'TX-1',
                executedBy: 'Admin',
                executedAt: '2030-01-01T00:02:00Z',
                isSuccessful: true,
                financialRefundId: 'not-a-financial-refund-id',
                status: 'Completed',
                commissionReversalAmount: 1,
                vendorEarningReversalAmount: 9,
                isPostPayoutAdjustment: false,
            },
        }, refundId)).toThrow(/financial authority rejected/i)
    })

    it('rejects malformed refund evidence before it reaches an admin visibility surface', () => {
        expect(() => requireSuperAdminRefundDetailsPresentation({
            ...detail,
            media: [{
                mediaId: refundId,
                url: 'https://evidence.example.test/refund.png',
                mediaType: 'document',
                mimeType: 'image/png',
                sizeBytes: 10,
                fileName: 'refund.png',
            }],
        }, refundId)).toThrow(/visibility authority rejected/i)
    })

    it('requires completed refund lifecycle to include execution evidence', () => {
        expect(() => requireSuperAdminRefundDetailsPresentation({ ...detail, status: 'Completed' }, refundId)).toThrow(/lifecycle authority rejected/i)
    })

    it('rejects selected refund details that contradict the current monitoring scope', () => {
        expect(() => requireSuperAdminRefundDetailsForCurrentSelection({
            ...detail,
            item: {
                ...detail.item,
                vendorId: '77777777-7777-4777-8777-777777777777',
            },
        }, listItem)).toThrow(/scope authority rejected/i)
    })

    it('allows backend lifecycle transitions without transferring refund scope', () => {
        expect(requireSuperAdminRefundDetailsForCurrentSelection({
            ...detail,
            status: 'Approved',
            canApprove: false,
            approvedAtUtc: '2030-01-01T00:02:00Z',
        }, listItem).status).toBe('Approved')
    })

    it('rejects embedded return evidence that contradicts refund identity', () => {
        expect(() => requireSuperAdminRefundDetailsPresentation({
            ...detail,
            returnRequest: {
                ...returnRequest,
                refundId: '88888888-8888-4888-8888-888888888888',
            },
        }, refundId)).toThrow(/refund identity authority rejected/i)
    })

    it('rejects standalone return responses that substitute return identity', () => {
        expect(() => requireSuperAdminReturnPresentation({
            ...returnRequest,
            returnId: '99999999-9999-4999-8999-999999999999',
        }, returnId)).toThrow(/refund identity authority rejected/i)
    })

    it('accepts vendor lifecycle grants as read-only monitoring evidence', () => {
        expect(requireSuperAdminReturnPresentation(returnRequest, returnId)).toBe(returnRequest)
    })

    it('freezes Super Admin returns to exact read-only targets', () => {
        const service = readFileSync(resolve(__dirname, 'refundsService.ts'), 'utf8')

        expect(service).toContain('return requireSuperAdminReturnPresentation(response.data, returnId)')
        expect(service).toContain('`/returns/${returnId}`')
        expect(service).not.toContain('`/returns/${returnId}/mark-failed`')
        expect(service).not.toContain('`/returns/${returnId}/override-status`')
    })

    it('freezes Super Admin refund mutations behind current backend lifecycle authority re-entry', () => {
        const page = readFileSync(resolve(__dirname, 'RefundsPage.tsx'), 'utf8')

        expect(page).toContain('async function requireCurrentRefundMutationAuthority()')
        expect(page).toContain('const refreshedRefunds = await refundsQuery.refetch()')
        expect(page).toContain('requireCurrentSuperAdminRefundSelection(selectedRefundId, refreshedRefunds.data.refunds)')
        expect(page).toContain('return getSuperAdminRefundDetails(currentRefund.refundId, currentRefund)')
        expect(page).toContain('if (!currentRefund.canApprove)')
        expect(page).toContain('if (!currentRefund.canReject)')
        expect(page).toContain('if (!currentRefund.canRetry)')
        expect(page).toContain('if (!currentRefund.canExecute || (!isRefundAwaitingPayout(currentRefund.status) && currentRefund.status !== \'Approved\'))')
        expect(page).toContain('const acceptedRefundDetails =')
        expect(page).toContain('refund={acceptedRefundDetails}')
        expect(page).toContain('executeModalOpen && acceptedRefundDetails')
        expect(page).not.toContain('ReturnTransitionModal')
    })

    it('does not expose Super Admin return mutation controls', () => {
        const page = readFileSync(resolve(__dirname, 'RefundsPage.tsx'), 'utf8')

        expect(page).not.toContain('onOverrideReturn')
        expect(page).not.toContain('onMarkReturnFailed')
        expect(page).not.toContain('returnMutation')
        expect(page).not.toContain('setReturnModalMode')
    })

    it('exposes canonical refund simulations only behind the Development frontend guard', () => {
        const page = readFileSync(resolve(__dirname, 'RefundsPage.tsx'), 'utf8')
        const service = readFileSync(resolve(__dirname, 'refundsService.ts'), 'utf8')

        expect(page).toContain("refund.financialPreview.executionPath === 'CashOnDeliveryDebtReduction'")
        expect(page).toContain("refund.financialPreview.executionPath === 'OnlineBeforePayout'")
        expect(page).toContain('No external money movement will be confirmed.')
        expect(page).toContain('superAdmin.refundsPage.actions.replayDevelopmentSimulation')
        expect(page).toContain('refund.execution?.isDevelopmentSimulation === true')
        expect(page).toContain('superAdmin.refundsPage.sections.payoutConfirmation.simulationCaption')
        expect(page.indexOf('const isDevelopmentSimulation = refund?.execution?.isDevelopmentSimulation === true'))
            .toBeGreaterThan(page.indexOf('function RefundDetailsDrawer({'))
        expect(page).toContain('Idempotency replay applied no additional financial effect.')
        expect(service).toContain("if (!import.meta.env.DEV)")
        expect(service).toContain('/execute-cod-debt-reduction')
        expect(service).toContain('/execute-online-before-payout')
        expect(service).toContain("result.evidenceType !== 'DevelopmentSimulatedCodCustomerRefund'")
        expect(service).toContain("result.evidenceType !== 'DevelopmentSimulatedOnlineCustomerRefund'")
        expect(service).toContain('result.isDevelopmentSimulation !== true')
        expect(service).toContain('result.externalMoneyMovementConfirmed !== false')
    })

    it('exposes only the bounded Gate 3R accounting requeue through authenticated Development UI', () => {
        const app = readFileSync(resolve(__dirname, '../../App.jsx'), 'utf8')
        const page = readFileSync(resolve(__dirname, 'RefundsPage.tsx'), 'utf8')
        const service = readFileSync(resolve(__dirname, 'refundsService.ts'), 'utf8')

        expect(app).toContain('<Route element={<SuperAdminQueryBoundary><SuperAdminProtectedRoute /></SuperAdminQueryBoundary>}>')
        expect(app).toContain('<SuperAdminCapabilityRoute capability="super-admin.refunds.manage" />')
        expect(page).toContain("const GATE3R_REFUND_ID = 'd9ac7cf0-3822-437c-9e66-e8c258c8b633'")
        expect(page).toContain('import.meta.env.DEV && refund.refundId.toLowerCase() === GATE3R_REFUND_ID')
        expect(page).toContain('window.confirm(')
        expect(page).toContain('disabled={isDevelopmentAccountingRequeuePending || Boolean(developmentAccountingRequeueResult)}')
        expect(page).toContain('setSuccessMessage(message)')
        expect(page).toContain('window.alert(message)')
        expect(service).toContain("export const GATE3R_ACCOUNTING_OUTBOX_MESSAGE_ID = '01a08680-3cff-7ef1-a6ff-58fa723f30f1'")
        expect(service).toContain('`/api/debug/outbox/requeue/${GATE3R_ACCOUNTING_OUTBOX_MESSAGE_ID}`')
        expect(service).toContain("if (!import.meta.env.DEV)")
        expect(service).not.toContain('/api/debug/outbox/process')
    })
})
