import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, CheckCircle2, Clock3, Landmark, Receipt, Upload, Wallet } from 'lucide-react'
import { useToast } from '../components/Toast'
import { useRTL } from '../hooks/useRTL'
import { useAuthStore } from '../store/authStore'
import {
    deriveCurrentPaymentMethodSelection,
    deriveVendorPaymentProofPresentation,
    deriveVendorPaymentsDashboardPresentation,
} from '../auth/commerceHubVendorFinancialParticipationAuthority'
import { deriveCommerceHubVendorEligibilityPresentation } from '../auth/posVendorSessionAuthority'
import { formatDateTime as formatLocalizedDateTime } from '../utils/dateFormat'
import { deriveCommerceHubAuthorityDiagnostic } from '../utils/commerceHubAuthorityDiagnostics'
import {
    createCommerceHubPaymentRequest,
    createVendorPaymentIdempotencyKey,
    getCommerceHubPaymentProofUrl,
    getCommerceHubPaymentsDashboard,
} from '../services/commerceHubPaymentsApi'
import {
    COMMERCEHUB_UNSUPPORTED_CURRENCY_LABEL,
    resolveCommerceHubDisplayCurrency,
} from '../utils/commerceHubCurrencyAuthority'

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
        return t('CommerceHubPayments.pendingReview')
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return t('CommerceHubPayments.pendingReview')
    }

    return formatLocalizedDateTime(date, locale)
}

function formatStatusLabel(status, t) {
    switch (status) {
        case 'Approved':
            return t('CommerceHubPayments.statuses.approved')
        case 'Rejected':
            return t('CommerceHubPayments.statuses.rejected')
        case 'PendingVerification':
            return t('CommerceHubPayments.statuses.pendingVerification')
        default:
            return status
    }
}

function formatDebtStatus(status, t) {
    switch (String(status || '').toUpperCase()) {
        case 'ACTIVE':
            return t('CommerceHubPayments.debtStatuses.active')
        case 'AT_RISK':
            return t('CommerceHubPayments.debtStatuses.atRisk')
        case 'SUSPENDED':
            return t('CommerceHubPayments.debtStatuses.suspended')
        case 'GRACE_PERIOD':
            return t('CommerceHubPayments.debtStatuses.gracePeriod')
        default:
            return String(status || t('CommerceHubPayments.debtStatuses.active'))
                .split('_')
                .join(' ')
    }
}

function getStatusTone(status) {
    switch (status) {
        case 'Approved':
            return 'border-emerald-200 bg-emerald-50 text-emerald-800'
        case 'Rejected':
            return 'border-rose-200 bg-rose-50 text-rose-800'
        default:
            return 'border-amber-200 bg-amber-50 text-amber-800'
    }
}

export default function CommerceHubPayments() {
    const { t } = useTranslation()
    const { dir, locale } = useRTL()
    const queryClient = useQueryClient()
    const { showSuccess, showError } = useToast()
    const user = useAuthStore((state) => state.user)
    const vendorSessionScope = ['commercehub-vendor-session', user?.tenantId ?? 'anonymous', user?.id ?? 'anonymous']
    const [form, setForm] = useState({
        methodId: '',
        amount: '',
        transferReference: '',
        proofFile: null,
    })

    const dashboardQuery = useQuery({
        queryKey: [...vendorSessionScope, 'commercehub-payments-dashboard'],
        queryFn: getCommerceHubPaymentsDashboard,
        staleTime: 30_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: true,
    })

    const createMutation = useMutation({
        mutationFn: async () => {
            const currentEligibility = deriveCommerceHubVendorEligibilityPresentation(user, {
                canSubmitPayment: dashboard?.canSubmitPayment,
            })
            if (currentEligibility?.canSubmitPayment !== true) {
                throw new Error('Current backend vendor eligibility does not allow payment submission.')
            }

            return createCommerceHubPaymentRequest({
                amount: Number(form.amount),
                methodId: form.methodId,
                transferReference: form.transferReference.trim() || undefined,
                idempotencyKey: createVendorPaymentIdempotencyKey(),
                proofFile: form.proofFile,
            })
        },
        onSuccess: () => {
            showSuccess(t('CommerceHubPayments.messages.submitSuccess'))
            setForm({
                methodId: '',
                amount: '',
                transferReference: '',
                proofFile: null,
            })
            queryClient.invalidateQueries({ queryKey: [...vendorSessionScope, 'commercehub-payments-dashboard'] })
        },
        onError: (error) => {
            showError(error.message || t('CommerceHubPayments.messages.submitError'))
        },
    })

    const dashboard = dashboardQuery.isFetching
        ? null
        : deriveVendorPaymentsDashboardPresentation(dashboardQuery.data, user)
    const vendorEligibility = deriveCommerceHubVendorEligibilityPresentation(user, {
        canSubmitPayment: dashboard?.canSubmitPayment,
    })
    const canSubmitPayment = vendorEligibility?.canSubmitPayment === true
    const selectedMethod = useMemo(
        () => deriveCurrentPaymentMethodSelection(form.methodId, dashboard?.paymentMethods || []),
        [dashboard?.paymentMethods, form.methodId],
    )

    useEffect(() => {
        const diagnostic = deriveCommerceHubAuthorityDiagnostic({
            surface: 'Payments dashboard',
            endpoint: '/api/admin/commercehub/payments',
            query: dashboardQuery,
            presentation: dashboard,
        })

        if (diagnostic) {
            console.error('CommerceHub authority load rejected', diagnostic)
        }
    }, [dashboard, dashboardQuery.data, dashboardQuery.error, dashboardQuery.isFetching])

    useEffect(() => {
        if (dashboardQuery.isFetching || !form.methodId) {
            return
        }

        if (!deriveCurrentPaymentMethodSelection(form.methodId, dashboard?.paymentMethods || [])) {
            setForm((current) => ({ ...current, methodId: '' }))
        }
    }, [dashboard?.paymentMethods, dashboardQuery.isFetching, form.methodId])

    if (!dashboard) {
        return (
            <div className="grid min-h-[60vh] place-items-center">
                <div className="w-full max-w-xl rounded-[32px] border border-rose-200 bg-white p-8 text-center shadow-[0_30px_80px_-48px_rgba(15,23,42,0.45)]">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-rose-50 text-rose-600">
                        <Wallet className="h-6 w-6" />
                    </div>
                    <h2 className="mt-5 text-2xl font-semibold text-slate-950">
                        {dashboardQuery.isFetching
                            ? t('CommerceHubPayments.unavailable.loading', 'Refreshing payment authority')
                            : t('CommerceHubPayments.unavailable.title')}
                    </h2>
                    <p className="mt-3 text-sm leading-6 text-slate-500">
                        {dashboardQuery.error?.message || t('CommerceHubPayments.unavailable.description')}
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800"
                        onClick={() => dashboardQuery.refetch()}
                        disabled={dashboardQuery.isFetching}
                    >
                        <Clock3 className="h-4 w-4" />
                        {t('CommerceHubPayments.actions.retry')}
                    </button>
                </div>
            </div>
        )
    }

    async function handleOpenProof(paymentRequestId) {
        try {
            const proof = deriveVendorPaymentProofPresentation(await getCommerceHubPaymentProofUrl(paymentRequestId))
            if (!proof) {
                throw new Error('Current backend payment proof authority is unavailable.')
            }
            window.open(proof.fileUrl, '_blank', 'noopener,noreferrer')
        } catch (error) {
            showError(error.message || t('CommerceHubPayments.messages.openProofError'))
        }
    }

    function handleSubmit(event) {
        event.preventDefault()
        createMutation.mutate()
    }

    return (
        <div dir={dir} className="space-y-6">
            <section className="overflow-hidden rounded-[36px] border border-slate-200 bg-white shadow-[0_36px_100px_-56px_rgba(15,23,42,0.45)]">
                <div className="relative px-6 py-7 sm:px-8">
                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(14,165,233,0.16),_transparent_30%),radial-gradient(circle_at_bottom_right,_rgba(245,158,11,0.12),_transparent_26%)]" />
                    <div className="relative flex flex-col gap-6 xl:flex-row xl:items-end xl:justify-between">
                        <div className="max-w-3xl">
                            <div className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-4 py-2 text-sm font-semibold text-sky-800">
                                <Wallet className="h-4 w-4" />
                                {t('CommerceHubPayments.hero.badge')}
                            </div>
                            <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950 sm:text-4xl">
                                {t('CommerceHubPayments.hero.title')}
                            </h1>
                            <p className="mt-4 max-w-2xl text-sm leading-7 text-slate-600 sm:text-base">
                                {t('CommerceHubPayments.hero.description')}
                            </p>
                        </div>

                        <div className="grid gap-3 sm:grid-cols-2">
                            <div className="rounded-3xl border border-amber-200 bg-amber-50 px-5 py-4 text-amber-950 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] opacity-70">{t('CommerceHubPayments.summary.currentDebt')}</p>
                                <p className="mt-2 text-2xl font-semibold">
                                    {formatCurrency(dashboard?.amountOwedByVendor, dashboard?.currency)}
                                </p>
                                <p className="mt-2 text-sm opacity-80">
                                    {t('CommerceHubPayments.summary.currentDebtWarning', {
                                        amount: formatCurrency(dashboard?.amountOwedByVendor, dashboard?.currency),
                                    })}
                                </p>
                            </div>

                            <div className="rounded-3xl border border-slate-200 bg-slate-50 px-5 py-4 text-slate-900 shadow-sm">
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubPayments.summary.debtStatus')}</p>
                                <p className="mt-2 text-2xl font-semibold">{formatDebtStatus(dashboard?.debtStatus, t)}</p>
                                <p className="mt-2 text-sm text-slate-600">
                                    {dashboard?.requiredAction || t('CommerceHubPayments.summary.requiredActionFallback')}
                                </p>
                            </div>
                        </div>
                    </div>
                </div>
            </section>

            <section className="grid gap-6 xl:grid-cols-[1.1fr_0.9fr]">
                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-700">
                            <Landmark className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubPayments.methods.badge')}</p>
                            <h2 className="mt-1 text-2xl font-semibold text-slate-950">{t('CommerceHubPayments.methods.title')}</h2>
                        </div>
                    </div>

                    <div className="mt-6 grid gap-4">
                        {(dashboard?.paymentMethods || []).map((method) => (
                            <button
                                key={method.id}
                                type="button"
                                onClick={() => setForm((current) => ({ ...current, methodId: method.id }))}
                                className={`rounded-[28px] border p-5 text-left transition ${
                                    form.methodId === method.id
                                        ? 'border-sky-300 bg-sky-50 shadow-sm'
                                        : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50'
                                }`}
                            >
                                <div className="flex items-start justify-between gap-4">
                                    <div>
                                        <p className="text-lg font-semibold text-slate-950">{method.name}</p>
                                        <p className="mt-2 text-sm font-medium text-slate-700">{method.destinationValue}</p>
                                        <p className="mt-3 text-sm leading-6 text-slate-600">{method.instructions}</p>
                                    </div>
                                    {form.methodId === method.id ? (
                                        <span className="inline-flex rounded-full border border-sky-200 bg-white px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-sky-700">
                                            {t('CommerceHubPayments.methods.selected')}
                                        </span>
                                    ) : null}
                                </div>
                            </button>
                        ))}

                        {!dashboardQuery.isLoading && (dashboard?.paymentMethods || []).length === 0 ? (
                            <div className="rounded-[28px] border border-dashed border-slate-300 bg-slate-50 px-5 py-6 text-sm text-slate-600">
                                {t('CommerceHubPayments.methods.empty')}
                            </div>
                        ) : null}
                    </div>
                </article>

                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                    <div className="flex items-center gap-3">
                        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-emerald-50 text-emerald-700">
                            <Upload className="h-5 w-5" />
                        </div>
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubPayments.form.badge')}</p>
                            <h2 className="mt-1 text-2xl font-semibold text-slate-950">{t('CommerceHubPayments.form.title')}</h2>
                        </div>
                    </div>

                    <form className="mt-6 space-y-4" onSubmit={handleSubmit}>
                        <label className="block space-y-2">
                            <span className="text-sm font-medium text-slate-700">{t('CommerceHubPayments.form.paymentMethod')}</span>
                            <select
                                value={form.methodId}
                                onChange={(event) => setForm((current) => ({ ...current, methodId: event.target.value }))}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                required
                            >
                                <option value="">{t('CommerceHubPayments.form.selectMethod')}</option>
                                {(dashboard?.paymentMethods || []).map((method) => (
                                    <option key={method.id} value={method.id}>
                                        {method.name}
                                    </option>
                                ))}
                            </select>
                        </label>

                        <label className="block space-y-2">
                            <span className="text-sm font-medium text-slate-700">{t('CommerceHubPayments.form.amount')}</span>
                            <input
                                type="number"
                                min="0.01"
                                step="0.01"
                                value={form.amount}
                                onChange={(event) => setForm((current) => ({ ...current, amount: event.target.value }))}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                placeholder={t('CommerceHubPayments.form.amountPlaceholder')}
                                required
                            />
                        </label>

                        <label className="block space-y-2">
                            <span className="text-sm font-medium text-slate-700">{t('CommerceHubPayments.form.transferReference')}</span>
                            <input
                                type="text"
                                value={form.transferReference}
                                onChange={(event) => setForm((current) => ({ ...current, transferReference: event.target.value }))}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                                placeholder={t('CommerceHubPayments.form.transferReferencePlaceholder')}
                            />
                        </label>

                        <label className="block space-y-2">
                            <span className="text-sm font-medium text-slate-700">{t('CommerceHubPayments.form.proofFile')}</span>
                            <input
                                type="file"
                                accept="image/jpeg,image/png,image/webp,video/mp4"
                                onChange={(event) =>
                                    setForm((current) => ({ ...current, proofFile: event.target.files?.[0] || null }))
                                }
                                className="block w-full rounded-2xl border border-dashed border-slate-300 bg-slate-50 px-4 py-3 text-sm text-slate-700"
                                required
                            />
                            <p className="text-xs text-slate-500">{t('CommerceHubPayments.form.proofFileHelp')}</p>
                        </label>

                        {selectedMethod ? (
                            <div className="rounded-[24px] border border-sky-200 bg-sky-50 px-4 py-4 text-sm text-sky-950">
                                <p className="font-semibold">{t('CommerceHubPayments.form.sendToMethod', { method: selectedMethod.name })}</p>
                                <p className="mt-2">{selectedMethod.destinationValue}</p>
                                <p className="mt-2 leading-6 opacity-80">{selectedMethod.instructions}</p>
                            </div>
                        ) : null}

                        <button
                            type="submit"
                            disabled={createMutation.isPending || !canSubmitPayment || !form.proofFile}
                            className="inline-flex w-full items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                        >
                            {createMutation.isPending ? (
                                <>
                                    <Clock3 className="h-4 w-4 animate-spin" />
                                    {t('CommerceHubPayments.actions.submitting')}
                                </>
                            ) : (
                                <>
                                    <Receipt className="h-4 w-4" />
                                    {t('CommerceHubPayments.actions.submitPaymentRequest')}
                                </>
                            )}
                        </button>
                    </form>
                </article>
            </section>

            <section className="rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex items-center gap-3 border-b border-slate-100 px-6 py-5">
                    <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-700">
                        <Receipt className="h-5 w-5" />
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">{t('CommerceHubPayments.history.badge')}</p>
                        <h2 className="mt-1 text-2xl font-semibold text-slate-950">{t('CommerceHubPayments.history.title')}</h2>
                    </div>
                </div>

                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-100">
                        <thead className="bg-slate-50/90">
                            <tr className="text-left text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                                <th className="px-6 py-4">{t('CommerceHubPayments.history.request')}</th>
                                <th className="px-6 py-4">{t('CommerceHubPayments.history.method')}</th>
                                <th className="px-6 py-4">{t('CommerceHubPayments.history.status')}</th>
                                <th className="px-6 py-4">{t('CommerceHubPayments.history.submitted')}</th>
                                <th className="px-6 py-4">{t('CommerceHubPayments.history.review')}</th>
                                <th className="px-6 py-4 text-right">{t('CommerceHubPayments.history.proof')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100 bg-white">
                            {(dashboard?.paymentHistory || []).length === 0 ? (
                                <tr>
                                    <td colSpan={6} className="px-6 py-16 text-center">
                                        <div className="mx-auto max-w-md">
                                            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100 text-slate-500">
                                                <Receipt className="h-6 w-6" />
                                            </div>
                                            <h3 className="mt-5 text-xl font-semibold text-slate-950">{t('CommerceHubPayments.history.emptyTitle')}</h3>
                                            <p className="mt-3 text-sm leading-6 text-slate-500">
                                                {t('CommerceHubPayments.history.emptyDescription')}
                                            </p>
                                        </div>
                                    </td>
                                </tr>
                            ) : (
                                dashboard.paymentHistory.map((request) => (
                                    <tr key={request.paymentRequestId} className="align-top">
                                        <td className="px-6 py-5">
                                            <p className="font-semibold text-slate-950">
                                                {formatCurrency(request.amount, request.currency)}
                                            </p>
                                            <p className="mt-1 text-sm text-slate-500">
                                                {t('CommerceHubPayments.history.reference', {
                                                    reference: request.transferReference || t('CommerceHubPayments.history.noReference'),
                                                })}
                                            </p>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-700">
                                            <p className="font-medium text-slate-900">{request.methodName}</p>
                                            <p className="mt-1 text-slate-500">{request.destinationValue}</p>
                                        </td>
                                        <td className="px-6 py-5">
                                            <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${getStatusTone(request.status)}`}>
                                                {formatStatusLabel(request.status, t)}
                                            </span>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-600">
                                            {formatDateTime(request.createdAtUtc, locale, t)}
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-600">
                                            {request.reviewReason ? (
                                                <div className="max-w-xs">
                                                    <p className="font-medium text-slate-900">
                                                        {request.status === 'Approved'
                                                            ? t('CommerceHubPayments.history.reviewApproved')
                                                            : t('CommerceHubPayments.history.reviewRejected')}
                                                    </p>
                                                    <p className="mt-1 leading-6">{request.reviewReason}</p>
                                                </div>
                                            ) : (
                                                <div className="inline-flex items-center gap-2 text-amber-700">
                                                    <AlertTriangle className="h-4 w-4" />
                                                    {t('CommerceHubPayments.history.pendingVerification')}
                                                </div>
                                            )}
                                        </td>
                                        <td className="px-6 py-5 text-right">
                                            {request.hasProof ? (
                                                <button
                                                    type="button"
                                                    className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                                    onClick={() => handleOpenProof(request.paymentRequestId)}
                                                >
                                                    {request.status === 'Approved' ? (
                                                        <CheckCircle2 className="h-4 w-4 text-emerald-600" />
                                                    ) : (
                                                        <Upload className="h-4 w-4" />
                                                    )}
                                                    {t('CommerceHubPayments.actions.viewProof')}
                                                </button>
                                            ) : (
                                                <span className="text-sm text-slate-400">{t('CommerceHubPayments.history.unavailable')}</span>
                                            )}
                                        </td>
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
