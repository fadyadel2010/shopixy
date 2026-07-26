import { useTranslation } from 'react-i18next'
import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowUpRight, Landmark, Plus, ShieldCheck, Wallet } from 'lucide-react'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { useAuthStore } from '../store/authStore'
import {
    deriveVendorEarningsDashboardPresentation,
    deriveVendorPayoutMethodsPresentation,
} from '../auth/commerceHubVendorFinancialParticipationAuthority'
import { deriveCommerceHubVendorEligibilityPresentation } from '../auth/posVendorSessionAuthority'
import { formatDateTime as formatLocalizedDateTime } from '../utils/dateFormat'
import {
    COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL,
    resolveCommerceHubDisplayCurrency,
} from '../utils/commerceHubCurrencyAuthority'
import {
    COMMERCEHUB_PAYOUT_METHOD_TYPES,
    createVendorPayoutMethod,
    getCommerceHubEarningsDashboard,
    getVendorPayoutMethods,
} from '../services/commerceHubEarningsApi'

function formatCurrency(value, currency) {
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

function formatDateTime(value, locale, t) {
    if (!value) {
        return t('CommerceHubEarnings.notProcessedYet')
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return t('CommerceHubEarnings.notProcessedYet')
    }

    return formatLocalizedDateTime(date, locale)
}

function formatMethodLabel(value, t) {
    switch (value) {
        case 'VodafoneCash':
            return t('CommerceHubEarnings.methodTypes.vodafoneCash')
        case 'InstaPay':
            return t('CommerceHubEarnings.methodTypes.instaPay')
        case 'BankTransfer':
            return t('CommerceHubEarnings.methodTypes.bankTransfer')
        default:
            return value
    }
}

function formatRiskLabel(value, t) {
    switch (value) {
        case 'VendorSuspended':
            return t('CommerceHubEarnings.riskFlags.vendorSuspended')
        case 'DeliveryHold':
            return t('CommerceHubEarnings.riskFlags.deliveryHold')
        case 'RefundWindowOpen':
            return t('CommerceHubEarnings.riskFlags.refundWindowOpen')
        case 'ActiveRefundRequest':
            return t('CommerceHubEarnings.riskFlags.activeRefundRequest')
        case 'NoDefaultPayoutMethod':
            return t('CommerceHubEarnings.riskFlags.noDefaultPayoutMethod')
        default:
            return value
    }
}

function formatPayoutStatus(value, t) {
    switch (String(value || '').toUpperCase()) {
        case 'COMPLETED':
            return t('CommerceHubEarnings.statuses.completed')
        case 'PENDING':
            return t('CommerceHubEarnings.statuses.pending')
        case 'PROCESSING':
            return t('CommerceHubEarnings.statuses.processing')
        case 'FAILED':
            return t('CommerceHubEarnings.statuses.failed')
        default:
            return value
    }
}

export default function CommerceHubEarnings() {
    const { t } = useTranslation()
    const { dir, locale } = useRTL()
    const queryClient = useQueryClient()
    const { showError, showSuccess } = useToast()
    const user = useAuthStore((state) => state.user)
    const vendorSessionScope = ['commercehub-vendor-session', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous']
    const [methodForm, setMethodForm] = useState({
        methodType: 'BankTransfer',
        destinationValue: '',
        isDefault: true,
    })

    const earningsQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-earnings-dashboard'],
        queryFn: getCommerceHubEarningsDashboard,
        staleTime: 30_000,
    })

    const methodsQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-payout-methods'],
        queryFn: getVendorPayoutMethods,
        staleTime: 30_000,
    })

    const createMethodMutation = useMutation({
        mutationFn: () => {
            const currentEligibility = deriveCommerceHubVendorEligibilityPresentation(user)
            if (currentEligibility?.canViewFinance !== true) {
                throw new Error('Current backend vendor eligibility does not allow payout method changes.')
            }

            return createVendorPayoutMethod(methodForm)
        },
        onSuccess: () => {
            showSuccess(t('CommerceHubEarnings.messages.methodSaved'))
            setMethodForm({
                methodType: 'BankTransfer',
                destinationValue: '',
                isDefault: false,
            })
            queryClient.invalidateQueries({ queryKey: [...vendorSessionScope, 'commercehub-payout-methods'] })
            queryClient.invalidateQueries({ queryKey: [...vendorSessionScope, 'commercehub-earnings-dashboard'] })
        },
        onError: (error) => showError(error.message || t('CommerceHubEarnings.messages.methodSaveError')),
    })

    const dashboard = earningsQuery.isFetching
        ? null
        : deriveVendorEarningsDashboardPresentation(earningsQuery.data, user)
    const methods = methodsQuery.isFetching
        ? []
        : deriveVendorPayoutMethodsPresentation(methodsQuery.data ?? [], user)
    const vendorEligibility = deriveCommerceHubVendorEligibilityPresentation(user)
    const canManagePayoutMethods = vendorEligibility?.canViewFinance === true
    const currency = dashboard?.currency

    useEffect(() => {
        if (methodsQuery.isFetching) {
            return
        }

        setMethodForm((current) => ({
            ...current,
            methodType: COMMERCEHUB_PAYOUT_METHOD_TYPES.includes(current.methodType)
                ? current.methodType
                : 'BankTransfer',
        }))
    }, [methodsQuery.isFetching])

    if (!dashboard) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-rose-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <Wallet className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                        {earningsQuery.isFetching
                            ? t('CommerceHubEarnings.unavailable.loading', 'Refreshing earnings authority')
                            : t('CommerceHubEarnings.unavailable.title', 'Earnings authority unavailable')}
                    </h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {earningsQuery.error?.message || t('CommerceHubEarnings.hero.description')}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-70"
                        onClick={() => earningsQuery.refetch()}
                        disabled={earningsQuery.isFetching}
                    >
                        {t('CommerceHubEarnings.actions.retry', 'Retry')}
                    </button>
                </div>
            </div>
        )
    }

    return (
        <div dir={dir} className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="relative px-6 py-7 sm:px-8">
                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.16),_transparent_30%),radial-gradient(circle_at_bottom_right,_rgba(14,165,233,0.12),_transparent_26%)]" />
                    <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                                <Wallet className="h-4 w-4" />{t('CommerceHubEarnings.hero.badge')}</div>
                            <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">{t('CommerceHubEarnings.hero.title')}</h1>
                            <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">{t('CommerceHubEarnings.hero.description')}</p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2">
                            <div className="rounded-3xl border border-emerald-200 bg-emerald-50 px-5 py-4 text-emerald-950 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-70">{t('CommerceHubEarnings.summary.eligibleNow')}</p>
                                <p className="mt-2 text-2xl font-semibold">
                                    {formatCurrency(dashboard?.eligiblePayoutAmount, currency)}
                                </p>
                                <p className="mt-2 text-sm opacity-80">{t('CommerceHubEarnings.summary.eligibleNowDescription')}</p>
                            </div>

                            <div className="rounded-3xl border border-sky-200 bg-sky-50 px-5 py-4 text-sky-950 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-70">{t('CommerceHubEarnings.summary.currentPayable')}</p>
                                <p className="mt-2 text-2xl font-semibold">
                                    {formatCurrency(dashboard?.totalPayable, currency)}
                                </p>
                                <p className="mt-2 text-sm opacity-80">{t('CommerceHubEarnings.summary.currentPayableDescription')}</p>
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard
                    label={t('CommerceHubEarnings.summary.totalEarnings')}
                    value={formatCurrency(dashboard?.totalEarnings, currency)}
                    tone="slate"
                    icon={<ArrowUpRight className="h-5 w-5" />}
                />
                <MetricCard
                    label={t('CommerceHubEarnings.summary.paidOut')}
                    value={formatCurrency(dashboard?.completedPayoutsTotal, currency)}
                    tone="emerald"
                    icon={<ShieldCheck className="h-5 w-5" />}
                />
                <MetricCard
                    label={t('CommerceHubEarnings.summary.pendingPayoutOps')}
                    value={formatCurrency(dashboard?.pendingPayoutsTotal, currency)}
                    tone="amber"
                    icon={<Landmark className="h-5 w-5" />}
                />
                <MetricCard
                    label={t('CommerceHubEarnings.summary.holdback')}
                    value={formatCurrency(dashboard?.holdbackAmount, currency)}
                    tone="sky"
                    icon={<AlertTriangle className="h-5 w-5" />}
                />
            </section>

            <section className="grid gap-6 xl:grid-cols-[0.95fr_1.05fr]">
                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-700">
                            <Landmark className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubEarnings.methods.badge')}</p>
                            <h2 className="mt-1 text-2xl font-semibold text-slate-950">{t('CommerceHubEarnings.methods.title')}</h2>
                        </div>
                    </div>

                    <div className="mt-6 grid gap-4">
                        {methods.length === 0 ? (
                            <div className="rounded-[28px] border border-dashed border-slate-300 bg-slate-50 px-5 py-6 text-sm text-slate-600">{t('CommerceHubEarnings.methods.empty')}</div>
                        ) : (
                            methods.map((method) => (
                                <div key={method.id} className="rounded-[28px] border border-slate-200 bg-slate-50 p-5">
                                    <div className="flex items-start justify-between gap-4">
                                        <div>
                                            <p className="text-lg font-semibold text-slate-950">{formatMethodLabel(method.methodType, t)}</p>
                                            <p className="mt-2 text-sm font-medium text-slate-700">{method.destinationValue}</p>
                                            <p className="mt-3 text-xs uppercase tracking-[0.18em] text-slate-500">
                                                {t('CommerceHubEarnings.methods.addedAt', {
                                                    timestamp: formatDateTime(method.createdAtUtc, locale, t),
                                                })}
                                            </p>
                                        </div>
                                        <div className="flex flex-col items-end gap-2">
                                            {method.isDefault ? (
                                                <span className="inline-flex rounded-full border border-emerald-200 bg-white px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-emerald-700">{t('CommerceHubEarnings.methods.default')}</span>
                                            ) : null}
                                            <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${method.isVerified ? 'border-sky-200 bg-white text-sky-700' : 'border-slate-200 bg-white text-slate-600'}`}>
                                                {method.isVerified
                                                    ? t('CommerceHubEarnings.methods.verified')
                                                    : t('CommerceHubEarnings.methods.unverified')}
                                            </span>
                                        </div>
                                    </div>
                                </div>
                            ))
                        )}
                    </div>

                    <div className="mt-6 border-t border-slate-100 pt-6">
                        <div className="grid gap-3">
                            <select
                                value={methodForm.methodType}
                                onChange={(event) => setMethodForm((current) => ({ ...current, methodType: event.target.value }))}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            >
                                {COMMERCEHUB_PAYOUT_METHOD_TYPES.map((methodType) => (
                                    <option key={methodType} value={methodType}>
                                        {formatMethodLabel(methodType, t)}
                                    </option>
                                ))}
                            </select>
                            <input
                                type="text"
                                value={methodForm.destinationValue}
                                onChange={(event) => setMethodForm((current) => ({ ...current, destinationValue: event.target.value }))}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                placeholder={t('CommerceHubEarnings.methods.destinationPlaceholder')}
                            />
                            <label className="inline-flex items-center gap-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-700">
                                <input
                                    type="checkbox"
                                    checked={methodForm.isDefault}
                                    onChange={(event) => setMethodForm((current) => ({ ...current, isDefault: event.target.checked }))}
                                    className="h-4 w-4 rounded border-slate-300"
                                />{t('CommerceHubEarnings.methods.makeDefault')}</label>
                            <button
                                type="button"
                                disabled={createMethodMutation.isPending || !methodForm.destinationValue.trim() || !canManagePayoutMethods}
                                onClick={() => createMethodMutation.mutate()}
                                className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                <Plus className="h-4 w-4" />{t('CommerceHubEarnings.methods.save')}</button>
                        </div>
                    </div>
                </article>

                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-amber-50 text-amber-700">
                            <AlertTriangle className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubEarnings.readiness.badge')}</p>
                            <h2 className="mt-1 text-2xl font-semibold text-slate-950">{t('CommerceHubEarnings.readiness.title')}</h2>
                        </div>
                    </div>

                    <div className="mt-6 space-y-4">
                        {(dashboard?.riskFlags || []).length === 0 ? (
                            <div className="rounded-[24px] border border-emerald-200 bg-emerald-50 px-4 py-4 text-sm text-emerald-950">{t('CommerceHubEarnings.readiness.empty')}</div>
                        ) : (
                            (dashboard?.riskFlags || []).map((flag) => (
                                <div key={flag} className="rounded-[24px] border border-amber-200 bg-amber-50 px-4 py-4 text-sm text-amber-950">
                                    {formatRiskLabel(flag, t)}
                                </div>
                            ))
                        )}
                    </div>

                    <div className="mt-6 rounded-[28px] border border-slate-200 bg-slate-50 p-5">
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubEarnings.readiness.lastPayout')}</p>
                        <p className="mt-2 text-lg font-semibold text-slate-950">
                            {formatDateTime(dashboard?.lastPayoutAtUtc, locale, t)}
                        </p>
                        <p className="mt-3 text-sm leading-6 text-slate-600">{t('CommerceHubEarnings.readiness.lastPayoutDescription')}</p>
                    </div>
                </article>
            </section>

            <section className="rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex items-center gap-3 border-b border-slate-100 px-6 py-5">
                    <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-700">
                        <Wallet className="h-5 w-5" />
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubEarnings.history.badge')}</p>
                        <h2 className="mt-1 text-2xl font-semibold text-slate-950">{t('CommerceHubEarnings.history.title')}</h2>
                    </div>
                </div>

                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-100">
                        <thead className="bg-slate-50/90">
                            <tr className="text-left text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                                <th className="px-6 py-4">{t('CommerceHubEarnings.history.amount')}</th>
                                <th className="px-6 py-4">{t('CommerceHubEarnings.history.method')}</th>
                                <th className="px-6 py-4">{t('CommerceHubEarnings.history.reference')}</th>
                                <th className="px-6 py-4">{t('CommerceHubEarnings.history.status')}</th>
                                <th className="px-6 py-4">{t('CommerceHubEarnings.history.processed')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100 bg-white">
                            {(dashboard?.payoutHistory || []).length === 0 ? (
                                <tr><td colSpan={5} className="px-6 py-16 text-center text-sm text-slate-500">{t('CommerceHubEarnings.history.empty')}</td></tr>
                            ) : (
                                dashboard.payoutHistory.map((payout) => (
                                    <tr key={payout.payoutId}>
                                        <td className="px-6 py-5">
                                            <p className="font-semibold text-slate-950">
                                                {formatCurrency(payout.amount, payout.currency)}
                                            </p>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-700">
                                            <p className="font-medium text-slate-900">{formatMethodLabel(payout.method, t)}</p>
                                            <p className="mt-1 text-slate-500">{payout.destinationValueSnapshot || t('CommerceHubEarnings.history.destinationUnavailable')}</p>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-600">{payout.reference || t('CommerceHubEarnings.history.noTransferReference')}</td>
                                        <td className="px-6 py-5">
                                            <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${payout.status === 'Completed' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-slate-200 bg-slate-50 text-slate-700'}`}>
                                                {formatPayoutStatus(payout.status, t)}
                                            </span>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-600">{formatDateTime(payout.processedAtUtc || payout.createdAtUtc, locale, t)}</td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>
            </section>
        </div>
    )
}

function MetricCard({ label, value, tone, icon }) {
    const tones = {
        slate: 'border-slate-200 bg-white text-slate-950',
        emerald: 'border-emerald-200 bg-emerald-50 text-emerald-950',
        amber: 'border-amber-200 bg-amber-50 text-amber-950',
        sky: 'border-sky-200 bg-sky-50 text-sky-950',
    }

    return (
        <article className={`rounded-[28px] border p-5 shadow-sm ${tones[tone] || tones.slate}`}>
            <div className="flex items-start justify-between gap-4">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-70">{label}</p>
                    <p className="mt-3 text-2xl font-semibold">{value}</p>
                </div>
                <div className="rounded-2xl bg-white/70 p-3">
                    {icon}
                </div>
            </div>
        </article>
    )
}
