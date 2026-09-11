import { useTranslation } from 'react-i18next'
import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
    AlertTriangle,
    ArrowDownLeft,
    ArrowUpRight,
    Download,
    Filter,
    Landmark,
    RefreshCw,
    Wallet,
} from 'lucide-react'
import { useDebouncedValue } from '../hooks/useDebouncedValue'
import { useRTL } from '../hooks/useRTL'
import { useAuthStore } from '../store/authStore'
import {
    deriveVendorEarningsDashboardPresentation,
    deriveVendorFinanceStatementPresentation,
    deriveVendorFinancialDashboardPresentation,
    deriveVendorPaymentsDashboardPresentation,
} from '../auth/commerceHubVendorFinancialParticipationAuthority'
import { getCommerceHubEarningsDashboard } from '../services/commerceHubEarningsApi'
import {
    COMMERCEHUB_FINANCE_TRANSACTION_TYPES,
    downloadCommerceHubFinanceStatementCsv,
    getCommerceHubFinanceDashboard,
    getCommerceHubFinanceStatement,
} from '../services/commerceHubFinanceApi'
import { getCommerceHubPaymentsDashboard } from '../services/commerceHubPaymentsApi'
import { formatDateTime as formatLocalizedDateTime } from '../utils/dateFormat'
import { deriveCommerceHubAuthorityDiagnostic } from '../utils/commerceHubAuthorityDiagnostics'
import {
    COMMERCEHUB_AUTHORITY_CURRENCY,
    COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL,
    resolveCommerceHubDisplayCurrency,
} from '../utils/commerceHubCurrencyAuthority'

const PAGE_SIZE = 25

function formatCurrency(value, currency = COMMERCEHUB_AUTHORITY_CURRENCY) {
    const normalizedCurrency = resolveCommerceHubDisplayCurrency(currency)
    if (!normalizedCurrency) {
        return COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: normalizedCurrency,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(value || 0))
}

function formatSignedCurrency(value, currency = COMMERCEHUB_AUTHORITY_CURRENCY) {
    const numericValue = Number(value || 0)
    const prefix = numericValue >= 0 ? '+' : '-'
    return `${prefix}${formatCurrency(Math.abs(numericValue), currency)}`
}

function formatDateTime(value, locale, t) {
    if (!value) {
        return t('CommerceHubFinance.labels.pending')
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return t('CommerceHubFinance.labels.pending')
    }

    return formatLocalizedDateTime(date, locale)
}

function formatStatusLabel(value, t) {
    const normalized = String(value || 'ACTIVE').toUpperCase()
    const translationMap = {
        ACTIVE: 'active',
        AT_RISK: 'atRisk',
        APPROVED: 'approved',
        CANCELLED: 'cancelled',
        COMPLETED: 'completed',
        FAILED: 'failed',
        GRACE_PERIOD: 'gracePeriod',
        PAID: 'paid',
        PENDING: 'pending',
        PENDING_VERIFICATION: 'pendingVerification',
        PROCESSING: 'processing',
        REJECTED: 'rejected',
        SUSPENDED: 'suspended',
    }
    const translationKey = translationMap[normalized]

    return translationKey
        ? t(`CommerceHubFinance.statuses.${translationKey}`)
        : String(value || 'ACTIVE').split('_').join(' ')
}

function formatRefundImpactType(value, t) {
    switch (value) {
        case 'DebtIncrease':
            return t('CommerceHubFinance.refundImpactTypes.debtIncrease')
        case 'DebtDecrease':
            return t('CommerceHubFinance.refundImpactTypes.debtDecrease')
        default:
            return value || t('CommerceHubFinance.refundImpactTypes.refundImpact')
    }
}

function typeLabel(value, t) {
    switch (value) {
        case 'OrderPayment':
            return t('CommerceHubFinance.transactionTypes.orderPayment')
        case 'Commission':
            return t('CommerceHubFinance.transactionTypes.commission')
        case 'Refund':
            return t('CommerceHubFinance.transactionTypes.refund')
        case 'Payout':
            return t('CommerceHubFinance.transactionTypes.payout')
        case 'Adjustment':
            return t('CommerceHubFinance.transactionTypes.adjustment')
        case 'Settlement':
            return t('CommerceHubFinance.transactionTypes.settlement')
        default:
            return value
    }
}

function getHistoryStatusTone(status) {
    const normalizedStatus = String(status || '').toUpperCase()

    if (
        normalizedStatus.includes('FAIL') ||
        normalizedStatus.includes('REJECT') ||
        normalizedStatus.includes('CANCEL')
    ) {
        return 'border-rose-200 bg-rose-50 text-rose-800'
    }

    if (
        normalizedStatus.includes('COMPLETE') ||
        normalizedStatus.includes('APPROV') ||
        normalizedStatus.includes('SUCCESS') ||
        normalizedStatus.includes('PAID')
    ) {
        return 'border-emerald-200 bg-emerald-50 text-emerald-800'
    }

    return 'border-amber-200 bg-amber-50 text-amber-800'
}

function SummaryCard({ icon: Icon, label, value, description, tone = 'slate' }) {
    const tones = {
        emerald: 'border-emerald-200 bg-emerald-50 text-emerald-900',
        sky: 'border-sky-200 bg-sky-50 text-sky-900',
        rose: 'border-rose-200 bg-rose-50 text-rose-900',
        amber: 'border-amber-200 bg-amber-50 text-amber-900',
        slate: 'border-slate-200 bg-white text-slate-950',
    }

    const toneClass = tones[tone] ?? tones.slate

    return (
        <article className={`rounded-[28px] border p-5 shadow-sm ${toneClass}`}>
            <div className="flex items-start justify-between gap-4">
                <div>
                    <p className="text-sm font-medium opacity-70">{label}</p>
                    <p className="mt-4 text-3xl font-semibold tracking-tight">{value}</p>
                    <p className="mt-3 text-sm opacity-75">{description}</p>
                </div>
                <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-white/80 shadow-sm">
                    <Icon className="h-5 w-5" />
                </div>
            </div>
        </article>
    )
}

export default function CommerceHubFinance() {
    const { t } = useTranslation()
    const { dir, locale } = useRTL()
    const user = useAuthStore((state) => state.user)
    const vendorSessionScope = ['commercehub-vendor-session', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous']
    const [filters, setFilters] = useState({
        fromDate: '',
        toDate: '',
        transactionTypes: [],
    })
    const [statementPage, setStatementPage] = useState(1)
    const [isDownloadingCsv, setIsDownloadingCsv] = useState(false)

    const dashboardQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-finance', 'dashboard'],
        queryFn: getCommerceHubFinanceDashboard,
        staleTime: 30_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: true,
    })
    const earningsQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-finance', 'earnings-dashboard'],
        queryFn: getCommerceHubEarningsDashboard,
        staleTime: 30_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: true,
    })
    const paymentsQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-finance', 'payments-dashboard'],
        queryFn: getCommerceHubPaymentsDashboard,
        staleTime: 30_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: true,
    })
    const debouncedStatementInputs = useDebouncedValue(
        {
            fromDate: filters.fromDate,
            toDate: filters.toDate,
            transactionTypes: filters.transactionTypes,
        },
        350,
    )

    const statementFilters = useMemo(
        () => ({
            fromDate: debouncedStatementInputs.fromDate || undefined,
            toDate: debouncedStatementInputs.toDate || undefined,
            transactionTypes: debouncedStatementInputs.transactionTypes,
            page: statementPage,
            pageSize: PAGE_SIZE,
        }),
        [debouncedStatementInputs, statementPage],
    )

    const statementQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-finance', 'statement', statementFilters],
        queryFn: () => getCommerceHubFinanceStatement(statementFilters),
        staleTime: 30_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: true,
    })

    const dashboard = dashboardQuery.isFetching
        ? null
        : deriveVendorFinancialDashboardPresentation(dashboardQuery.data, user)
    const earningsDashboard = earningsQuery.isFetching
        ? null
        : deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)
    const paymentsDashboard = paymentsQuery.isFetching
        ? null
        : deriveVendorPaymentsDashboardPresentation(paymentsQuery.data, user)
    const statementMeta = statementQuery.isFetching
        ? null
        : deriveVendorFinanceStatementPresentation(statementQuery.data, user)
    const statementRows = statementMeta?.transactions ?? []
    const hasOutstandingDebt = Number(dashboard?.amountOwedByVendor || 0) > 0
    const recentRefundImpacts = dashboard?.recentRefundImpacts || []
    const payoutHistory = earningsDashboard?.payoutHistory || []
    const paymentHistory = paymentsDashboard?.paymentHistory || []
    const statementTotalPages = statementMeta
        ? Math.max(1, Math.ceil(statementMeta.totalCount / Math.max(statementMeta.pageSize || PAGE_SIZE, 1)))
        : 1
    const activeFilterCount =
        Number(Boolean(filters.fromDate)) +
        Number(Boolean(filters.toDate)) +
        filters.transactionTypes.length

    useEffect(() => {
        const diagnostics = [
            deriveCommerceHubAuthorityDiagnostic({
                surface: 'Finance dashboard',
                endpoint: '/api/admin/commercehub/finance/dashboard',
                query: dashboardQuery,
                presentation: dashboard,
            }),
            deriveCommerceHubAuthorityDiagnostic({
                surface: 'Finance earnings',
                endpoint: '/api/commercehub/earnings',
                query: earningsQuery,
                presentation: earningsDashboard,
            }),
            deriveCommerceHubAuthorityDiagnostic({
                surface: 'Finance payments',
                endpoint: '/api/admin/commercehub/payments',
                query: paymentsQuery,
                presentation: paymentsDashboard,
            }),
            deriveCommerceHubAuthorityDiagnostic({
                surface: 'Finance statement',
                endpoint: '/api/admin/commercehub/finance/statement',
                query: statementQuery,
                presentation: statementMeta,
            }),
        ].filter(Boolean)

        diagnostics.forEach((diagnostic) => {
            console.error('CommerceHub authority load rejected', diagnostic)
        })
    }, [
        dashboard,
        dashboardQuery.data,
        dashboardQuery.error,
        dashboardQuery.isFetching,
        earningsDashboard,
        earningsQuery.data,
        earningsQuery.error,
        earningsQuery.isFetching,
        paymentsDashboard,
        paymentsQuery.data,
        paymentsQuery.error,
        paymentsQuery.isFetching,
        statementMeta,
        statementQuery.data,
        statementQuery.error,
        statementQuery.isFetching,
    ])

    function refreshAll() {
        dashboardQuery.refetch()
        earningsQuery.refetch()
        paymentsQuery.refetch()
        statementQuery.refetch()
    }

    function toggleTransactionType(type) {
        setFilters((current) => ({
            ...current,
            transactionTypes: current.transactionTypes.includes(type)
                ? current.transactionTypes.filter((item) => item !== type)
                : [...current.transactionTypes, type],
        }))
        setStatementPage(1)
    }

    function clearFilters() {
        setFilters({
            fromDate: '',
            toDate: '',
            transactionTypes: [],
        })
        setStatementPage(1)
    }

    async function handleDownloadCsv() {
        setIsDownloadingCsv(true)

        try {
            const blob = await downloadCommerceHubFinanceStatementCsv({
                fromDate: filters.fromDate || undefined,
                toDate: filters.toDate || undefined,
                transactionTypes: filters.transactionTypes,
            })

            const downloadUrl = window.URL.createObjectURL(blob)
            const link = document.createElement('a')
            link.href = downloadUrl
            link.download = `commercehub-vendor-statement-${new Date().toISOString().slice(0, 10)}.csv`
            document.body.appendChild(link)
            link.click()
            link.remove()
            window.URL.revokeObjectURL(downloadUrl)
        } catch (error) {
            console.error('Failed to download CommerceHub finance CSV', error)
            window.alert(t('CommerceHubFinance.actions.downloadCsvError'))
        } finally {
            setIsDownloadingCsv(false)
        }
    }

    if (!dashboard) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-rose-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <Wallet className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                        {dashboardQuery.isFetching
                            ? t('CommerceHubFinance.actions.refreshing')
                            : t('CommerceHubFinance.unavailable.title')}
                    </h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {dashboardQuery.error?.response?.data?.message ||
                            dashboardQuery.error?.message ||
                            t('CommerceHubFinance.unavailable.description')}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        onClick={refreshAll}
                        disabled={dashboardQuery.isFetching}
                    >
                        <RefreshCw className="h-4 w-4" />{t('CommerceHubFinance.actions.retryRequest')}</button>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="relative overflow-hidden px-6 py-6 sm:px-8 sm:py-8">
                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.18),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(14,165,233,0.12),_transparent_24%)]" />
                    <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                <Wallet className="h-4 w-4" />{t('CommerceHubFinance.hero.badge')}</div>

                            <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">{t('CommerceHubFinance.hero.title')}</h1>
                            <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">{t('CommerceHubFinance.hero.description')}</p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2 xl:min-w-[360px]">
                            <div className="rounded-3xl border border-slate-200 bg-slate-50 px-5 py-4 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubFinance.hero.lastLedgerActivity')}</p>
                                <p className="mt-2 text-lg font-semibold text-slate-950">
                                    {formatDateTime(dashboard?.lastTransactionAtUtc, locale, t)}
                                </p>
                            </div>

                            <button
                                type="button"
                                className="inline-flex items-center justify-center gap-2 rounded-3xl bg-slate-950 px-5 py-4 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                disabled={dashboardQuery.isFetching || earningsQuery.isFetching || paymentsQuery.isFetching || statementQuery.isFetching}
                                onClick={refreshAll}
                            >
                                <RefreshCw className={`h-4 w-4 ${dashboardQuery.isFetching || earningsQuery.isFetching || paymentsQuery.isFetching || statementQuery.isFetching ? 'animate-spin' : ''}`} />
                                {dashboardQuery.isFetching || earningsQuery.isFetching || paymentsQuery.isFetching || statementQuery.isFetching
                                    ? t('CommerceHubFinance.actions.refreshing')
                                    : t('CommerceHubFinance.actions.refresh')}
                            </button>
                        </div>
                    </div>
                </div>
            </section>

            <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                {hasOutstandingDebt ? (
                    <article
                        className={`md:col-span-2 xl:col-span-4 rounded-[28px] border p-5 shadow-sm ${
                            dashboard?.isSuspendedFromNewOrders
                                ? 'border-rose-200 bg-rose-50 text-rose-950'
                                : 'border-amber-200 bg-amber-50 text-amber-950'
                        }`}
                    >
                        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                            <div className="flex items-start gap-3">
                                <div className="mt-1 flex h-11 w-11 items-center justify-center rounded-2xl bg-white/80 shadow-sm">
                                    <AlertTriangle className="h-5 w-5" />
                                </div>
                                <div>
                                    <p className="text-sm font-semibold uppercase tracking-[0.18em] opacity-70">
                                        {dashboard?.isSuspendedFromNewOrders
                                            ? t('CommerceHubFinance.debtBanner.collectionEnforcement')
                                            : dashboard?.isGracePeriodActive
                                                ? t('CommerceHubFinance.debtBanner.debtGracePeriod')
                                                : t('CommerceHubFinance.debtBanner.debtMonitoring')}
                                    </p>
                                    <h2 className="mt-2 text-2xl font-semibold tracking-tight">
                                        {dashboard?.isSuspendedFromNewOrders
                                            ? t('CommerceHubFinance.debtBanner.pausedTitle')
                                            : dashboard?.isGracePeriodActive
                                                ? t('CommerceHubFinance.debtBanner.graceTitle')
                                                : t('CommerceHubFinance.debtBanner.monitoringTitle')}
                                    </h2>
                                    <p className="mt-3 max-w-3xl text-sm leading-7 opacity-80">{t('CommerceHubFinance.debtBanner.description')}</p>
                                </div>
                            </div>

                            <div className="grid gap-3 sm:grid-cols-3">
                                <div className="rounded-3xl border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-60">{t('CommerceHubFinance.debtBanner.amountOwed')}</p>
                                    <p className="mt-2 text-2xl font-semibold">
                                        {formatCurrency(dashboard?.amountOwedByVendor, dashboard?.currency)}
                                    </p>
                                </div>
                                <div className="rounded-3xl border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-60">{t('CommerceHubFinance.debtBanner.debtStatus')}</p>
                                    <p className="mt-2 text-2xl font-semibold">
                                        {formatStatusLabel(dashboard?.debtStatus, t)}
                                    </p>
                                </div>
                                <div className="rounded-3xl border border-white/70 bg-white/80 px-5 py-4 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-60">
                                        {dashboard?.isSuspendedFromNewOrders
                                            ? t('CommerceHubFinance.debtBanner.restoreOrdering')
                                            : dashboard?.isGracePeriodActive
                                                ? t('CommerceHubFinance.debtBanner.graceDeadline')
                                                : t('CommerceHubFinance.debtBanner.suspensionThreshold')}
                                    </p>
                                    <p className="mt-2 text-2xl font-semibold">
                                        {dashboard?.isSuspendedFromNewOrders
                                            ? formatCurrency(dashboard?.minimumPaymentToRestoreOrdering, dashboard?.currency)
                                            : dashboard?.isGracePeriodActive
                                                ? formatDateTime(dashboard?.graceDeadlineUtc, locale, t)
                                                : formatCurrency(dashboard?.hardLimitAmount, dashboard?.currency)}
                                    </p>
                                </div>
                            </div>
                        </div>
                    </article>
                ) : null}

                <SummaryCard
                    icon={Wallet}
                    label={t('CommerceHubFinance.summary.currentBalance')}
                    value={formatCurrency(dashboard?.currentBalance, dashboard?.currency)}
                    description={t('CommerceHubFinance.summary.currentBalanceDescription')}
                    tone="emerald"
                />
                <SummaryCard
                    icon={ArrowUpRight}
                    label={t('CommerceHubFinance.summary.totalEarnings')}
                    value={formatCurrency(dashboard?.totalEarnings, dashboard?.currency)}
                    description={t('CommerceHubFinance.summary.totalEarningsDescription')}
                    tone="sky"
                />
                <SummaryCard
                    icon={Landmark}
                    label={t('CommerceHubFinance.summary.totalCommissions')}
                    value={formatCurrency(dashboard?.totalCommissions, dashboard?.currency)}
                    description={t('CommerceHubFinance.summary.totalCommissionsDescription')}
                    tone="amber"
                />
                <SummaryCard
                    icon={ArrowDownLeft}
                    label={t('CommerceHubFinance.summary.totalRefundImpact')}
                    value={formatCurrency(dashboard?.totalRefunds, dashboard?.currency)}
                    description={t('CommerceHubFinance.summary.totalRefundImpactDescription')}
                    tone="rose"
                />
            </section>

            <section className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">{t('CommerceHubFinance.debtHistory.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{t('CommerceHubFinance.debtHistory.title')}</h2>
                        <p className="mt-2 text-sm leading-6 text-slate-500">{t('CommerceHubFinance.debtHistory.description')}</p>
                    </div>

                    <div className="rounded-3xl border border-slate-200 bg-slate-50 px-5 py-4 shadow-sm">
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubFinance.debtHistory.currentDebt')}</p>
                        <p className="mt-2 text-2xl font-semibold text-slate-950">
                            {formatCurrency(dashboard?.amountOwedByVendor, dashboard?.currency)}
                        </p>
                    </div>
                </div>

                {recentRefundImpacts.length === 0 ? (
                    <div className="mt-6 rounded-[28px] border border-dashed border-slate-200 bg-slate-50 px-5 py-10 text-center text-sm text-slate-500">{t('CommerceHubFinance.debtHistory.empty')}</div>
                ) : (
                    <div className="mt-6 overflow-hidden rounded-[28px] border border-slate-200">
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-slate-200 text-sm">
                                <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    <tr>
                                        <th className="px-5 py-4">{t('CommerceHubFinance.common.date')}</th>
                                        <th className="px-5 py-4">{t('CommerceHubFinance.debtHistory.impact')}</th>
                                        <th className="px-5 py-4">{t('CommerceHubFinance.debtHistory.debtChange')}</th>
                                        <th className="px-5 py-4">{t('CommerceHubFinance.common.notes')}</th>
                                    </tr>
                                </thead>
                                <tbody className="divide-y divide-slate-100 bg-white">
                                    {recentRefundImpacts.map((impact) => (
                                        <tr key={`${impact.refundId}-${impact.correlationId || impact.createdAtUtc}`}>
                                            <td className="px-5 py-4 text-slate-600">{formatDateTime(impact.createdAtUtc, locale, t)}</td>
                                            <td className="px-5 py-4">
                                                <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] ${
                                                    impact.signedDebtAmount >= 0
                                                        ? 'border-rose-200 bg-rose-50 text-rose-800'
                                                        : 'border-emerald-200 bg-emerald-50 text-emerald-800'
                                                }`}>
                                                    {formatRefundImpactType(impact.impactType, t)}
                                                </span>
                                            </td>
                                            <td className={`px-5 py-4 font-semibold ${impact.signedDebtAmount >= 0 ? 'text-rose-700' : 'text-emerald-700'}`}>
                                                {formatSignedCurrency(impact.signedDebtAmount, impact.currency)}
                                            </td>
                                            <td className="px-5 py-4 text-slate-600">
                                                <div>{impact.description || t('CommerceHubFinance.debtHistory.defaultImpactDescription')}</div>
                                                <div className="mt-1 font-mono text-xs text-slate-400">
                                                    {t('CommerceHubFinance.debtHistory.refundReference', { refundId: impact.refundId })}
                                                </div>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </div>
                )}
            </section>

            <section className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">{t('CommerceHubFinance.moneyFlow.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{t('CommerceHubFinance.moneyFlow.title')}</h2>
                        <p className="mt-2 text-sm leading-6 text-slate-500">{t('CommerceHubFinance.moneyFlow.description')}</p>
                    </div>

                    <div className="grid gap-3 sm:grid-cols-2">
                        <div className="rounded-3xl border border-slate-200 bg-slate-50 px-5 py-4 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubFinance.moneyFlow.payoutsReceived')}</p>
                            <p className="mt-2 text-2xl font-semibold text-slate-950">
                                {formatCurrency(dashboard?.totalPayouts, dashboard?.currency)}
                            </p>
                        </div>
                        <div className="rounded-3xl border border-slate-200 bg-slate-50 px-5 py-4 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubFinance.moneyFlow.debtOutstanding')}</p>
                            <p className="mt-2 text-2xl font-semibold text-slate-950">
                                {formatCurrency(paymentsDashboard?.amountOwedByVendor, paymentsDashboard?.currency || dashboard?.currency)}
                            </p>
                        </div>
                    </div>
                </div>

                <div className="mt-6 grid gap-6 xl:grid-cols-2">
                    <div className="overflow-hidden rounded-[28px] border border-slate-200">
                        <div className="border-b border-slate-200 bg-slate-50 px-5 py-4">
                            <h3 className="text-lg font-semibold text-slate-950">{t('CommerceHubFinance.moneyFlow.payoutsTitle')}</h3>
                            <p className="mt-1 text-sm text-slate-500">{t('CommerceHubFinance.moneyFlow.payoutsDescription')}</p>
                        </div>
                        {earningsQuery.isLoading ? (
                            <div className="px-5 py-10 text-center text-sm text-slate-500">{t('CommerceHubFinance.moneyFlow.loadingPayoutHistory')}</div>
                        ) : payoutHistory.length === 0 ? (
                            <div className="px-5 py-10 text-center text-sm text-slate-500">{t('CommerceHubFinance.moneyFlow.noPayouts')}</div>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-slate-200 text-sm">
                                    <thead className="bg-white text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        <tr>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.date')}</th>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.method')}</th>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.amount')}</th>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.status')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-slate-100 bg-white">
                                        {payoutHistory.map((payout) => (
                                            <tr key={payout.payoutId} className="align-top">
                                                <td className="px-5 py-4 text-slate-600">
                                                    <div className="font-medium text-slate-900">
                                                        {formatDateTime(payout.processedAtUtc || payout.createdAtUtc, locale, t)}
                                                    </div>
                                                    <div className="mt-1 text-xs text-slate-500">
                                                        {t('CommerceHubFinance.moneyFlow.createdAt', {
                                                            timestamp: formatDateTime(payout.createdAtUtc, locale, t),
                                                        })}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-4 text-slate-600">
                                                    <div className="font-medium text-slate-900">{payout.method || t('CommerceHubFinance.moneyFlow.manual')}</div>
                                                    <div className="mt-1 text-xs text-slate-500">
                                                        {payout.reference || payout.externalReferenceId || t('CommerceHubFinance.moneyFlow.noPayoutReference')}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-4 font-semibold text-emerald-700">
                                                    {formatCurrency(payout.amount, payout.currency)}
                                                </td>
                                                <td className="px-5 py-4">
                                                    <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] ${getHistoryStatusTone(payout.status)}`}>
                                                        {formatStatusLabel(payout.status, t)}
                                                    </span>
                                                    <div className="mt-2 text-xs text-slate-500">
                                                        {payout.failureReason || t('CommerceHubFinance.moneyFlow.earningItems', { count: payout.items?.length ?? 0 })}
                                                    </div>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>

                    <div className="overflow-hidden rounded-[28px] border border-slate-200">
                        <div className="border-b border-slate-200 bg-slate-50 px-5 py-4">
                            <h3 className="text-lg font-semibold text-slate-950">{t('CommerceHubFinance.moneyFlow.paymentsTitle')}</h3>
                            <p className="mt-1 text-sm text-slate-500">{t('CommerceHubFinance.moneyFlow.paymentsDescription')}</p>
                        </div>
                        {paymentsQuery.isLoading ? (
                            <div className="px-5 py-10 text-center text-sm text-slate-500">{t('CommerceHubFinance.moneyFlow.loadingPaymentHistory')}</div>
                        ) : paymentHistory.length === 0 ? (
                            <div className="px-5 py-10 text-center text-sm text-slate-500">{t('CommerceHubFinance.moneyFlow.noPayments')}</div>
                        ) : (
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-slate-200 text-sm">
                                    <thead className="bg-white text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        <tr>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.date')}</th>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.method')}</th>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.amount')}</th>
                                            <th className="px-5 py-4">{t('CommerceHubFinance.common.status')}</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-slate-100 bg-white">
                                        {paymentHistory.map((payment) => (
                                            <tr key={payment.paymentRequestId} className="align-top">
                                                <td className="px-5 py-4 text-slate-600">
                                                    <div className="font-medium text-slate-900">
                                                        {formatDateTime(
                                                            payment.approvedAtUtc ||
                                                                payment.rejectedAtUtc ||
                                                                payment.createdAtUtc,
                                                            locale,
                                                            t,
                                                        )}
                                                    </div>
                                                    <div className="mt-1 text-xs text-slate-500">
                                                        {t('CommerceHubFinance.moneyFlow.submittedAt', {
                                                            timestamp: formatDateTime(payment.createdAtUtc, locale, t),
                                                        })}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-4 text-slate-600">
                                                    <div className="font-medium text-slate-900">{payment.methodName}</div>
                                                    <div className="mt-1 text-xs text-slate-500">
                                                        {payment.transferReference || payment.destinationValue || t('CommerceHubFinance.moneyFlow.noTransferReference')}
                                                    </div>
                                                </td>
                                                <td className="px-5 py-4 font-semibold text-rose-700">
                                                    {formatCurrency(payment.amount, payment.currency)}
                                                </td>
                                                <td className="px-5 py-4">
                                                    <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-[0.16em] ${getHistoryStatusTone(payment.status)}`}>
                                                        {formatStatusLabel(payment.status, t)}
                                                    </span>
                                                    <div className="mt-2 text-xs text-slate-500">
                                                        {payment.reviewReason || (payment.hasProof
                                                            ? t('CommerceHubFinance.moneyFlow.proofUploaded')
                                                            : t('CommerceHubFinance.moneyFlow.noProofAttachment'))}
                                                    </div>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                </div>
            </section>

            <section className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">{t('CommerceHubFinance.filters.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{t('CommerceHubFinance.filters.title')}</h2>
                        <p className="mt-2 text-sm leading-6 text-slate-500">{t('CommerceHubFinance.filters.description')}</p>
                    </div>

                    <div className="flex flex-wrap items-center gap-3">
                        <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-medium text-slate-600">
                            <Filter className="h-4 w-4" />
                            {activeFilterCount > 0
                                ? t('CommerceHubFinance.filters.activeCount', { count: activeFilterCount })
                                : t('CommerceHubFinance.filters.noneApplied')}
                        </div>

                        <button
                            type="button"
                            className="inline-flex items-center gap-2 rounded-full bg-slate-950 px-4 py-2 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                            onClick={handleDownloadCsv}
                            disabled={isDownloadingCsv}
                        >
                            <Download className="h-4 w-4" />
                            {isDownloadingCsv ? t('CommerceHubFinance.actions.preparingCsv') : t('CommerceHubFinance.actions.downloadCsv')}
                        </button>
                    </div>
                </div>

                <div className="mt-6 grid gap-4 xl:grid-cols-[repeat(2,minmax(0,14rem))_1fr_auto]">
                    <label className="space-y-2">
                        <span className="text-sm font-medium text-slate-700">{t('CommerceHubFinance.filters.fromDate')}</span>
                        <input
                            type="date"
                            value={filters.fromDate}
                            onChange={(event) => {
                                setStatementPage(1)
                                setFilters((current) => ({ ...current, fromDate: event.target.value }))
                            }}
                            className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm focus:border-emerald-300 focus:outline-none"
                        />
                    </label>

                    <label className="space-y-2">
                        <span className="text-sm font-medium text-slate-700">{t('CommerceHubFinance.filters.toDate')}</span>
                        <input
                            type="date"
                            value={filters.toDate}
                            onChange={(event) => {
                                setStatementPage(1)
                                setFilters((current) => ({ ...current, toDate: event.target.value }))
                            }}
                            className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm focus:border-emerald-300 focus:outline-none"
                        />
                    </label>

                    <div className="space-y-2">
                        <span className="text-sm font-medium text-slate-700">{t('CommerceHubFinance.filters.transactionType')}</span>
                        <div className="flex flex-wrap gap-2">
                            {COMMERCEHUB_FINANCE_TRANSACTION_TYPES.map((type) => {
                                const active = filters.transactionTypes.includes(type)

                                return (
                                    <button
                                        key={type}
                                        type="button"
                                        className={`rounded-full border px-3 py-2 text-sm font-medium transition ${
                                            active
                                                ? 'border-emerald-300 bg-emerald-50 text-emerald-800'
                                                : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300'
                                        }`}
                                        onClick={() => toggleTransactionType(type)}
                                    >
                                        {typeLabel(type, t)}
                                    </button>
                                )
                            })}
                        </div>
                    </div>

                    <div className="flex items-end">
                        <button
                            type="button"
                            className="inline-flex w-full items-center justify-center rounded-2xl border border-dashed border-slate-300 px-4 py-3 text-sm font-semibold text-slate-600 transition hover:border-slate-400 hover:text-slate-900 xl:w-auto"
                            onClick={clearFilters}
                        >{t('CommerceHubFinance.actions.clearFilters')}</button>
                    </div>
                </div>
            </section>

            <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex flex-col gap-4 border-b border-slate-100 px-6 py-5 lg:flex-row lg:items-end lg:justify-between">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">{t('CommerceHubFinance.statement.badge')}</p>
                        <h2 className="mt-2 text-2xl font-semibold text-slate-950">{t('CommerceHubFinance.statement.title')}</h2>
                        <p className="mt-2 text-sm leading-6 text-slate-500">
                            {statementMeta
                                ? t('CommerceHubFinance.statement.matchingTransactions', { count: statementMeta.totalCount })
                                : t('CommerceHubFinance.statement.loadingTransactions')}
                        </p>
                    </div>

                    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('CommerceHubFinance.statement.openingBalance')}</p>
                            <p className="mt-2 text-lg font-semibold text-slate-950">
                                {formatCurrency(statementMeta?.openingBalance, statementMeta?.currency || dashboard?.currency)}
                            </p>
                        </div>
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('CommerceHubFinance.statement.closingBalance')}</p>
                            <p className="mt-2 text-lg font-semibold text-slate-950">
                                {formatCurrency(statementMeta?.closingBalance, statementMeta?.currency || dashboard?.currency)}
                            </p>
                        </div>
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('CommerceHubFinance.statement.filteredCredits')}</p>
                            <p className="mt-2 text-lg font-semibold text-emerald-700">
                                {formatCurrency(statementMeta?.filteredCredits, statementMeta?.currency || dashboard?.currency)}
                            </p>
                        </div>
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('CommerceHubFinance.statement.filteredDebits')}</p>
                            <p className="mt-2 text-lg font-semibold text-rose-700">
                                {formatCurrency(statementMeta?.filteredDebits, statementMeta?.currency || dashboard?.currency)}
                            </p>
                        </div>
                        <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">{t('CommerceHubFinance.statement.currentBalance')}</p>
                            <p className="mt-2 text-lg font-semibold text-slate-950">
                                {formatCurrency(statementMeta?.currentBalance ?? dashboard?.currentBalance, statementMeta?.currency || dashboard?.currency)}
                            </p>
                        </div>
                    </div>
                </div>

                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-100">
                        <thead className="bg-slate-50/80">
                            <tr className="text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                <th className="px-6 py-4">{t('CommerceHubFinance.common.date')}</th>
                                <th className="px-6 py-4">{t('CommerceHubFinance.statement.type')}</th>
                                <th className="px-6 py-4">{t('CommerceHubFinance.common.amount')}</th>
                                <th className="px-6 py-4">{t('CommerceHubFinance.statement.runningBalance')}</th>
                                <th className="px-6 py-4">{t('CommerceHubFinance.statement.reference')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100 bg-white">
                            {statementQuery.isLoading ? (
                                Array.from({ length: 6 }).map((_, index) => (
                                    <tr key={`finance-skeleton-${index}`}>
                                        <td className="px-6 py-5">
                                            <div className="h-4 w-28 animate-pulse rounded-full bg-slate-200" />
                                        </td>
                                        <td className="px-6 py-5">
                                            <div className="h-4 w-20 animate-pulse rounded-full bg-slate-200" />
                                        </td>
                                        <td className="px-6 py-5">
                                            <div className="h-4 w-24 animate-pulse rounded-full bg-slate-200" />
                                        </td>
                                        <td className="px-6 py-5">
                                            <div className="h-4 w-24 animate-pulse rounded-full bg-slate-200" />
                                        </td>
                                        <td className="px-6 py-5">
                                            <div className="h-4 w-32 animate-pulse rounded-full bg-slate-200" />
                                        </td>
                                    </tr>
                                ))
                            ) : null}

                            {statementQuery.isError && !statementQuery.isLoading ? (
                                <tr>
                                    <td colSpan={5} className="px-6 py-16 text-center">
                                        <div className="mx-auto max-w-md rounded-[28px] border border-rose-200 bg-rose-50 px-6 py-8 text-sm text-rose-700">
                                            {statementQuery.error?.message ||
                                                t('CommerceHubFinance.statement.error')}
                                        </div>
                                    </td>
                                </tr>
                            ) : null}

                            {!statementQuery.isLoading && !statementQuery.isError && statementRows.length === 0 ? (
                                <tr>
                                    <td colSpan={5} className="px-6 py-16 text-center">
                                        <div className="mx-auto max-w-md">
                                            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100 text-slate-500">
                                                <Wallet className="h-6 w-6" />
                                            </div>
                                            <h3 className="mt-5 text-xl font-semibold text-slate-950">{t('CommerceHubFinance.statement.emptyTitle')}</h3>
                                            <p className="mt-3 text-sm leading-6 text-slate-500">{t('CommerceHubFinance.statement.emptyDescription')}</p>
                                        </div>
                                    </td>
                                </tr>
                            ) : null}

                            {statementRows.map((transaction) => {
                                const isCredit = transaction.direction === 'Credit'

                                return (
                                    <tr key={transaction.transactionId} className="align-top">
                                        <td className="px-6 py-5 text-sm text-slate-600">
                                            <div className="font-medium text-slate-900">
                                                {formatDateTime(transaction.createdAtUtc, locale, t)}
                                            </div>
                                            <div className="mt-1 text-xs text-slate-500">
                                                {transaction.description}
                                            </div>
                                        </td>
                                        <td className="px-6 py-5 text-sm">
                                            <span
                                                title={transaction.description}
                                                className={`inline-flex rounded-full border px-3 py-1 font-semibold ${
                                                    isCredit
                                                        ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
                                                        : 'border-rose-200 bg-rose-50 text-rose-800'
                                                }`}
                                            >
                                                {typeLabel(transaction.transactionType, t)}
                                            </span>
                                        </td>
                                        <td
                                            className={`px-6 py-5 text-sm font-semibold ${
                                                isCredit ? 'text-emerald-700' : 'text-rose-700'
                                            }`}
                                        >
                                            {formatSignedCurrency(transaction.signedAmount, transaction.currency)}
                                        </td>
                                        <td className="px-6 py-5 text-sm font-semibold text-slate-950">
                                            {formatCurrency(transaction.runningBalance, transaction.currency)}
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-600">
                                            <div className="font-medium text-slate-900">
                                                {transaction.referenceType}
                                            </div>
                                            <div className="mt-1 font-mono text-xs text-slate-500">
                                                {transaction.referenceId}
                                            </div>
                                            <div className="mt-1 text-xs text-slate-400" title={transaction.correlationId}>
                                                {t('CommerceHubFinance.statement.correlation', {
                                                    value: transaction.correlationId || t('CommerceHubFinance.statement.unavailable'),
                                                })}
                                            </div>
                                        </td>
                                    </tr>
                                )
                            })}
                        </tbody>
                    </table>
                </div>

                <div className="border-t border-slate-100 px-6 py-5">
                    <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                        <p className="text-sm text-slate-500">
                            {t('CommerceHubFinance.statement.pageSummary', {
                                page: statementMeta?.page ?? statementPage,
                                totalPages: statementTotalPages,
                                count: statementRows.length,
                            })}
                        </p>

                        <div className="flex gap-3">
                            <button
                                type="button"
                                className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                                onClick={() => setStatementPage((current) => Math.max(1, current - 1))}
                                disabled={statementPage <= 1 || statementQuery.isFetching}
                            >{t('CommerceHubFinance.actions.previous')}</button>
                            <button
                                type="button"
                                className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/10 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                                onClick={() => setStatementPage((current) => current + 1)}
                                disabled={!statementMeta?.hasMore || statementQuery.isFetching}
                            >
                                <RefreshCw className={`h-4 w-4 ${statementQuery.isFetching ? 'animate-spin' : ''}`} />{t('CommerceHubFinance.actions.next')}</button>
                        </div>
                    </div>
                </div>
            </section>
        </div>
    )
}
