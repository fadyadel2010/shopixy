import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Landmark, Loader2, Send, ShieldCheck } from 'lucide-react'
import { useToast } from '../../components/Toast'
import {
    createVendorPayoutIdempotencyKey,
    executeSuperAdminVendorPayout,
    getSuperAdminPayoutQueue,
    type SuperAdminVendorPayoutQueueCurrencySummary,
    type SuperAdminVendorPayoutMethodChoice,
    type SuperAdminVendorPayoutQueueItem,
} from './payoutsService'
import { requireCurrentPayoutExecutionAmount, requireCurrentPayoutMethodSelection } from './payoutFinancialAuthority'

const QUEUE_PAGE_SIZE = 20
const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(amount: number, currency = 'EGP') {
    if (String(currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(amount || 0))
}

function formatDateTime(value?: string | null) {
    if (!value) {
        return 'No recent orders'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'No recent orders'
    }

    return date.toLocaleString()
}

function formatMethodLabel(value: string): string {
    switch (value) {
        case 'VodafoneCash':
            return 'Vodafone Cash'
        case 'InstaPay':
            return 'InstaPay'
        case 'BankTransfer':
            return 'Bank Transfer'
        default:
            return value
    }
}

function formatSummaryCurrencyLabel(value: string): string {
    return String(value || '').trim().toUpperCase() === COMMERCEHUB_AUTHORITY_CURRENCY
        ? COMMERCEHUB_AUTHORITY_CURRENCY
        : UNSUPPORTED_COMMERCEHUB_CURRENCY
}

function formatCurrencySummaryAmount(amount: number, currency: string): string {
    return formatCurrency(amount, currency)
}

function formatRiskLabel(value: string): string {
    switch (value) {
        case 'VendorSuspended':
            return 'Vendor suspended'
        case 'GovernanceSuspended':
            return 'Governance suspended'
        case 'GovernanceBanned':
            return 'Governance banned'
        case 'GovernanceFinancialLock':
            return 'Governance financial lock'
        case 'DeliveryHold':
            return 'Undelivered orders still holding balance'
        case 'RefundWindowOpen':
            return 'Refund window still open'
        case 'SettlementDelayActive':
            return 'Payout delay still active'
        case 'ActiveRefundRequest':
            return 'Active refund request'
        case 'ExternalTransferReconciliationRequired':
            return 'Prior payout needs reconciliation'
        case 'StaleExternalTransferProcessing':
            return 'Prior payout stuck in processing'
        case 'PayoutBlockedByAuthorityConflict':
            return 'Authority conflict'
        case 'NoDefaultPayoutMethod':
            return 'Default payout method missing'
        default:
            return value
    }
}

function getErrorMessage(error: unknown, fallback: string) {
    return error instanceof Error && error.message ? error.message : fallback
}

interface ExecuteModalState {
    item: SuperAdminVendorPayoutQueueItem
    amount: string
    payoutMethodId: string
    transferReference: string
    externalReferenceId: string
    bankTransactionId: string
}

export default function PayoutsPage() {
    const queryClient = useQueryClient()
    const { showError, showSuccess } = useToast()
    const [executeModal, setExecuteModal] = useState<ExecuteModalState | null>(null)
    const [page, setPage] = useState(1)

    const queueQuery = useQuery({
        queryKey: ['super-admin-payout-queue', page, QUEUE_PAGE_SIZE],
        queryFn: () => getSuperAdminPayoutQueue(page, QUEUE_PAGE_SIZE),
        staleTime: 30_000,
    })

    const queueResult = queueQuery.data
    const queue = queueResult?.payouts ?? []
    const currentExecuteItem = executeModal
        ? queue.find((item) => item.vendorId === executeModal.item.vendorId) ?? null
        : null
    const currentExecuteAmount = executeModal && currentExecuteItem
        ? Number(executeModal.amount)
        : null
    const queueMeta = queueResult?.meta
    const currencySummaries = queueResult?.currencySummaries ?? []
    const hasCurrencyMix = queueResult?.hasCurrencyMix ?? false
    const aggregateCurrency = queueResult?.aggregateCurrency ?? null
    const totalEligible = queueResult?.totalEligibleAmount ?? 0
    const totalHeld = queueResult?.totalHeldAmount ?? 0
    const totalPayable = queueResult?.totalPayableAmount ?? 0
    const totalCount = queueMeta?.totalCount ?? queue.length
    const pageStart = totalCount === 0 ? 0 : ((page - 1) * QUEUE_PAGE_SIZE) + 1
    const pageEnd = totalCount === 0 ? 0 : Math.min(page * QUEUE_PAGE_SIZE, totalCount)

    const executeMutation = useMutation({
        mutationFn: () => {
            if (!executeModal || !currentExecuteItem?.canRecordCompletedPayout) {
                throw new Error('Current backend payout authority is no longer available for this vendor.')
            }

            return executeSuperAdminVendorPayout(currentExecuteItem.vendorId, {
                amount: requireCurrentPayoutExecutionAmount(Number(executeModal.amount), currentExecuteItem),
                payoutMethodId: requireCurrentPayoutMethodSelection(executeModal.payoutMethodId, currentExecuteItem).id,
                transferReference: executeModal.transferReference.trim() || undefined,
                externalReferenceId: executeModal.externalReferenceId.trim() || undefined,
                bankTransactionId: executeModal.bankTransactionId.trim() || undefined,
                idempotencyKey: createVendorPayoutIdempotencyKey(),
            })
        },
        onSuccess: () => {
            showSuccess('Completed payout evidence recorded and posted to the CommerceHub ledger.')
            setExecuteModal(null)
            queryClient.invalidateQueries({ queryKey: ['super-admin-payout-queue'] })
        },
        onError: (error) => showError(getErrorMessage(error, 'Unable to record payout evidence.')),
    })

    useEffect(() => {
        if (!queueMeta?.totalPages) {
            return
        }

        if (page > queueMeta.totalPages && queueMeta.totalPages > 0) {
            setPage((currentPage) => Math.min(currentPage, queueMeta.totalPages!))
        }
    }, [page, queueMeta?.totalPages])

    useEffect(() => {
        if (!executeModal || queueQuery.isFetching) {
            return
        }

        if (!currentExecuteItem?.canRecordCompletedPayout) {
            setExecuteModal(null)
        }
    }, [currentExecuteItem, executeModal, queueQuery.isFetching])

    function openExecuteModal(item: SuperAdminVendorPayoutQueueItem) {
        const defaultMethod = item.payoutMethods.find((method) => method.isDefault) ?? item.payoutMethods[0]
        setExecuteModal({
            item,
            amount: item.eligibleAmount > 0 ? item.eligibleAmount.toFixed(2) : item.totalPayable.toFixed(2),
            payoutMethodId: defaultMethod?.id ?? '',
            transferReference: '',
            externalReferenceId: '',
            bankTransactionId: '',
        })
    }

    function resolveSelectedMethod(): SuperAdminVendorPayoutMethodChoice | null {
        if (!executeModal || !currentExecuteItem) {
            return null
        }

        return currentExecuteItem.payoutMethods.find((method) => method.id === executeModal.payoutMethodId) ?? null
    }

    const executeModalHasExternalEvidence = !!(
        executeModal?.externalReferenceId.trim() ||
        executeModal?.bankTransactionId.trim()
    )

    const renderCurrencySummaryCard = (summary: SuperAdminVendorPayoutQueueCurrencySummary) => (
        <article key={summary.currency} className="rounded-[32px] border border-emerald-200 bg-emerald-50 p-6 shadow-sm">
            <div className="flex items-center justify-between gap-3">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-700">Queue Currency</p>
                    <p className="mt-2 text-2xl font-semibold text-emerald-950">{formatSummaryCurrencyLabel(summary.currency)}</p>
                </div>
                <p className="text-sm font-semibold text-emerald-800">Currency-scoped authority</p>
            </div>
            <div className="mt-5 grid gap-3 text-sm text-emerald-950/90">
                <div className="flex items-center justify-between gap-3">
                    <span>Eligible</span>
                    <span className="font-semibold">{formatCurrencySummaryAmount(summary.totalEligibleAmount, summary.currency)}</span>
                </div>
                <div className="flex items-center justify-between gap-3">
                    <span>Held</span>
                    <span className="font-semibold">{formatCurrencySummaryAmount(summary.totalHeldAmount, summary.currency)}</span>
                </div>
                <div className="flex items-center justify-between gap-3">
                    <span>Payable</span>
                    <span className="font-semibold">{formatCurrencySummaryAmount(summary.totalPayableAmount, summary.currency)}</span>
                </div>
            </div>
        </article>
    )

    return (
        <div className="space-y-6">
            <section className={`grid gap-4 ${currencySummaries.length > 1 ? 'xl:grid-cols-[1.2fr_repeat(2,minmax(0,0.8fr))]' : 'xl:grid-cols-[1.2fr_0.8fr_0.8fr]'}`}>
                <article className="rounded-[32px] border border-slate-200 bg-white p-6 shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                    <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                        <Landmark className="h-4 w-4" />
                        CommerceHub vendor payout queue
                    </div>
                    <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950">Vendor liabilities ready for payout planning and completed-transfer recognition</h1>
                    <p className="mt-4 text-sm leading-7 text-slate-600">
                        Eligible amounts are calculated from delivered online orders, refund-window holds, active refund blockers, and prior payout reservations. Recording a completed payout recognizes already executed external transfer evidence and posts the cash and payable movements into the immutable CommerceHub ledger.
                    </p>
                    {hasCurrencyMix ? (
                        <p className="mt-4 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm leading-6 text-amber-900">
                            Queue-wide totals are split by authoritative currency. Cross-currency payout aggregation is intentionally disabled so operators do not act on a fabricated combined money total.
                        </p>
                    ) : null}
                </article>

                {!hasCurrencyMix && currencySummaries.length <= 1 && aggregateCurrency != null ? (
                    <>
                        <article className="rounded-[32px] border border-emerald-200 bg-emerald-50 p-6 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-700">Eligible Batch</p>
                            <p className="mt-3 text-3xl font-semibold text-emerald-950">{formatCurrency(totalEligible, aggregateCurrency)}</p>
                            <p className="mt-2 text-sm text-emerald-900/80">Authoritative eligible total across the full vendor payout queue before final execution-evidence and platform-cash verification.</p>
                        </article>

                        <article className="rounded-[32px] border border-amber-200 bg-amber-50 p-6 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-amber-700">Held Back</p>
                            <p className="mt-3 text-3xl font-semibold text-amber-950">{formatCurrency(totalHeld, aggregateCurrency)}</p>
                            <p className="mt-2 text-sm text-amber-900/80">Authoritative holdback total across the full queue, still protected by delivery, refund, or readiness blockers.</p>
                        </article>
                    </>
                ) : (
                    currencySummaries.slice(0, 2).map(renderCurrencySummaryCard)
                )}
            </section>

            {currencySummaries.length > 2 ? (
                <section className="grid gap-4 xl:grid-cols-3">
                    {currencySummaries.slice(2).map(renderCurrencySummaryCard)}
                </section>
            ) : null}

            <section className="rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
                <div className="flex items-center gap-3 border-b border-slate-100 px-6 py-5">
                    <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-700">
                        <ShieldCheck className="h-5 w-5" />
                    </div>
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Queue</p>
                        <h2 className="mt-1 text-2xl font-semibold text-slate-950">Vendor payout execution board</h2>
                        <p className="mt-2 text-sm text-slate-500">
                            {totalCount === 0
                                ? 'No vendors currently require payout review.'
                                : hasCurrencyMix || aggregateCurrency == null
                                    ? `Showing vendors ${pageStart}-${pageEnd} of ${totalCount}. Queue totals are split by currency below.`
                                    : `Showing vendors ${pageStart}-${pageEnd} of ${totalCount}. Total queue payable: ${formatCurrency(totalPayable, aggregateCurrency)}.`}
                        </p>
                    </div>
                </div>

                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-100">
                        <thead className="bg-slate-50/90">
                            <tr className="text-left text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">
                                <th className="px-6 py-4">Vendor</th>
                                <th className="px-6 py-4">Payable</th>
                                <th className="px-6 py-4">Eligible</th>
                                <th className="px-6 py-4">Default method</th>
                                <th className="px-6 py-4">Risk flags</th>
                                <th className="px-6 py-4">Last order</th>
                                <th className="px-6 py-4 text-right">Action</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100 bg-white">
                            {queue.length === 0 ? (
                                <tr>
                                    <td colSpan={7} className="px-6 py-16 text-center text-sm text-slate-500">
                                        No vendor payouts are pending right now.
                                    </td>
                                </tr>
                            ) : (
                                queue.map((item) => (
                                    <tr key={item.vendorId} className="align-top">
                                        <td className="px-6 py-5">
                                            <p className="font-semibold text-slate-950">{item.vendorName}</p>
                                            <p className="mt-1 text-sm text-slate-500">
                                                Paid out: {formatCurrency(item.completedPayoutsTotal, item.currency)}
                                            </p>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-700">
                                            <p className="font-medium text-slate-900">{formatCurrency(item.totalPayable, item.currency)}</p>
                                            <p className="mt-1 text-slate-500">Held: {formatCurrency(item.holdbackAmount, item.currency)}</p>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-700">
                                            <p className="font-medium text-emerald-700">{formatCurrency(item.eligibleAmount, item.currency)}</p>
                                            <p className="mt-1 text-slate-500">Pending ops: {formatCurrency(item.pendingPayoutsTotal, item.currency)}</p>
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-700">
                                            {item.defaultPayoutMethodType ? (
                                                <>
                                                    <p className="font-medium text-slate-900">{formatMethodLabel(item.defaultPayoutMethodType)}</p>
                                                    <p className="mt-1 text-slate-500">{item.defaultPayoutDestinationValue}</p>
                                                </>
                                            ) : (
                                                <span className="text-slate-400">Not configured</span>
                                            )}
                                        </td>
                                        <td className="px-6 py-5">
                                            <div className="flex max-w-xs flex-wrap gap-2">
                                                {(item.riskFlags.length === 0 ? ['Clear'] : item.riskFlags).map((flag) => (
                                                    <span key={flag} className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${flag === 'Clear' ? 'border-emerald-200 bg-emerald-50 text-emerald-900' : 'border-amber-200 bg-amber-50 text-amber-900'}`}>
                                                        {flag === 'Clear' ? 'Clear' : formatRiskLabel(flag)}
                                                    </span>
                                                ))}
                                            </div>
                                            {item.executionBlockedReason ? (
                                                <p className="mt-3 max-w-xs text-xs leading-5 text-amber-900/80">
                                                    {item.executionBlockedReason}
                                                </p>
                                            ) : null}
                                        </td>
                                        <td className="px-6 py-5 text-sm text-slate-600">{formatDateTime(item.lastOrderAt)}</td>
                                        <td className="px-6 py-5 text-right">
                                            <button
                                                type="button"
                                                disabled={!item.canRecordCompletedPayout}
                                                onClick={() => openExecuteModal(item)}
                                                className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-4 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
                                                title={item.executionBlockedReason ?? undefined}
                                            >
                                                <Send className="h-4 w-4" />
                                                Record completed payout
                                            </button>
                                        </td>
                                    </tr>
                                ))
                            )}
                        </tbody>
                    </table>
                </div>

                {queueMeta?.totalPages && queueMeta.totalPages > 1 ? (
                    <div className="flex flex-col gap-3 border-t border-slate-100 px-6 py-5 text-sm text-slate-600 md:flex-row md:items-center md:justify-between">
                        <p>
                            Showing {pageStart}-{pageEnd} of {totalCount} vendors
                        </p>
                        <div className="flex items-center gap-3">
                            <button
                                type="button"
                                onClick={() => setPage((currentPage) => Math.max(1, currentPage - 1))}
                                disabled={page <= 1 || queueQuery.isFetching}
                                className="rounded-2xl border border-slate-200 px-4 py-2 font-semibold text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Previous
                            </button>
                            <span className="font-medium text-slate-900">
                                Page {page} of {queueMeta.totalPages}
                            </span>
                            <button
                                type="button"
                                onClick={() => setPage((currentPage) => Math.min(queueMeta.totalPages!, currentPage + 1))}
                                disabled={page >= queueMeta.totalPages || queueQuery.isFetching}
                                className="rounded-2xl border border-slate-200 px-4 py-2 font-semibold text-slate-700 transition hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                            >
                                Next
                            </button>
                        </div>
                    </div>
                ) : null}
            </section>

            {executeModal && currentExecuteItem ? (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/55 px-4">
                    <div className="w-full max-w-2xl rounded-[32px] border border-slate-200 bg-white p-6 shadow-2xl">
                        <div className="flex items-start justify-between gap-4">
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">Record completed payout</p>
                                <h3 className="mt-2 text-2xl font-semibold text-slate-950">{currentExecuteItem.vendorName}</h3>
                                <p className="mt-2 text-sm text-slate-600">
                                    Eligible now: {formatCurrency(currentExecuteItem.eligibleAmount, currentExecuteItem.currency)}
                                </p>
                                <p className="mt-2 max-w-xl text-sm leading-6 text-slate-500">
                                    Use this only after the external transfer has already been executed. Immutable provider or bank evidence is required before POS can recognize the payout in ledger truth.
                                </p>
                            </div>
                            <button type="button" onClick={() => setExecuteModal(null)} className="rounded-2xl border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-700">
                                Close
                            </button>
                        </div>

                        <div className="mt-6 grid gap-4">
                            <label className="block space-y-2">
                                <span className="text-sm font-medium text-slate-700">Amount</span>
                                <input
                                    type="number"
                                    min="0.01"
                                    step="0.01"
                                    value={executeModal.amount}
                                    onChange={(event) => setExecuteModal((current) => current ? { ...current, amount: event.target.value } : current)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                            </label>

                            <label className="block space-y-2">
                                <span className="text-sm font-medium text-slate-700">Payout method</span>
                                <select
                                    value={executeModal.payoutMethodId}
                                    onChange={(event) => setExecuteModal((current) => current ? { ...current, payoutMethodId: event.target.value } : current)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                >
                                    {currentExecuteItem.payoutMethods.map((method) => (
                                        <option key={method.id} value={method.id}>
                                            {formatMethodLabel(method.methodType)} - {method.destinationValue}
                                        </option>
                                    ))}
                                </select>
                            </label>

                            <label className="block space-y-2">
                                <span className="text-sm font-medium text-slate-700">Transfer reference</span>
                                <input
                                    type="text"
                                    value={executeModal.transferReference}
                                    onChange={(event) => setExecuteModal((current) => current ? { ...current, transferReference: event.target.value } : current)}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                    placeholder="Bank or wallet transfer reference"
                                />
                            </label>

                            <div className="grid gap-4 md:grid-cols-2">
                                <label className="block space-y-2">
                                    <span className="text-sm font-medium text-slate-700">External reference ID</span>
                                    <input
                                        type="text"
                                        value={executeModal.externalReferenceId}
                                        onChange={(event) => setExecuteModal((current) => current ? { ...current, externalReferenceId: event.target.value } : current)}
                                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                        placeholder="Provider transfer or settlement reference"
                                    />
                                </label>

                                <label className="block space-y-2">
                                    <span className="text-sm font-medium text-slate-700">Bank transaction ID</span>
                                    <input
                                        type="text"
                                        value={executeModal.bankTransactionId}
                                        onChange={(event) => setExecuteModal((current) => current ? { ...current, bankTransactionId: event.target.value } : current)}
                                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                        placeholder="Bank confirmation or ledger transaction ID"
                                    />
                                </label>
                            </div>

                            <div className={`rounded-[24px] border px-4 py-4 text-sm ${executeModalHasExternalEvidence ? 'border-emerald-200 bg-emerald-50 text-emerald-950' : 'border-amber-200 bg-amber-50 text-amber-950'}`}>
                                {executeModalHasExternalEvidence
                                    ? 'Execution evidence captured. POS can use this to recognize the already completed transfer.'
                                    : 'Provide at least one immutable evidence field: external reference ID or bank transaction ID.'}
                            </div>

                            {currentExecuteAmount != null && currentExecuteItem && currentExecuteAmount - currentExecuteItem.eligibleAmount > 0.005 ? (
                                <div className="rounded-[24px] border border-rose-200 bg-rose-50 px-4 py-4 text-sm text-rose-950">
                                    Amount exceeds current backend eligible payout authority.
                                </div>
                            ) : null}

                            {resolveSelectedMethod() ? (
                                <div className="rounded-[24px] border border-sky-200 bg-sky-50 px-4 py-4 text-sm text-sky-950">
                                    Recording payout to {formatMethodLabel(resolveSelectedMethod()!.methodType)} at {resolveSelectedMethod()!.destinationValue}
                                </div>
                            ) : null}
                        </div>

                        <div className="mt-6 flex justify-end">
                            <button
                                type="button"
                                disabled={
                                    executeMutation.isPending ||
                                    !currentExecuteItem.canRecordCompletedPayout ||
                                    !executeModal.payoutMethodId ||
                                    !resolveSelectedMethod() ||
                                    currentExecuteAmount == null ||
                                    !Number.isFinite(currentExecuteAmount) ||
                                    currentExecuteAmount <= 0 ||
                                    currentExecuteAmount - currentExecuteItem.eligibleAmount > 0.005 ||
                                    !executeModalHasExternalEvidence
                                }
                                onClick={() => executeMutation.mutate()}
                                className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                            >
                                {executeMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                                Record completed payout
                            </button>
                        </div>
                    </div>
                </div>
            ) : null}
        </div>
    )
}
