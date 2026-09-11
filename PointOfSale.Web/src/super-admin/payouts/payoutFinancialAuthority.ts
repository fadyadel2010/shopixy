import type {
    SuperAdminPayoutDto,
    SuperAdminActiveVendorPayout,
    SuperAdminVendorPayoutMethodChoice,
    SuperAdminVendorPayoutQueueCurrencySummary,
    SuperAdminVendorPayoutQueueItem,
    SuperAdminVendorPayoutQueueResult,
} from './payoutsService'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const MONEY_EPSILON = 0.005

const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const optionalText = (value: unknown) => value == null || typeof value === 'string'
const guid = (value: unknown): value is string => text(value) && GUID.test(value)
const money = (value: unknown) => typeof value === 'number' && Number.isFinite(value) && value >= 0
const optionalDate = (value: unknown) => value == null || (text(value) && Number.isFinite(Date.parse(value)))
const sameMoney = (left: number, right: number) => Math.abs(left - right) < MONEY_EPSILON
const authorityCurrency = (value: unknown) =>
    typeof value === 'string' && value.trim().toUpperCase() === COMMERCEHUB_AUTHORITY_CURRENCY

const PAYOUT_METHODS = new Set(['BankTransfer', 'VodafoneCash', 'InstaPay'])
const PAYOUT_STATUSES = new Set(['Pending', 'Processing', 'Completed', 'Failed', 'ReconciliationRequired'])
const ACTIVE_PAYOUT_STATUSES = new Set(['Pending', 'Processing', 'ReconciliationRequired'])
const RISK_FLAGS = new Set([
    'VendorSuspended',
    'GovernanceSuspended',
    'GovernanceBanned',
    'GovernanceFinancialLock',
    'DeliveryHold',
    'RefundWindowOpen',
    'SettlementDelayActive',
    'ExternalTransferReconciliationRequired',
    'StaleExternalTransferProcessing',
    'ActiveRefundRequest',
    'LargePayableDelay',
    'NoDefaultPayoutMethod',
    'LedgerCurrencySplit',
    'OperationalCurrencyMismatch',
    'PayoutCurrencyMismatch',
    'RefundCurrencyMismatch',
    'LedgerPayableDrift',
    'PayoutBlockedByAuthorityConflict',
])

function requirePayoutMethod(value: unknown): SuperAdminVendorPayoutMethodChoice {
    const method = value as SuperAdminVendorPayoutMethodChoice
    if (
        !guid(method?.id) ||
        !PAYOUT_METHODS.has(method.methodType) ||
        !text(method.destinationValue) ||
        typeof method.isDefault !== 'boolean' ||
        typeof method.isVerified !== 'boolean'
    ) {
        throw new Error('Super Admin payout financial authority rejected malformed payout-method evidence.')
    }

    return method
}

function requireActivePayout(value: unknown): SuperAdminActiveVendorPayout {
    const payout = value as SuperAdminActiveVendorPayout
    if (
        !guid(payout?.payoutId) ||
        !money(payout.amount) ||
        payout.amount <= 0 ||
        !authorityCurrency(payout.currency) ||
        !ACTIVE_PAYOUT_STATUSES.has(payout.status) ||
        !PAYOUT_METHODS.has(payout.method) ||
        !(payout.payoutMethodId == null || guid(payout.payoutMethodId)) ||
        !optionalText(payout.destinationValueSnapshot) ||
        !text(payout.idempotencyKey) ||
        !text(payout.externalTransferIdempotencyKey) ||
        !optionalText(payout.externalTransferHandoffToken) ||
        !optionalDate(payout.externalTransferHandoffCommittedAtUtc)
    ) {
        throw new Error('Super Admin payout financial authority rejected malformed committed handoff evidence.')
    }

    const hasCommittedHandoff = payout.status !== 'Pending'
    if (hasCommittedHandoff &&
        (!text(payout.externalTransferHandoffToken) || !payout.externalTransferHandoffCommittedAtUtc)) {
        throw new Error('Super Admin payout financial authority rejected missing committed handoff evidence.')
    }

    if (!hasCommittedHandoff && (payout.externalTransferHandoffToken || payout.externalTransferHandoffCommittedAtUtc)) {
        throw new Error('Super Admin payout financial authority rejected premature handoff evidence.')
    }

    return payout
}

function requireQueueItem(value: unknown): SuperAdminVendorPayoutQueueItem {
    const item = value as SuperAdminVendorPayoutQueueItem
    if (
        !guid(item?.vendorId) ||
        !text(item.vendorName) ||
        !authorityCurrency(item.currency) ||
        !money(item.totalPayable) ||
        !money(item.eligibleAmount) ||
        !money(item.holdbackAmount) ||
        !money(item.completedPayoutsTotal) ||
        !money(item.pendingPayoutsTotal) ||
        !optionalDate(item.lastOrderAt) ||
        !Array.isArray(item.payoutMethods) ||
        !Array.isArray(item.riskFlags) ||
        typeof item.canPreparePayout !== 'boolean' ||
        typeof item.canCompletePayout !== 'boolean' ||
        !optionalText(item.defaultPayoutMethodId) ||
        !optionalText(item.defaultPayoutMethodType) ||
        !optionalText(item.defaultPayoutDestinationValue) ||
        !optionalText(item.executionBlockedCode) ||
        !optionalText(item.executionBlockedReason)
    ) {
        throw new Error('Super Admin payout financial authority rejected malformed queue evidence.')
    }

    if (item.eligibleAmount - item.totalPayable > MONEY_EPSILON) {
        throw new Error('Super Admin payout financial authority rejected eligible/payable contradiction.')
    }

    if (!sameMoney(item.totalPayable, item.eligibleAmount + item.holdbackAmount)) {
        throw new Error('Super Admin payout financial authority rejected payable breakdown contradiction.')
    }

    const methods = item.payoutMethods.map(requirePayoutMethod)
    const activePayout = item.activePayout == null ? null : requireActivePayout(item.activePayout)
    const methodIds = new Set<string>()
    for (const method of methods) {
        const methodId = method.id.toLowerCase()
        if (methodIds.has(methodId)) {
            throw new Error('Super Admin payout financial authority rejected duplicate payout-method evidence.')
        }
        methodIds.add(methodId)
    }

    if (item.defaultPayoutMethodId && !methodIds.has(item.defaultPayoutMethodId.toLowerCase())) {
        throw new Error('Super Admin payout financial authority rejected default payout-method contradiction.')
    }

    if (item.defaultPayoutMethodType && !PAYOUT_METHODS.has(item.defaultPayoutMethodType)) {
        throw new Error('Super Admin payout financial authority rejected malformed default method evidence.')
    }

    if (item.riskFlags.some((flag) => !RISK_FLAGS.has(flag))) {
        throw new Error('Super Admin payout financial authority rejected malformed risk evidence.')
    }

    const hasPendingPayout = activePayout?.status === 'Pending'
    const pendingCanPrepare = !!(
        hasPendingPayout &&
        activePayout?.payoutMethodId &&
        activePayout.destinationValueSnapshot
    )
    if (item.canPreparePayout && !hasPendingPayout && (activePayout || item.eligibleAmount <= 0 || methods.length === 0)) {
        throw new Error('Super Admin payout financial authority rejected payout preparation without eligible method authority.')
    }

    if (item.canPreparePayout && item.canCompletePayout) {
        throw new Error('Super Admin payout financial authority rejected simultaneous preparation and completion authority.')
    }

    if (activePayout && (
        hasPendingPayout
            ? item.canCompletePayout || item.canPreparePayout !== pendingCanPrepare
            : item.canPreparePayout || !item.canCompletePayout
    )) {
        throw new Error('Super Admin payout financial authority rejected contradictory payout handoff authority.')
    }

    return { ...item, activePayout, payoutMethods: methods }
}

function requireCurrencySummary(value: unknown): SuperAdminVendorPayoutQueueCurrencySummary {
    const summary = value as SuperAdminVendorPayoutQueueCurrencySummary
    if (
        !authorityCurrency(summary?.currency) ||
        !money(summary.totalPayableAmount) ||
        !money(summary.totalEligibleAmount) ||
        !money(summary.totalHeldAmount)
    ) {
        throw new Error('Super Admin payout financial authority rejected malformed currency summary evidence.')
    }

    if (!sameMoney(summary.totalPayableAmount, summary.totalEligibleAmount + summary.totalHeldAmount)) {
        throw new Error('Super Admin payout financial authority rejected currency summary contradiction.')
    }

    return summary
}

export function requireSuperAdminPayoutQueueFinancialAuthority(value: unknown): SuperAdminVendorPayoutQueueResult {
    const result = value as SuperAdminVendorPayoutQueueResult
    if (
        !result ||
        typeof result !== 'object' ||
        !Array.isArray(result.payouts) ||
        !Array.isArray(result.currencySummaries) ||
        typeof result.hasCurrencyMix !== 'boolean' ||
        !money(result.totalPayableAmount) ||
        !money(result.totalEligibleAmount) ||
        !money(result.totalHeldAmount) ||
        !(result.aggregateCurrency == null || authorityCurrency(result.aggregateCurrency))
    ) {
        throw new Error('Super Admin payout financial authority rejected malformed queue response evidence.')
    }

    const vendorIds = new Set<string>()
    const payouts = result.payouts.map((payout) => {
        const accepted = requireQueueItem(payout)
        const vendorId = accepted.vendorId.toLowerCase()
        if (vendorIds.has(vendorId)) {
            throw new Error('Super Admin payout financial authority rejected duplicate vendor payout evidence.')
        }
        vendorIds.add(vendorId)
        return accepted
    })

    const currencySummaries = result.currencySummaries.map(requireCurrencySummary)
    const summaryTotalPayable = currencySummaries.reduce((sum, summary) => sum + summary.totalPayableAmount, 0)
    const summaryTotalEligible = currencySummaries.reduce((sum, summary) => sum + summary.totalEligibleAmount, 0)
    const summaryTotalHeld = currencySummaries.reduce((sum, summary) => sum + summary.totalHeldAmount, 0)

    if (
        !sameMoney(result.totalPayableAmount, summaryTotalPayable) ||
        !sameMoney(result.totalEligibleAmount, summaryTotalEligible) ||
        !sameMoney(result.totalHeldAmount, summaryTotalHeld)
    ) {
        throw new Error('Super Admin payout financial authority rejected queue summary contradiction.')
    }

    if (!result.hasCurrencyMix && currencySummaries.length === 1 && result.aggregateCurrency !== currencySummaries[0]?.currency) {
        throw new Error('Super Admin payout financial authority rejected aggregate currency contradiction.')
    }

    if (result.hasCurrencyMix && result.aggregateCurrency != null) {
        throw new Error('Super Admin payout financial authority rejected mixed-currency aggregate authority.')
    }

    return { ...result, payouts, currencySummaries }
}

export function requireCurrentPayoutExecutionAmount(amount: number, currentItem: SuperAdminVendorPayoutQueueItem): number {
    if (!money(amount) || amount <= 0 || amount - currentItem.eligibleAmount > MONEY_EPSILON) {
        throw new Error('Super Admin payout financial authority rejected execution amount outside current backend eligible authority.')
    }

    return amount
}

export function requireCurrentPayoutMethodSelection(
    payoutMethodId: string,
    currentItem: SuperAdminVendorPayoutQueueItem,
): SuperAdminVendorPayoutMethodChoice {
    if (!guid(payoutMethodId)) {
        throw new Error('Super Admin payout scope authority rejected malformed payout-method selection.')
    }

    const method = currentItem.payoutMethods.find((candidate) => candidate.id.toLowerCase() === payoutMethodId.toLowerCase())
    if (!method) {
        throw new Error('Super Admin payout scope authority rejected stale payout-method selection.')
    }

    return method
}

export function requireSuperAdminPayoutExecutionPresentation(
    value: unknown,
    expectedVendorId: string,
    expectedAmount: number,
    expectedStatuses: ReadonlyArray<'Processing' | 'Completed'>,
): SuperAdminPayoutDto {
    const payout = value as SuperAdminPayoutDto
    if (
        !guid(expectedVendorId) ||
        !payout ||
        typeof payout !== 'object' ||
        !guid(payout.payoutId) ||
        !guid(payout.tenantId) ||
        payout.tenantId.toLowerCase() !== expectedVendorId.toLowerCase() ||
        !money(payout.amount) ||
        !sameMoney(payout.amount, expectedAmount) ||
        !authorityCurrency(payout.currency) ||
        !PAYOUT_STATUSES.has(payout.status) ||
        !expectedStatuses.includes(payout.status as 'Processing' | 'Completed') ||
        !PAYOUT_METHODS.has(payout.method) ||
        !text(payout.idempotencyKey) ||
        !text(payout.createdAtUtc) ||
        !optionalDate(payout.processedAtUtc) ||
        !optionalText(payout.payoutMethodId) ||
        !optionalText(payout.destinationValueSnapshot) ||
        !optionalText(payout.reference) ||
        !optionalText(payout.externalReferenceId) ||
        !optionalText(payout.bankTransactionId) ||
        !optionalText(payout.externalTransferIdempotencyKey) ||
        !optionalText(payout.externalTransferHandoffToken) ||
        !optionalDate(payout.externalTransferHandoffCommittedAtUtc) ||
        !optionalText(payout.processedBy)
    ) {
        throw new Error('Super Admin payout financial authority rejected execution response evidence.')
    }

    if (
        !text(payout.externalTransferIdempotencyKey) ||
        !text(payout.externalTransferHandoffToken) ||
        !payout.externalTransferHandoffCommittedAtUtc
    ) {
        throw new Error('Super Admin payout financial authority rejected missing committed handoff evidence.')
    }

    return payout
}
