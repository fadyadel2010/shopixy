import type {
    SuperAdminRefundDetails,
    SuperAdminRefundListItem,
    SuperAdminRefundsResult,
    SuperAdminRefundStatus,
    SuperAdminReturnDetails,
    SuperAdminReturnStatus,
} from './refundsService'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const CURRENCY = /^[A-Z]{3}$/
const REFUND_STATUSES = new Set<SuperAdminRefundStatus>([
    'Requested',
    'UnderReview',
    'Approved',
    'WaitingForReturn',
    'AwaitingPayout',
    'ReadyForExecution',
    'Rejected',
    'Cancelled',
    'Processing',
    'Paid',
    'Executed',
    'Completed',
    'Failed',
])
const PAYMENT_STATUSES = new Set([
    'Pending',
    'Authorized',
    'Collected',
    'Paid',
    'PartiallyPaid',
    'Failed',
    'Refunded',
    'PartiallyRefunded',
    'AwaitingVerification',
    'Rejected',
    'Settled',
    'CashOnDelivery',
    'Expired',
    'AmountMismatch',
])
const PAYMENT_RECORD_STATUSES = new Set([
    'Pending',
    'Paid',
    'Failed',
    'Ignored',
    'RefundRequired',
    'Refunded',
    'RefundFailed',
])
const REFUND_EXECUTION_STATUSES = new Set(['Pending', 'Processing', 'Completed', 'Failed'])
const REFUND_REASON_CODES = new Set(['DamagedItem', 'NotAsDescribed', 'WrongItem', 'MissingParts', 'QualityIssue', 'Other'])
const REFUND_PAYOUT_METHODS = new Set(['VodafoneCash', 'InstaPay', 'BankTransfer'])
const REFUND_MEDIA_TYPES = new Set(['image', 'video'])
const RETURN_STATUSES = new Set<SuperAdminReturnStatus>([
    'Pending',
    'PickupScheduled',
    'PickedUp',
    'InTransit',
    'DeliveredToVendor',
    'Confirmed',
    'Failed',
])
const REFUND_EXECUTION_TRIGGERS = new Set(['OnPickup', 'OnDelivery', 'OnVendorConfirmation'])

const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const optionalText = (value: unknown) => value == null || typeof value === 'string'
const guid = (value: unknown): value is string => text(value) && GUID.test(value)
const money = (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value >= 0
const signedMoney = (value: unknown) => typeof value === 'number' && Number.isFinite(value)
const positiveInt = (value: unknown) => typeof value === 'number' && Number.isInteger(value) && value > 0
const nonNegativeInt = (value: unknown) => typeof value === 'number' && Number.isInteger(value) && value >= 0
const date = (value: unknown) => text(value) && Number.isFinite(Date.parse(value))
const optionalDate = (value: unknown) => value == null || date(value)

function requireRefundMediaPresentation(media: unknown, mediaUrls: unknown): void {
    if (!Array.isArray(media) || !Array.isArray(mediaUrls) || mediaUrls.some((url) => !text(url))) {
        throw new Error('Super Admin refund visibility authority rejected malformed refund evidence.')
    }

    const ids = new Set<string>()
    for (const item of media) {
        const evidence = item as Record<string, unknown>
        if (
            !item ||
            typeof item !== 'object' ||
            !guid(evidence.mediaId) ||
            ids.has(evidence.mediaId) ||
            !text(evidence.url) ||
            !REFUND_MEDIA_TYPES.has(evidence.mediaType) ||
            !text(evidence.mimeType) ||
            !money(evidence.sizeBytes) ||
            !text(evidence.fileName)
        ) {
            throw new Error('Super Admin refund visibility authority rejected malformed refund evidence.')
        }

        ids.add(evidence.mediaId)
    }
}

export function requireSuperAdminReturnForCurrentRefund(
    value: unknown,
    currentRefund: Pick<SuperAdminRefundDetails, 'refundId' | 'customer' | 'item'>,
): SuperAdminReturnDetails {
    const returnRequest = value as SuperAdminReturnDetails
    if (
        !currentRefund ||
        !guid(currentRefund.refundId) ||
        !guid(currentRefund.customer?.customerId) ||
        !guid(currentRefund.item?.orderItemId) ||
        !guid(currentRefund.item?.vendorId) ||
        !returnRequest ||
        typeof returnRequest !== 'object' ||
        !guid(returnRequest.returnId) ||
        !guid(returnRequest.refundId) ||
        returnRequest.refundId.toLowerCase() !== currentRefund.refundId.toLowerCase() ||
        !guid(returnRequest.orderItemId) ||
        returnRequest.orderItemId.toLowerCase() !== currentRefund.item.orderItemId.toLowerCase() ||
        !guid(returnRequest.customerId) ||
        returnRequest.customerId.toLowerCase() !== currentRefund.customer.customerId.toLowerCase() ||
        !guid(returnRequest.vendorId) ||
        returnRequest.vendorId.toLowerCase() !== currentRefund.item.vendorId.toLowerCase() ||
        !RETURN_STATUSES.has(returnRequest.status) ||
        !REFUND_EXECUTION_TRIGGERS.has(returnRequest.refundExecutionTrigger) ||
        !optionalDate(returnRequest.pickupScheduledAt) ||
        !optionalDate(returnRequest.pickedUpAt) ||
        !optionalDate(returnRequest.inTransitAt) ||
        !optionalDate(returnRequest.deliveredAt) ||
        !optionalDate(returnRequest.confirmedAt) ||
        !optionalText(returnRequest.trackingNumber) ||
        !optionalText(returnRequest.courierName) ||
        !optionalText(returnRequest.notes) ||
        !date(returnRequest.createdAt) ||
        !optionalDate(returnRequest.updatedAt) ||
        typeof returnRequest.canSchedulePickup !== 'boolean' ||
        typeof returnRequest.canMarkPickedUp !== 'boolean' ||
        typeof returnRequest.canMarkInTransit !== 'boolean' ||
        typeof returnRequest.canMarkDelivered !== 'boolean' ||
        typeof returnRequest.canMarkFailed !== 'boolean' ||
        typeof returnRequest.canConfirmReceipt !== 'boolean' ||
        typeof returnRequest.isExecutionTriggerReached !== 'boolean'
    ) {
        throw new Error('Super Admin refund identity authority rejected malformed return/refund evidence.')
    }

    return returnRequest
}

export function requireSuperAdminReturnPresentation(
    value: unknown,
    expectedReturnId: string,
): SuperAdminReturnDetails {
    const returnRequest = value as SuperAdminReturnDetails
    if (
        !guid(expectedReturnId) ||
        !returnRequest ||
        typeof returnRequest !== 'object' ||
        !guid(returnRequest.returnId) ||
        returnRequest.returnId.toLowerCase() !== expectedReturnId.toLowerCase() ||
        !guid(returnRequest.refundId) ||
        !guid(returnRequest.orderItemId) ||
        !guid(returnRequest.customerId) ||
        !guid(returnRequest.vendorId) ||
        !RETURN_STATUSES.has(returnRequest.status) ||
        !REFUND_EXECUTION_TRIGGERS.has(returnRequest.refundExecutionTrigger) ||
        !optionalDate(returnRequest.pickupScheduledAt) ||
        !optionalDate(returnRequest.pickedUpAt) ||
        !optionalDate(returnRequest.inTransitAt) ||
        !optionalDate(returnRequest.deliveredAt) ||
        !optionalDate(returnRequest.confirmedAt) ||
        !optionalText(returnRequest.trackingNumber) ||
        !optionalText(returnRequest.courierName) ||
        !optionalText(returnRequest.notes) ||
        !date(returnRequest.createdAt) ||
        !optionalDate(returnRequest.updatedAt) ||
        typeof returnRequest.canSchedulePickup !== 'boolean' ||
        typeof returnRequest.canMarkPickedUp !== 'boolean' ||
        typeof returnRequest.canMarkInTransit !== 'boolean' ||
        typeof returnRequest.canMarkDelivered !== 'boolean' ||
        typeof returnRequest.canMarkFailed !== 'boolean' ||
        typeof returnRequest.canConfirmReceipt !== 'boolean' ||
        typeof returnRequest.isExecutionTriggerReached !== 'boolean'
    ) {
        throw new Error('Super Admin refund identity authority rejected malformed return evidence.')
    }

    return returnRequest
}

function requireRefundListItem(value: unknown): SuperAdminRefundListItem {
    const refund = value as SuperAdminRefundListItem
    if (
        !refund ||
        typeof refund !== 'object' ||
        !guid(refund.refundId) ||
        !guid(refund.orderId) ||
        !text(refund.orderNumber) ||
        !guid(refund.vendorId) ||
        !text(refund.vendorName) ||
        !guid(refund.customerId) ||
        !text(refund.customerName) ||
        !text(refund.productName) ||
        !positiveInt(refund.quantity) ||
        !money(refund.amount) ||
        !CURRENCY.test(refund.currency) ||
        !REFUND_STATUSES.has(refund.status) ||
        !date(refund.createdAt)
    ) {
        throw new Error('Super Admin refund lifecycle authority rejected malformed monitoring evidence.')
    }

    return refund
}

function requireRefundFinancialPresentation(refund: SuperAdminRefundDetails): void {
    const preview = refund.financialPreview
    if (
        !text(preview.paymentType) ||
        typeof preview.hasPayoutOccurred !== 'boolean' ||
        !money(preview.commissionReversalAmount) ||
        !money(preview.vendorEarningChange) ||
        typeof preview.gatewayRefundRequired !== 'boolean' ||
        (preview.paymentId != null && !guid(preview.paymentId)) ||
        !optionalText(preview.paymentProvider) ||
        (preview.paymentRecordStatus != null && !PAYMENT_RECORD_STATUSES.has(preview.paymentRecordStatus)) ||
        !money(preview.paymentRefundedAmountBefore) ||
        !money(preview.paymentRefundedAmountAfter) ||
        preview.paymentRefundedAmountAfter < preview.paymentRefundedAmountBefore ||
        !money(preview.vendorNetEarningsBefore) ||
        !money(preview.vendorNetEarningsAfter) ||
        typeof preview.hasPaidOutVendorEarnings !== 'boolean' ||
        typeof preview.hasPendingPayoutReservation !== 'boolean' ||
        !text(preview.executionPath) ||
        !signedMoney(preview.platformCashDelta) ||
        !signedMoney(preview.platformPayableDelta) ||
        !signedMoney(preview.vendorDebtDelta) ||
        !signedMoney(preview.commissionReceivableDelta) ||
        !money(preview.vendorPayableBefore) ||
        !money(preview.vendorPayableAfter) ||
        !money(preview.vendorDebtBefore) ||
        !money(preview.vendorDebtAfter) ||
        !text(preview.impactSummary)
    ) {
        throw new Error('Super Admin refund financial authority rejected malformed financial preview evidence.')
    }

    if (refund.execution != null) {
        const execution = refund.execution
        if (
            !guid(execution.executionId) ||
            !REFUND_PAYOUT_METHODS.has(execution.method) ||
            !text(execution.maskedAccountIdentifier) ||
            !text(execution.transactionReference) ||
            !text(execution.executedBy) ||
            !date(execution.executedAt) ||
            typeof execution.isSuccessful !== 'boolean' ||
            !optionalText(execution.failureReason) ||
            !guid(execution.financialRefundId) ||
            !REFUND_EXECUTION_STATUSES.has(execution.status) ||
            (execution.paymentId != null && !guid(execution.paymentId)) ||
            !optionalText(execution.gatewayStatus) ||
            !optionalText(execution.gatewayReference) ||
            typeof execution.isDevelopmentSimulation !== 'boolean' ||
            typeof execution.externalMoneyMovementConfirmed !== 'boolean' ||
            !money(execution.commissionReversalAmount) ||
            !money(execution.vendorEarningReversalAmount) ||
            typeof execution.isPostPayoutAdjustment !== 'boolean' ||
            !optionalDate(execution.processingStartedAtUtc) ||
            !optionalDate(execution.processedAtUtc)
        ) {
            throw new Error('Super Admin refund financial authority rejected malformed execution evidence.')
        }

        if (execution.isDevelopmentSimulation && execution.externalMoneyMovementConfirmed) {
            throw new Error('Super Admin refund financial authority rejected contradictory execution evidence.')
        }
    }
}

export function requireSuperAdminRefundsPresentation(value: unknown): SuperAdminRefundsResult {
    const result = value as SuperAdminRefundsResult
    if (!result || typeof result !== 'object' || !Array.isArray(result.refunds)) {
        throw new Error('Super Admin refund lifecycle authority rejected malformed monitoring evidence.')
    }

    const ids = new Set<string>()
    const refunds = result.refunds.map((refund) => {
        const accepted = requireRefundListItem(refund)
        if (ids.has(accepted.refundId)) {
            throw new Error('Super Admin refund lifecycle authority rejected duplicate monitoring evidence.')
        }

        ids.add(accepted.refundId)
        return accepted
    })

    return { ...result, refunds }
}

export function requireCurrentSuperAdminRefundSelection(
    refundId: string | null,
    refunds: SuperAdminRefundListItem[],
): SuperAdminRefundListItem | null {
    if (!refundId || !guid(refundId)) {
        return null
    }

    return refunds.find((refund) => refund.refundId.toLowerCase() === refundId.toLowerCase()) ?? null
}

export function requireSuperAdminRefundDetailsPresentation(
    value: unknown,
    expectedRefundId: string,
): SuperAdminRefundDetails {
    const refund = value as SuperAdminRefundDetails
    if (
        !guid(expectedRefundId) ||
        !refund ||
        typeof refund !== 'object' ||
        !guid(refund.refundId) ||
        refund.refundId.toLowerCase() !== expectedRefundId.toLowerCase() ||
        !REFUND_STATUSES.has(refund.status) ||
        !text(refund.reason) ||
        !REFUND_REASON_CODES.has(refund.reasonCode) ||
        !optionalText(refund.reasonText) ||
        !positiveInt(refund.quantity) ||
        !money(refund.unitPriceSnapshot) ||
        !money(refund.amount) ||
        Math.abs(refund.amount - refund.unitPriceSnapshot * refund.quantity) >= 0.005 ||
        !CURRENCY.test(refund.currency) ||
        !date(refund.createdAt) ||
        !optionalDate(refund.updatedAt) ||
        !optionalDate(refund.reviewedAtUtc) ||
        !optionalDate(refund.approvedAtUtc) ||
        !optionalDate(refund.rejectedAtUtc) ||
        !optionalDate(refund.processingStartedAtUtc) ||
        !optionalDate(refund.completedAtUtc) ||
        !optionalDate(refund.failedAtUtc) ||
        !nonNegativeInt(refund.executionAttempts) ||
        !refund.order ||
        !guid(refund.order.orderId) ||
        !text(refund.order.orderNumber) ||
        !PAYMENT_STATUSES.has(refund.order.paymentStatus) ||
        !money(refund.order.totalAmount) ||
        !money(refund.order.shippingTotal) ||
        !CURRENCY.test(refund.order.currency) ||
        !date(refund.order.createdAt) ||
        !refund.item ||
        !guid(refund.item.orderItemId) ||
        !guid(refund.item.subOrderId) ||
        !guid(refund.item.vendorId) ||
        !money(refund.item.unitPrice) ||
        !money(refund.item.totalPrice) ||
        !money(refund.item.platformCommissionAmount) ||
        !money(refund.item.vendorEarningAmount) ||
        !refund.financialPreview
    ) {
        throw new Error('Super Admin refund lifecycle authority rejected malformed detail evidence.')
    }

    if (
        !refund.customer ||
        !guid(refund.customer.customerId) ||
        !text(refund.customer.name) ||
        !text(refund.customer.email) ||
        !optionalText(refund.customer.phoneNumber) ||
        (refund.payout != null &&
            (!REFUND_PAYOUT_METHODS.has(refund.payout.method) ||
                !text(refund.payout.maskedAccountIdentifier) ||
                !optionalText(refund.payout.accountHolderName) ||
                typeof refund.payout.isVerified !== 'boolean'))
    ) {
        throw new Error('Super Admin refund visibility authority rejected malformed refund detail evidence.')
    }

    requireRefundMediaPresentation(refund.media, refund.mediaUrls)
    requireRefundFinancialPresentation(refund)

    if (refund.returnRequest != null) {
        requireSuperAdminReturnForCurrentRefund(refund.returnRequest, refund)
    }

    if ((refund.status === 'Paid' || refund.status === 'Executed' || refund.status === 'Completed') && !refund.execution) {
        throw new Error('Super Admin refund lifecycle authority rejected completed refund without execution evidence.')
    }

    if (refund.status === 'Failed' && !text(refund.failureReason) && !refund.execution?.failureReason) {
        throw new Error('Super Admin refund lifecycle authority rejected failed refund without failure evidence.')
    }

    if (!optionalText(refund.adminNote) || !optionalText(refund.failureReason)) {
        throw new Error('Super Admin refund lifecycle authority rejected malformed detail text evidence.')
    }

    return refund
}

export function requireSuperAdminRefundDetailsForCurrentSelection(
    value: SuperAdminRefundDetails,
    currentSelection: SuperAdminRefundListItem,
): SuperAdminRefundDetails {
    const refund = requireSuperAdminRefundDetailsPresentation(value, currentSelection.refundId)

    if (
        refund.order.orderId.toLowerCase() !== currentSelection.orderId.toLowerCase() ||
        refund.item.vendorId.toLowerCase() !== currentSelection.vendorId.toLowerCase() ||
        refund.customer.customerId.toLowerCase() !== currentSelection.customerId.toLowerCase() ||
        refund.currency !== currentSelection.currency ||
        Math.abs(refund.amount - currentSelection.amount) >= 0.005
    ) {
        throw new Error('Super Admin refund scope authority rejected detail/list scope contradiction.')
    }

    return refund
}
