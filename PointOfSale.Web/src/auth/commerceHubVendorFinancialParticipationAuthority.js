const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const FINANCE_TRANSACTION_TYPES = new Set([
    'OrderPayment',
    'Commission',
    'Refund',
    'Payout',
    'Adjustment',
    'Settlement',
])
const FINANCE_DIRECTIONS = new Set(['Credit', 'Debit'])
const FINANCE_ENTITY_TYPES = new Set(['Vendor'])
const REFUND_IMPACT_TYPES = new Set(['DebtIncrease', 'DebtDecrease'])
const SETTLEMENT_TYPES = new Set(['SettlementPayment', 'DebtClearance', 'AdminAdjustment'])
const SETTLEMENT_DIRECTIONS = new Set(['Credit', 'Debit'])
const DEBT_STATUSES = new Set(['ACTIVE', 'AT_RISK', 'SUSPENDED', 'GRACE_PERIOD'])
const PAYMENT_STATUSES = new Set(['Approved', 'Rejected', 'PendingVerification'])
const PAYOUT_STATUSES = new Set(['Pending', 'Processing', 'Completed', 'Failed', 'ReconciliationRequired'])
const EARNING_STATUSES = new Set(['Pending', 'Available', 'PaidOut', 'InPayout', 'Reversed'])
const PAYOUT_METHOD_TYPES = new Set(['BankTransfer', 'VodafoneCash', 'InstaPay'])
const PAYOUT_RISK_FLAGS = new Set([
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

const normalizeString = (value) => (typeof value === 'string' ? value.trim() : '')
const numberFields = (value, fields) => fields.every((field) => Number.isFinite(Number(value?.[field])))
const booleanFields = (value, fields) => fields.every((field) => typeof value?.[field] === 'boolean')
const optionalDate = (value) => value === undefined || value === null || isValidDate(value)
const isValidDate = (value) => {
    const text = normalizeString(value)
    return Boolean(text) && !Number.isNaN(new Date(text).getTime())
}
const sameGuid = (left, right) => normalizeString(left).toLowerCase() === normalizeString(right).toLowerCase()

const resolveVendorId = (value) => normalizeString(value?.vendorId ?? value?.VendorId ?? value?.tenantId ?? value?.TenantId ?? value?.entityId ?? value?.EntityId)

const resolveCurrentFinancialVendorScope = (currentUser) => {
    const tenantId = normalizeString(currentUser?.tenantId)
    const userId = normalizeString(currentUser?.id ?? currentUser?.userId)

    if (!GUID_PATTERN.test(tenantId) || !GUID_PATTERN.test(userId)) {
        return null
    }

    return { tenantId, userId }
}

const hasCurrentVendorAuthority = (value, currentUser) => {
    const current = resolveCurrentFinancialVendorScope(currentUser)
    const vendorId = resolveVendorId(value)
    return Boolean(current && GUID_PATTERN.test(vendorId) && sameGuid(vendorId, current.tenantId))
}

const hasAuthorityCurrency = (value) => normalizeString(value) === 'EGP'

const isNonNegative = (value) => Number.isFinite(Number(value)) && Number(value) >= 0
const isInteger = (value) => Number.isInteger(Number(value))
const hasDate = (value) => value !== undefined && value !== null && isValidDate(value)
const amountsMatch = (left, right) => Math.abs(Number(left) - Number(right)) < 0.000001

const validateRefundImpacts = (rows, currentUser) => Array.isArray(rows) && rows.every((row) => (
    hasCurrentVendorAuthority(row, currentUser) &&
    GUID_PATTERN.test(normalizeString(row?.refundId)) &&
    REFUND_IMPACT_TYPES.has(normalizeString(row?.impactType)) &&
    numberFields(row, ['amount', 'signedDebtAmount']) &&
    hasAuthorityCurrency(row?.currency) &&
    isValidDate(row?.createdAtUtc)
))

const validateRecentSettlements = (rows, currentUser) => Array.isArray(rows) && rows.every((row) => (
    hasCurrentVendorAuthority(row, currentUser) &&
    GUID_PATTERN.test(normalizeString(row?.settlementId)) &&
    SETTLEMENT_TYPES.has(normalizeString(row?.settlementType)) &&
    SETTLEMENT_DIRECTIONS.has(normalizeString(row?.direction)) &&
    numberFields(row, ['amount', 'signedAmount']) &&
    hasAuthorityCurrency(row?.currency) &&
    normalizeString(row?.idempotencyKey) &&
    normalizeString(row?.externalReference) &&
    isValidDate(row?.createdAtUtc)
))

const validateFinanceTransactions = (rows, currentUser) => Array.isArray(rows) && rows.every((row) => (
    GUID_PATTERN.test(normalizeString(row?.transactionId)) &&
    hasCurrentVendorAuthority(row, currentUser) &&
    FINANCE_ENTITY_TYPES.has(normalizeString(row?.entityType)) &&
    FINANCE_TRANSACTION_TYPES.has(normalizeString(row?.transactionType)) &&
    FINANCE_DIRECTIONS.has(normalizeString(row?.direction)) &&
    numberFields(row, ['amount', 'signedAmount']) &&
    (row?.runningBalance === undefined || row?.runningBalance === null || Number.isFinite(Number(row.runningBalance))) &&
    hasAuthorityCurrency(row?.currency) &&
    GUID_PATTERN.test(normalizeString(row?.referenceId)) &&
    isValidDate(row?.createdAtUtc)
))

const validatePayoutItems = (items, payoutId, currentUser) => Array.isArray(items) && items.every((item) => (
    hasCurrentVendorAuthority(item, currentUser) &&
    sameGuid(item?.payoutId, payoutId) &&
    GUID_PATTERN.test(normalizeString(item?.payoutItemId)) &&
    GUID_PATTERN.test(normalizeString(item?.earningId)) &&
    GUID_PATTERN.test(normalizeString(item?.subOrderId)) &&
    normalizeString(item?.orderNumber) &&
    isNonNegative(item?.amount)
))

const validatePayoutHistory = (rows, currentUser) => Array.isArray(rows) && rows.every((row) => {
    const payoutId = normalizeString(row?.payoutId)

    return GUID_PATTERN.test(payoutId) &&
        hasCurrentVendorAuthority(row, currentUser) &&
        isNonNegative(row?.amount) &&
        hasAuthorityCurrency(row?.currency) &&
        PAYOUT_STATUSES.has(normalizeString(row?.status)) &&
        isValidDate(row?.createdAtUtc) &&
        optionalDate(row?.processedAtUtc) &&
        validatePayoutItems(row?.items ?? [], payoutId, currentUser)
})

const hasValidEarningLifecycleEvidence = (item) => {
    const status = normalizeString(item?.status)
    const hasPayout = GUID_PATTERN.test(normalizeString(item?.payoutId))
    const available = hasDate(item?.availableAtUtc)
    const paidOut = hasDate(item?.paidOutAtUtc)
    const reversed = hasDate(item?.reversedAtUtc)

    switch (status) {
        case 'Pending':
            return !hasPayout && !available && !paidOut && !reversed
        case 'Available':
            return !hasPayout && available && !reversed
        case 'InPayout':
            return hasPayout && available && !reversed
        case 'PaidOut':
            // A completed positive earning has its active PayoutId cleared by the backend;
            // historical payout-item lineage is the durable completion evidence.
            return available && paidOut && !reversed
        case 'Reversed':
            return reversed
        default:
            return false
    }
}

const validateEarningItems = (items, currentUser) => Array.isArray(items) && items.every((item) => (
    hasCurrentVendorAuthority(item, currentUser) &&
    GUID_PATTERN.test(normalizeString(item?.earningId)) &&
    GUID_PATTERN.test(normalizeString(item?.subOrderId)) &&
    (item?.originalEarningId === undefined || item?.originalEarningId === null || GUID_PATTERN.test(normalizeString(item.originalEarningId))) &&
    (item?.refundId === undefined || item?.refundId === null || GUID_PATTERN.test(normalizeString(item.refundId))) &&
    (item?.payoutId === undefined || item?.payoutId === null || GUID_PATTERN.test(normalizeString(item.payoutId))) &&
    normalizeString(item?.orderNumber) &&
    isNonNegative(item?.amount) &&
    hasAuthorityCurrency(item?.currency) &&
    EARNING_STATUSES.has(normalizeString(item?.status)) &&
    isValidDate(item?.createdAt ?? item?.createdAtUtc) &&
    optionalDate(item?.availableAtUtc) &&
    optionalDate(item?.paidOutAtUtc) &&
    optionalDate(item?.reversedAtUtc) &&
    hasValidEarningLifecycleEvidence(item)
))

const validatePayoutEarningBindings = (payoutHistory, earningItems) => {
    const earningsById = new Map(earningItems.map((item) => [normalizeString(item.earningId).toLowerCase(), item]))

    return payoutHistory.every((payout) => payout.items.every((item) => {
        const earning = earningsById.get(normalizeString(item.earningId).toLowerCase())
        if (!earning || !sameGuid(earning.subOrderId, item.subOrderId)) {
            return false
        }

        const earningStatus = normalizeString(earning.status)
        const payoutStatus = normalizeString(payout.status)
        const activePayout = payoutStatus === 'Pending' || payoutStatus === 'Processing' || payoutStatus === 'ReconciliationRequired'

        if (activePayout) {
            return earningStatus === 'InPayout' && sameGuid(earning.payoutId, payout.payoutId)
        }

        if (payoutStatus === 'Completed') {
            return (earningStatus === 'PaidOut' || earningStatus === 'Available') &&
                (!earning.payoutId || sameGuid(earning.payoutId, payout.payoutId))
        }

        return payoutStatus === 'Failed' && earningStatus !== 'InPayout'
    })) && earningItems.every((earning) => {
        if (normalizeString(earning.status) !== 'InPayout') {
            return true
        }

        return payoutHistory.some((payout) =>
            (normalizeString(payout.status) === 'Pending' || normalizeString(payout.status) === 'Processing' || normalizeString(payout.status) === 'ReconciliationRequired') &&
            sameGuid(payout.payoutId, earning.payoutId) &&
            payout.items.some((item) => sameGuid(item.earningId, earning.earningId)))
    })
}

const validateEarningLifecycleTotals = (value, items) => {
    const totals = {
        Pending: 'pendingTotal',
        Available: 'availableTotal',
        PaidOut: 'paidOutTotal',
        InPayout: 'inPayoutTotal',
        Reversed: 'reversedTotal',
    }

    return Object.entries(totals).every(([status, field]) =>
        isNonNegative(value?.[field]) &&
        amountsMatch(value[field], items
            .filter((item) => normalizeString(item.status) === status)
            .reduce((total, item) => total + Number(item.amount), 0))) &&
        amountsMatch(value?.totalEarnings, items
            .filter((item) => Number(item.amount) > 0)
            .reduce((total, item) => total + Number(item.amount), 0))
}

// These are coherence checks for backend-issued evidence, not client-side
// earnings or payout calculations. A consolidated dashboard must never turn a
// contradictory payout breakdown or mixed-currency row set into presentation
// truth.
const validatePayoutReadinessTotals = (value) => (
    isNonNegative(value?.totalPayable) &&
    isNonNegative(value?.eligiblePayoutAmount) &&
    isNonNegative(value?.holdbackAmount) &&
    isNonNegative(value?.completedPayoutsTotal) &&
    isNonNegative(value?.pendingPayoutsTotal) &&
    Number(value.eligiblePayoutAmount) <= Number(value.totalPayable) &&
    amountsMatch(value.holdbackAmount, Number(value.totalPayable) - Number(value.eligiblePayoutAmount))
)

const hasSingleAuthorityCurrency = (currency, rows) =>
    Array.isArray(rows) && rows.every((row) => normalizeString(row?.currency) === normalizeString(currency))

const validateStatementPagination = (value) => {
    const transactions = value?.transactions ?? []
    const page = Number(value?.page)
    const pageSize = Number(value?.pageSize)
    const totalCount = Number(value?.totalCount)

    if (!Array.isArray(transactions) || transactions.length > pageSize || transactions.length > totalCount) {
        return false
    }

    // The response window is backend evidence.  Its metadata must describe that
    // window exactly so a forged/cache-replayed page cannot make additional rows
    // appear navigable as current vendor statement visibility.
    return Boolean(value?.hasMore) === (page * pageSize < totalCount)
}

const validatePaymentMethods = (rows) => Array.isArray(rows) && rows.every((row) => (
    GUID_PATTERN.test(normalizeString(row?.id)) &&
    normalizeString(row?.name) &&
    normalizeString(row?.destinationValue) &&
    normalizeString(row?.instructions) &&
    typeof row?.isActive === 'boolean' &&
    isValidDate(row?.createdAtUtc)
))

const validatePaymentHistory = (rows) => Array.isArray(rows) && rows.every((row) => (
    GUID_PATTERN.test(normalizeString(row?.paymentRequestId)) &&
    GUID_PATTERN.test(normalizeString(row?.paymentMethodId)) &&
    isNonNegative(row?.amount) &&
    hasAuthorityCurrency(row?.currency) &&
    PAYMENT_STATUSES.has(normalizeString(row?.status)) &&
    normalizeString(row?.methodName) &&
    normalizeString(row?.destinationValue) &&
    isValidDate(row?.createdAtUtc) &&
    optionalDate(row?.approvedAtUtc) &&
    optionalDate(row?.rejectedAtUtc) &&
    typeof row?.hasProof === 'boolean'
))

export const deriveVendorFinancialDashboardPresentation = (value, currentUser) => {
    const scopedValue = value
    if (!scopedValue || typeof scopedValue !== 'object' || Array.isArray(scopedValue) || !hasCurrentVendorAuthority(scopedValue, currentUser)) {
        return null
    }

    if (!numberFields(scopedValue, [
        'currentBalance',
        'amountOwedByVendor',
        'hardLimitAmount',
        'minimumPaymentToRestoreOrdering',
        'totalSettlementPaymentsToPlatform',
        'totalEarnings',
        'totalCommissions',
        'grossCommissions',
        'commissionReversals',
        'totalRefunds',
        'totalPayouts',
        'transactionsCount',
    ])) {
        return null
    }

    if (!booleanFields(scopedValue, ['isAtRisk', 'isSuspendedFromNewOrders', 'isGracePeriodActive', 'isNearThreshold'])) {
        return null
    }

    if (!hasAuthorityCurrency(scopedValue.currency) || !DEBT_STATUSES.has(normalizeString(scopedValue.debtStatus))) {
        return null
    }

    if (!isInteger(scopedValue.gracePeriodDays) || !isInteger(scopedValue.transactionsCount)) {
        return null
    }

    if (!optionalDate(scopedValue.thresholdCrossedAtUtc) || !optionalDate(scopedValue.graceDeadlineUtc) || !optionalDate(scopedValue.lastTransactionAtUtc)) {
        return null
    }

    if (!validateRefundImpacts(scopedValue.recentRefundImpacts ?? [], currentUser) ||
        !validateRecentSettlements(scopedValue.recentSettlements ?? [], currentUser)) {
        return null
    }

    return scopedValue
}

export const deriveVendorEarningsDashboardPresentation = (value, currentUser) => {
    const scopedValue = value
    if (!scopedValue || typeof scopedValue !== 'object' || Array.isArray(scopedValue) || !hasCurrentVendorAuthority(scopedValue, currentUser)) {
        return null
    }

    if (!numberFields(scopedValue, [
        'pendingTotal',
        'availableTotal',
        'paidOutTotal',
        'inPayoutTotal',
        'reversedTotal',
        'totalEarnings',
        'totalPayable',
        'eligiblePayoutAmount',
        'holdbackAmount',
        'completedPayoutsTotal',
        'pendingPayoutsTotal',
    ])) {
        return null
    }

    if (!hasAuthorityCurrency(scopedValue.currency) || !optionalDate(scopedValue.lastPayoutAtUtc)) {
        return null
    }

    if (!Array.isArray(scopedValue.riskFlags) || scopedValue.riskFlags.some((flag) => !PAYOUT_RISK_FLAGS.has(normalizeString(flag)))) {
        return null
    }

    const payoutHistory = scopedValue.payoutHistory ?? []
    const earningItems = scopedValue.items ?? []

    if (!validatePayoutHistory(payoutHistory, currentUser)) {
        return null
    }

    if (!validateEarningItems(earningItems, currentUser) ||
        !validateEarningLifecycleTotals(scopedValue, earningItems) ||
        !validatePayoutReadinessTotals(scopedValue) ||
        !hasSingleAuthorityCurrency(scopedValue.currency, earningItems) ||
        !hasSingleAuthorityCurrency(scopedValue.currency, payoutHistory) ||
        !validatePayoutEarningBindings(payoutHistory, earningItems)) {
        return null
    }

    return scopedValue
}

export const deriveVendorPayoutMethodsPresentation = (values, currentUser) => {
    if (!Array.isArray(values)) {
        return []
    }

    if (!values.every((method) => (
        hasCurrentVendorAuthority(method, currentUser) &&
        GUID_PATTERN.test(normalizeString(method?.id)) &&
        PAYOUT_METHOD_TYPES.has(normalizeString(method?.methodType)) &&
        normalizeString(method?.destinationValue) &&
        typeof method?.isDefault === 'boolean' &&
        typeof method?.isVerified === 'boolean' &&
        isValidDate(method?.createdAtUtc)
    ))) {
        return []
    }

    return values
}

export const deriveVendorPaymentsDashboardPresentation = (value, currentUser) => {
    const scopedValue = value
    if (!scopedValue || typeof scopedValue !== 'object' || Array.isArray(scopedValue) || !hasCurrentVendorAuthority(scopedValue, currentUser)) {
        return null
    }

    if (!numberFields(scopedValue, ['amountOwedByVendor']) || !hasAuthorityCurrency(scopedValue.currency)) {
        return null
    }

    if (!DEBT_STATUSES.has(normalizeString(scopedValue.debtStatus)) || typeof scopedValue.canSubmitPayment !== 'boolean') {
        return null
    }

    if (!validatePaymentMethods(scopedValue.paymentMethods ?? []) || !validatePaymentHistory(scopedValue.paymentHistory ?? [])) {
        return null
    }

    return scopedValue
}

export const deriveVendorFinanceStatementPresentation = (value, currentUser) => {
    const scopedValue = value
    if (!scopedValue || typeof scopedValue !== 'object' || Array.isArray(scopedValue) || !hasCurrentVendorAuthority(scopedValue, currentUser)) {
        return null
    }

    if (normalizeString(scopedValue.entityType) !== 'Vendor' || !hasAuthorityCurrency(scopedValue.currency)) {
        return null
    }

    if (!numberFields(scopedValue, ['currentBalance', 'filteredCredits', 'filteredDebits', 'openingBalance', 'closingBalance', 'page', 'pageSize', 'totalCount'])) {
        return null
    }

    if (!isInteger(scopedValue.page) || !isInteger(scopedValue.pageSize) || !isInteger(scopedValue.totalCount) || scopedValue.page < 1 || scopedValue.pageSize < 1 || scopedValue.totalCount < 0) {
        return null
    }

    if (typeof scopedValue.hasMore !== 'boolean' || !Array.isArray(scopedValue.appliedTransactionTypes)) {
        return null
    }

    if (scopedValue.appliedTransactionTypes.some((type) => !FINANCE_TRANSACTION_TYPES.has(normalizeString(type)))) {
        return null
    }

    if (!validateStatementPagination(scopedValue) ||
        !validateFinanceTransactions(scopedValue.transactions ?? [], currentUser) ||
        !hasSingleAuthorityCurrency(scopedValue.currency, scopedValue.transactions ?? [])) {
        return null
    }

    return scopedValue
}

export const deriveVendorPaymentProofPresentation = (value) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        return null
    }

    const fileUrl = normalizeString(value.fileUrl)
    const contentType = normalizeString(value.contentType)
    const fileName = normalizeString(value.fileName)

    if (!fileUrl || !contentType || !fileName || !isNonNegative(value.sizeBytes) || !isValidDate(value.uploadedAtUtc)) {
        return null
    }

    return value
}

export const deriveCurrentPaymentMethodSelection = (methodId, methods) => {
    const selectedId = normalizeString(methodId)
    if (!GUID_PATTERN.test(selectedId) || !Array.isArray(methods)) {
        return null
    }

    return methods.find((method) => sameGuid(method?.id, selectedId)) ?? null
}
