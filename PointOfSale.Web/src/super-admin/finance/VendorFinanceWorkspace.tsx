import { useDeferredValue, useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import {
    AlertTriangle,
    Download,
    Filter,
    History,
    Landmark,
    RefreshCw,
    Search,
    ShieldAlert,
    Wallet,
    X,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import {
    createSuperAdminVendorAdjustment,
    createSuperAdminVendorSettlement,
    downloadSuperAdminVendorStatementCsv,
    getSuperAdminVendorAuditTimeline,
    getSuperAdminVendorFinance,
    getSuperAdminVendorStatement,
    type CommerceHubFinancialDirection,
    type CommerceHubFinancialTransactionType,
    type SuperAdminSortDirection,
    type SuperAdminVendorAdjustmentRequest,
    type SuperAdminVendorFinanceAuditItem,
    type SuperAdminVendorFinanceFilters,
    type SuperAdminVendorFinanceListItem,
    type SuperAdminVendorFinanceSortField,
    type SuperAdminVendorSettlementFlow,
    type SuperAdminVendorSettlementRequest,
    type SuperAdminVendorStatement,
} from './financeService'

const DEFAULT_PAGE_SIZE = 20
const DEFAULT_STATEMENT_PAGE_SIZE = 25
const TRANSACTION_TYPE_OPTIONS: CommerceHubFinancialTransactionType[] = [
    'OrderPayment',
    'Commission',
    'Refund',
    'Payout',
    'Adjustment',
    'Settlement',
]
const TRANSACTION_TYPE_LABEL_KEYS: Record<CommerceHubFinancialTransactionType, string> = {
    OrderPayment: 'superAdmin.vendorFinanceWorkspace.transactionTypes.orderPayment',
    Commission: 'superAdmin.vendorFinanceWorkspace.transactionTypes.commission',
    Refund: 'superAdmin.vendorFinanceWorkspace.transactionTypes.refund',
    Payout: 'superAdmin.vendorFinanceWorkspace.transactionTypes.payout',
    Adjustment: 'superAdmin.vendorFinanceWorkspace.transactionTypes.adjustment',
    Settlement: 'superAdmin.vendorFinanceWorkspace.transactionTypes.settlement',
}

const SORT_FIELD_LABEL_KEYS: Record<SuperAdminVendorFinanceSortField, string> = {
    LastActivityAt: 'superAdmin.vendorFinanceWorkspace.sortFields.lastActivityAt',
    VendorName: 'superAdmin.vendorFinanceWorkspace.sortFields.vendorName',
    CurrentBalance: 'superAdmin.vendorFinanceWorkspace.sortFields.currentBalance',
    TotalEarnings: 'superAdmin.vendorFinanceWorkspace.sortFields.totalEarnings',
    TotalCommissions: 'superAdmin.vendorFinanceWorkspace.sortFields.totalCommissions',
    TotalRefunds: 'superAdmin.vendorFinanceWorkspace.sortFields.totalRefunds',
    TotalPayouts: 'superAdmin.vendorFinanceWorkspace.sortFields.totalPayouts',
    RefundRatio: 'superAdmin.vendorFinanceWorkspace.sortFields.refundRatio',
    Status: 'superAdmin.vendorFinanceWorkspace.sortFields.status',
}

const SORT_DIRECTION_LABEL_KEYS: Record<SuperAdminSortDirection, string> = {
    Desc: 'superAdmin.vendorFinanceWorkspace.sortDirections.desc',
    Asc: 'superAdmin.vendorFinanceWorkspace.sortDirections.asc',
}

const STATUS_LABEL_KEYS: Record<string, string> = {
    ACTIVE: 'superAdmin.vendorFinanceWorkspace.statuses.active',
    AT_RISK: 'superAdmin.vendorFinanceWorkspace.statuses.atRisk',
    SUSPENDED: 'superAdmin.vendorFinanceWorkspace.statuses.suspended',
}

const AUDIT_ACTION_LABEL_KEYS: Record<string, string> = {
    Settlement: 'superAdmin.vendorFinanceWorkspace.audit.actionTypes.settlement',
    Refund: 'superAdmin.vendorFinanceWorkspace.audit.actionTypes.refund',
    Override: 'superAdmin.vendorFinanceWorkspace.audit.actionTypes.override',
}

const ADJUSTMENT_DIRECTION_LABEL_KEYS: Record<CommerceHubFinancialDirection, string> = {
    Credit: 'superAdmin.vendorFinanceWorkspace.adjustmentDirections.credit',
    Debit: 'superAdmin.vendorFinanceWorkspace.adjustmentDirections.debit',
}

const SETTLEMENT_FLOW_LABEL_KEYS: Record<SuperAdminVendorSettlementFlow, string> = {
    VendorPaysPlatform: 'superAdmin.vendorFinanceWorkspace.settlementFlows.vendorPaysPlatform',
    PlatformPaysVendor: 'superAdmin.vendorFinanceWorkspace.settlementFlows.platformPaysVendor',
}
const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(amount: number, currency = 'EGP', locale = 'en-US'): string {
    if (String(currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(amount || 0))
}

function formatSignedCurrency(amount: number, currency = 'EGP', locale = 'en-US'): string {
    const formatted = formatCurrency(Math.abs(amount), currency, locale)
    return `${amount >= 0 ? '+' : '-'}${formatted}`
}

function formatPercent(value: number, locale = 'en-US'): string {
    return `${new Intl.NumberFormat(locale, {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format((value || 0) * 100)}%`
}

function formatDateTime(value: string | null | undefined, locale: string, fallback: string): string {
    if (!value) {
        return fallback
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return fallback
    }

    return date.toLocaleString(locale)
}

function buildVendorDebtSummary(vendor: SuperAdminVendorFinanceListItem, t: TFunction, locale: string): string {
    if (vendor.isSuspended) {
        return t('superAdmin.vendorFinanceWorkspace.debtSummary.suspended', {
            amount: formatCurrency(vendor.amountOwedByVendor, vendor.currency, locale),
            threshold: formatCurrency(vendor.debtThresholdAmount, vendor.currency, locale),
        })
    }

    if (vendor.isGracePeriodActive && vendor.graceDeadlineUtc) {
        return t('superAdmin.vendorFinanceWorkspace.debtSummary.grace', {
            amount: formatCurrency(vendor.amountOwedByVendor, vendor.currency, locale),
            graceDeadline: formatDateTime(
                vendor.graceDeadlineUtc,
                locale,
                t('superAdmin.vendorFinanceWorkspace.labels.noLedgerActivity'),
            ),
        })
    }

    if (vendor.amountOwedByVendor > 0) {
        return t('superAdmin.vendorFinanceWorkspace.debtSummary.threshold', {
            amount: formatCurrency(vendor.amountOwedByVendor, vendor.currency, locale),
            threshold: formatCurrency(vendor.debtThresholdAmount, vendor.currency, locale),
        })
    }

    return t('superAdmin.vendorFinanceWorkspace.debtSummary.clear')
}

function formatTransactionType(value: string, t: TFunction): string {
    return value in TRANSACTION_TYPE_LABEL_KEYS
        ? t(TRANSACTION_TYPE_LABEL_KEYS[value as CommerceHubFinancialTransactionType])
        : value
}

function buildIdempotencyKey(prefix: string, vendorId: string): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `${prefix}:${vendorId}:${crypto.randomUUID()}`
    }

    return `${prefix}:${vendorId}:${Date.now()}`
}

function getErrorMessage(error: unknown, fallback: string): string {
    return error instanceof Error && error.message ? error.message : fallback
}

function getStatusTone(status: string): string {
    switch (status) {
        case 'SUSPENDED':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        case 'AT_RISK':
            return 'border-amber-200 bg-amber-50 text-amber-900'
        default:
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
    }
}

function getSignedTone(direction: string): string {
    return direction === 'Credit' ? 'text-emerald-700' : 'text-rose-700'
}

function formatActorId(actorId?: string | null): string {
    if (!actorId) {
        return ''
    }

    return actorId.length <= 12 ? actorId : `${actorId.slice(0, 8)}...${actorId.slice(-4)}`
}

function buildAuditMessage(item: SuperAdminVendorFinanceAuditItem, t: TFunction, locale: string): string {
    const amount = formatSignedCurrency(item.signedAmount, item.currency, locale)
    const actor = formatActorId(item.actorId) || t('superAdmin.vendorFinanceWorkspace.labels.unknownAdmin')

    switch (item.actionType) {
        case 'Settlement':
            return t('superAdmin.vendorFinanceWorkspace.audit.messages.settlement', { actor, amount })
        case 'Refund':
            return t('superAdmin.vendorFinanceWorkspace.audit.messages.refund', { actor, amount })
        case 'Override':
            return t('superAdmin.vendorFinanceWorkspace.audit.messages.override', { actor, amount })
        default:
            return t('superAdmin.vendorFinanceWorkspace.audit.messages.default', {
                actor,
                amount,
                actionType: item.actionType,
            })
    }
}

function buildTransactionTooltip(transaction: { transactionType: string; description: string }, t: TFunction): string {
    if (transaction.description) {
        return transaction.description
    }

    switch (transaction.transactionType) {
        case 'Commission':
            return t('superAdmin.vendorFinanceWorkspace.tooltips.commission')
        case 'Settlement':
            return t('superAdmin.vendorFinanceWorkspace.tooltips.settlement')
        case 'Adjustment':
            return t('superAdmin.vendorFinanceWorkspace.tooltips.adjustment')
        default:
            return t('superAdmin.vendorFinanceWorkspace.tooltips.default')
    }
}

function StatusBadge({ status }: { status: string }) {
    const { t } = useTranslation()

    return (
        <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] ${getStatusTone(status)}`}>
            {t(STATUS_LABEL_KEYS[status] ?? 'superAdmin.vendorFinanceWorkspace.statuses.active')}
        </span>
    )
}

function MetricCard({
    label,
    value,
    detail,
}: {
    label: string
    value: string
    detail: string
}) {
    return (
        <div className="rounded-[24px] border border-slate-200 bg-white px-4 py-4 shadow-sm">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{label}</p>
            <p className="mt-3 text-2xl font-semibold text-slate-950">{value}</p>
            <p className="mt-2 text-sm text-slate-500">{detail}</p>
        </div>
    )
}

function CompactHistorySection({
    title,
    caption,
    rows,
    isLoading,
    loadingMessage,
    emptyMessage,
}: {
    title: string
    caption: string
    rows: SuperAdminVendorStatement['transactions']
    isLoading: boolean
    loadingMessage: string
    emptyMessage: string
}) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const noLedgerActivity = t('superAdmin.vendorFinanceWorkspace.labels.noLedgerActivity')

    return (
        <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm">
            <div className="border-b border-slate-100 px-5 py-4">
                <h3 className="text-lg font-semibold text-slate-950">{title}</h3>
                <p className="mt-1 text-sm text-slate-500">{caption}</p>
            </div>

            {isLoading ? (
                <div className="px-5 py-10 text-center text-sm text-slate-500">{loadingMessage}</div>
            ) : rows.length === 0 ? (
                <div className="px-5 py-10 text-center text-sm text-slate-500">{emptyMessage}</div>
            ) : (
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-200 text-sm">
                        <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                            <tr>
                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.date')}</th>
                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.type')}</th>
                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.amount')}</th>
                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.notes')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100 bg-white">
                            {rows.map((transaction) => (
                                <tr key={`${title}-${transaction.transactionId}`} className="align-top">
                                    <td className="px-5 py-4 text-slate-600">{formatDateTime(transaction.createdAtUtc, locale, noLedgerActivity)}</td>
                                    <td className="px-5 py-4 font-medium text-slate-900">{formatTransactionType(transaction.transactionType, t)}</td>
                                    <td className={`px-5 py-4 font-semibold ${transaction.signedAmount >= 0 ? 'text-emerald-700' : 'text-rose-700'}`}>
                                        {formatSignedCurrency(transaction.signedAmount, transaction.currency, locale)}
                                    </td>
                                    <td className="px-5 py-4 text-slate-600">{transaction.description}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </section>
    )
}

function StatementDrawer({
    vendor,
    statement,
    debtHistory,
    payoutHistory,
    auditTimeline,
    auditTotalCount,
    auditPage,
    auditHasMore,
    isOpen,
    isLoading,
    isFetching,
    isError,
    errorMessage,
    auditLoading,
    auditFetching,
    auditError,
    auditErrorMessage,
    debtHistoryLoading,
    payoutHistoryLoading,
    exportBusy,
    feedback,
    onClose,
    onRefresh,
    onExport,
    onOpenAdjustment,
    onOpenSettlement,
    fromDate,
    toDate,
    selectedTypes,
    onFromDateChange,
    onToDateChange,
    onToggleTransactionType,
    onResetFilters,
    onPreviousPage,
    onNextPage,
    onPreviousAuditPage,
    onNextAuditPage,
}: {
    vendor: SuperAdminVendorFinanceListItem | null
    statement?: SuperAdminVendorStatement
    debtHistory: SuperAdminVendorStatement['transactions']
    payoutHistory: SuperAdminVendorStatement['transactions']
    auditTimeline: SuperAdminVendorFinanceAuditItem[]
    auditTotalCount: number
    auditPage: number
    auditHasMore: boolean
    isOpen: boolean
    isLoading: boolean
    isFetching: boolean
    isError: boolean
    errorMessage?: string | null
    auditLoading: boolean
    auditFetching: boolean
    auditError: boolean
    auditErrorMessage?: string | null
    debtHistoryLoading: boolean
    payoutHistoryLoading: boolean
    exportBusy: boolean
    feedback?: { tone: 'success' | 'error'; message: string } | null
    onClose: () => void
    onRefresh: () => void
    onExport: () => void
    onOpenAdjustment: () => void
    onOpenSettlement: () => void
    fromDate: string
    toDate: string
    selectedTypes: CommerceHubFinancialTransactionType[]
    onFromDateChange: (value: string) => void
    onToDateChange: (value: string) => void
    onToggleTransactionType: (value: CommerceHubFinancialTransactionType) => void
    onResetFilters: () => void
    onPreviousPage: () => void
    onNextPage: () => void
    onPreviousAuditPage: () => void
    onNextAuditPage: () => void
}) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const noLedgerActivity = t('superAdmin.vendorFinanceWorkspace.labels.noLedgerActivity')

    return (
        <>
            <div
                className={`fixed inset-0 z-40 bg-slate-950/45 transition-opacity duration-300 ${isOpen ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0'}`}
                onClick={onClose}
            />

            <aside
                className={`fixed right-0 top-0 z-50 h-full w-full max-w-3xl transform overflow-y-auto border-l border-slate-200 bg-slate-50 shadow-[0_30px_120px_-40px_rgba(15,23,42,0.45)] transition duration-300 ${isOpen ? 'translate-x-0' : 'translate-x-full'}`}
            >
                <div className="sticky top-0 z-20 border-b border-slate-200 bg-white/95 px-6 py-5 backdrop-blur">
                    <div className="flex items-start justify-between gap-4">
                        <div>
                            <div className="flex flex-wrap items-center gap-3">
                                <h2 className="text-2xl font-semibold text-slate-950">{vendor?.vendorName ?? t('superAdmin.vendorFinanceWorkspace.labels.vendorStatement')}</h2>
                                {vendor ? <StatusBadge status={vendor.status} /> : null}
                            </div>
                            <p className="mt-2 text-sm text-slate-500">
                                {t('superAdmin.vendorFinanceWorkspace.drawer.description')}
                            </p>
                        </div>

                        <button
                            type="button"
                            className="flex h-11 w-11 items-center justify-center rounded-2xl border border-slate-200 bg-white text-slate-600"
                            onClick={onClose}
                        >
                            <X className="h-5 w-5" />
                        </button>
                    </div>

                    <div className="mt-5 flex flex-wrap gap-3">
                        <button
                            type="button"
                            className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                            onClick={onOpenAdjustment}
                        >
                            <Wallet className="h-4 w-4" />
                            {t('superAdmin.vendorFinanceWorkspace.actions.addAdjustment')}
                        </button>
                        <button
                            type="button"
                            className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            onClick={onOpenSettlement}
                        >
                            <Landmark className="h-4 w-4" />
                            {t('superAdmin.vendorFinanceWorkspace.actions.recordSettlement')}
                        </button>
                        <button
                            type="button"
                            className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            onClick={onExport}
                        >
                            <Download className="h-4 w-4" />
                            {exportBusy
                                ? t('superAdmin.vendorFinanceWorkspace.actions.exporting')
                                : t('superAdmin.vendorFinanceWorkspace.actions.exportCsv')}
                        </button>
                        <button
                            type="button"
                            className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                            onClick={onRefresh}
                        >
                            <RefreshCw className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`} />
                            {t('superAdmin.common.actions.refresh')}
                        </button>
                    </div>
                </div>

                <div className="space-y-6 px-6 py-6">
                    {vendor && vendor.amountOwedByVendor > 0 ? (
                        <div className={`rounded-[28px] border px-5 py-4 ${vendor.isSuspended ? 'border-rose-200 bg-rose-50 text-rose-900' : 'border-amber-200 bg-amber-50 text-amber-900'}`}>
                            <div className="flex items-start gap-3">
                                <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0" />
                                <div>
                                    <p className="font-semibold">
                                        {vendor.isSuspended
                                            ? t('superAdmin.vendorFinanceWorkspace.drawer.debtAlert.suspendedTitle')
                                            : vendor.isGracePeriodActive
                                                ? t('superAdmin.vendorFinanceWorkspace.drawer.debtAlert.graceTitle')
                                                : t('superAdmin.vendorFinanceWorkspace.drawer.debtAlert.owesTitle')}
                                    </p>
                                    <p className="mt-1 text-sm">
                                        {buildVendorDebtSummary(vendor, t, locale)}{' '}
                                        {t('superAdmin.vendorFinanceWorkspace.drawer.debtAlert.currentBalance', {
                                            balance: formatCurrency(vendor.currentBalance, vendor.currency, locale),
                                        })}
                                    </p>
                                </div>
                            </div>
                        </div>
                    ) : null}

                    {feedback ? (
                        <div className={`rounded-[24px] border px-4 py-3 text-sm ${feedback.tone === 'error' ? 'border-rose-200 bg-rose-50 text-rose-900' : 'border-emerald-200 bg-emerald-50 text-emerald-900'}`}>
                            {feedback.message}
                        </div>
                    ) : null}

                    <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                        <MetricCard
                            label={t('superAdmin.vendorFinanceWorkspace.metrics.currentBalance.label')}
                            value={formatCurrency(vendor?.currentBalance ?? 0, vendor?.currency, locale)}
                            detail={t('superAdmin.vendorFinanceWorkspace.metrics.currentBalance.detail')}
                        />
                        <MetricCard
                            label={t('superAdmin.vendorFinanceWorkspace.metrics.openingBalance.label')}
                            value={formatCurrency(statement?.openingBalance ?? 0, vendor?.currency, locale)}
                            detail={t('superAdmin.vendorFinanceWorkspace.metrics.openingBalance.detail')}
                        />
                        <MetricCard
                            label={t('superAdmin.vendorFinanceWorkspace.metrics.closingBalance.label')}
                            value={formatCurrency(statement?.closingBalance ?? 0, vendor?.currency, locale)}
                            detail={t('superAdmin.vendorFinanceWorkspace.metrics.closingBalance.detail')}
                        />
                        <MetricCard
                            label={t('superAdmin.vendorFinanceWorkspace.metrics.filteredNet.label')}
                            value={formatCurrency((statement?.filteredCredits ?? 0) - (statement?.filteredDebits ?? 0), vendor?.currency, locale)}
                            detail={t('superAdmin.vendorFinanceWorkspace.metrics.filteredNet.detail')}
                        />
                    </div>

                    <div className="grid gap-6 xl:grid-cols-2">
                        <CompactHistorySection
                            title={t('superAdmin.vendorFinanceWorkspace.history.debt.title')}
                            caption={t('superAdmin.vendorFinanceWorkspace.history.debt.caption')}
                            rows={debtHistory}
                            isLoading={debtHistoryLoading}
                            loadingMessage={t('superAdmin.vendorFinanceWorkspace.history.debt.loading')}
                            emptyMessage={t('superAdmin.vendorFinanceWorkspace.history.debt.empty')}
                        />
                        <CompactHistorySection
                            title={t('superAdmin.vendorFinanceWorkspace.history.payout.title')}
                            caption={t('superAdmin.vendorFinanceWorkspace.history.payout.caption')}
                            rows={payoutHistory}
                            isLoading={payoutHistoryLoading}
                            loadingMessage={t('superAdmin.vendorFinanceWorkspace.history.payout.loading')}
                            emptyMessage={t('superAdmin.vendorFinanceWorkspace.history.payout.empty')}
                        />
                    </div>

                    <section className="rounded-[28px] border border-slate-200 bg-white shadow-sm">
                        <div className="border-b border-slate-100 px-5 py-4">
                            <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                                <div>
                                    <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                                        <Filter className="h-4 w-4" />
                                        {t('superAdmin.vendorFinanceWorkspace.statementFilters.badge')}
                                    </div>
                                    <p className="mt-3 text-sm text-slate-500">
                                        {t('superAdmin.vendorFinanceWorkspace.statementFilters.description')}
                                    </p>
                                </div>
                                <button
                                    type="button"
                                    className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                    onClick={onResetFilters}
                                >
                                    {t('superAdmin.vendorFinanceWorkspace.actions.resetFilters')}
                                </button>
                            </div>
                        </div>

                        <div className="grid gap-4 px-5 py-5 md:grid-cols-2">
                            <label className="space-y-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.vendorFinanceWorkspace.statementFilters.from')}</span>
                                <input type="date" value={fromDate} className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" onChange={(event) => onFromDateChange(event.target.value)} />
                            </label>
                            <label className="space-y-2">
                                <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.vendorFinanceWorkspace.statementFilters.to')}</span>
                                <input type="date" value={toDate} className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" onChange={(event) => onToDateChange(event.target.value)} />
                            </label>
                        </div>

                        <div className="flex flex-wrap gap-2 px-5 pb-5">
                            {TRANSACTION_TYPE_OPTIONS.map((type) => {
                                const active = selectedTypes.includes(type)

                                return (
                                    <button
                                        key={type}
                                        type="button"
                                        className={`rounded-full border px-3 py-2 text-sm font-medium transition ${active ? 'border-slate-950 bg-slate-950 text-white' : 'border-slate-200 bg-white text-slate-700 hover:border-slate-300 hover:bg-slate-50'}`}
                                        onClick={() => onToggleTransactionType(type)}
                                    >
                                        {formatTransactionType(type, t)}
                                    </button>
                                )
                            })}
                        </div>
                    </section>

                    <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm">
                        <div className="flex items-center justify-between border-b border-slate-100 px-5 py-4">
                            <div>
                                <h3 className="text-lg font-semibold text-slate-950">{t('superAdmin.vendorFinanceWorkspace.statement.title')}</h3>
                                <p className="mt-1 text-sm text-slate-500">
                                    {t('superAdmin.vendorFinanceWorkspace.statement.description')}
                                </p>
                            </div>
                            <p className="text-sm text-slate-500">
                                {t('superAdmin.vendorFinanceWorkspace.statement.page', { page: statement?.page ?? 1 })}
                            </p>
                        </div>

                        {isLoading ? (
                            <div className="px-5 py-12 text-center text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.statement.loading')}</div>
                        ) : isError ? (
                            <div className="px-5 py-12 text-center text-sm text-rose-700">{errorMessage}</div>
                        ) : (
                            <>
                                <div className="overflow-x-auto">
                                    <table className="min-w-full divide-y divide-slate-200 text-sm">
                                        <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                            <tr>
                                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.date')}</th>
                                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.type')}</th>
                                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.tableHeaders.amount')}</th>
                                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.statement.table.runningBalance')}</th>
                                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.statement.table.correlation')}</th>
                                                <th className="px-5 py-4">{t('superAdmin.vendorFinanceWorkspace.statement.table.description')}</th>
                                            </tr>
                                        </thead>
                                        <tbody className="divide-y divide-slate-100 bg-white">
                                            {(statement?.transactions ?? []).map((transaction) => (
                                                <tr key={transaction.transactionId} className="align-top">
                                                    <td className="px-5 py-4 text-slate-600">{formatDateTime(transaction.createdAtUtc, locale, noLedgerActivity)}</td>
                                                    <td className="px-5 py-4 font-medium text-slate-900" title={buildTransactionTooltip(transaction, t)}>
                                                        {formatTransactionType(transaction.transactionType, t)}
                                                    </td>
                                                    <td className={`px-5 py-4 font-semibold ${getSignedTone(transaction.direction)}`}>
                                                        {formatCurrency(transaction.signedAmount, transaction.currency, locale)}
                                                    </td>
                                                    <td className="px-5 py-4 font-medium text-slate-900">{formatCurrency(transaction.runningBalance ?? 0, transaction.currency, locale)}</td>
                                                    <td className="px-5 py-4 font-mono text-xs text-slate-600">{transaction.correlationId}</td>
                                                    <td className="px-5 py-4 text-slate-600" title={transaction.description}>{transaction.description}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>

                                {(statement?.transactions?.length ?? 0) === 0 ? (
                                    <div className="px-5 py-10 text-center text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.statement.empty')}</div>
                                ) : null}

                                <div className="flex flex-col gap-4 border-t border-slate-100 px-5 py-4 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between">
                                    <p>
                                        {t('superAdmin.vendorFinanceWorkspace.statement.totalCount', {
                                            count: statement?.totalCount ?? 0,
                                        })}
                                    </p>
                                    <div className="flex gap-3">
                                        <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={!statement || statement.page <= 1} onClick={onPreviousPage}>{t('superAdmin.common.actions.previous')}</button>
                                        <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={!statement?.hasMore} onClick={onNextPage}>{t('superAdmin.common.actions.next')}</button>
                                    </div>
                                </div>
                            </>
                        )}
                    </section>

                    <section className="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm">
                        <div className="flex items-center justify-between border-b border-slate-100 px-5 py-4">
                                <div>
                                    <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                                        <History className="h-4 w-4" />
                                        {t('superAdmin.vendorFinanceWorkspace.audit.title')}
                                    </div>
                                    <p className="mt-3 text-sm text-slate-500">
                                        {t('superAdmin.vendorFinanceWorkspace.audit.description')}
                                    </p>
                                </div>
                            <p className="text-sm text-slate-500">
                                {t('superAdmin.vendorFinanceWorkspace.audit.count', { count: auditTotalCount })}
                            </p>
                        </div>

                        {auditLoading ? (
                            <div className="px-5 py-10 text-center text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.audit.loading')}</div>
                        ) : auditError ? (
                            <div className="px-5 py-10 text-center text-sm text-rose-700">{auditErrorMessage}</div>
                        ) : auditTimeline.length === 0 ? (
                            <div className="px-5 py-10 text-center text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.audit.empty')}</div>
                        ) : (
                            <>
                                <div className="divide-y divide-slate-100">
                                    {auditTimeline.map((item) => (
                                        <article key={item.transactionId} className="space-y-3 px-5 py-4">
                                            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                                                <div className="space-y-2">
                                                    <div className="flex flex-wrap items-center gap-2">
                                                        <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] ${item.actionType === 'Settlement' ? 'border-sky-200 bg-sky-50 text-sky-900' : item.actionType === 'Refund' ? 'border-amber-200 bg-amber-50 text-amber-900' : 'border-slate-200 bg-slate-50 text-slate-800'}`}>
                                                            {t(AUDIT_ACTION_LABEL_KEYS[item.actionType] ?? 'superAdmin.vendorFinanceWorkspace.audit.actionTypes.default', {
                                                                actionType: item.actionType,
                                                            })}
                                                        </span>
                                                        <span className={`text-sm font-semibold ${item.signedAmount >= 0 ? 'text-emerald-700' : 'text-rose-700'}`}>
                                                            {formatSignedCurrency(item.signedAmount, item.currency, locale)}
                                                        </span>
                                                    </div>
                                                    <p className="text-sm text-slate-700">{buildAuditMessage(item, t, locale)}</p>
                                                    <p className="text-sm text-slate-500">{item.reason}</p>
                                                </div>

                                                <div className="space-y-1 text-right text-xs text-slate-500">
                                                    <p>{formatDateTime(item.createdAtUtc, locale, noLedgerActivity)}</p>
                                                    <p className="font-mono" title={item.actorId ?? undefined}>
                                                        {t('superAdmin.vendorFinanceWorkspace.audit.actor', {
                                                            actor: formatActorId(item.actorId) || t('superAdmin.vendorFinanceWorkspace.labels.unknownAdmin'),
                                                        })}
                                                    </p>
                                                </div>
                                            </div>

                                            <div className="flex flex-wrap gap-4 text-xs text-slate-500">
                                                <span className="font-mono">{t('superAdmin.vendorFinanceWorkspace.audit.correlation', { value: item.correlationId })}</span>
                                                <span className="font-mono">{t('superAdmin.vendorFinanceWorkspace.audit.reference', { value: item.referenceId })}</span>
                                            </div>
                                        </article>
                                    ))}
                                </div>

                                <div className="flex flex-col gap-4 border-t border-slate-100 px-5 py-4 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between">
                                    <p>
                                        {t('superAdmin.vendorFinanceWorkspace.audit.page', { page: auditPage })}
                                    </p>
                                    <div className="flex gap-3">
                                        <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={auditPage <= 1 || auditFetching} onClick={onPreviousAuditPage}>{t('superAdmin.common.actions.previous')}</button>
                                        <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={!auditHasMore || auditFetching} onClick={onNextAuditPage}>{t('superAdmin.common.actions.next')}</button>
                                    </div>
                                </div>
                            </>
                        )}
                    </section>
                </div>
            </aside>
        </>
    )
}

export default function VendorFinanceWorkspace() {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const noLedgerActivity = t('superAdmin.vendorFinanceWorkspace.labels.noLedgerActivity')
    const queryClient = useQueryClient()
    const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; message: string } | null>(null)
    const [activeDialog, setActiveDialog] = useState<'adjustment' | 'settlement' | null>(null)
    const [selectedVendor, setSelectedVendor] = useState<SuperAdminVendorFinanceListItem | null>(null)
    const [filters, setFilters] = useState<SuperAdminVendorFinanceFilters>({
        page: 1,
        pageSize: DEFAULT_PAGE_SIZE,
        search: '',
        negativeBalanceOnly: false,
        highRefundRatioOnly: false,
        inactiveOnly: false,
        inactiveDays: 30,
        highRefundRatioThreshold: 0.25,
        sortBy: 'LastActivityAt',
        sortDirection: 'Desc',
    })
    const [statementFilters, setStatementFilters] = useState({
        page: 1,
        pageSize: DEFAULT_STATEMENT_PAGE_SIZE,
        fromDate: '',
        toDate: '',
        transactionTypes: [] as CommerceHubFinancialTransactionType[],
    })
    const [auditFilters, setAuditFilters] = useState({
        page: 1,
        pageSize: 8,
    })
    const [adjustmentDraft, setAdjustmentDraft] = useState({
        amount: '',
        direction: 'Credit' as CommerceHubFinancialDirection,
        reason: '',
        idempotencyKey: '',
    })
    const [settlementDraft, setSettlementDraft] = useState({
        amount: '',
        flow: 'VendorPaysPlatform' as SuperAdminVendorSettlementFlow,
        reason: '',
        idempotencyKey: '',
    })

    const deferredSearch = useDeferredValue(filters.search ?? '')
    const vendorQueryFilters = useMemo(
        () => ({
            ...filters,
            search: deferredSearch,
        }),
        [deferredSearch, filters],
    )

    const vendorsQuery = useQuery({
        queryKey: ['super-admin', 'vendor-finance', vendorQueryFilters],
        queryFn: () => getSuperAdminVendorFinance(vendorQueryFilters),
        staleTime: 30_000,
        refetchOnWindowFocus: true,
    })

    const statementQuery = useQuery({
        enabled: Boolean(selectedVendor),
        queryKey: ['super-admin', 'vendor-finance', 'statement', selectedVendor?.vendorId, statementFilters],
        queryFn: () =>
            getSuperAdminVendorStatement(selectedVendor!.vendorId, {
                page: statementFilters.page,
                pageSize: statementFilters.pageSize,
                fromDate: statementFilters.fromDate || undefined,
                toDate: statementFilters.toDate || undefined,
                transactionTypes: statementFilters.transactionTypes,
        }),
        staleTime: 15_000,
    })

    const debtHistoryQuery = useQuery({
        enabled: Boolean(selectedVendor),
        queryKey: ['super-admin', 'vendor-finance', 'debt-history', selectedVendor?.vendorId],
        queryFn: () =>
            getSuperAdminVendorStatement(selectedVendor!.vendorId, {
                page: 1,
                pageSize: 8,
                transactionTypes: ['Commission', 'Refund', 'Settlement', 'Adjustment'],
            }),
        staleTime: 15_000,
    })

    const payoutHistoryQuery = useQuery({
        enabled: Boolean(selectedVendor),
        queryKey: ['super-admin', 'vendor-finance', 'payout-history', selectedVendor?.vendorId],
        queryFn: () =>
            getSuperAdminVendorStatement(selectedVendor!.vendorId, {
                page: 1,
                pageSize: 8,
                transactionTypes: ['Payout'],
            }),
        staleTime: 15_000,
    })

    const auditQuery = useQuery({
        enabled: Boolean(selectedVendor),
        queryKey: ['super-admin', 'vendor-finance', 'audit', selectedVendor?.vendorId, auditFilters],
        queryFn: () =>
            getSuperAdminVendorAuditTimeline(selectedVendor!.vendorId, {
                page: auditFilters.page,
                pageSize: auditFilters.pageSize,
            }),
        staleTime: 15_000,
    })

    useEffect(() => {
        if (!selectedVendor) {
            return
        }

        const latestVendor = vendorsQuery.data?.vendors.find((item) => item.vendorId === selectedVendor.vendorId)
        if (
            latestVendor &&
            (
                latestVendor.currentBalance !== selectedVendor.currentBalance ||
                latestVendor.amountOwedByVendor !== selectedVendor.amountOwedByVendor ||
                latestVendor.status !== selectedVendor.status ||
                latestVendor.isGracePeriodActive !== selectedVendor.isGracePeriodActive ||
                latestVendor.graceDeadlineUtc !== selectedVendor.graceDeadlineUtc ||
                latestVendor.refundRatio !== selectedVendor.refundRatio ||
                latestVendor.lastActivityAt !== selectedVendor.lastActivityAt
            )
        ) {
            setSelectedVendor(latestVendor)
        }
    }, [selectedVendor, vendorsQuery.data?.vendors])

    const adjustmentMutation = useMutation({
        mutationFn: (payload: SuperAdminVendorAdjustmentRequest) =>
            createSuperAdminVendorAdjustment(selectedVendor!.vendorId, payload),
        onSuccess: async (result) => {
            setFeedback({
                tone: 'success',
                message: t('superAdmin.vendorFinanceWorkspace.feedback.adjustmentRecorded', {
                    correlationId: result.action.correlationId,
                }),
            })
            setSelectedVendor(result.vendor)
            setActiveDialog(null)
            setAdjustmentDraft({
                amount: '',
                direction: 'Credit',
                reason: '',
                idempotencyKey: '',
            })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance'] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'statement', result.vendor.vendorId] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'debt-history', result.vendor.vendorId] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'payout-history', result.vendor.vendorId] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'audit', result.vendor.vendorId] })
        },
    })

    const settlementMutation = useMutation({
        mutationFn: (payload: SuperAdminVendorSettlementRequest) =>
            createSuperAdminVendorSettlement(selectedVendor!.vendorId, payload),
        onSuccess: async (result) => {
            setFeedback({
                tone: 'success',
                message: t('superAdmin.vendorFinanceWorkspace.feedback.settlementRecorded', {
                    correlationId: result.action.correlationId,
                }),
            })
            setSelectedVendor(result.vendor)
            setActiveDialog(null)
            setSettlementDraft({
                amount: '',
                flow: 'VendorPaysPlatform',
                reason: '',
                idempotencyKey: '',
            })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance'] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'statement', result.vendor.vendorId] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'debt-history', result.vendor.vendorId] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'payout-history', result.vendor.vendorId] })
            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'vendor-finance', 'audit', result.vendor.vendorId] })
        },
    })

    const [isExporting, setIsExporting] = useState(false)
    const vendors = vendorsQuery.data?.vendors ?? []
    const meta = vendorsQuery.data?.meta

    function updateFilters(partial: Partial<SuperAdminVendorFinanceFilters>) {
        setFilters((current) => ({
            ...current,
            ...partial,
            page: partial.page ?? 1,
        }))
    }

    function openVendor(vendor: SuperAdminVendorFinanceListItem) {
        setActiveDialog(null)
        setSelectedVendor(vendor)
        setFeedback(null)
        setStatementFilters({
            page: 1,
            pageSize: DEFAULT_STATEMENT_PAGE_SIZE,
            fromDate: '',
            toDate: '',
            transactionTypes: [],
        })
        setAuditFilters({
            page: 1,
            pageSize: 8,
        })
        setAdjustmentDraft((current) => ({ ...current, idempotencyKey: '' }))
        setSettlementDraft((current) => ({ ...current, idempotencyKey: '' }))
    }

    function openAdjustmentDialog() {
        if (!selectedVendor) {
            return
        }

        adjustmentMutation.reset()
        setAdjustmentDraft((current) => ({
            ...current,
            idempotencyKey: current.idempotencyKey || buildIdempotencyKey('vendor-adjustment', selectedVendor.vendorId),
        }))
        setActiveDialog('adjustment')
    }

    function openSettlementDialog() {
        if (!selectedVendor) {
            return
        }

        settlementMutation.reset()
        setSettlementDraft((current) => ({
            ...current,
            idempotencyKey: current.idempotencyKey || buildIdempotencyKey('vendor-settlement', selectedVendor.vendorId),
        }))
        setActiveDialog('settlement')
    }

    async function handleExport() {
        if (!selectedVendor) {
            return
        }

        setIsExporting(true)

        try {
            const blob = await downloadSuperAdminVendorStatementCsv(selectedVendor.vendorId, {
                fromDate: statementFilters.fromDate || undefined,
                toDate: statementFilters.toDate || undefined,
                transactionTypes: statementFilters.transactionTypes,
            })

            const downloadUrl = window.URL.createObjectURL(blob)
            const link = document.createElement('a')
            link.href = downloadUrl
            link.download = `vendor-statement-${selectedVendor.vendorSlug || selectedVendor.vendorId}-${new Date().toISOString().slice(0, 10)}.csv`
            document.body.appendChild(link)
            link.click()
            link.remove()
            window.URL.revokeObjectURL(downloadUrl)
        } catch (error) {
            setFeedback({
                tone: 'error',
                message: getErrorMessage(error, t('superAdmin.vendorFinanceWorkspace.feedback.exportFailed')),
            })
        } finally {
            setIsExporting(false)
        }
    }

    function submitAdjustment() {
        if (!selectedVendor) {
            return
        }

        adjustmentMutation.mutate({
            amount: Number.parseFloat(adjustmentDraft.amount),
            direction: adjustmentDraft.direction,
            currency: 'EGP',
            idempotencyKey: adjustmentDraft.idempotencyKey,
            reason: adjustmentDraft.reason,
        })
    }

    function submitSettlement() {
        if (!selectedVendor) {
            return
        }

        settlementMutation.mutate({
            amount: Number.parseFloat(settlementDraft.amount),
            flow: settlementDraft.flow,
            currency: 'EGP',
            idempotencyKey: settlementDraft.idempotencyKey,
            reason: settlementDraft.reason,
        })
    }

    const statementErrorMessage = getErrorMessage(
        statementQuery.error,
        t('superAdmin.vendorFinanceWorkspace.feedback.statementLoadFailed'),
    )
    const auditErrorMessage = getErrorMessage(
        auditQuery.error,
        t('superAdmin.vendorFinanceWorkspace.feedback.auditLoadFailed'),
    )
    const vendorsErrorMessage = getErrorMessage(
        vendorsQuery.error,
        t('superAdmin.vendorFinanceWorkspace.feedback.vendorsLoadFailed'),
    )
    const debtHistoryRows = (debtHistoryQuery.data?.transactions ?? []).filter(
        (transaction) => transaction.financialBucket === 'Receivable',
    )
    const payoutHistoryRows = (payoutHistoryQuery.data?.transactions ?? []).filter(
        (transaction) => transaction.transactionType === 'Payout',
    )

    return (
        <>
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_30px_90px_-52px_rgba(15,23,42,0.45)]">
                <div className="border-b border-slate-100 px-6 py-5">
                    <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                        <div>
                            <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                                <ShieldAlert className="h-4 w-4" />
                                {t('superAdmin.vendorFinanceWorkspace.hero.badge')}
                            </div>
                            <h2 className="mt-4 text-2xl font-semibold text-slate-950">{t('superAdmin.vendorFinanceWorkspace.hero.title')}</h2>
                            <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-500">
                                {t('superAdmin.vendorFinanceWorkspace.hero.description')}
                            </p>
                        </div>

                        <button
                            type="button"
                            className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                            onClick={() => vendorsQuery.refetch()}
                        >
                            <RefreshCw className={`h-4 w-4 ${vendorsQuery.isFetching ? 'animate-spin' : ''}`} />
                            {t('superAdmin.vendorFinanceWorkspace.actions.refreshVendors')}
                        </button>
                    </div>
                </div>

                <div className="grid gap-4 border-b border-slate-100 bg-slate-50/80 px-6 py-5 md:grid-cols-2 xl:grid-cols-4">
                    <label className="space-y-2 xl:col-span-2">
                        <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.vendorFinanceWorkspace.filters.search')}</span>
                        <div className="flex items-center gap-3 rounded-2xl border border-slate-200 bg-white px-4 py-3 shadow-sm">
                            <Search className="h-4 w-4 text-slate-400" />
                            <input
                                type="search"
                                value={filters.search ?? ''}
                                onChange={(event) => updateFilters({ search: event.target.value })}
                                placeholder={t('superAdmin.vendorFinanceWorkspace.filters.searchPlaceholder')}
                                className="w-full bg-transparent text-sm text-slate-900 outline-none"
                            />
                        </div>
                    </label>

                    <label className="space-y-2">
                        <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.vendorFinanceWorkspace.filters.sortBy')}</span>
                        <select value={filters.sortBy} onChange={(event) => updateFilters({ sortBy: event.target.value as SuperAdminVendorFinanceSortField })} className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                            {(['LastActivityAt', 'CurrentBalance', 'TotalEarnings', 'TotalRefunds', 'RefundRatio', 'Status', 'VendorName'] as SuperAdminVendorFinanceSortField[]).map((field) => (
                                <option key={field} value={field}>{t(SORT_FIELD_LABEL_KEYS[field])}</option>
                            ))}
                        </select>
                    </label>

                    <div className="grid gap-4 sm:grid-cols-2">
                        <label className="space-y-2">
                            <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.vendorFinanceWorkspace.filters.direction')}</span>
                            <select value={filters.sortDirection} onChange={(event) => updateFilters({ sortDirection: event.target.value as SuperAdminSortDirection })} className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="Desc">{t(SORT_DIRECTION_LABEL_KEYS.Desc)}</option>
                                <option value="Asc">{t(SORT_DIRECTION_LABEL_KEYS.Asc)}</option>
                            </select>
                        </label>
                        <label className="space-y-2">
                            <span className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.vendorFinanceWorkspace.filters.pageSize')}</span>
                            <select value={filters.pageSize} onChange={(event) => updateFilters({ pageSize: Number(event.target.value) })} className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                {[20, 50, 100].map((size) => (
                                    <option key={size} value={size}>{size}</option>
                                ))}
                            </select>
                        </label>
                    </div>
                </div>

                <div className="flex flex-wrap gap-3 px-6 py-5">
                    {[
                        { key: 'negativeBalanceOnly', label: t('superAdmin.vendorFinanceWorkspace.filters.chips.outstandingDebt') },
                        { key: 'highRefundRatioOnly', label: t('superAdmin.vendorFinanceWorkspace.filters.chips.highRefundRatio') },
                        { key: 'inactiveOnly', label: t('superAdmin.vendorFinanceWorkspace.filters.chips.inactiveVendors') },
                    ].map((option) => {
                        const active = filters[option.key as keyof SuperAdminVendorFinanceFilters] as boolean

                        return (
                            <button
                                key={option.key}
                                type="button"
                                className={`rounded-full border px-4 py-2 text-sm font-medium transition ${active ? 'border-slate-950 bg-slate-950 text-white' : 'border-slate-200 bg-white text-slate-700 hover:border-slate-300 hover:bg-slate-50'}`}
                                onClick={() => updateFilters({ [option.key]: !active } as Partial<SuperAdminVendorFinanceFilters>)}
                            >
                                {option.label}
                            </button>
                        )
                    })}
                </div>

                {vendorsQuery.isError ? (
                    <div className="border-t border-slate-100 px-6 py-10 text-center text-sm text-rose-700">{vendorsErrorMessage}</div>
                ) : (
                    <>
                        <div className="overflow-x-auto border-t border-slate-100">
                            <table className="min-w-full divide-y divide-slate-200 text-sm">
                                <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <tr>
                                        <th className="px-6 py-4">{t('superAdmin.vendorFinanceWorkspace.table.vendor')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.vendorFinanceWorkspace.table.payable')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.vendorFinanceWorkspace.table.debt')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.vendorFinanceWorkspace.table.status')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.vendorFinanceWorkspace.table.lastActivity')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100 bg-white">
                                    {vendors.map((vendor) => (
                                        <tr key={vendor.vendorId} className="cursor-pointer transition hover:bg-slate-50" onClick={() => openVendor(vendor)}>
                                            <td className="px-6 py-4">
                                                <div>
                                                    <p className="font-semibold text-slate-950">{vendor.vendorName}</p>
                                                    <p className="mt-1 text-xs text-slate-500">{vendor.contactEmail || vendor.vendorSlug || vendor.vendorId}</p>
                                                </div>
                                            </td>
                                            <td className="px-6 py-4 text-slate-700">
                                                <div className="font-semibold text-emerald-700">
                                                    {formatCurrency(Math.max(vendor.currentBalance, 0), vendor.currency, locale)}
                                                </div>
                                                <div className="mt-1 text-xs text-slate-500">
                                                    {t('superAdmin.vendorFinanceWorkspace.table.netBalance', {
                                                        amount: formatCurrency(vendor.currentBalance, vendor.currency, locale),
                                                    })}
                                                </div>
                                            </td>
                                            <td className="px-6 py-4 text-slate-700">
                                                <div className={`font-semibold ${vendor.amountOwedByVendor > 0 ? 'text-rose-700' : 'text-slate-900'}`}>
                                                    {formatCurrency(vendor.amountOwedByVendor, vendor.currency, locale)}
                                                </div>
                                                <div className="mt-1 text-xs text-slate-500">
                                                    {t('superAdmin.vendorFinanceWorkspace.table.refundRatio', {
                                                        ratio: formatPercent(vendor.refundRatio, locale),
                                                    })}
                                                </div>
                                            </td>
                                            <td className="px-6 py-4">
                                                <div className="space-y-2">
                                                    <StatusBadge status={vendor.status} />
                                                    {vendor.isGracePeriodActive ? (
                                                        <p className="text-xs text-amber-700">
                                                            {t('superAdmin.vendorFinanceWorkspace.table.graceUntil', {
                                                                date: formatDateTime(vendor.graceDeadlineUtc, locale, noLedgerActivity),
                                                            })}
                                                        </p>
                                                    ) : null}
                                                    {!vendor.isGracePeriodActive && vendor.isNearThreshold ? <p className="text-xs text-amber-700">{t('superAdmin.vendorFinanceWorkspace.table.debtNearThreshold')}</p> : null}
                                                    {vendor.hasHighRefundRatio ? <p className="text-xs text-amber-700">{t('superAdmin.vendorFinanceWorkspace.table.highRefundSignal')}</p> : null}
                                                </div>
                                            </td>
                                            <td className="px-6 py-4 text-slate-600">{formatDateTime(vendor.lastActivityAt, locale, noLedgerActivity)}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>

                        {vendorsQuery.isLoading ? (
                            <div className="px-6 py-10 text-center text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.loading.overview')}</div>
                        ) : null}

                        {vendors.length === 0 && !vendorsQuery.isLoading ? (
                            <div className="px-6 py-10 text-center text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.empty.noVendors')}</div>
                        ) : null}

                        <div className="flex flex-col gap-4 border-t border-slate-100 px-6 py-4 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between">
                            <p>
                                {t('superAdmin.vendorFinanceWorkspace.pagination.summary', {
                                    page: meta?.page ?? filters.page,
                                    totalPages: meta?.totalPages ?? 1,
                                    count: meta?.totalCount ?? vendors.length,
                                })}
                            </p>
                            <div className="flex gap-3">
                                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={(meta?.page ?? filters.page) <= 1} onClick={() => updateFilters({ page: (meta?.page ?? filters.page) - 1 })}>{t('superAdmin.common.actions.previous')}</button>
                                <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-2 font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60" disabled={(meta?.page ?? filters.page) >= (meta?.totalPages ?? 1)} onClick={() => updateFilters({ page: (meta?.page ?? filters.page) + 1 })}>{t('superAdmin.common.actions.next')}</button>
                            </div>
                        </div>
                    </>
                )}
            </section>

            <StatementDrawer
                vendor={selectedVendor}
                statement={statementQuery.data}
                debtHistory={debtHistoryRows}
                payoutHistory={payoutHistoryRows}
                auditTimeline={auditQuery.data?.timeline ?? []}
                auditTotalCount={auditQuery.data?.meta?.totalCount ?? auditQuery.data?.timeline.length ?? 0}
                auditPage={auditQuery.data?.meta?.page ?? auditFilters.page}
                auditHasMore={(auditQuery.data?.meta?.page ?? auditFilters.page) < (auditQuery.data?.meta?.totalPages ?? 1)}
                isOpen={Boolean(selectedVendor)}
                isLoading={statementQuery.isLoading}
                isFetching={statementQuery.isFetching}
                isError={statementQuery.isError}
                errorMessage={statementErrorMessage}
                auditLoading={auditQuery.isLoading}
                auditFetching={auditQuery.isFetching}
                auditError={auditQuery.isError}
                auditErrorMessage={auditErrorMessage}
                debtHistoryLoading={debtHistoryQuery.isLoading}
                payoutHistoryLoading={payoutHistoryQuery.isLoading}
                exportBusy={isExporting}
                feedback={feedback}
                fromDate={statementFilters.fromDate}
                toDate={statementFilters.toDate}
                selectedTypes={statementFilters.transactionTypes}
                onClose={() => {
                    setActiveDialog(null)
                    setSelectedVendor(null)
                }}
                onRefresh={() => {
                    statementQuery.refetch()
                    debtHistoryQuery.refetch()
                    payoutHistoryQuery.refetch()
                    auditQuery.refetch()
                }}
                onExport={handleExport}
                onOpenAdjustment={openAdjustmentDialog}
                onOpenSettlement={openSettlementDialog}
                onFromDateChange={(value) => setStatementFilters((current) => ({ ...current, fromDate: value, page: 1 }))}
                onToDateChange={(value) => setStatementFilters((current) => ({ ...current, toDate: value, page: 1 }))}
                onToggleTransactionType={(value) =>
                    setStatementFilters((current) => ({
                        ...current,
                        page: 1,
                        transactionTypes: current.transactionTypes.includes(value)
                            ? current.transactionTypes.filter((item) => item !== value)
                            : [...current.transactionTypes, value],
                    }))
                }
                onResetFilters={() => setStatementFilters((current) => ({ ...current, page: 1, fromDate: '', toDate: '', transactionTypes: [] }))}
                onPreviousPage={() => setStatementFilters((current) => ({ ...current, page: Math.max(1, current.page - 1) }))}
                onNextPage={() => setStatementFilters((current) => ({ ...current, page: current.page + 1 }))}
                onPreviousAuditPage={() => setAuditFilters((current) => ({ ...current, page: Math.max(1, current.page - 1) }))}
                onNextAuditPage={() => setAuditFilters((current) => ({ ...current, page: current.page + 1 }))}
            />

            {activeDialog === 'adjustment' && selectedVendor ? (
                <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/45 px-4">
                    <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_30px_90px_-40px_rgba(15,23,42,0.45)]">
                        <h3 className="text-2xl font-semibold text-slate-950">{t('superAdmin.vendorFinanceWorkspace.dialogs.adjustment.title')}</h3>
                        <p className="mt-2 text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.dialogs.adjustment.description', { vendorName: selectedVendor.vendorName })}</p>
                        <div className="mt-6 grid gap-4 md:grid-cols-2">
                            <input type="number" min="0.01" step="0.01" value={adjustmentDraft.amount} onChange={(event) => setAdjustmentDraft((current) => ({ ...current, amount: event.target.value }))} placeholder={t('superAdmin.vendorFinanceWorkspace.dialogs.adjustment.amount')} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                            <select value={adjustmentDraft.direction} onChange={(event) => setAdjustmentDraft((current) => ({ ...current, direction: event.target.value as CommerceHubFinancialDirection }))} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="Credit">{t(ADJUSTMENT_DIRECTION_LABEL_KEYS.Credit)}</option>
                                <option value="Debit">{t(ADJUSTMENT_DIRECTION_LABEL_KEYS.Debit)}</option>
                            </select>
                            <input type="text" value="EGP" readOnly aria-readonly="true" className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600" />
                            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
                                {selectedVendor.amountOwedByVendor > 0
                                    ? t('superAdmin.vendorFinanceWorkspace.dialogs.adjustment.debtOutstanding')
                                    : t('superAdmin.vendorFinanceWorkspace.dialogs.adjustment.debtClear')}
                            </div>
                        </div>
                        <textarea value={adjustmentDraft.reason} onChange={(event) => setAdjustmentDraft((current) => ({ ...current, reason: event.target.value }))} placeholder={t('superAdmin.vendorFinanceWorkspace.dialogs.adjustment.reason')} className="mt-4 min-h-[120px] w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        {adjustmentMutation.isError ? <p className="mt-3 text-sm text-rose-700">{getErrorMessage(adjustmentMutation.error, t('superAdmin.vendorFinanceWorkspace.feedback.adjustmentFailed'))}</p> : null}
                        <div className="mt-6 flex justify-end gap-3">
                            <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700" onClick={() => setActiveDialog(null)}>{t('superAdmin.vendorFinanceWorkspace.actions.cancel')}</button>
                            <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60" disabled={adjustmentMutation.isPending || !adjustmentDraft.amount || !adjustmentDraft.reason.trim()} onClick={submitAdjustment}>{adjustmentMutation.isPending ? t('superAdmin.vendorFinanceWorkspace.actions.posting') : t('superAdmin.vendorFinanceWorkspace.actions.recordAdjustment')}</button>
                        </div>
                    </div>
                </div>
            ) : null}

            {activeDialog === 'settlement' && selectedVendor ? (
                <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/45 px-4">
                    <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_30px_90px_-40px_rgba(15,23,42,0.45)]">
                        <h3 className="text-2xl font-semibold text-slate-950">{t('superAdmin.vendorFinanceWorkspace.dialogs.settlement.title')}</h3>
                        <p className="mt-2 text-sm text-slate-500">{t('superAdmin.vendorFinanceWorkspace.dialogs.settlement.description')}</p>
                        <div className="mt-6 grid gap-4 md:grid-cols-2">
                            <input type="number" min="0.01" step="0.01" value={settlementDraft.amount} onChange={(event) => setSettlementDraft((current) => ({ ...current, amount: event.target.value }))} placeholder={t('superAdmin.vendorFinanceWorkspace.dialogs.settlement.amount')} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                            <select value={settlementDraft.flow} onChange={(event) => setSettlementDraft((current) => ({ ...current, flow: event.target.value as SuperAdminVendorSettlementFlow }))} className="rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100">
                                <option value="VendorPaysPlatform">{t(SETTLEMENT_FLOW_LABEL_KEYS.VendorPaysPlatform)}</option>
                                <option value="PlatformPaysVendor">{t(SETTLEMENT_FLOW_LABEL_KEYS.PlatformPaysVendor)}</option>
                            </select>
                            <input type="text" value="EGP" readOnly aria-readonly="true" className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600" />
                            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
                                {settlementDraft.flow === 'VendorPaysPlatform'
                                    ? t('superAdmin.vendorFinanceWorkspace.dialogs.settlement.vendorPaysHint')
                                    : t('superAdmin.vendorFinanceWorkspace.dialogs.settlement.platformPaysHint')}
                            </div>
                        </div>
                        <textarea value={settlementDraft.reason} onChange={(event) => setSettlementDraft((current) => ({ ...current, reason: event.target.value }))} placeholder={t('superAdmin.vendorFinanceWorkspace.dialogs.settlement.reason')} className="mt-4 min-h-[120px] w-full rounded-2xl border border-slate-200 px-4 py-3 text-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100" />
                        {settlementMutation.isError ? <p className="mt-3 text-sm text-rose-700">{getErrorMessage(settlementMutation.error, t('superAdmin.vendorFinanceWorkspace.feedback.settlementFailed'))}</p> : null}
                        <div className="mt-6 flex justify-end gap-3">
                            <button type="button" className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700" onClick={() => setActiveDialog(null)}>{t('superAdmin.vendorFinanceWorkspace.actions.cancel')}</button>
                            <button type="button" className="rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-60" disabled={settlementMutation.isPending || !settlementDraft.amount || !settlementDraft.reason.trim()} onClick={submitSettlement}>{settlementMutation.isPending ? t('superAdmin.vendorFinanceWorkspace.actions.posting') : t('superAdmin.vendorFinanceWorkspace.actions.confirmSettlement')}</button>
                        </div>
                    </div>
                </div>
            ) : null}
        </>
    )
}
