import { type ReactNode, useCallback, useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
    AlertTriangle,
    ArrowUpRight,
    CalendarDays,
    CheckCircle2,
    Clock3,
    Loader2,
    PackageCheck,
    RefreshCw,
    RotateCcw,
    ShieldAlert,
    Truck,
    X,
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { getSuperAdminOrderTenants, type SuperAdminTenantOption } from '../orders/ordersService'
import { commerceHubRefundGuidanceContent } from '../../shared/content/commerceHubRefundGuidance'
import {
    SUPER_ADMIN_REFUND_STATUS_OPTIONS,
    approveSuperAdminRefund,
    executeSuperAdminRefund,
    getSuperAdminRefundDetails,
    getSuperAdminRefunds,
    markSuperAdminReturnFailed,
    overrideSuperAdminReturnStatus,
    rejectSuperAdminRefund,
    retrySuperAdminRefund,
    type RefundPayoutMethod,
    type RefundExecutionTrigger,
    type SuperAdminRefundDetails,
    type SuperAdminOverrideReturnStatus,
    type SuperAdminReturnDetails,
    type SuperAdminReturnStatus,
    type SuperAdminRefundStatus,
    type SuperAdminRefundsFilters,
} from './refundsService'
import {
    requireCurrentSuperAdminRefundSelection,
    requireSuperAdminRefundDetailsForCurrentSelection,
    requireSuperAdminReturnForCurrentRefund,
} from './refundLifecycleAuthority'

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 20
const pageSizeOptions = [20, 50, 100]
const REFUND_STATUS_LABEL_KEYS: Record<string, string> = {
    Requested: 'superAdmin.refundsPage.statuses.requested',
    UnderReview: 'superAdmin.refundsPage.statuses.underReview',
    Approved: 'superAdmin.refundsPage.statuses.approved',
    WaitingForReturn: 'superAdmin.refundsPage.statuses.waitingForReturn',
    AwaitingPayout: 'superAdmin.refundsPage.statuses.awaitingPayout',
    ReadyForExecution: 'superAdmin.refundsPage.statuses.awaitingPayout',
    Processing: 'superAdmin.refundsPage.statuses.processing',
    Paid: 'superAdmin.refundsPage.statuses.paid',
    Executed: 'superAdmin.refundsPage.statuses.paid',
    Completed: 'superAdmin.refundsPage.statuses.paid',
    Failed: 'superAdmin.refundsPage.statuses.failed',
    Rejected: 'superAdmin.refundsPage.statuses.rejected',
    Cancelled: 'superAdmin.refundsPage.statuses.cancelled',
}

const EXECUTION_PATH_LABEL_KEYS: Record<string, string> = {
    OnlineBeforePayout: 'superAdmin.refundsPage.executionPaths.onlineBeforePayout',
    OnlineAfterPayout: 'superAdmin.refundsPage.executionPaths.onlineAfterPayout',
    CashOnDeliveryDebtReduction: 'superAdmin.refundsPage.executionPaths.cashOnDeliveryDebtReduction',
}

const PAYMENT_TYPE_LABEL_KEYS: Record<string, string> = {
    CashOnDelivery: 'superAdmin.refundsPage.paymentTypes.cashOnDelivery',
    Online: 'superAdmin.refundsPage.paymentTypes.online',
}

const REFUND_REASON_LABEL_KEYS: Record<string, string> = {
    DamagedItem: 'superAdmin.refundsPage.reasons.damagedItem',
    NotAsDescribed: 'superAdmin.refundsPage.reasons.notAsDescribed',
    WrongItem: 'superAdmin.refundsPage.reasons.wrongItem',
    MissingParts: 'superAdmin.refundsPage.reasons.missingParts',
    QualityIssue: 'superAdmin.refundsPage.reasons.qualityIssue',
    Other: 'superAdmin.refundsPage.reasons.other',
}

const PAYOUT_METHOD_LABEL_KEYS: Record<string, string> = {
    VodafoneCash: 'superAdmin.refundsPage.payoutMethods.vodafoneCash',
    InstaPay: 'superAdmin.refundsPage.payoutMethods.instaPay',
    BankTransfer: 'superAdmin.refundsPage.payoutMethods.bankTransfer',
}

const RETURN_STATUS_LABEL_KEYS: Record<string, string> = {
    Pending: 'superAdmin.refundsPage.returnStatuses.pending',
    PickupScheduled: 'superAdmin.refundsPage.returnStatuses.pickupScheduled',
    PickedUp: 'superAdmin.refundsPage.returnStatuses.pickedUp',
    InTransit: 'superAdmin.refundsPage.returnStatuses.inTransit',
    DeliveredToVendor: 'superAdmin.refundsPage.returnStatuses.deliveredToVendor',
    Confirmed: 'superAdmin.refundsPage.returnStatuses.confirmed',
    Failed: 'superAdmin.refundsPage.returnStatuses.failed',
}

const RETURN_TRIGGER_LABEL_KEYS: Record<string, string> = {
    OnPickup: 'superAdmin.refundsPage.returnTriggers.onPickup',
    OnDelivery: 'superAdmin.refundsPage.returnTriggers.onDelivery',
    OnVendorConfirmation: 'superAdmin.refundsPage.returnTriggers.onVendorConfirmation',
}

function parsePositiveInt(value: string | null, fallback: number): number {
    const parsed = Number.parseInt(value ?? '', 10)
    return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function parseStatus(value: string | null): SuperAdminRefundStatus | '' {
    return SUPER_ADMIN_REFUND_STATUS_OPTIONS.includes(value as SuperAdminRefundStatus)
        ? (value as SuperAdminRefundStatus)
        : ''
}

function buildFilters(searchParams: URLSearchParams): SuperAdminRefundsFilters {
    return {
        page: parsePositiveInt(searchParams.get('page'), DEFAULT_PAGE),
        pageSize: parsePositiveInt(searchParams.get('pageSize'), DEFAULT_PAGE_SIZE),
        status: parseStatus(searchParams.get('status')),
        vendorId: searchParams.get('vendorId') ?? '',
        fromDate: searchParams.get('fromDate') ?? '',
        toDate: searchParams.get('toDate') ?? '',
    }
}

function formatCurrency(amount: number, currency: string, locale = 'en-US'): string {
    return new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: currency || 'EGP',
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(amount)
}

function formatSignedCurrency(amount: number, currency: string, locale = 'en-US'): string {
    const prefix = amount >= 0 ? '+' : '-'
    return `${prefix}${formatCurrency(Math.abs(amount), currency, locale)}`
}

function formatDateTime(value: string | null | undefined, locale: string, fallback: string): string {
    if (!value) {
        return fallback
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return fallback
    }

    return parsed.toLocaleString(locale)
}

function getErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof Error && error.message) {
        return error.message
    }

    return fallback
}

function getRefundStatusTone(status: string): string {
    switch (status) {
        case 'Requested':
        case 'Cancelled':
            return 'border-slate-200 bg-slate-100 text-slate-800'
        case 'UnderReview':
            return 'border-sky-200 bg-sky-50 text-sky-900'
        case 'Approved':
        case 'WaitingForReturn':
            return 'border-amber-200 bg-amber-50 text-amber-900'
        case 'AwaitingPayout':
        case 'ReadyForExecution':
            return 'border-yellow-200 bg-yellow-50 text-yellow-900'
        case 'Processing':
            return 'border-yellow-200 bg-yellow-50 text-yellow-900'
        case 'Paid':
        case 'Executed':
        case 'Completed':
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
        case 'Failed':
        case 'Rejected':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        default:
            return 'border-slate-200 bg-slate-50 text-slate-700'
    }
}

function FiltersPanel({
    filters,
    tenants,
    isLoadingTenants,
    isRefreshing,
    onStatusChange,
    onVendorChange,
    onFromDateChange,
    onToDateChange,
    onReset,
}: {
    filters: SuperAdminRefundsFilters
    tenants: SuperAdminTenantOption[]
    isLoadingTenants: boolean
    isRefreshing: boolean
    onStatusChange: (value: SuperAdminRefundStatus | '') => void
    onVendorChange: (value: string) => void
    onFromDateChange: (value: string) => void
    onToDateChange: (value: string) => void
    onReset: () => void
}) {
    const { t } = useTranslation()

    return (
        <section className="sticky top-4 z-20 overflow-hidden rounded-[32px] border border-slate-200 bg-white/95 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)] backdrop-blur">
            <div className="border-b border-slate-100 px-6 py-5">
                <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                            <ShieldAlert className="h-4 w-4" />
                            {t('superAdmin.refundsPage.filters.badge')}
                        </div>
                        <p className="mt-3 text-sm leading-6 text-slate-500">
                            {t('superAdmin.refundsPage.filters.description')}
                        </p>
                    </div>

                    <button
                        type="button"
                        className="inline-flex items-center justify-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        onClick={onReset}
                    >
                        <RotateCcw className="h-4 w-4" />
                        {t('superAdmin.refundsPage.filters.reset')}
                    </button>
                </div>
            </div>

            <div className="grid gap-4 px-6 py-6 md:grid-cols-2 xl:grid-cols-4">
                <div className="space-y-2">
                    <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.refundsPage.filters.status')}</label>
                    <select
                        value={filters.status ?? ''}
                        onChange={(event) => onStatusChange(event.target.value as SuperAdminRefundStatus | '')}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    >
                        <option value="">{t('superAdmin.refundsPage.filters.allStatuses')}</option>
                        {SUPER_ADMIN_REFUND_STATUS_OPTIONS.map((status) => (
                            <option key={status} value={status}>
                                {formatRefundStatusLabel(status, t)}
                            </option>
                        ))}
                    </select>
                </div>

                <div className="space-y-2">
                    <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.refundsPage.filters.vendor')}</label>
                    <select
                        value={filters.vendorId ?? ''}
                        disabled={isLoadingTenants}
                        onChange={(event) => onVendorChange(event.target.value)}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100 disabled:cursor-not-allowed disabled:bg-slate-50"
                    >
                        <option value="">{isLoadingTenants ? t('superAdmin.refundsPage.filters.loadingVendors') : t('superAdmin.refundsPage.filters.allVendors')}</option>
                        {tenants.map((tenant) => (
                            <option key={tenant.id} value={tenant.id}>
                                {tenant.name}
                            </option>
                        ))}
                    </select>
                </div>

                <div className="space-y-2">
                    <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.refundsPage.filters.from')}</label>
                    <input
                        type="date"
                        value={filters.fromDate ?? ''}
                        onChange={(event) => onFromDateChange(event.target.value)}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    />
                </div>

                <div className="space-y-2">
                    <label className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('superAdmin.refundsPage.filters.to')}</label>
                    <input
                        type="date"
                        value={filters.toDate ?? ''}
                        onChange={(event) => onToDateChange(event.target.value)}
                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    />
                </div>
            </div>

            <div className="border-t border-slate-100 bg-slate-50/80 px-6 py-4 text-xs font-medium tracking-[0.04em] text-slate-500">
                {isRefreshing
                    ? t('superAdmin.refundsPage.filters.refreshing')
                    : t('superAdmin.refundsPage.filters.footer')}
            </div>
        </section>
    )
}

function ActionModal({
    title,
    description,
    confirmLabel,
    confirmTone,
    noteLabel,
    noteValue,
    notePlaceholder,
    onNoteChange,
    isSubmitting,
    errorMessage,
    onCancel,
    onConfirm,
}: {
    title: string
    description: string
    confirmLabel: string
    confirmTone: string
    noteLabel: string
    noteValue: string
    notePlaceholder: string
    onNoteChange: (value: string) => void
    isSubmitting: boolean
    errorMessage?: string | null
    onCancel: () => void
    onConfirm: () => void
}) {
    const { t } = useTranslation()

    return (
        <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-950/55 px-4">
            <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-2xl">
                <div className="flex items-start justify-between gap-4">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-600">{t('superAdmin.refundsPage.actionModal.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{title}</h2>
                    </div>

                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900"
                        aria-label={t('superAdmin.refundsPage.actions.close')}
                        onClick={onCancel}
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <p className="mt-4 text-sm leading-7 text-slate-600">{description}</p>

                <div className="mt-5 space-y-2">
                    <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{noteLabel}</label>
                    <textarea
                        value={noteValue}
                        onChange={(event) => onNoteChange(event.target.value)}
                        placeholder={notePlaceholder}
                        rows={4}
                        className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                    />
                </div>

                {errorMessage ? (
                    <div className="mt-4 rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                        {errorMessage}
                    </div>
                ) : null}

                <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:justify-end">
                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        onClick={onCancel}
                        disabled={isSubmitting}
                    >
                        {t('superAdmin.refundsPage.actions.cancel')}
                    </button>
                    <button
                        type="button"
                        className={`inline-flex items-center justify-center gap-2 rounded-2xl px-5 py-3 text-sm font-semibold text-white transition ${confirmTone}`}
                        onClick={onConfirm}
                        disabled={isSubmitting}
                    >
                        {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
                        {confirmLabel}
                    </button>
                </div>
            </div>
        </div>
    )
}

function formatRefundStatusLabel(status: string, t: (key: string, options?: Record<string, unknown>) => string): string {
    return REFUND_STATUS_LABEL_KEYS[status]
        ? t(REFUND_STATUS_LABEL_KEYS[status])
        : status.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatExecutionPathLabel(value: string, t: (key: string, options?: Record<string, unknown>) => string): string {
    return EXECUTION_PATH_LABEL_KEYS[value]
        ? t(EXECUTION_PATH_LABEL_KEYS[value])
        : value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatPaymentTypeLabel(value: string, t: (key: string, options?: Record<string, unknown>) => string): string {
    return PAYMENT_TYPE_LABEL_KEYS[value]
        ? t(PAYMENT_TYPE_LABEL_KEYS[value])
        : value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatRefundReasonLabel(value: string, t: (key: string, options?: Record<string, unknown>) => string): string {
    return REFUND_REASON_LABEL_KEYS[value]
        ? t(REFUND_REASON_LABEL_KEYS[value])
        : value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatRefundPayoutMethodLabel(value: string, t: (key: string, options?: Record<string, unknown>) => string): string {
    return PAYOUT_METHOD_LABEL_KEYS[value]
        ? t(PAYOUT_METHOD_LABEL_KEYS[value])
        : value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatReturnStatusLabel(value: SuperAdminReturnStatus, t: (key: string, options?: Record<string, unknown>) => string): string {
    return RETURN_STATUS_LABEL_KEYS[value]
        ? t(RETURN_STATUS_LABEL_KEYS[value])
        : value.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function formatReturnTriggerLabel(value: RefundExecutionTrigger, t: (key: string, options?: Record<string, unknown>) => string): string {
    return RETURN_TRIGGER_LABEL_KEYS[value]
        ? t(RETURN_TRIGGER_LABEL_KEYS[value])
        : value
}

function getReturnStatusTone(status: SuperAdminReturnStatus): string {
    switch (status) {
        case 'Pending':
            return 'border-slate-200 bg-slate-100 text-slate-800'
        case 'PickupScheduled':
            return 'border-sky-200 bg-sky-50 text-sky-900'
        case 'PickedUp':
            return 'border-amber-200 bg-amber-50 text-amber-900'
        case 'InTransit':
            return 'border-indigo-200 bg-indigo-50 text-indigo-900'
        case 'DeliveredToVendor':
            return 'border-emerald-200 bg-emerald-50 text-emerald-900'
        case 'Confirmed':
            return 'border-green-200 bg-green-50 text-green-900'
        case 'Failed':
            return 'border-rose-200 bg-rose-50 text-rose-900'
        default:
            return 'border-slate-200 bg-slate-50 text-slate-700'
    }
}

function formatDateTimeInputValue(value?: string | null): string {
    const date = value ? new Date(value) : new Date()
    if (Number.isNaN(date.getTime())) {
        return ''
    }

    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
    return local.toISOString().slice(0, 16)
}

function getReturnStageTimestamp(
    returnRequest: SuperAdminReturnDetails,
    status: SuperAdminOverrideReturnStatus | 'Failed',
): string | null | undefined {
    switch (status) {
        case 'PickupScheduled':
            return returnRequest.pickupScheduledAt
        case 'PickedUp':
            return returnRequest.pickedUpAt
        case 'InTransit':
            return returnRequest.inTransitAt
        case 'DeliveredToVendor':
            return returnRequest.deliveredAt
        case 'Confirmed':
            return returnRequest.confirmedAt
        case 'Failed':
            return returnRequest.updatedAt
        case 'Pending':
        default:
            return returnRequest.updatedAt ?? returnRequest.createdAt
    }
}

function getRefundUnlockedAt(refund: SuperAdminRefundDetails): string | null {
    const returnRequest = refund.returnRequest
    if (!returnRequest || !returnRequest.isExecutionTriggerReached) {
        return null
    }

    switch (returnRequest.refundExecutionTrigger) {
        case 'OnPickup':
            return returnRequest.pickedUpAt ?? returnRequest.updatedAt ?? null
        case 'OnDelivery':
            return returnRequest.deliveredAt ?? returnRequest.updatedAt ?? null
        case 'OnVendorConfirmation':
            return returnRequest.confirmedAt ?? returnRequest.updatedAt ?? null
        default:
            return returnRequest.updatedAt ?? null
    }
}

function isRefundAwaitingPayout(status: SuperAdminRefundStatus): boolean {
    return status === 'AwaitingPayout' || status === 'ReadyForExecution'
}

function isRefundPaid(status: SuperAdminRefundStatus): boolean {
    return status === 'Paid' || status === 'Executed' || status === 'Completed'
}

function ExecuteRefundModal({
    refund,
    method,
    transactionReference,
    notes,
    confirmMoneySent,
    confirmMethodOverride,
    isSubmitting,
    errorMessage,
    onMethodChange,
    onTransactionReferenceChange,
    onNotesChange,
    onConfirmMoneySentChange,
    onConfirmMethodOverrideChange,
    onCancel,
    onConfirm,
}: {
    refund: SuperAdminRefundDetails
    method: RefundPayoutMethod
    transactionReference: string
    notes: string
    confirmMoneySent: boolean
    confirmMethodOverride: boolean
    isSubmitting: boolean
    errorMessage?: string | null
    onMethodChange: (value: RefundPayoutMethod) => void
    onTransactionReferenceChange: (value: string) => void
    onNotesChange: (value: string) => void
    onConfirmMoneySentChange: (value: boolean) => void
    onConfirmMethodOverrideChange: (value: boolean) => void
    onCancel: () => void
    onConfirm: () => void
}) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const payoutMethod = refund.payout?.method
    const methodMismatch = Boolean(payoutMethod && payoutMethod !== method)

    return (
        <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/55 px-4">
            <div className="w-full max-w-2xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-2xl">
                <div className="flex items-start justify-between gap-4">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-amber-700">{t('superAdmin.refundsPage.execute.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{t('superAdmin.refundsPage.execute.title')}</h2>
                    </div>

                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900"
                        aria-label={t('superAdmin.refundsPage.actions.close')}
                        onClick={onCancel}
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <div className="mt-5 rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-950">
                    <p className="font-semibold">{t('superAdmin.refundsPage.execute.manualPayoutRequired')}</p>
                    <p className="mt-2">{t('superAdmin.refundsPage.execute.sendBeforeConfirm')}</p>
                    <p className="mt-4 font-semibold">{t('superAdmin.refundsPage.execute.payoutDestination')}</p>
                    <p className="mt-2">
                        {formatRefundPayoutMethodLabel(method, t)} {'->'} {refund.payout?.maskedAccountIdentifier ?? t('superAdmin.refundsPage.labels.unavailable')}
                    </p>
                    <p className="mt-2">{t('superAdmin.refundsPage.execute.amount', { amount: formatCurrency(refund.amount, refund.currency, locale) })}</p>
                    <p className="mt-2 font-semibold">{t('superAdmin.refundsPage.execute.confirmAfterSending')}</p>
                </div>

                <div className="mt-6 grid gap-4 md:grid-cols-2">
                    <div className="space-y-2">
                        <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.refundsPage.execute.method')}</label>
                        <select
                            value={method}
                            onChange={(event) => onMethodChange(event.target.value as RefundPayoutMethod)}
                            className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-amber-300 focus:ring-4 focus:ring-amber-100"
                            disabled={isSubmitting}
                        >
                            <option value="VodafoneCash">{t('superAdmin.refundsPage.payoutMethods.vodafoneCash')}</option>
                            <option value="InstaPay">{t('superAdmin.refundsPage.payoutMethods.instaPay')}</option>
                            <option value="BankTransfer">{t('superAdmin.refundsPage.payoutMethods.bankTransfer')}</option>
                        </select>
                    </div>

                    <div className="space-y-2">
                        <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.refundsPage.execute.transactionReference')}</label>
                        <input
                            type="text"
                            value={transactionReference}
                            onChange={(event) => onTransactionReferenceChange(event.target.value)}
                            placeholder={t('superAdmin.refundsPage.execute.transactionReferencePlaceholder')}
                            className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-amber-300 focus:ring-4 focus:ring-amber-100"
                            disabled={isSubmitting}
                        />
                    </div>
                </div>

                <div className="mt-4 space-y-2">
                    <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.refundsPage.execute.notes')}</label>
                    <textarea
                        value={notes}
                        onChange={(event) => onNotesChange(event.target.value)}
                        placeholder={t('superAdmin.refundsPage.execute.notesPlaceholder')}
                        rows={4}
                        className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-amber-300 focus:ring-4 focus:ring-amber-100"
                        disabled={isSubmitting}
                    />
                </div>

                <div className="mt-4 rounded-[24px] border border-amber-200 bg-amber-50/70 px-4 py-4 text-sm text-amber-950">
                    <label className="flex items-start gap-3">
                        <input
                            type="checkbox"
                            checked={confirmMoneySent}
                            onChange={(event) => onConfirmMoneySentChange(event.target.checked)}
                            className="mt-1 h-4 w-4 rounded border-amber-300 text-amber-600 focus:ring-amber-500"
                            disabled={isSubmitting}
                        />
                        <span>{t('superAdmin.refundsPage.execute.confirmMoneySent')}</span>
                    </label>
                </div>

                {methodMismatch ? (
                    <div className="mt-4 rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-4 text-sm text-rose-950">
                        <p className="font-semibold">
                            {t('superAdmin.refundsPage.execute.methodMismatch', {
                                method: formatRefundPayoutMethodLabel(payoutMethod as string, t),
                            })}
                        </p>
                        <label className="mt-3 flex items-start gap-3">
                            <input
                                type="checkbox"
                                checked={confirmMethodOverride}
                                onChange={(event) => onConfirmMethodOverrideChange(event.target.checked)}
                                className="mt-1 h-4 w-4 rounded border-slate-300 text-rose-600 focus:ring-rose-500"
                                disabled={isSubmitting}
                            />
                            <span>{t('superAdmin.refundsPage.execute.confirmMethodOverride')}</span>
                        </label>
                    </div>
                ) : null}

                {errorMessage ? (
                    <div className="mt-4 rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                        {errorMessage}
                    </div>
                ) : null}

                <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:justify-end">
                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        onClick={onCancel}
                        disabled={isSubmitting}
                    >
                        {t('superAdmin.refundsPage.actions.cancel')}
                    </button>
                    <button
                        type="button"
                        className="inline-flex items-center justify-center gap-2 rounded-2xl bg-amber-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700 disabled:cursor-not-allowed disabled:bg-amber-400"
                        onClick={onConfirm}
                        disabled={isSubmitting}
                    >
                        {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
                        {t('superAdmin.refundsPage.execute.title')}
                    </button>
                </div>
            </div>
        </div>
    )
}

function ReturnTimeline({ returnRequest }: { returnRequest: SuperAdminReturnDetails }) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const unavailable = t('superAdmin.refundsPage.labels.unavailable')
    const steps = [
        {
            key: 'Pending',
            label: t('superAdmin.refundsPage.returnStatuses.pending'),
            completed: true,
            date: returnRequest.createdAt,
        },
        {
            key: 'PickupScheduled',
            label: t('superAdmin.refundsPage.returnStatuses.pickupScheduled'),
            completed: Boolean(returnRequest.pickupScheduledAt),
            date: returnRequest.pickupScheduledAt,
        },
        {
            key: 'PickedUp',
            label: t('superAdmin.refundsPage.returnStatuses.pickedUp'),
            completed: Boolean(returnRequest.pickedUpAt),
            date: returnRequest.pickedUpAt,
        },
        {
            key: 'InTransit',
            label: t('superAdmin.refundsPage.returnStatuses.inTransit'),
            completed: Boolean(returnRequest.inTransitAt),
            date: returnRequest.inTransitAt,
        },
        {
            key: 'DeliveredToVendor',
            label: t('superAdmin.refundsPage.returnStatuses.deliveredToVendor'),
            completed: Boolean(returnRequest.deliveredAt),
            date: returnRequest.deliveredAt,
        },
        {
            key: 'Confirmed',
            label: t('superAdmin.refundsPage.returnStatuses.confirmed'),
            completed: Boolean(returnRequest.confirmedAt),
            date: returnRequest.confirmedAt,
        },
        {
            key: 'Failed',
            label: t('superAdmin.refundsPage.returnStatuses.failed'),
            completed: returnRequest.status === 'Failed',
            date: returnRequest.status === 'Failed' ? returnRequest.updatedAt : null,
        },
    ] as const

    return (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {steps.map((step) => (
                <div
                    key={step.key}
                    className={`rounded-[24px] border px-4 py-4 shadow-sm ${
                        step.completed
                            ? 'border-emerald-200 bg-emerald-50 text-emerald-950'
                            : 'border-slate-200 bg-white text-slate-700'
                    }`}
                >
                    <div className="flex items-center gap-2 text-sm font-semibold">
                        {step.completed ? <PackageCheck className="h-4 w-4" /> : <Truck className="h-4 w-4" />}
                        {step.label}
                    </div>
                    <p className="mt-3 text-xs uppercase tracking-[0.18em] opacity-70">
                        {step.completed ? t('superAdmin.refundsPage.timeline.recorded') : t('superAdmin.refundsPage.timeline.awaiting')}
                    </p>
                    <p className="mt-2 text-sm font-medium">{formatDateTime(step.date, locale, unavailable)}</p>
                </div>
            ))}
        </div>
    )
}

function getDefaultOverrideStatus(returnRequest: SuperAdminReturnDetails): SuperAdminOverrideReturnStatus | null {
    if (!returnRequest.canOverrideStatus || returnRequest.allowedOverrideStatuses.length === 0) {
        return null
    }

    const currentStatus = returnRequest.status as SuperAdminOverrideReturnStatus
    return returnRequest.allowedOverrideStatuses.includes(currentStatus)
        ? currentStatus
        : returnRequest.allowedOverrideStatuses[0] ?? null
}

function RefundWorkflowTimeline({ refund }: { refund: SuperAdminRefundDetails }) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const unavailable = t('superAdmin.refundsPage.labels.unavailable')
    const unlockedAt = getRefundUnlockedAt(refund)
    const paidAt = refund.execution?.executedAt ?? refund.completedAtUtc ?? null
    const steps = [
        {
            key: 'Requested',
            label: t('superAdmin.refundsPage.workflow.requestSubmitted'),
            completed: true,
            date: refund.createdAt,
        },
        {
            key: 'Approved',
            label: t('superAdmin.refundsPage.statuses.approved'),
            completed: Boolean(refund.approvedAtUtc),
            date: refund.approvedAtUtc,
        },
        {
            key: 'AwaitingPayout',
            label: t('superAdmin.refundsPage.workflow.manualPayoutRequired'),
            completed: Boolean(unlockedAt),
            date: unlockedAt,
        },
        {
            key: 'Paid',
            label: t('superAdmin.refundsPage.workflow.refundPaid'),
            completed: isRefundPaid(refund.status),
            date: paidAt,
        },
    ] as const

    return (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {steps.map((step) => (
                <div
                    key={step.key}
                    className={`rounded-[24px] border px-4 py-4 shadow-sm ${
                        step.completed
                            ? 'border-emerald-200 bg-emerald-50 text-emerald-950'
                            : 'border-slate-200 bg-white text-slate-700'
                    }`}
                >
                    <div className="flex items-center gap-2 text-sm font-semibold">
                        {step.completed ? <CheckCircle2 className="h-4 w-4" /> : <Clock3 className="h-4 w-4" />}
                        {step.label}
                    </div>
                    <p className="mt-3 text-xs uppercase tracking-[0.18em] opacity-70">
                        {step.completed ? t('superAdmin.refundsPage.timeline.recorded') : t('superAdmin.refundsPage.timeline.awaiting')}
                    </p>
                    <p className="mt-2 text-sm font-medium">{formatDateTime(step.date, locale, unavailable)}</p>
                </div>
            ))}
        </div>
    )
}

function ReturnTransitionModal({
    title,
    description,
    statusLabel,
    selectedStatus,
    statusOptions,
    timestampLabel,
    confirmLabel,
    confirmTone = 'bg-sky-600 hover:bg-sky-700 disabled:cursor-not-allowed disabled:bg-sky-400',
    accentTone = 'text-sky-700',
    isSubmitting,
    errorMessage,
    timestampValue,
    trackingNumber,
    courierName,
    notes,
    onStatusChange,
    onTimestampChange,
    onTrackingNumberChange,
    onCourierNameChange,
    onNotesChange,
    onCancel,
    onConfirm,
}: {
    title: string
    description: string
    statusLabel?: string
    selectedStatus?: string
    statusOptions?: readonly string[]
    timestampLabel: string
    confirmLabel: string
    confirmTone?: string
    accentTone?: string
    isSubmitting: boolean
    errorMessage?: string | null
    timestampValue: string
    trackingNumber: string
    courierName: string
    notes: string
    onStatusChange?: (value: string) => void
    onTimestampChange: (value: string) => void
    onTrackingNumberChange: (value: string) => void
    onCourierNameChange: (value: string) => void
    onNotesChange: (value: string) => void
    onCancel: () => void
    onConfirm: () => void
}) {
    const { t } = useTranslation()

    return (
        <div className="fixed inset-0 z-[70] flex items-center justify-center bg-slate-950/55 px-4">
            <div className="w-full max-w-2xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-2xl">
                <div className="flex items-start justify-between gap-4">
                    <div>
                        <p className={`text-xs font-semibold uppercase tracking-[0.24em] ${accentTone}`}>{t('superAdmin.refundsPage.returnTransition.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{title}</h2>
                        <p className="mt-3 max-w-2xl text-sm leading-6 text-slate-600">{description}</p>
                    </div>

                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900"
                        aria-label={t('superAdmin.refundsPage.actions.close')}
                        onClick={onCancel}
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <div className="mt-6 grid gap-4 md:grid-cols-2">
                    {statusOptions && selectedStatus && onStatusChange ? (
                        <div className="space-y-2 md:col-span-2">
                            <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                {statusLabel ?? t('superAdmin.refundsPage.returnTransition.targetStatus')}
                            </label>
                            <select
                                value={selectedStatus}
                                onChange={(event) => onStatusChange(event.target.value)}
                                className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                disabled={isSubmitting}
                            >
                                {statusOptions.map((status) => (
                                    <option key={status} value={status}>
                                        {formatReturnStatusLabel(status as SuperAdminReturnStatus, t)}
                                    </option>
                                ))}
                            </select>
                        </div>
                    ) : null}

                    <div className="space-y-2">
                        <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                            {timestampLabel}
                        </label>
                        <input
                            type="datetime-local"
                            value={timestampValue}
                            onChange={(event) => onTimestampChange(event.target.value)}
                            className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                            disabled={isSubmitting}
                        />
                    </div>

                    <div className="space-y-2">
                        <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.refundsPage.returnTransition.trackingNumber')}</label>
                        <input
                            type="text"
                            value={trackingNumber}
                            onChange={(event) => onTrackingNumberChange(event.target.value)}
                            placeholder={t('superAdmin.refundsPage.returnTransition.trackingPlaceholder')}
                            className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                            disabled={isSubmitting}
                        />
                    </div>
                </div>

                <div className="mt-4 grid gap-4 md:grid-cols-2">
                    <div className="space-y-2">
                        <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.refundsPage.returnTransition.courier')}</label>
                        <input
                            type="text"
                            value={courierName}
                            onChange={(event) => onCourierNameChange(event.target.value)}
                            placeholder={t('superAdmin.refundsPage.returnTransition.courierPlaceholder')}
                            className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                            disabled={isSubmitting}
                        />
                    </div>

                    <div className="space-y-2">
                        <label className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">{t('superAdmin.refundsPage.execute.notes')}</label>
                        <textarea
                            value={notes}
                            onChange={(event) => onNotesChange(event.target.value)}
                            rows={3}
                            placeholder={t('superAdmin.refundsPage.returnTransition.notesPlaceholder')}
                            className="w-full rounded-[24px] border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                            disabled={isSubmitting}
                        />
                    </div>
                </div>

                {errorMessage ? (
                    <div className="mt-4 rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                        {errorMessage}
                    </div>
                ) : null}

                <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:justify-end">
                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                        onClick={onCancel}
                        disabled={isSubmitting}
                    >
                        {t('superAdmin.refundsPage.actions.cancel')}
                    </button>
                    <button
                        type="button"
                        className={`inline-flex items-center justify-center gap-2 rounded-2xl px-5 py-3 text-sm font-semibold text-white transition ${confirmTone}`}
                        onClick={onConfirm}
                        disabled={isSubmitting}
                    >
                        {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <CalendarDays className="h-4 w-4" />}
                        {confirmLabel}
                    </button>
                </div>
            </div>
        </div>
    )
}

function describeExecutionPath(
    details: SuperAdminRefundDetails['financialPreview'],
    t: (key: string, options?: Record<string, unknown>) => string,
): string {
    if (details.impactSummary) {
        return details.impactSummary
    }

    switch (details.executionPath) {
        case 'OnlineBeforePayout':
            return t('superAdmin.refundsPage.executionDescriptions.onlineBeforePayout')
        case 'OnlineAfterPayout':
            return t('superAdmin.refundsPage.executionDescriptions.onlineAfterPayout')
        case 'CashOnDeliveryDebtReduction':
            return t('superAdmin.refundsPage.executionDescriptions.cashOnDeliveryDebtReduction')
        default:
            return t('superAdmin.refundsPage.executionDescriptions.default')
    }
}

function StatCard({
    label,
    value,
    caption,
    tone,
}: {
    label: string
    value: string
    caption: string
    tone: string
}) {
    return (
        <div className={`rounded-3xl border px-5 py-4 shadow-sm ${tone}`}>
            <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-75">{label}</p>
            <p className="mt-2 text-2xl font-semibold">{value}</p>
            <p className="mt-2 text-xs font-medium opacity-80">{caption}</p>
        </div>
    )
}

function DetailField({
    label,
    value,
}: {
    label: string
    value: string
}) {
    return (
        <div className="rounded-3xl border border-slate-200 bg-slate-50 px-4 py-3">
            <p className="text-[11px] font-semibold uppercase tracking-[0.2em] text-slate-500">{label}</p>
            <p className="mt-2 text-sm font-medium text-slate-900">{value}</p>
        </div>
    )
}

function DetailSection({
    title,
    caption,
    children,
}: {
    title: string
    caption: string
    children: ReactNode
}) {
    return (
        <section className="rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm">
            <div className="flex flex-col gap-1 border-b border-slate-100 pb-4">
                <h3 className="text-lg font-semibold text-slate-950">{title}</h3>
                <p className="text-sm leading-6 text-slate-500">{caption}</p>
            </div>
            <div className="mt-4">{children}</div>
        </section>
    )
}

function EvidenceGallery({
    media,
}: {
    media: SuperAdminRefundDetails['media']
}) {
    const { t } = useTranslation()

    if (!media.length) {
        return (
            <div className="rounded-[24px] border border-dashed border-slate-300 bg-slate-50 px-5 py-6 text-sm text-slate-500">
                {t('superAdmin.refundsPage.evidence.empty')}
            </div>
        )
    }

    return (
        <div className="grid gap-4 md:grid-cols-2">
            {media.map((item) => (
                <a
                    key={item.mediaId}
                    href={item.url}
                    target="_blank"
                    rel="noreferrer"
                    className="overflow-hidden rounded-[24px] border border-slate-200 bg-slate-50 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"
                >
                    <div className="aspect-[4/3] overflow-hidden bg-slate-100">
                        {item.mediaType === 'video' ? (
                            <video src={item.url} className="h-full w-full object-cover" controls preload="metadata" />
                        ) : (
                            <img src={item.url} alt={item.fileName} className="h-full w-full object-cover" />
                        )}
                    </div>
                    <div className="px-4 py-3">
                        <p className="truncate text-sm font-semibold text-slate-950">{item.fileName}</p>
                        <p className="mt-1 text-xs text-slate-500">
                            {item.mediaType === 'video' ? t('superAdmin.refundsPage.evidence.video') : t('superAdmin.refundsPage.evidence.image')} Â· {item.mimeType}
                        </p>
                    </div>
                </a>
            ))}
        </div>
    )
}

function EmptyState({ hasFilters }: { hasFilters: boolean }) {
    const { t } = useTranslation()

    return (
        <section className="rounded-[32px] border border-dashed border-slate-300 bg-white px-6 py-14 text-center shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100 text-slate-500">
                <ShieldAlert className="h-6 w-6" />
            </div>
            <h2 className="mt-5 text-2xl font-semibold text-slate-950">{t('superAdmin.refundsPage.empty.title')}</h2>
            <p className="mx-auto mt-3 max-w-2xl text-sm leading-6 text-slate-500">
                {hasFilters
                    ? t('superAdmin.refundsPage.empty.filteredDescription')
                    : t('superAdmin.refundsPage.empty.description')}
            </p>
        </section>
    )
}

function BilingualCopyBlock({
    copy,
    primaryClassName = 'text-sm font-medium text-slate-900',
    secondaryClassName = 'mt-1 text-sm text-slate-600',
}: {
    copy: { en: string; ar: string }
    primaryClassName?: string
    secondaryClassName?: string
}) {
    const { i18n } = useTranslation()
    const language = (i18n.resolvedLanguage || i18n.language || 'en').toLowerCase()
    const isArabic = language.startsWith('ar')

    return (
        <div>
            <p className={primaryClassName} lang={isArabic ? 'ar' : 'en'} dir={isArabic ? 'rtl' : 'ltr'}>
                {isArabic ? copy.ar : copy.en}
            </p>
            {secondaryClassName ? null : null}
        </div>
    )
}

function AdminGuidancePanel() {
    const { t } = useTranslation()
    const [isOpen, setIsOpen] = useState(true)
    const guidance = commerceHubRefundGuidanceContent.superAdmin

    return (
        <section className="overflow-hidden rounded-[32px] border border-sky-200 bg-sky-50/70 shadow-sm">
            <div className="flex items-center justify-between gap-3 px-6 py-5">
                <div className="flex items-center gap-3">
                    <div className="flex h-11 w-11 items-center justify-center rounded-full bg-white shadow-sm">
                        <ShieldAlert className="h-5 w-5 text-sky-600" />
                    </div>
                    <div>
                        <BilingualCopyBlock
                            copy={guidance.title}
                            primaryClassName="text-sm font-semibold text-sky-950"
                            secondaryClassName="mt-1 text-xs text-sky-700"
                        />
                    </div>
                </div>

                <button
                    type="button"
                    className="rounded-full border border-sky-200 bg-white p-2 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900"
                    onClick={(event) => {
                        event.stopPropagation()
                        setIsOpen((current) => !current)
                    }}
                    aria-label={isOpen ? t('superAdmin.refundsPage.guidance.collapse') : t('superAdmin.refundsPage.guidance.expand')}
                >
                    <svg
                        className={`h-5 w-5 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`}
                        viewBox="0 0 20 20"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="1.8"
                        aria-hidden="true"
                    >
                        <path strokeLinecap="round" strokeLinejoin="round" d="m5 7.5 5 5 5-5" />
                    </svg>
                </button>
            </div>

            {isOpen ? (
                <div className="border-t border-sky-200/70 px-6 py-5">
                    <div className="mb-5">
                        <BilingualCopyBlock
                            copy={guidance.subtitle}
                            primaryClassName="text-sm text-sky-900"
                            secondaryClassName="mt-1 text-xs text-sky-700"
                        />
                    </div>

                    <div className="rounded-[24px] border border-white/70 bg-white px-5 py-4 shadow-sm">
                        <BilingualCopyBlock
                            copy={guidance.summary}
                            primaryClassName="text-sm font-semibold text-slate-950"
                            secondaryClassName="mt-1 text-xs text-slate-600"
                        />

                        <div className="mt-4 grid gap-3 md:grid-cols-2">
                            {guidance.steps.map((step) => (
                                <div key={step.en} className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                                    <BilingualCopyBlock
                                        copy={step}
                                        primaryClassName="text-sm font-medium text-slate-900"
                                        secondaryClassName="mt-1 text-xs text-slate-600"
                                    />
                                </div>
                            ))}
                        </div>
                    </div>

                    <div className="mt-4 rounded-[24px] border border-amber-200 bg-amber-50 px-5 py-4">
                        <div className="flex items-start gap-3">
                            <div className="mt-0.5 flex h-10 w-10 items-center justify-center rounded-full bg-white">
                                <AlertTriangle className="h-5 w-5 text-amber-600" />
                            </div>
                            <div className="flex-1">
                                <BilingualCopyBlock
                                    copy={guidance.importantTitle}
                                    primaryClassName="text-sm font-semibold text-amber-950"
                                    secondaryClassName="mt-1 text-xs text-amber-700"
                                />

                                <div className="mt-3 space-y-3">
                                    {guidance.warnings.map((warning) => (
                                        <div key={warning.en} className="rounded-2xl border border-white/70 bg-white px-4 py-3">
                                            <BilingualCopyBlock
                                                copy={warning}
                                                primaryClassName="text-sm font-medium text-amber-950"
                                                secondaryClassName="mt-1 text-xs text-amber-700"
                                            />
                                        </div>
                                    ))}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            ) : null}
        </section>
    )
}

function RefundDetailsDrawer({
    refund,
    isOpen,
    isLoading,
    isError,
    errorMessage,
    onClose,
    onRetryLoad,
    onApprove,
    onExecute,
    onOverrideReturn,
    onMarkReturnFailed,
    onReject,
    onRetryExecution,
}: {
    refund?: SuperAdminRefundDetails
    isOpen: boolean
    isLoading: boolean
    isError: boolean
    errorMessage?: string | null
    onClose: () => void
    onRetryLoad: () => void
    onApprove: () => void
    onExecute: () => void
    onOverrideReturn: () => void
    onMarkReturnFailed: () => void
    onReject: () => void
    onRetryExecution: () => void
}) {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const unavailable = t('superAdmin.refundsPage.labels.unavailable')

    if (!isOpen) {
        return null
    }

    return (
        <div className="fixed inset-0 z-50 flex justify-end bg-slate-950/45 backdrop-blur-sm">
            <button type="button" aria-label={t('superAdmin.refundsPage.drawer.close')} className="flex-1" onClick={onClose} />
            <aside className="relative flex h-full w-full max-w-3xl flex-col border-l border-slate-200 bg-slate-50 shadow-2xl">
                <div className="flex items-start justify-between gap-4 border-b border-slate-200 bg-white px-6 py-5">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-600">{t('superAdmin.refundsPage.drawer.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">
                            {refund ? t('superAdmin.refundsPage.drawer.title', { id: refund.refundId.slice(0, 8) }) : t('superAdmin.refundsPage.drawer.loadingTitle')}
                        </h2>
                    </div>

                    <button
                        type="button"
                        className="rounded-2xl border border-slate-200 bg-white p-3 text-slate-500 transition hover:bg-slate-50 hover:text-slate-900"
                        aria-label={t('superAdmin.refundsPage.actions.close')}
                        onClick={onClose}
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <div className="flex-1 overflow-y-auto px-6 py-6">
                    {isLoading ? (
                        <div className="space-y-4">
                            {Array.from({ length: 4 }).map((_, index) => (
                                <div
                                    key={index}
                                    className="h-32 animate-pulse rounded-[28px] border border-slate-200 bg-white"
                                />
                            ))}
                        </div>
                    ) : null}

                    {!isLoading && isError ? (
                        <div className="rounded-[28px] border border-rose-200 bg-rose-50 px-5 py-4 text-sm text-rose-900 shadow-sm">
                            <p className="font-semibold">{t('superAdmin.refundsPage.drawer.loadErrorTitle')}</p>
                            <p className="mt-1 leading-6">{errorMessage}</p>
                            <button
                                type="button"
                                className="mt-4 inline-flex items-center gap-2 rounded-2xl bg-rose-600 px-4 py-3 text-sm font-semibold text-white transition hover:bg-rose-700"
                                onClick={onRetryLoad}
                            >
                                <RefreshCw className="h-4 w-4" />
                                {t('superAdmin.common.actions.retry')}
                            </button>
                        </div>
                    ) : null}

                    {!isLoading && !isError && refund ? (
                        <div className="space-y-5">
                            <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-sm">
                                <div className="relative overflow-hidden px-6 py-6">
                                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.16),_transparent_34%),radial-gradient(circle_at_bottom_right,_rgba(59,130,246,0.12),_transparent_30%)]" />
                                    <div className="relative flex flex-col gap-5">
                                        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                                            <div>
                                                <div
                                                    className={`inline-flex items-center gap-2 rounded-full border px-4 py-2 text-sm font-semibold ${getRefundStatusTone(refund.status)}`}
                                                >
                                                    {formatRefundStatusLabel(refund.status, t)}
                                                </div>
                                                <h3 className="mt-4 text-3xl font-semibold tracking-tight text-slate-950">
                                                    {formatCurrency(refund.amount, refund.currency, locale)}
                                                </h3>
                                                <p className="mt-2 text-sm font-semibold text-slate-900">
                                                    {formatRefundReasonLabel(refund.reasonCode, t)}
                                                </p>
                                                {refund.reasonText ? (
                                                    <p className="mt-2 max-w-2xl text-sm leading-7 text-slate-600">
                                                        {refund.reasonText}
                                                    </p>
                                                ) : null}
                                            </div>

                                            <div className="grid gap-3 sm:grid-cols-2">
                                                <DetailField label={t('superAdmin.refundsPage.fields.quantity')} value={String(refund.quantity)} />
                                                <DetailField
                                                    label={t('superAdmin.refundsPage.fields.unitPriceSnapshot')}
                                                    value={formatCurrency(refund.unitPriceSnapshot, refund.currency, locale)}
                                                />
                                            </div>
                                        </div>

                                        {refund.failureReason ? (
                                            <div className="rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-900">
                                                <p className="font-semibold">{t('superAdmin.refundsPage.fields.failureReason')}</p>
                                                <p className="mt-1 leading-6">{refund.failureReason}</p>
                                            </div>
                                        ) : null}

                                        {refund.adminNote ? (
                                            <div className="rounded-[24px] border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                                                <p className="font-semibold text-slate-900">{t('superAdmin.refundsPage.fields.adminNote')}</p>
                                                <p className="mt-1 leading-6">{refund.adminNote}</p>
                                            </div>
                                        ) : null}

                                        {isRefundAwaitingPayout(refund.status) || (refund.status === 'Approved' && refund.canExecute) ? (
                                            <div className="rounded-[24px] border border-amber-200 bg-amber-50 px-4 py-4">
                                                <p className="text-sm font-semibold text-amber-950">
                                                    {t('superAdmin.refundsPage.execute.sendBeforeConfirm')}
                                                </p>
                                            </div>
                                        ) : null}
                                    </div>
                                </div>
                            </section>

                            <AdminGuidancePanel />

                            <DetailSection
                                title={t('superAdmin.refundsPage.sections.evidence.title')}
                                caption={t('superAdmin.refundsPage.sections.evidence.caption')}
                            >
                                <EvidenceGallery media={refund.media} />
                            </DetailSection>

                            <DetailSection
                                title={t('superAdmin.refundsPage.sections.orderSnapshot.title')}
                                caption={t('superAdmin.refundsPage.sections.orderSnapshot.caption')}
                            >
                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                    <DetailField label={t('superAdmin.refundsPage.fields.order')} value={refund.order.orderNumber} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.paymentMethod')} value={refund.order.paymentMethodName} />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.paymentStatus')}
                                        value={formatRefundStatusLabel(refund.order.paymentStatus, t)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.orderTotal')}
                                        value={formatCurrency(refund.order.totalAmount, refund.order.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.shippingTotal')}
                                        value={formatCurrency(refund.order.shippingTotal, refund.order.currency, locale)}
                                    />
                                    <DetailField label={t('superAdmin.refundsPage.fields.createdAt')} value={formatDateTime(refund.order.createdAt, locale, unavailable)} />
                                </div>
                            </DetailSection>

                            <DetailSection
                                title={t('superAdmin.refundsPage.sections.customerSnapshot.title')}
                                caption={t('superAdmin.refundsPage.sections.customerSnapshot.caption')}
                            >
                                <div className="grid gap-3 md:grid-cols-2">
                                    <DetailField label={t('superAdmin.refundsPage.fields.customer')} value={refund.customer.name} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.email')} value={refund.customer.email} />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.phone')}
                                        value={refund.customer.phoneNumber || unavailable}
                                    />
                                    <DetailField label={t('superAdmin.refundsPage.fields.customerId')} value={refund.customer.customerId} />
                                </div>
                            </DetailSection>

                            {refund.payout ? (
                                <DetailSection
                                    title={t('superAdmin.refundsPage.sections.payoutSnapshot.title')}
                                    caption={t('superAdmin.refundsPage.sections.payoutSnapshot.caption')}
                                >
                                    <div className="grid gap-3 md:grid-cols-2">
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.method')}
                                            value={formatRefundPayoutMethodLabel(refund.payout.method, t)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.maskedAccount')}
                                            value={refund.payout.maskedAccountIdentifier}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.refundAmount')}
                                            value={formatCurrency(refund.amount, refund.currency, locale)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.accountHolder')}
                                            value={refund.payout.accountHolderName || unavailable}
                                        />
                                    </div>
                                </DetailSection>
                            ) : null}

                            {refund.returnRequest ? (
                                <DetailSection
                                    title={t('superAdmin.refundsPage.sections.returnLifecycle.title')}
                                    caption={t('superAdmin.refundsPage.sections.returnLifecycle.caption')}
                                >
                                    <div className="mb-4 flex flex-wrap items-center gap-3">
                                        <span
                                            className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold ${getReturnStatusTone(refund.returnRequest.status)}`}
                                        >
                                            {formatReturnStatusLabel(refund.returnRequest.status, t)}
                                        </span>
                                        <span className="inline-flex items-center rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold text-sky-900">
                                            {formatReturnTriggerLabel(refund.returnRequest.refundExecutionTrigger, t)}
                                        </span>
                                        <span
                                            className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold ${
                                                (isRefundAwaitingPayout(refund.status) || (refund.status === 'Approved' && refund.canExecute))
                                                    ? 'border-yellow-200 bg-yellow-50 text-yellow-900'
                                                    : isRefundPaid(refund.status)
                                                      ? 'border-emerald-200 bg-emerald-50 text-emerald-900'
                                                    : refund.returnRequest.isExecutionTriggerReached
                                                      ? 'border-emerald-200 bg-emerald-50 text-emerald-900'
                                                    : 'border-amber-200 bg-amber-50 text-amber-900'
                                            }`}
                                        >
                                            {isRefundAwaitingPayout(refund.status) || (refund.status === 'Approved' && refund.canExecute)
                                                ? t('superAdmin.refundsPage.returnLifecycle.awaitingPayout')
                                                : isRefundPaid(refund.status)
                                                  ? t('superAdmin.refundsPage.returnLifecycle.refundPaidAfterReturn')
                                                : refund.returnRequest.isExecutionTriggerReached
                                                  ? t('superAdmin.refundsPage.returnLifecycle.returnMilestoneReached')
                                                  : t('superAdmin.refundsPage.returnLifecycle.refundTriggerWaiting')}
                                        </span>
                                    </div>

                                    <ReturnTimeline returnRequest={refund.returnRequest} />

                                    <div className="mt-4 grid gap-3 md:grid-cols-2">
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.trackingNumber')}
                                            value={refund.returnRequest.trackingNumber || unavailable}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.courier')}
                                            value={refund.returnRequest.courierName || unavailable}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.pickupScheduled')}
                                            value={formatDateTime(refund.returnRequest.pickupScheduledAt, locale, unavailable)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.deliveredToVendor')}
                                            value={formatDateTime(refund.returnRequest.deliveredAt, locale, unavailable)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.vendorConfirmed')}
                                            value={formatDateTime(refund.returnRequest.confirmedAt, locale, unavailable)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.execute.notes')}
                                            value={refund.returnRequest.notes || unavailable}
                                        />
                                    </div>

                                    <div className="mt-5 rounded-[24px] border border-sky-200 bg-sky-50 px-5 py-4">
                                        <p className="text-sm font-semibold text-sky-950">
                                            {t('superAdmin.refundsPage.returnLifecycle.vendorManaged')}
                                        </p>
                                        <div className="mt-4 rounded-[20px] border border-white/80 bg-white px-4 py-3 text-sm leading-6 text-slate-600">
                                            {t('superAdmin.refundsPage.returnLifecycle.vendorManagedDescription')}
                                        </div>
                                    </div>

                                    <div className="mt-5 flex flex-wrap gap-3">
                                        {refund.returnRequest.canOverrideStatus ? (
                                            <button
                                                type="button"
                                                className="inline-flex items-center justify-center gap-2 rounded-2xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm font-semibold text-sky-900 transition hover:border-sky-300 hover:bg-sky-100"
                                                onClick={onOverrideReturn}
                                            >
                                                <CalendarDays className="h-4 w-4" />
                                                {t('superAdmin.refundsPage.actions.overrideStatus')}
                                            </button>
                                        ) : null}

                                        {refund.returnRequest.canMarkFailed ? (
                                            <button
                                                type="button"
                                                className="inline-flex items-center justify-center gap-2 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-semibold text-rose-900 transition hover:border-rose-300 hover:bg-rose-100"
                                                onClick={onMarkReturnFailed}
                                            >
                                                <AlertTriangle className="h-4 w-4" />
                                                {t('superAdmin.refundsPage.actions.markFailed')}
                                            </button>
                                        ) : null}
                                    </div>
                                </DetailSection>
                            ) : null}

                            <DetailSection
                                title={t('superAdmin.refundsPage.sections.itemSnapshot.title')}
                                caption={t('superAdmin.refundsPage.sections.itemSnapshot.caption')}
                            >
                                <div className="grid gap-3 md:grid-cols-2">
                                    <DetailField label={t('superAdmin.refundsPage.fields.product')} value={refund.item.productName} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.variant')} value={refund.item.variantName || t('superAdmin.refundsPage.labels.defaultVariant')} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.vendor')} value={refund.item.vendorName} />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.itemStatus')}
                                        value={formatRefundStatusLabel(refund.item.status, t)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.purchasedQuantity')}
                                        value={String(refund.item.purchasedQuantity)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.deliveredAt')}
                                        value={formatDateTime(refund.item.deliveredAt, locale, unavailable)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.itemTotal')}
                                        value={formatCurrency(refund.item.totalPrice, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.platformCommission')}
                                        value={formatCurrency(refund.item.platformCommissionAmount, refund.currency, locale)}
                                    />
                                </div>
                            </DetailSection>

                            <DetailSection
                                title={t('superAdmin.refundsPage.sections.financialImpactPreview.title')}
                                caption={t('superAdmin.refundsPage.sections.financialImpactPreview.caption')}
                            >
                                <div className="rounded-[24px] border border-amber-200 bg-amber-50 px-4 py-3 text-sm font-medium leading-6 text-amber-950">
                                    {describeExecutionPath(refund.financialPreview, t)}
                                </div>

                                <div className="mt-4 grid gap-3 md:grid-cols-2">
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.paymentType')}
                                        value={formatPaymentTypeLabel(refund.financialPreview.paymentType, t)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.payoutAlreadyDone')}
                                        value={refund.financialPreview.hasPayoutOccurred ? t('superAdmin.refundsPage.labels.yes') : t('superAdmin.refundsPage.labels.no')}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.commissionReversal')}
                                        value={formatCurrency(refund.financialPreview.commissionReversalAmount, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorEarningChange')}
                                        value={formatCurrency(refund.financialPreview.vendorEarningChange, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.executionPath')}
                                        value={formatExecutionPathLabel(refund.financialPreview.executionPath, t)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.platformCashImpact')}
                                        value={formatSignedCurrency(refund.financialPreview.platformCashDelta, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.platformPayableImpact')}
                                        value={formatSignedCurrency(refund.financialPreview.platformPayableDelta, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorDebtImpact')}
                                        value={formatSignedCurrency(refund.financialPreview.vendorDebtDelta, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.commissionReceivableImpact')}
                                        value={formatSignedCurrency(refund.financialPreview.commissionReceivableDelta, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.paymentRefundedBefore')}
                                        value={formatCurrency(refund.financialPreview.paymentRefundedAmountBefore, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.paymentRefundedAfter')}
                                        value={formatCurrency(refund.financialPreview.paymentRefundedAmountAfter, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorNetBefore')}
                                        value={formatCurrency(refund.financialPreview.vendorNetEarningsBefore, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorNetAfter')}
                                        value={formatCurrency(refund.financialPreview.vendorNetEarningsAfter, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorPayableBefore')}
                                        value={formatCurrency(refund.financialPreview.vendorPayableBefore, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorPayableAfter')}
                                        value={formatCurrency(refund.financialPreview.vendorPayableAfter, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorDebtBefore')}
                                        value={formatCurrency(refund.financialPreview.vendorDebtBefore, refund.currency, locale)}
                                    />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.vendorDebtAfter')}
                                        value={formatCurrency(refund.financialPreview.vendorDebtAfter, refund.currency, locale)}
                                    />
                                </div>
                            </DetailSection>

                            <DetailSection
                                title={t('superAdmin.refundsPage.sections.refundTimeline.title')}
                                caption={t('superAdmin.refundsPage.sections.refundTimeline.caption')}
                            >
                                <RefundWorkflowTimeline refund={refund} />

                                <div className="grid gap-3 md:grid-cols-2">
                                    <DetailField label={t('superAdmin.refundsPage.fields.requestedAt')} value={formatDateTime(refund.createdAt, locale, unavailable)} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.reviewedAt')} value={formatDateTime(refund.reviewedAtUtc, locale, unavailable)} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.approvedAt')} value={formatDateTime(refund.approvedAtUtc, locale, unavailable)} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.rejectedAt')} value={formatDateTime(refund.rejectedAtUtc, locale, unavailable)} />
                                    <DetailField
                                        label={t('superAdmin.refundsPage.fields.payoutProcessingStarted')}
                                        value={formatDateTime(refund.processingStartedAtUtc, locale, unavailable)}
                                    />
                                    <DetailField label={t('superAdmin.refundsPage.fields.paidAt')} value={formatDateTime(refund.completedAtUtc, locale, unavailable)} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.failedAt')} value={formatDateTime(refund.failedAtUtc, locale, unavailable)} />
                                    <DetailField label={t('superAdmin.refundsPage.fields.payoutAttempts')} value={String(refund.executionAttempts)} />
                                </div>
                            </DetailSection>

                            {refund.execution ? (
                                <DetailSection
                                    title={t('superAdmin.refundsPage.sections.payoutConfirmation.title')}
                                    caption={t('superAdmin.refundsPage.sections.payoutConfirmation.caption')}
                                >
                                    <div className="grid gap-3 md:grid-cols-2">
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.method')}
                                            value={formatRefundPayoutMethodLabel(refund.execution.method, t)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.maskedAccount')}
                                            value={refund.execution.maskedAccountIdentifier}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.transactionReference')}
                                            value={refund.execution.transactionReference || unavailable}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.paidAt')}
                                            value={formatDateTime(refund.execution.executedAt, locale, unavailable)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.payoutResult')}
                                            value={refund.execution.isSuccessful ? t('superAdmin.refundsPage.labels.successful') : t('superAdmin.refundsPage.labels.failed')}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.financialRefundStatus')}
                                            value={formatRefundStatusLabel(refund.execution.status, t)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.settlementStatus')}
                                            value={refund.execution.gatewayStatus || unavailable}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.postPayoutAdjustment')}
                                            value={refund.execution.isPostPayoutAdjustment ? t('superAdmin.refundsPage.labels.yes') : t('superAdmin.refundsPage.labels.no')}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.commissionReversal')}
                                            value={formatCurrency(refund.execution.commissionReversalAmount, refund.currency, locale)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.fields.vendorEarningReversal')}
                                            value={formatCurrency(refund.execution.vendorEarningReversalAmount, refund.currency, locale)}
                                        />
                                        <DetailField
                                            label={t('superAdmin.refundsPage.execute.notes')}
                                            value={refund.execution.notes || unavailable}
                                        />
                                    </div>
                                </DetailSection>
                            ) : null}
                        </div>
                    ) : null}
                </div>

                {refund ? (
                    <div className="border-t border-slate-200 bg-white px-6 py-5">
                        <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
                            {refund.canReject ? (
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-rose-200 bg-rose-50 px-5 py-3 text-sm font-semibold text-rose-900 transition hover:border-rose-300 hover:bg-rose-100"
                                    onClick={onReject}
                                >
                                    <X className="h-4 w-4" />
                                    {t('superAdmin.refundsPage.actions.reject')}
                                </button>
                            ) : null}

                            {refund.canRetry ? (
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-amber-200 bg-amber-50 px-5 py-3 text-sm font-semibold text-amber-900 transition hover:border-amber-300 hover:bg-amber-100"
                                    onClick={onRetryExecution}
                                >
                                    <RefreshCw className="h-4 w-4" />
                                    {t('superAdmin.refundsPage.actions.retryRefund')}
                                </button>
                            ) : null}

                            {refund.canExecute && (isRefundAwaitingPayout(refund.status) || refund.status === 'Approved') ? (
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-2xl bg-amber-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-amber-700"
                                    onClick={onExecute}
                                >
                                    <CheckCircle2 className="h-4 w-4" />
                                    {t('superAdmin.refundsPage.actions.sendMoneyAndConfirm')}
                                </button>
                            ) : null}

                            {refund.canApprove ? (
                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                                    onClick={onApprove}
                                >
                                    <CheckCircle2 className="h-4 w-4" />
                                    {t('superAdmin.refundsPage.actions.approveAndStartReturnFlow')}
                                </button>
                            ) : null}
                        </div>
                    </div>
                ) : null}
            </aside>
        </div>
    )
}

export default function RefundsPage() {
    const { t, i18n } = useTranslation()
    const locale = i18n.resolvedLanguage || i18n.language || 'en'
    const unavailable = t('superAdmin.refundsPage.labels.unavailable')
    const [searchParams, setSearchParams] = useSearchParams()
    const queryClient = useQueryClient()
    const filters = useMemo(() => buildFilters(searchParams), [searchParams])
    const selectedRefundId = searchParams.get('refundId')
    const [actionModal, setActionModal] = useState<'approve' | 'reject' | 'retry' | null>(null)
    const [actionNote, setActionNote] = useState('')
    const [actionError, setActionError] = useState<string | null>(null)
    const [executeModalOpen, setExecuteModalOpen] = useState(false)
    const [executeMethod, setExecuteMethod] = useState<RefundPayoutMethod>('VodafoneCash')
    const [executeTransactionReference, setExecuteTransactionReference] = useState('')
    const [executeNotes, setExecuteNotes] = useState('')
    const [confirmMoneySent, setConfirmMoneySent] = useState(false)
    const [confirmMethodOverride, setConfirmMethodOverride] = useState(false)
    const [executeError, setExecuteError] = useState<string | null>(null)
    const [returnModalMode, setReturnModalMode] = useState<'override' | 'failed' | null>(null)
    const [overrideStatus, setOverrideStatus] = useState<SuperAdminOverrideReturnStatus>('PickupScheduled')
    const [returnTimestamp, setReturnTimestamp] = useState('')
    const [returnTrackingNumber, setReturnTrackingNumber] = useState('')
    const [returnCourierName, setReturnCourierName] = useState('')
    const [returnNotes, setReturnNotes] = useState('')
    const [returnError, setReturnError] = useState<string | null>(null)
    const [successMessage, setSuccessMessage] = useState<string | null>(null)

    const updateParams = useCallback(
        (updates: Record<string, string | null | undefined>) => {
            setSearchParams((previous) => {
                const next = new URLSearchParams(previous)

                Object.entries(updates).forEach(([key, value]) => {
                    if (!value) {
                        next.delete(key)
                        return
                    }

                    next.set(key, value)
                })

                return next
            })
        },
        [setSearchParams],
    )

    const refundsQuery = useQuery({
        queryKey: ['super-admin', 'refunds', filters],
        queryFn: () => getSuperAdminRefunds(filters),
        staleTime: 30_000,
        refetchOnWindowFocus: true,
        refetchInterval: 30_000,
    })

    const tenantsQuery = useQuery({
        queryKey: ['super-admin', 'orders', 'tenants'],
        queryFn: getSuperAdminOrderTenants,
        staleTime: 5 * 60_000,
    })

    const approveMutation = useMutation({
        mutationFn: ({ refundId, note }: { refundId: string; note?: string }) => approveSuperAdminRefund(refundId, note),
        onSuccess: (result) => {
            const accepted = currentSelectedRefund
                ? requireSuperAdminRefundDetailsForCurrentSelection(result, currentSelectedRefund)
                : result
            queryClient.setQueryData(['super-admin', 'refunds', 'details', accepted.refundId], accepted)
            queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds'] })
            setActionModal(null)
            setActionNote('')
            setActionError(null)
            setSuccessMessage(
                isRefundAwaitingPayout(accepted.status)
                    ? t('superAdmin.refundsPage.feedback.approvalQueuedForPayout')
                    : accepted.status === 'WaitingForReturn'
                      ? t('superAdmin.refundsPage.feedback.approvalWaitingForReturn')
                      : t('superAdmin.refundsPage.feedback.approvalSaved'),
            )
        },
        onError: (error) => {
            setActionError(getErrorMessage(error, t('superAdmin.refundsPage.feedback.approvalFailed')))
        },
    })

    const rejectMutation = useMutation({
        mutationFn: ({ refundId, reason }: { refundId: string; reason: string }) => rejectSuperAdminRefund(refundId, reason),
        onSuccess: (result) => {
            const accepted = currentSelectedRefund
                ? requireSuperAdminRefundDetailsForCurrentSelection(result, currentSelectedRefund)
                : result
            queryClient.setQueryData(['super-admin', 'refunds', 'details', accepted.refundId], accepted)
            queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds'] })
            setActionModal(null)
            setActionNote('')
            setActionError(null)
            setSuccessMessage(t('superAdmin.refundsPage.feedback.refundRejected'))
        },
        onError: (error) => {
            setActionError(getErrorMessage(error, t('superAdmin.refundsPage.feedback.rejectFailed')))
        },
    })

    const retryMutation = useMutation({
        mutationFn: ({ refundId, note }: { refundId: string; note?: string }) => retrySuperAdminRefund(refundId, note),
        onSuccess: (result) => {
            const accepted = currentSelectedRefund
                ? requireSuperAdminRefundDetailsForCurrentSelection(result, currentSelectedRefund)
                : result
            queryClient.setQueryData(['super-admin', 'refunds', 'details', accepted.refundId], accepted)
            queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds'] })
            setActionModal(null)
            setActionNote('')
            setActionError(null)
            setSuccessMessage(
                isRefundAwaitingPayout(accepted.status)
                    ? t('superAdmin.refundsPage.feedback.retryQueuedForPayout')
                    : t('superAdmin.refundsPage.feedback.retryWaitingForReturn'),
            )
        },
        onError: (error) => {
            setActionError(getErrorMessage(error, t('superAdmin.refundsPage.feedback.retryFailed')))
        },
    })

    const executeMutation = useMutation({
        mutationFn: ({
            refundId,
            method,
            transactionReference,
            notes,
            confirmMoneySent,
            confirmMethodOverride,
        }: {
            refundId: string
            method: RefundPayoutMethod
            transactionReference: string
            notes?: string
            confirmMoneySent: boolean
            confirmMethodOverride?: boolean
        }) =>
            executeSuperAdminRefund(refundId, {
                method,
                transactionReference,
                notes,
                confirmMoneySent,
                confirmMethodOverride,
            }),
        onSuccess: (result) => {
            const accepted = currentSelectedRefund
                ? requireSuperAdminRefundDetailsForCurrentSelection(result, currentSelectedRefund)
                : result
            queryClient.setQueryData(['super-admin', 'refunds', 'details', accepted.refundId], accepted)
            queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds'] })
            setExecuteModalOpen(false)
            setExecuteTransactionReference('')
            setExecuteNotes('')
            setConfirmMoneySent(false)
            setConfirmMethodOverride(false)
            setExecuteError(null)
            setSuccessMessage(t('superAdmin.refundsPage.feedback.manualPayoutConfirmed'))
        },
        onError: (error) => {
            setExecuteError(getErrorMessage(error, t('superAdmin.refundsPage.feedback.executeFailed')))
        },
    })

    const returnMutation = useMutation({
        mutationFn: ({
            returnId,
            mode,
            payload,
        }: {
            returnId: string
            mode: 'override' | 'failed'
            payload: {
                status?: SuperAdminOverrideReturnStatus
                timestampUtc?: string
                trackingNumber?: string
                courierName?: string
                notes?: string
            }
        }) => {
            const currentRefundDetails = detailsQuery.data
            if (!currentRefundDetails) {
                throw new Error(t('superAdmin.refundsPage.validation.currentRefundRequired'))
            }

            if (mode === 'override') {
                if (!payload.status) {
                    throw new Error(t('superAdmin.refundsPage.validation.overrideStatusRequired'))
                }

                return overrideSuperAdminReturnStatus(returnId, {
                    status: payload.status,
                    timestampUtc: payload.timestampUtc,
                    trackingNumber: payload.trackingNumber,
                    courierName: payload.courierName,
                    notes: payload.notes,
                }).then((returnRequest) => requireSuperAdminReturnForCurrentRefund(returnRequest, currentRefundDetails))
            }

            return markSuperAdminReturnFailed(returnId, payload)
                .then((returnRequest) => requireSuperAdminReturnForCurrentRefund(returnRequest, currentRefundDetails))
        },
        onSuccess: async (_, variables) => {
            if (currentSelectedRefund) {
                await queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds', 'details', currentSelectedRefund.refundId] })
            }

            await queryClient.invalidateQueries({ queryKey: ['super-admin', 'refunds'] })
            setReturnModalMode(null)
            setReturnError(null)
            setSuccessMessage(
                variables.mode === 'override'
                    ? t('superAdmin.refundsPage.feedback.returnOverrideSuccess', {
                        status: formatReturnStatusLabel((variables.payload.status ?? 'Pending') as SuperAdminReturnStatus, t),
                    })
                    : t('superAdmin.refundsPage.feedback.returnMarkedFailed'),
            )
        },
        onError: (error) => {
            setReturnError(getErrorMessage(error, t('superAdmin.refundsPage.feedback.returnUpdateFailed')))
        },
    })

    const refunds = refundsQuery.data?.refunds ?? []
    const currentSelectedRefund = refundsQuery.isFetching || refundsQuery.isError
        ? null
        : requireCurrentSuperAdminRefundSelection(selectedRefundId, refunds)
    const currentSelectedRefundId = currentSelectedRefund?.refundId ?? null
    const meta = refundsQuery.data?.meta
    const totalCount = meta?.totalCount ?? 0
    const totalPages = meta?.totalPages ?? 0
    const activeFilterCount = [filters.status, filters.vendorId, filters.fromDate, filters.toDate].filter(Boolean).length
    const requestedCount = refunds.filter((refund) => refund.status === 'Requested' || refund.status === 'UnderReview').length
    const awaitingPayoutCount = refunds.filter((refund) => isRefundAwaitingPayout(refund.status)).length
    const failedCount = refunds.filter((refund) => refund.status === 'Failed').length

    const currentActionIsSubmitting =
        actionModal === 'approve'
            ? approveMutation.isPending
            : actionModal === 'reject'
              ? rejectMutation.isPending
              : retryMutation.isPending

    const currentActionError =
        actionModal === 'approve'
            ? actionError ?? (approveMutation.error ? getErrorMessage(approveMutation.error, t('superAdmin.refundsPage.feedback.approvalFailed')) : null)
            : actionModal === 'reject'
              ? actionError ?? (rejectMutation.error ? getErrorMessage(rejectMutation.error, t('superAdmin.refundsPage.feedback.rejectFailed')) : null)
              : actionError ?? (retryMutation.error ? getErrorMessage(retryMutation.error, t('superAdmin.refundsPage.feedback.retryFailed')) : null)

    const detailsQuery = useQuery({
        queryKey: ['super-admin', 'refunds', 'details', currentSelectedRefundId],
        queryFn: () => getSuperAdminRefundDetails(currentSelectedRefundId as string, currentSelectedRefund ?? undefined),
        enabled: Boolean(currentSelectedRefundId),
        staleTime: 15_000,
        refetchInterval: (query) => {
            const refund = query.state.data
            if (!refund) {
                return false
            }

            if (refund.status === 'Processing') {
                return 5_000
            }

            const returnStatus = refund.returnRequest?.status
            return returnStatus && returnStatus !== 'Confirmed' && returnStatus !== 'Failed' ? 10_000 : false
        },
    })

    // A selected id and a query-cache entry are only candidates.  Do not keep
    // a previously authorized refund detail visible while the current
    // monitoring authority is being refreshed, has failed, or no longer
    // contains that refund.
    const acceptedRefundDetails =
        currentSelectedRefund &&
        !refundsQuery.isFetching &&
        !refundsQuery.isError &&
        !detailsQuery.isFetching &&
        !detailsQuery.isError
            ? detailsQuery.data ?? null
            : null

    useEffect(() => {
        if (selectedRefundId && !refundsQuery.isFetching && !currentSelectedRefund) {
            handleSelectRefund(null)
        }
    }, [currentSelectedRefund, refundsQuery.isFetching, selectedRefundId])

    useEffect(() => {
        if (acceptedRefundDetails) {
            return
        }

        setActionModal(null)
        setExecuteModalOpen(false)
        setReturnModalMode(null)
    }, [acceptedRefundDetails])

    function handleResetFilters() {
        setActionModal(null)
        setActionNote('')
        setActionError(null)
        setExecuteModalOpen(false)
        setExecuteTransactionReference('')
        setExecuteNotes('')
        setConfirmMoneySent(false)
        setConfirmMethodOverride(false)
        setExecuteError(null)
        setSuccessMessage(null)
        setSearchParams({
            page: String(DEFAULT_PAGE),
            pageSize: String(DEFAULT_PAGE_SIZE),
        })
    }

    function handleSetFilter(
        key: 'status' | 'vendorId' | 'fromDate' | 'toDate',
        value: string,
    ) {
        setSuccessMessage(null)
        updateParams({
            [key]: value || null,
            page: String(DEFAULT_PAGE),
        })
    }

    function handleSelectRefund(refundId: string | null) {
        setActionModal(null)
        setActionNote('')
        setActionError(null)
        setExecuteModalOpen(false)
        setExecuteTransactionReference('')
        setExecuteNotes('')
        setConfirmMoneySent(false)
        setConfirmMethodOverride(false)
        setExecuteError(null)
        updateParams({
            refundId,
        })
    }

    function handlePageChange(page: number) {
        updateParams({ page: String(page) })
    }

    function handlePageSizeChange(pageSize: number) {
        updateParams({
            pageSize: String(pageSize),
            page: String(DEFAULT_PAGE),
        })
    }

    async function refreshAll() {
        await Promise.all([
            refundsQuery.refetch(),
            tenantsQuery.refetch(),
            currentSelectedRefundId ? detailsQuery.refetch() : Promise.resolve(),
        ])
    }

    function openActionModal(kind: 'approve' | 'reject' | 'retry') {
        setActionError(null)
        setExecuteModalOpen(false)
        setSuccessMessage(null)
        setActionModal(kind)
        if (kind === 'reject') {
            setActionNote('')
            return
        }

        setActionNote(detailsQuery.data?.adminNote ?? '')
    }

    function openExecuteModal() {
        const payoutMethod = detailsQuery.data?.payout?.method ?? 'VodafoneCash'
        setActionModal(null)
        setActionError(null)
        setExecuteError(null)
        setSuccessMessage(null)
        setExecuteMethod(payoutMethod)
        setExecuteTransactionReference('')
        setExecuteNotes(detailsQuery.data?.execution?.notes ?? '')
        setConfirmMoneySent(false)
        setConfirmMethodOverride(false)
        setExecuteModalOpen(true)
    }

    function openReturnOverrideModal() {
        const returnRequest = detailsQuery.data?.returnRequest
        const defaultOverrideStatus = returnRequest ? getDefaultOverrideStatus(returnRequest) : null
        if (!returnRequest || !defaultOverrideStatus) {
            return
        }

        setActionModal(null)
        setExecuteModalOpen(false)
        setActionError(null)
        setExecuteError(null)
        setReturnError(null)
        setSuccessMessage(null)
        setReturnModalMode('override')
        setOverrideStatus(defaultOverrideStatus)
        setReturnTrackingNumber(returnRequest.trackingNumber ?? '')
        setReturnCourierName(returnRequest.courierName ?? '')
        setReturnNotes(returnRequest.notes ?? '')
        setReturnTimestamp(formatDateTimeInputValue(getReturnStageTimestamp(returnRequest, defaultOverrideStatus)))
    }

    function openReturnFailedModal() {
        const returnRequest = detailsQuery.data?.returnRequest
        if (!returnRequest) {
            return
        }

        setActionModal(null)
        setExecuteModalOpen(false)
        setActionError(null)
        setExecuteError(null)
        setReturnError(null)
        setSuccessMessage(null)
        setReturnModalMode('failed')
        setReturnTrackingNumber(returnRequest.trackingNumber ?? '')
        setReturnCourierName(returnRequest.courierName ?? '')
        setReturnNotes('')
        setReturnTimestamp(formatDateTimeInputValue(getReturnStageTimestamp(returnRequest, 'Failed')))
    }

    async function requireCurrentRefundMutationAuthority(): Promise<SuperAdminRefundDetails> {
        if (!selectedRefundId) {
            throw new Error(t('superAdmin.refundsPage.validation.currentRefundRequired'))
        }

        const refreshedRefunds = await refundsQuery.refetch()
        const currentRefund = refreshedRefunds.data
            ? requireCurrentSuperAdminRefundSelection(selectedRefundId, refreshedRefunds.data.refunds)
            : null

        if (refreshedRefunds.isError || !currentRefund) {
            throw new Error(t('superAdmin.refundsPage.validation.currentRefundRequired'))
        }

        return getSuperAdminRefundDetails(currentRefund.refundId, currentRefund)
    }

    async function handleActionConfirm() {
        if (!currentSelectedRefundId) {
            return
        }

        setActionError(null)

        let currentRefund: SuperAdminRefundDetails
        try {
            currentRefund = await requireCurrentRefundMutationAuthority()
        } catch (error) {
            setActionError(getErrorMessage(error, t('superAdmin.refundsPage.validation.currentRefundRequired')))
            return
        }

        if (actionModal === 'approve') {
            if (!currentRefund.canApprove) {
                setActionError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
                return
            }

            approveMutation.mutate({
                refundId: currentRefund.refundId,
                note: actionNote.trim() || undefined,
            })
            return
        }

        if (actionModal === 'reject') {
            if (!currentRefund.canReject) {
                setActionError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
                return
            }

            const reason = actionNote.trim()
            if (!reason) {
                setActionError(t('superAdmin.refundsPage.validation.rejectionReasonRequired'))
                return
            }

            rejectMutation.mutate({
                refundId: currentRefund.refundId,
                reason,
            })
            return
        }

        if (actionModal === 'retry') {
            if (!currentRefund.canRetry) {
                setActionError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
                return
            }

            retryMutation.mutate({
                refundId: currentRefund.refundId,
                note: actionNote.trim() || undefined,
            })
        }
    }

    async function handleExecuteConfirm() {
        if (!currentSelectedRefundId || !detailsQuery.data) {
            return
        }

        const transactionReference = executeTransactionReference.trim()
        if (!transactionReference) {
            setExecuteError(t('superAdmin.refundsPage.validation.transactionReferenceRequired'))
            return
        }

        if (!confirmMoneySent) {
            setExecuteError(t('superAdmin.refundsPage.validation.confirmMoneySentRequired'))
            return
        }

        let currentRefund: SuperAdminRefundDetails
        try {
            currentRefund = await requireCurrentRefundMutationAuthority()
        } catch (error) {
            setExecuteError(getErrorMessage(error, t('superAdmin.refundsPage.validation.currentRefundRequired')))
            return
        }

        if (!currentRefund.canExecute || (!isRefundAwaitingPayout(currentRefund.status) && currentRefund.status !== 'Approved')) {
            setExecuteError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
            return
        }

        if (currentRefund.payout?.method && currentRefund.payout.method !== executeMethod && !confirmMethodOverride) {
            setExecuteError(t('superAdmin.refundsPage.validation.confirmMethodOverrideRequired'))
            return
        }

        setExecuteError(null)
        executeMutation.mutate({
            refundId: currentRefund.refundId,
            method: executeMethod,
            transactionReference,
            notes: executeNotes.trim() || undefined,
            confirmMoneySent,
            confirmMethodOverride,
        })
    }

    async function handleReturnConfirm() {
        if (!returnModalMode) {
            return
        }

        let currentRefund: SuperAdminRefundDetails
        try {
            currentRefund = await requireCurrentRefundMutationAuthority()
        } catch (error) {
            setReturnError(getErrorMessage(error, t('superAdmin.refundsPage.validation.currentRefundRequired')))
            return
        }

        const returnRequest = currentRefund.returnRequest
        if (!returnRequest) {
            setReturnError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
            return
        }

        if (returnModalMode === 'failed' && !returnNotes.trim()) {
            setReturnError(t('superAdmin.refundsPage.validation.returnFailureReasonRequired'))
            return
        }

        if (returnModalMode === 'failed' && !returnRequest.canMarkFailed) {
            setReturnError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
            return
        }

        if (returnModalMode === 'override' && (
            !returnRequest.canOverrideStatus ||
            !returnRequest.allowedOverrideStatuses.includes(overrideStatus)
        )) {
            setReturnError(t('superAdmin.refundsPage.validation.currentRefundRequired'))
            return
        }

        setReturnError(null)
        returnMutation.mutate({
            returnId: returnRequest.returnId,
            mode: returnModalMode,
            payload: {
                status: returnModalMode === 'override' ? overrideStatus : undefined,
                timestampUtc: returnTimestamp ? new Date(returnTimestamp).toISOString() : undefined,
                trackingNumber: returnTrackingNumber.trim() || undefined,
                courierName: returnCourierName.trim() || undefined,
                notes: returnNotes.trim() || undefined,
            },
        })
    }

    if (refundsQuery.isError && !refundsQuery.data) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-slate-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <ShieldAlert className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">{t('superAdmin.refundsPage.unavailable.title')}</h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {getErrorMessage(refundsQuery.error, t('superAdmin.refundsPage.unavailable.description'))}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        onClick={() => refreshAll()}
                    >
                        <RefreshCw className="h-4 w-4" />
                        {t('superAdmin.refundsPage.unavailable.retry')}
                    </button>
                </div>
            </div>
        )
    }

    return (
        <>
            <div className="space-y-6">
                <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                    <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(59,130,246,0.12),_transparent_26%)]" />
                        <div className="relative flex flex-col gap-6">
                            <div className="flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                                <div className="max-w-3xl">
                                    <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                        <ArrowUpRight className="h-4 w-4" />
                                        {t('superAdmin.refundsPage.hero.badge')}
                                    </div>
                                    <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                        {t('superAdmin.refundsPage.hero.title')}
                                    </h1>
                                    <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                        {t('superAdmin.refundsPage.hero.description')}
                                    </p>
                                </div>

                                <button
                                    type="button"
                                    className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                    disabled={refundsQuery.isFetching || detailsQuery.isFetching}
                                    onClick={() => refreshAll()}
                                >
                                    <RefreshCw
                                        className={`h-4 w-4 ${refundsQuery.isFetching || detailsQuery.isFetching ? 'animate-spin' : ''}`}
                                    />
                                    {refundsQuery.isFetching || detailsQuery.isFetching ? t('superAdmin.common.actions.refreshing') : t('superAdmin.common.actions.refresh')}
                                </button>
                            </div>

                            <div className="grid gap-3 lg:grid-cols-4">
                                <StatCard
                                    label={t('superAdmin.refundsPage.stats.openRequests')}
                                    value={String(requestedCount)}
                                    caption={t('superAdmin.refundsPage.stats.openRequestsCaption')}
                                    tone="border-slate-200 bg-white text-slate-950"
                                />
                                <StatCard
                                    label={t('superAdmin.refundsPage.stats.failedPayouts')}
                                    value={String(failedCount)}
                                    caption={t('superAdmin.refundsPage.stats.failedPayoutsCaption')}
                                    tone="border-rose-200 bg-rose-50 text-rose-950"
                                />
                                <StatCard
                                    label={t('superAdmin.refundsPage.stats.awaitingPayout')}
                                    value={String(awaitingPayoutCount)}
                                    caption={t('superAdmin.refundsPage.stats.awaitingPayoutCaption')}
                                    tone="border-yellow-200 bg-yellow-50 text-yellow-950"
                                />
                                <StatCard
                                    label={t('superAdmin.refundsPage.stats.matchingRefunds')}
                                    value={String(totalCount)}
                                    caption={t('superAdmin.refundsPage.stats.matchingRefundsCaption')}
                                    tone="border-emerald-200 bg-emerald-50 text-emerald-950"
                                />
                            </div>
                        </div>
                    </div>

                    <div className="grid gap-4 border-t border-slate-100 bg-slate-50/80 px-6 py-5 text-sm text-slate-600 sm:grid-cols-3 sm:px-8">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.refundsPage.metadata.apiTimestamp')}</p>
                            <p className="mt-2 font-medium text-slate-900">{formatDateTime(meta?.timestamp, locale, unavailable)}</p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.refundsPage.metadata.activeFilters')}</p>
                            <p className="mt-2 font-medium text-slate-900">{activeFilterCount}</p>
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.refundsPage.metadata.selectedRefund')}</p>
                            <p className="mt-2 font-medium text-slate-900">{selectedRefundId ? selectedRefundId.slice(0, 8) : t('superAdmin.refundsPage.labels.none')}</p>
                        </div>
                    </div>
                </section>
                {successMessage ? (
                    <div className="rounded-[28px] border border-emerald-200 bg-emerald-50 px-5 py-4 text-sm text-emerald-900 shadow-sm">
                        {successMessage}
                    </div>
                ) : null}

                {refundsQuery.isError && refundsQuery.data ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        {t('superAdmin.refundsPage.alerts.cachedQueue')}
                    </div>
                ) : null}

                {tenantsQuery.isError ? (
                    <div className="rounded-[28px] border border-amber-200 bg-amber-50 px-5 py-4 text-sm text-amber-900 shadow-sm">
                        {t('superAdmin.refundsPage.alerts.vendorLookupUnavailable')}
                    </div>
                ) : null}

                <FiltersPanel
                    filters={filters}
                    tenants={tenantsQuery.data ?? []}
                    isLoadingTenants={tenantsQuery.isLoading}
                    isRefreshing={refundsQuery.isFetching}
                    onStatusChange={(value) => handleSetFilter('status', value)}
                    onVendorChange={(value) => handleSetFilter('vendorId', value)}
                    onFromDateChange={(value) => handleSetFilter('fromDate', value)}
                    onToDateChange={(value) => handleSetFilter('toDate', value)}
                    onReset={handleResetFilters}
                />

                {refunds.length === 0 && !refundsQuery.isLoading ? (
                    <EmptyState hasFilters={activeFilterCount > 0} />
                ) : (
                    <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                        <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-6 py-5">
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.refundsPage.queue.badge')}</p>
                                <h2 className="mt-2 text-xl font-semibold text-slate-950">{t('superAdmin.refundsPage.queue.title')}</h2>
                            </div>
                            {refundsQuery.isFetching ? (
                                <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-semibold uppercase tracking-[0.16em] text-slate-600">
                                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                    {t('superAdmin.common.actions.refreshing')}
                                </div>
                            ) : null}
                        </div>

                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-slate-100">
                                <thead className="bg-slate-50/90">
                                    <tr className="text-left text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.refund')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.order')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.customer')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.product')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.amount')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.status')}</th>
                                        <th className="px-6 py-4">{t('superAdmin.refundsPage.table.created')}</th>
                                        <th className="px-6 py-4 text-right">{t('superAdmin.refundsPage.table.review')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100">
                                    {refundsQuery.isLoading && !refundsQuery.data
                                        ? Array.from({ length: 6 }).map((_, index) => (
                                              <tr key={index}>
                                                  {Array.from({ length: 8 }).map((__, cellIndex) => (
                                                      <td key={cellIndex} className="px-6 py-5">
                                                          <div className="h-5 animate-pulse rounded-full bg-slate-100" />
                                                      </td>
                                                  ))}
                                              </tr>
                                          ))
                                        : refunds.map((refund) => {
                                              const isSelected = refund.refundId === currentSelectedRefundId

                                              return (
                                                  <tr
                                                      key={refund.refundId}
                                                      className={[
                                                          'transition hover:bg-slate-50/80',
                                                          isSelected ? 'bg-emerald-50/40' : 'bg-white',
                                                      ].join(' ')}
                                                  >
                                                      <td className="px-6 py-5">
                                                          <div className="min-w-[11rem]">
                                                              <p className="font-semibold text-slate-950">{refund.refundId.slice(0, 8)}</p>
                                                              <p className="mt-1 text-sm text-slate-500">{refund.vendorName}</p>
                                                          </div>
                                                      </td>
                                                      <td className="px-6 py-5">
                                                          <div>
                                                              <p className="font-medium text-slate-900">{refund.orderNumber}</p>
                                                              <p className="mt-1 text-sm text-slate-500">{refund.orderId.slice(0, 8)}</p>
                                                          </div>
                                                      </td>
                                                      <td className="px-6 py-5">
                                                          <div>
                                                              <p className="font-medium text-slate-900">{refund.customerName}</p>
                                                              <p className="mt-1 text-sm text-slate-500">{refund.customerId.slice(0, 8)}</p>
                                                          </div>
                                                      </td>
                                                      <td className="px-6 py-5">
                                                          <div className="max-w-xs">
                                                              <p className="font-medium text-slate-900">{refund.productName}</p>
                                                              <p className="mt-1 text-sm text-slate-500">{t('superAdmin.refundsPage.table.quantity', { count: refund.quantity })}</p>
                                                          </div>
                                                      </td>
                                                      <td className="px-6 py-5">
                                                          <p className="font-semibold text-slate-950">{formatCurrency(refund.amount, refund.currency, locale)}</p>
                                                      </td>
                                                      <td className="px-6 py-5">
                                                          <span
                                                              className={`inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold ${getRefundStatusTone(refund.status)}`}
                                                          >
                                                              {formatRefundStatusLabel(refund.status, t)}
                                                          </span>
                                                      </td>
                                                      <td className="px-6 py-5 text-sm text-slate-600">
                                                          {formatDateTime(refund.createdAt, locale, unavailable)}
                                                      </td>
                                                      <td className="px-6 py-5 text-right">
                                                          <button
                                                              type="button"
                                                              className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                                              onClick={() => handleSelectRefund(refund.refundId)}
                                                          >
                                                              {t('superAdmin.refundsPage.table.review')}
                                                              <ArrowUpRight className="h-4 w-4" />
                                                          </button>
                                                      </td>
                                                  </tr>
                                              )
                                          })}
                                </tbody>
                            </table>
                        </div>
                    </section>
                )}

                <section className="flex flex-col gap-4 rounded-[32px] border border-slate-200 bg-white px-6 py-5 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)] lg:flex-row lg:items-center lg:justify-between">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-600">
                            <AlertTriangle className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('superAdmin.refundsPage.pagination.badge')}</p>
                            <p className="mt-1 text-sm text-slate-700">
                                {t('superAdmin.refundsPage.pagination.summary', {
                                    page: filters.page,
                                    total: totalPages || 1,
                                    count: totalCount,
                                })}
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                        <label className="flex items-center gap-3 text-sm text-slate-600">
                            <span>{t('superAdmin.refundsPage.pagination.pageSize')}</span>
                            <select
                                value={filters.pageSize}
                                onChange={(event) => handlePageSizeChange(Number(event.target.value))}
                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                {pageSizeOptions.map((size) => (
                                    <option key={size} value={size}>
                                        {size}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <div className="flex items-center gap-3">
                            <button
                                type="button"
                                className="inline-flex items-center justify-center rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={filters.page <= 1}
                                onClick={() => handlePageChange(filters.page - 1)}
                            >
                                {t('superAdmin.common.actions.previous')}
                            </button>
                            <button
                                type="button"
                                className="inline-flex items-center justify-center rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                                disabled={!totalPages || filters.page >= totalPages}
                                onClick={() => handlePageChange(filters.page + 1)}
                            >
                                {t('superAdmin.common.actions.next')}
                            </button>
                        </div>
                    </div>
                </section>
            </div>

            <RefundDetailsDrawer
                refund={acceptedRefundDetails}
                isOpen={Boolean(currentSelectedRefundId)}
                isLoading={detailsQuery.isLoading}
                isError={detailsQuery.isError}
                errorMessage={
                    detailsQuery.isError
                        ? getErrorMessage(detailsQuery.error, t('superAdmin.refundsPage.drawer.loadErrorDescription'))
                        : null
                }
                onClose={() => handleSelectRefund(null)}
                onRetryLoad={() => detailsQuery.refetch()}
                onApprove={() => openActionModal('approve')}
                onExecute={openExecuteModal}
                onOverrideReturn={openReturnOverrideModal}
                onMarkReturnFailed={openReturnFailedModal}
                onReject={() => openActionModal('reject')}
                onRetryExecution={() => openActionModal('retry')}
            />

            {actionModal && currentSelectedRefundId ? (
                <ActionModal
                    title={
                        actionModal === 'approve'
                            ? t('superAdmin.refundsPage.actionModal.titles.approve')
                            : actionModal === 'reject'
                              ? t('superAdmin.refundsPage.actionModal.titles.reject')
                              : t('superAdmin.refundsPage.actionModal.titles.retry')
                    }
                    description={
                        actionModal === 'approve'
                            ? t('superAdmin.refundsPage.actionModal.descriptions.approve')
                            : actionModal === 'reject'
                              ? t('superAdmin.refundsPage.actionModal.descriptions.reject')
                              : t('superAdmin.refundsPage.actionModal.descriptions.retry')
                    }
                    confirmLabel={
                        actionModal === 'approve'
                            ? t('superAdmin.refundsPage.actionModal.confirmLabels.approve')
                            : actionModal === 'reject'
                              ? t('superAdmin.refundsPage.actionModal.confirmLabels.reject')
                              : t('superAdmin.refundsPage.actionModal.confirmLabels.retry')
                    }
                    confirmTone={
                        actionModal === 'approve'
                            ? 'bg-emerald-600 hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-emerald-400'
                            : actionModal === 'reject'
                              ? 'bg-rose-600 hover:bg-rose-700 disabled:cursor-not-allowed disabled:bg-rose-400'
                              : 'bg-amber-600 hover:bg-amber-700 disabled:cursor-not-allowed disabled:bg-amber-400'
                    }
                    noteLabel={
                        actionModal === 'reject'
                            ? t('superAdmin.refundsPage.actionModal.noteLabels.rejectionReason')
                            : t('superAdmin.refundsPage.actionModal.noteLabels.internalNote')
                    }
                    noteValue={actionNote}
                    notePlaceholder={
                        actionModal === 'approve'
                            ? t('superAdmin.refundsPage.actionModal.placeholders.approve')
                            : actionModal === 'reject'
                              ? t('superAdmin.refundsPage.actionModal.placeholders.reject')
                              : t('superAdmin.refundsPage.actionModal.placeholders.retry')
                    }
                    onNoteChange={setActionNote}
                    isSubmitting={currentActionIsSubmitting}
                    errorMessage={currentActionError}
                    onCancel={() => {
                        setActionModal(null)
                        setActionError(null)
                    }}
                    onConfirm={handleActionConfirm}
                />
            ) : null}

            {executeModalOpen && acceptedRefundDetails ? (
                <ExecuteRefundModal
                    refund={acceptedRefundDetails}
                    method={executeMethod}
                    transactionReference={executeTransactionReference}
                    notes={executeNotes}
                    confirmMoneySent={confirmMoneySent}
                    confirmMethodOverride={confirmMethodOverride}
                    isSubmitting={executeMutation.isPending}
                    errorMessage={executeError}
                    onMethodChange={setExecuteMethod}
                    onTransactionReferenceChange={setExecuteTransactionReference}
                    onNotesChange={setExecuteNotes}
                    onConfirmMoneySentChange={setConfirmMoneySent}
                    onConfirmMethodOverrideChange={setConfirmMethodOverride}
                    onCancel={() => {
                        setExecuteModalOpen(false)
                        setConfirmMoneySent(false)
                        setExecuteError(null)
                    }}
                    onConfirm={handleExecuteConfirm}
                />
            ) : null}

            {returnModalMode && acceptedRefundDetails?.returnRequest ? (
                <ReturnTransitionModal
                    title={
                        returnModalMode === 'override'
                            ? t('superAdmin.refundsPage.returnTransition.titles.override')
                            : t('superAdmin.refundsPage.returnTransition.titles.failed')
                    }
                    description={
                        returnModalMode === 'override'
                            ? t('superAdmin.refundsPage.returnTransition.descriptions.override')
                            : t('superAdmin.refundsPage.returnTransition.descriptions.failed')
                    }
                    statusLabel={t('superAdmin.refundsPage.returnTransition.targetStatus')}
                    selectedStatus={returnModalMode === 'override' ? overrideStatus : undefined}
                    statusOptions={returnModalMode === 'override' ? acceptedRefundDetails.returnRequest.allowedOverrideStatuses : undefined}
                    timestampLabel={
                        returnModalMode === 'override'
                            ? t('superAdmin.refundsPage.returnTransition.effectiveAt')
                            : t('superAdmin.refundsPage.returnTransition.failureRecordedAt')
                    }
                    confirmLabel={
                        returnModalMode === 'override'
                            ? t('superAdmin.refundsPage.actions.applyOverride')
                            : t('superAdmin.refundsPage.actions.markFailed')
                    }
                    confirmTone={
                        returnModalMode === 'override'
                            ? 'bg-sky-600 hover:bg-sky-700 disabled:cursor-not-allowed disabled:bg-sky-400'
                            : 'bg-rose-600 hover:bg-rose-700 disabled:cursor-not-allowed disabled:bg-rose-400'
                    }
                    accentTone={returnModalMode === 'override' ? 'text-sky-700' : 'text-rose-700'}
                    isSubmitting={returnMutation.isPending}
                    errorMessage={returnError}
                    timestampValue={returnTimestamp}
                    trackingNumber={returnTrackingNumber}
                    courierName={returnCourierName}
                    notes={returnNotes}
                    onStatusChange={(value) => {
                        const nextStatus = value as SuperAdminOverrideReturnStatus
                        if (!acceptedRefundDetails.returnRequest!.allowedOverrideStatuses.includes(nextStatus)) {
                            return
                        }

                        setOverrideStatus(nextStatus)
                        setReturnTimestamp(
                            formatDateTimeInputValue(getReturnStageTimestamp(acceptedRefundDetails.returnRequest!, nextStatus)),
                        )
                    }}
                    onTimestampChange={setReturnTimestamp}
                    onTrackingNumberChange={setReturnTrackingNumber}
                    onCourierNameChange={setReturnCourierName}
                    onNotesChange={setReturnNotes}
                    onCancel={() => {
                        setReturnModalMode(null)
                        setReturnError(null)
                    }}
                    onConfirm={handleReturnConfirm}
                />
            ) : null}
        </>
    )
}

