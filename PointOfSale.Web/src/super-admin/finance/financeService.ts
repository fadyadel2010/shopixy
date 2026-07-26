import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiMeta } from '../auth/superAdminAuth.types'

export type SuperAdminSortDirection = 'Asc' | 'Desc'

export type SuperAdminVendorFinanceSortField =
    | 'LastActivityAt'
    | 'VendorName'
    | 'CurrentBalance'
    | 'TotalEarnings'
    | 'TotalCommissions'
    | 'TotalRefunds'
    | 'TotalPayouts'
    | 'RefundRatio'
    | 'Status'

export type SuperAdminVendorFinanceStatus = 'ACTIVE' | 'AT_RISK' | 'SUSPENDED'

export type SuperAdminVendorSettlementFlow = 'VendorPaysPlatform' | 'PlatformPaysVendor'

export type CommerceHubFinancialDirection = 'Credit' | 'Debit'

export type CommerceHubFinancialTransactionType =
    | 'OrderPayment'
    | 'Commission'
    | 'Refund'
    | 'Payout'
    | 'Adjustment'
    | 'Settlement'

export interface SuperAdminFinanceBreakdown {
    transactionType: string
    totalCredits: number
    totalDebits: number
    netAmount: number
    count: number
}

export interface SuperAdminFinanceActivity {
    transactionId: string
    entityType: string
    entityId?: string | null
    financialBucket: string
    transactionType: string
    direction: string
    amount: number
    signedAmount: number
    runningBalance?: number | null
    currency: string
    referenceType: string
    referenceId: string
    description: string
    correlationId: string
    createdAtUtc: string
}

export interface SuperAdminFinanceOverview {
    cashBalance: number
    receivablesBalance: number
    payablesBalance: number
    netPosition: number
    platformBalance: number
    totalCommissions: number
    commissionReversals: number
    totalPayouts: number
    totalRefunds: number
    netProfit: number
    currency: string
    transactionsCount: number
    lastTransactionAtUtc?: string | null
    breakdown: SuperAdminFinanceBreakdown[]
    recentTransactions: SuperAdminFinanceActivity[]
}

export interface SuperAdminFinanceOverviewResult {
    metrics: SuperAdminFinanceOverview
    meta?: SuperAdminApiMeta
}

export interface SuperAdminFinanceReportQuery {
    fromDate?: string
    toDate?: string
}

export interface SuperAdminFinanceCashflowPoint {
    dateUtc: string
    onlinePaymentsIn: number
    vendorSettlementsIn: number
    cashInTotal: number
    refundsOut: number
    vendorPayoutsOut: number
    cashOutTotal: number
    netCashFlow: number
}

export interface SuperAdminFinanceCashflow {
    fromDateUtc?: string | null
    toDateUtc?: string | null
    onlinePaymentsIn: number
    vendorSettlementsIn: number
    cashInTotal: number
    refundsOut: number
    vendorPayoutsOut: number
    cashOutTotal: number
    netCashFlow: number
    currency: string
    transactionsCount: number
    series: SuperAdminFinanceCashflowPoint[]
}

export interface SuperAdminFinanceProfitAndLoss {
    fromDateUtc?: string | null
    toDateUtc?: string | null
    revenue: number
    refundLosses: number
    netProfit: number
    currency: string
    transactionsCount: number
}

export interface SuperAdminFinanceReconciliation {
    status: string
    platformNetBalance: number
    vendorsNetBalance: number
    totalTransactions: number
    missingIdempotencyKeyCount: number
    missingCorrelationIdCount: number
    duplicateIdempotencyKeyCount: number
    duplicateReferenceConstraintCount: number
    missingPairCount: number
    duplicateEntryCount: number
    correlationIntegrityMismatchCount: number
    payableMismatchCount: number
    receivableMismatchCount: number
    orphanTransactionsCount: number
    orderFlowMismatchCount: number
    refundFlowMismatchCount: number
    payoutFlowMismatchCount: number
    adjustmentFlowMismatchCount: number
    multiCurrencyReferenceCount: number
    invalidEntityScopeCount: number
    executedAtUtc: string
}

export interface SuperAdminVendorFinanceListItem {
    vendorId: string
    vendorName: string
    vendorSlug?: string | null
    contactEmail?: string | null
    currency: string
    currentBalance: number
    amountOwedByVendor: number
    totalEarnings: number
    totalCommissions: number
    totalRefunds: number
    totalPayouts: number
    refundRatio: number
    lastActivityAt?: string | null
    transactionsCount: number
    status: SuperAdminVendorFinanceStatus
    isAtRisk: boolean
    isSuspended: boolean
    isGracePeriodActive: boolean
    isNearThreshold: boolean
    debtThresholdAmount: number
    gracePeriodDays: number
    thresholdCrossedAtUtc?: string | null
    graceDeadlineUtc?: string | null
    hasHighRefundRatio: boolean
    isInactive: boolean
}

export interface SuperAdminVendorFinanceFilters {
    page: number
    pageSize: number
    search?: string
    negativeBalanceOnly?: boolean
    highRefundRatioOnly?: boolean
    inactiveOnly?: boolean
    inactiveDays?: number
    highRefundRatioThreshold?: number
    sortBy: SuperAdminVendorFinanceSortField
    sortDirection: SuperAdminSortDirection
}

export interface SuperAdminVendorFinanceMeta extends SuperAdminApiMeta {
    totalPages?: number
    sortBy?: string
    sortDirection?: string
    highRefundRatioThreshold?: number
    inactiveDays?: number
}

export interface SuperAdminVendorFinanceResult {
    vendors: SuperAdminVendorFinanceListItem[]
    meta?: SuperAdminVendorFinanceMeta
}

export interface SuperAdminVendorFinanceAuditItem {
    transactionId: string
    vendorId: string
    actionType: string
    actorId?: string | null
    direction: CommerceHubFinancialDirection
    amount: number
    signedAmount: number
    currency: string
    reason: string
    description: string
    correlationId: string
    referenceId: string
    referenceType: string
    createdAtUtc: string
}

export interface SuperAdminVendorFinanceAuditQuery {
    page?: number
    pageSize?: number
}

export interface SuperAdminVendorFinanceAuditResult {
    timeline: SuperAdminVendorFinanceAuditItem[]
    meta?: SuperAdminVendorFinanceMeta
}

export interface SuperAdminVendorFinanceAlertItem {
    vendorId: string
    vendorName: string
    severity: 'WARNING' | 'CRITICAL'
    alertType: 'DEBT_OUTSTANDING' | 'DEBT_NEAR_THRESHOLD' | 'SUSPENDED_DEBT' | 'HIGH_REFUND_RATIO' | string
    message: string
    currency: string
    currentBalance: number
    amountOwedByVendor: number
    refundRatio: number
    isGracePeriodActive: boolean
    graceDeadlineUtc?: string | null
    lastActivityAt?: string | null
}

export interface SuperAdminVendorFinanceAlertsQuery {
    page?: number
    pageSize?: number
    highRefundRatioThreshold?: number
    inactiveDays?: number
}

export interface SuperAdminVendorFinanceAlertsResult {
    alerts: SuperAdminVendorFinanceAlertItem[]
    meta?: SuperAdminVendorFinanceMeta
}

export interface SuperAdminVendorStatement {
    entityType: string
    entityId?: string | null
    currency: string
    fromDateUtc?: string | null
    toDateUtc?: string | null
    appliedTransactionTypes: string[]
    currentBalance: number
    filteredCredits: number
    filteredDebits: number
    openingBalance: number
    closingBalance: number
    page: number
    pageSize: number
    totalCount: number
    hasMore: boolean
    transactions: SuperAdminFinanceActivity[]
}

export interface SuperAdminVendorStatementQuery {
    page?: number
    pageSize?: number
    fromDate?: string
    toDate?: string
    transactionTypes?: CommerceHubFinancialTransactionType[]
}

export interface SuperAdminVendorAdjustmentRequest {
    amount: number
    direction: CommerceHubFinancialDirection
    currency?: string
    idempotencyKey: string
    correlationId?: string
    reason: string
}

export interface SuperAdminVendorSettlementRequest {
    amount: number
    flow: SuperAdminVendorSettlementFlow
    currency?: string
    idempotencyKey: string
    correlationId?: string
    reason: string
}

export interface SuperAdminVendorFinancialAction {
    vendorId: string
    vendorName: string
    actionType: string
    vendorDirection: CommerceHubFinancialDirection
    settlementFlow?: SuperAdminVendorSettlementFlow | null
    amount: number
    currency: string
    reason: string
    referenceId: string
    referenceType: string
    idempotencyKey: string
    correlationId: string
    createdAtUtc: string
    createdBy?: string | null
}

export interface SuperAdminVendorFinancialActionResult {
    action: SuperAdminVendorFinancialAction
    vendor: SuperAdminVendorFinanceListItem
}

export type SuperAdminFinancialTrendGroupBy = 'Day' | 'Month'
export type SuperAdminPromoAnalyticsSortField =
    | 'PromoCode'
    | 'UsageCount'
    | 'PromoCost'
    | 'RevenueGenerated'
    | 'NetImpact'
    | 'LastUsedAt'

export type SuperAdminVendorPerformanceSortField =
    | 'VendorName'
    | 'TotalSales'
    | 'GrossCommission'
    | 'PromoCost'
    | 'RefundRate'
    | 'TotalRefundAmount'
    | 'NetContribution'
    | 'OrdersCount'

export interface SuperAdminFinancialDashboardFilter {
    fromDate?: string
    toDate?: string
    vendorSearch?: string
    promoCode?: string
}

export interface SuperAdminFinancialOverview {
    fromDateUtc: string
    toDateUtc: string
    currency: string
    baseCurrencyCode: string
    exchangeRateToBase?: number | null
    totalGrossCommission: number
    totalPromoCost: number
    totalNetCommission: number
    totalRefundAmount: number
    refundImpactOnRevenue: number
    netRevenue: number
    totalOrdersCount: number
    averageOrderValue: number
    refundedOrdersCount: number
    refundRate: number
    orderScope: string
    appliedVendorFilter?: string | null
    appliedPromoCode?: string | null
    dataSource: string
    isEventuallyConsistent: boolean
    consistencyModel: string
    dataFreshnessNote: string
    generatedAtUtc: string
    aggregatedThroughUtc?: string | null
}

export interface SuperAdminFinancialTrendPoint {
    bucketStartUtc: string
    grossCommission: number
    promoCost: number
    netCommission: number
    commissionReversals: number
    netRevenue: number
    refundAmount: number
    ordersCount: number
}

export interface SuperAdminFinancialTrends {
    fromDateUtc: string
    toDateUtc: string
    currency: string
    baseCurrencyCode: string
    exchangeRateToBase?: number | null
    groupBy: SuperAdminFinancialTrendGroupBy
    dataSource: string
    isEventuallyConsistent: boolean
    consistencyModel: string
    dataFreshnessNote: string
    generatedAtUtc: string
    aggregatedThroughUtc?: string | null
    points: SuperAdminFinancialTrendPoint[]
}

export interface SuperAdminPromoAnalyticsItem {
    promoCodeId?: string | null
    promoCode: string
    isActive: boolean
    totalUsageCount: number
    totalPromoCost: number
    revenueGenerated: number
    commissionReversals: number
    netImpact: number
    averageDiscountPerUse: number
    conversionRate?: number | null
    lastUsedAtUtc?: string | null
    currency: string
}

export interface SuperAdminPromoAnalyticsQuery extends SuperAdminFinancialDashboardFilter {
    page: number
    pageSize: number
    sortBy: SuperAdminPromoAnalyticsSortField
    sortDirection: SuperAdminSortDirection
}

export interface SuperAdminPromoAnalyticsResult {
    items: SuperAdminPromoAnalyticsItem[]
    meta?: SuperAdminApiMeta & {
        totalPages?: number
        sortBy?: string
        sortDirection?: string
    }
}

export interface SuperAdminVendorPerformanceItem {
    vendorId: string
    vendorName: string
    vendorSlug?: string | null
    contactEmail?: string | null
    ordersCount: number
    totalSales: number
    grossCommission: number
    promoCost: number
    commissionReversals: number
    totalRefundAmount: number
    refundedOrdersCount: number
    refundRate: number
    netContribution: number
    currency: string
    lastOrderAtUtc?: string | null
}

export interface SuperAdminVendorPerformanceQuery extends SuperAdminFinancialDashboardFilter {
    page: number
    pageSize: number
    sortBy: SuperAdminVendorPerformanceSortField
    sortDirection: SuperAdminSortDirection
}

export interface SuperAdminVendorPerformanceResult {
    items: SuperAdminVendorPerformanceItem[]
    meta?: SuperAdminApiMeta & {
        totalPages?: number
        sortBy?: string
        sortDirection?: string
    }
}

export interface SuperAdminRefundTrendPoint {
    bucketStartUtc: string
    refundAmount: number
    refundCount: number
    promoReversalAmount: number
}

export interface SuperAdminRefundVendorItem {
    vendorId: string
    vendorName: string
    refundAmount: number
    refundCount: number
    promoReversalAmount: number
    currency: string
}

export interface SuperAdminRefundItem {
    orderItemId?: string | null
    subOrderId: string
    itemLabel: string
    vendorName: string
    refundAmount: number
    refundCount: number
    refundedQuantity: number
    currency: string
}

export interface SuperAdminRefundAlert {
    severity: string
    alertType: string
    message: string
    bucketStartUtc?: string | null
}

export interface SuperAdminRefundAnalysis {
    fromDateUtc: string
    toDateUtc: string
    currency: string
    baseCurrencyCode: string
    exchangeRateToBase?: number | null
    groupBy: SuperAdminFinancialTrendGroupBy
    totalRefundAmount: number
    totalRefundCount: number
    refundedOrdersCount: number
    refundRate: number
    promoReversalAmount: number
    dataSource: string
    isEventuallyConsistent: boolean
    consistencyModel: string
    dataFreshnessNote: string
    generatedAtUtc: string
    aggregatedThroughUtc?: string | null
    points: SuperAdminRefundTrendPoint[]
    topRefundedVendors: SuperAdminRefundVendorItem[]
    topRefundedItems: SuperAdminRefundItem[]
    alerts: SuperAdminRefundAlert[]
}

export interface SuperAdminRefundAnalysisQuery extends SuperAdminFinancialDashboardFilter {
    groupBy: SuperAdminFinancialTrendGroupBy
}

export interface SuperAdminFinancialIntegrityIssue {
    severity: string
    issueType: string
    referenceType: string
    referenceId?: string | null
    summary: string
    expectedAmount?: number | null
    actualAmount?: number | null
    currency?: string | null
    occurredAtUtc?: string | null
}

export interface SuperAdminFinancialIntegrityCheck {
    status: string
    executedAtUtc: string
    scopeDescription: string
    supportsGlobalDebitCreditParity: boolean
    debitCreditParityStatus: string
    debitCreditParityNote?: string | null
    totalIssueCount: number
    orphanTransactionsCount: number
    orderFlowMismatchCount: number
    refundFlowMismatchCount: number
    payoutFlowMismatchCount: number
    adjustmentFlowMismatchCount: number
    payableMismatchCount: number
    receivableMismatchCount: number
    promoUsageWithoutLedgerCount: number
    promoLedgerWithoutUsageCount: number
    duplicatePromoUsageCount: number
    missingRefundReversalCount: number
    correlationIntegrityMismatchCount: number
    missingPairCount: number
    duplicateEntryCount: number
    missingIdempotencyKeyCount: number
    missingCorrelationIdCount: number
    multiCurrencyReferenceCount: number
    invalidEntityScopeCount: number
    issues: SuperAdminFinancialIntegrityIssue[]
}

export interface SuperAdminFinancialIntegrityQuery extends SuperAdminFinancialDashboardFilter {
    maxIssues?: number
}

interface SuperAdminVendorFinanceListResponse {
    vendors: SuperAdminVendorFinanceListItem[]
}

interface SuperAdminVendorFinanceAuditResponse {
    timeline: SuperAdminVendorFinanceAuditItem[]
}

interface SuperAdminVendorFinanceAlertsResponse {
    alerts: SuperAdminVendorFinanceAlertItem[]
}

interface SuperAdminPromoAnalyticsResponse {
    items: SuperAdminPromoAnalyticsItem[]
}

interface SuperAdminVendorPerformanceResponse {
    items: SuperAdminVendorPerformanceItem[]
}

function appendDate(params: URLSearchParams, key: string, value?: string, endOfDay = false): void {
    if (!value) {
        return
    }

    const suffix = endOfDay ? 'T23:59:59.999Z' : 'T00:00:00.000Z'
    params.set(key, `${value}${suffix}`)
}

function buildVendorFinanceParams(filters: Partial<SuperAdminVendorFinanceFilters>): URLSearchParams {
    const params = new URLSearchParams()

    if (typeof filters.page === 'number') {
        params.set('page', String(filters.page))
    }

    if (typeof filters.pageSize === 'number') {
        params.set('pageSize', String(filters.pageSize))
    }

    if (filters.search?.trim()) {
        params.set('search', filters.search.trim())
    }

    if (filters.negativeBalanceOnly) {
        params.set('negativeBalanceOnly', 'true')
    }

    if (filters.highRefundRatioOnly) {
        params.set('highRefundRatioOnly', 'true')
    }

    if (filters.inactiveOnly) {
        params.set('inactiveOnly', 'true')
    }

    if (typeof filters.inactiveDays === 'number') {
        params.set('inactiveDays', String(filters.inactiveDays))
    }

    if (typeof filters.highRefundRatioThreshold === 'number') {
        params.set('highRefundRatioThreshold', String(filters.highRefundRatioThreshold))
    }

    if (filters.sortBy) {
        params.set('sortBy', filters.sortBy)
    }

    if (filters.sortDirection) {
        params.set('sortDirection', filters.sortDirection)
    }

    return params
}

function buildStatementParams(query: SuperAdminVendorStatementQuery): URLSearchParams {
    const params = new URLSearchParams()

    if (typeof query.page === 'number') {
        params.set('page', String(query.page))
    }

    if (typeof query.pageSize === 'number') {
        params.set('pageSize', String(query.pageSize))
        params.set('size', String(query.pageSize))
    }

    appendDate(params, 'fromDateUtc', query.fromDate)
    appendDate(params, 'toDateUtc', query.toDate, true)

    query.transactionTypes?.forEach((type) => {
        params.append('transactionTypes', type)
    })

    return params
}

function buildAuditParams(query: SuperAdminVendorFinanceAuditQuery): URLSearchParams {
    const params = new URLSearchParams()

    if (typeof query.page === 'number') {
        params.set('page', String(query.page))
    }

    if (typeof query.pageSize === 'number') {
        params.set('pageSize', String(query.pageSize))
    }

    return params
}

function buildAlertsParams(query: SuperAdminVendorFinanceAlertsQuery): URLSearchParams {
    const params = new URLSearchParams()

    if (typeof query.page === 'number') {
        params.set('page', String(query.page))
    }

    if (typeof query.pageSize === 'number') {
        params.set('pageSize', String(query.pageSize))
    }

    if (typeof query.highRefundRatioThreshold === 'number') {
        params.set('highRefundRatioThreshold', String(query.highRefundRatioThreshold))
    }

    if (typeof query.inactiveDays === 'number') {
        params.set('inactiveDays', String(query.inactiveDays))
    }

    return params
}

function buildFinanceReportParams(query: SuperAdminFinanceReportQuery): URLSearchParams {
    const params = new URLSearchParams()

    appendDate(params, 'fromDateUtc', query.fromDate)
    appendDate(params, 'toDateUtc', query.toDate, true)

    return params
}

function buildFinancialDashboardParams(query: SuperAdminFinancialDashboardFilter): URLSearchParams {
    const params = new URLSearchParams()

    appendDate(params, 'fromDateUtc', query.fromDate)
    appendDate(params, 'toDateUtc', query.toDate, true)

    if (query.vendorSearch?.trim()) {
        params.set('vendorSearch', query.vendorSearch.trim())
    }

    if (query.promoCode?.trim()) {
        params.set('promoCode', query.promoCode.trim().toUpperCase())
    }

    return params
}

function buildTrendsParams(
    query: SuperAdminFinancialDashboardFilter & { groupBy: SuperAdminFinancialTrendGroupBy },
): URLSearchParams {
    const params = buildFinancialDashboardParams(query)
    params.set('groupBy', query.groupBy)
    return params
}

function buildPromoAnalyticsParams(query: SuperAdminPromoAnalyticsQuery): URLSearchParams {
    const params = buildFinancialDashboardParams(query)
    params.set('page', String(query.page))
    params.set('pageSize', String(query.pageSize))
    params.set('sortBy', query.sortBy)
    params.set('sortDirection', query.sortDirection)
    return params
}

function buildVendorPerformanceParams(query: SuperAdminVendorPerformanceQuery): URLSearchParams {
    const params = buildFinancialDashboardParams(query)
    params.set('page', String(query.page))
    params.set('pageSize', String(query.pageSize))
    params.set('sortBy', query.sortBy)
    params.set('sortDirection', query.sortDirection)
    return params
}

function buildIntegrityParams(query: SuperAdminFinancialIntegrityQuery): URLSearchParams {
    const params = buildFinancialDashboardParams(query)

    if (typeof query.maxIssues === 'number') {
        params.set('maxIssues', String(query.maxIssues))
    }

    return params
}

export async function getFinanceOverview(): Promise<SuperAdminFinanceOverviewResult> {
    const response = await superAdminApiClient.get<SuperAdminFinanceOverview>('/finance/ledger-overview')

    return {
        metrics: response.data,
        meta: response.meta,
    }
}

export async function getFinanceCashflow(
    query: SuperAdminFinanceReportQuery,
): Promise<SuperAdminFinanceCashflow> {
    const params = buildFinanceReportParams(query)
    const suffix = params.toString()
    const response = await superAdminApiClient.get<SuperAdminFinanceCashflow>(
        `/finance/cashflow${suffix ? `?${suffix}` : ''}`,
    )

    return response.data
}

export async function getFinanceProfitAndLoss(
    query: SuperAdminFinanceReportQuery,
): Promise<SuperAdminFinanceProfitAndLoss> {
    const params = buildFinanceReportParams(query)
    const suffix = params.toString()
    const response = await superAdminApiClient.get<SuperAdminFinanceProfitAndLoss>(
        `/finance/pnl${suffix ? `?${suffix}` : ''}`,
    )

    return response.data
}

export async function getFinanceReconciliation(): Promise<SuperAdminFinanceReconciliation> {
    const response = await superAdminApiClient.get<SuperAdminFinanceReconciliation>('/finance/reconciliation')
    return response.data
}

export async function getFinancialOverview(
    query: SuperAdminFinancialDashboardFilter,
): Promise<SuperAdminFinancialOverview> {
    const params = buildFinancialDashboardParams(query)
    const suffix = params.toString()
    const response = await superAdminApiClient.get<SuperAdminFinancialOverview>(
        `/finance/overview${suffix ? `?${suffix}` : ''}`,
    )

    return response.data
}

export async function getFinancialTrends(
    query: SuperAdminFinancialDashboardFilter & { groupBy: SuperAdminFinancialTrendGroupBy },
): Promise<SuperAdminFinancialTrends> {
    const params = buildTrendsParams(query)
    const suffix = params.toString()
    const response = await superAdminApiClient.get<SuperAdminFinancialTrends>(
        `/finance/trends${suffix ? `?${suffix}` : ''}`,
    )

    return response.data
}

export async function getFinancialPromos(
    query: SuperAdminPromoAnalyticsQuery,
): Promise<SuperAdminPromoAnalyticsResult> {
    const params = buildPromoAnalyticsParams(query)
    const response = await superAdminApiClient.get<SuperAdminPromoAnalyticsResponse>(
        `/finance/promos?${params.toString()}`,
    )

    return {
        items: response.data.items,
        meta: response.meta,
    }
}

export async function getFinancialVendors(
    query: SuperAdminVendorPerformanceQuery,
): Promise<SuperAdminVendorPerformanceResult> {
    const params = buildVendorPerformanceParams(query)
    const response = await superAdminApiClient.get<SuperAdminVendorPerformanceResponse>(
        `/finance/vendors?${params.toString()}`,
    )

    return {
        items: response.data.items,
        meta: response.meta,
    }
}

export async function getFinancialRefunds(
    query: SuperAdminRefundAnalysisQuery,
): Promise<SuperAdminRefundAnalysis> {
    const params = buildTrendsParams(query)
    const suffix = params.toString()
    const response = await superAdminApiClient.get<SuperAdminRefundAnalysis>(
        `/finance/refunds${suffix ? `?${suffix}` : ''}`,
    )

    return response.data
}

export async function getFinancialIntegrityCheck(
    query: SuperAdminFinancialIntegrityQuery,
): Promise<SuperAdminFinancialIntegrityCheck> {
    const params = buildIntegrityParams(query)
    const suffix = params.toString()
    const response = await superAdminApiClient.get<SuperAdminFinancialIntegrityCheck>(
        `/finance/integrity-check${suffix ? `?${suffix}` : ''}`,
    )

    return response.data
}

export async function getSuperAdminVendorFinance(
    filters: SuperAdminVendorFinanceFilters,
): Promise<SuperAdminVendorFinanceResult> {
    const params = buildVendorFinanceParams(filters)
    const response = await superAdminApiClient.get<SuperAdminVendorFinanceListResponse>(
        `/vendors/finance?${params.toString()}`,
    )

    return {
        vendors: response.data.vendors,
        meta: response.meta as SuperAdminVendorFinanceMeta | undefined,
    }
}

export async function getSuperAdminVendorFinanceAlerts(
    query: SuperAdminVendorFinanceAlertsQuery,
): Promise<SuperAdminVendorFinanceAlertsResult> {
    const params = buildAlertsParams(query)
    const response = await superAdminApiClient.get<SuperAdminVendorFinanceAlertsResponse>(
        `/vendors/alerts?${params.toString()}`,
    )

    return {
        alerts: response.data.alerts,
        meta: response.meta as SuperAdminVendorFinanceMeta | undefined,
    }
}

export async function getSuperAdminVendorStatement(
    vendorId: string,
    query: SuperAdminVendorStatementQuery,
): Promise<SuperAdminVendorStatement> {
    const params = buildStatementParams(query)
    const response = await superAdminApiClient.get<SuperAdminVendorStatement>(
        `/vendors/${vendorId}/statement?${params.toString()}`,
    )

    return response.data
}

export async function getSuperAdminVendorAuditTimeline(
    vendorId: string,
    query: SuperAdminVendorFinanceAuditQuery,
): Promise<SuperAdminVendorFinanceAuditResult> {
    const params = buildAuditParams(query)
    const response = await superAdminApiClient.get<SuperAdminVendorFinanceAuditResponse>(
        `/vendors/${vendorId}/audit?${params.toString()}`,
    )

    return {
        timeline: response.data.timeline,
        meta: response.meta as SuperAdminVendorFinanceMeta | undefined,
    }
}

export async function downloadSuperAdminVendorStatementCsv(
    vendorId: string,
    query: SuperAdminVendorStatementQuery,
): Promise<Blob> {
    const params = buildStatementParams(query)
    return superAdminApiClient.getBlob(`/api/super-admin/vendors/${vendorId}/statement/export?${params.toString()}`)
}

export async function createSuperAdminVendorAdjustment(
    vendorId: string,
    payload: SuperAdminVendorAdjustmentRequest,
): Promise<SuperAdminVendorFinancialActionResult> {
    const response = await superAdminApiClient.post<SuperAdminVendorFinancialActionResult>(
        `/vendors/${vendorId}/adjustment`,
        payload,
    )

    return response.data
}

export async function createSuperAdminVendorSettlement(
    vendorId: string,
    payload: SuperAdminVendorSettlementRequest,
): Promise<SuperAdminVendorFinancialActionResult> {
    const response = await superAdminApiClient.post<SuperAdminVendorFinancialActionResult>(
        `/vendors/${vendorId}/settlement`,
        payload,
    )

    return response.data
}
