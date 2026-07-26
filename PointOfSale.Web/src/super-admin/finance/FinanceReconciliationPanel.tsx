import { ActivitySquare, AlertTriangle, ShieldCheck, Workflow } from 'lucide-react'
import type { SuperAdminFinancialIntegrityCheck } from './financeService'

interface FinanceReconciliationPanelProps {
    data?: SuperAdminFinancialIntegrityCheck
    isLoading?: boolean
    errorMessage?: string | null
}

function formatCount(value: number | undefined): string {
    return Number(value ?? 0).toLocaleString('en-US')
}

function formatDateTime(value?: string): string {
    if (!value) {
        return 'Pending'
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
        return 'Pending'
    }

    return date.toLocaleString()
}

const COMMERCEHUB_AUTHORITY_CURRENCY = 'EGP'
const UNSUPPORTED_COMMERCEHUB_CURRENCY = 'CommerceHub currency blocked'

function formatCurrency(value?: number | null, currency = 'EGP'): string {
    if (String(currency || '').trim().toUpperCase() !== COMMERCEHUB_AUTHORITY_CURRENCY) {
        return UNSUPPORTED_COMMERCEHUB_CURRENCY
    }

    return new Intl.NumberFormat('en-US', {
        style: 'currency',
        currency: COMMERCEHUB_AUTHORITY_CURRENCY,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(Number(value ?? 0))
}

function getStatusTone(status?: string): string {
    return status === 'OK'
        ? 'border-emerald-200 bg-emerald-50 text-emerald-900'
        : 'border-rose-200 bg-rose-50 text-rose-900'
}

const counters = [
    { key: 'orderFlowMismatchCount', label: 'Order Flow' },
    { key: 'refundFlowMismatchCount', label: 'Refund Flow' },
    { key: 'promoUsageWithoutLedgerCount', label: 'Usage w/o Ledger' },
    { key: 'promoLedgerWithoutUsageCount', label: 'Ledger w/o Usage' },
    { key: 'duplicatePromoUsageCount', label: 'Duplicate Usage' },
    { key: 'missingRefundReversalCount', label: 'Missing Reversal' },
    { key: 'orphanTransactionsCount', label: 'Orphan Refs' },
    { key: 'duplicateEntryCount', label: 'Duplicates' },
    { key: 'payableMismatchCount', label: 'Payables' },
    { key: 'receivableMismatchCount', label: 'Receivables' },
] as const

export default function FinanceReconciliationPanel({
    data,
    isLoading,
    errorMessage,
}: FinanceReconciliationPanelProps) {
    return (
        <section className="rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_80px_-48px_rgba(15,23,42,0.4)]">
            <div className="border-b border-slate-100 px-6 py-5">
                <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
                    <div>
                        <div className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-2 text-sm font-semibold text-slate-700">
                            <Workflow className="h-4 w-4" />
                            Integrity Check
                        </div>
                        <h2 className="mt-4 text-2xl font-semibold text-slate-950">
                            Ledger integrity and reference-level financial consistency
                        </h2>
                        <p className="mt-3 max-w-3xl text-sm leading-7 text-slate-500">
                            The dashboard validates order math, refund math, promo usage backfills, promo-ledger linkage, missing reversals, and orphan ledger references. Global debit/credit parity is intentionally marked as not applicable because CommerceHub uses an operational bucket ledger instead of a general ledger.
                        </p>
                    </div>

                    <div className={`inline-flex items-center gap-2 rounded-full border px-4 py-2 text-sm font-semibold ${getStatusTone(data?.status)}`}>
                        {data?.status === 'OK' ? <ShieldCheck className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
                        {data?.status ?? 'UNKNOWN'}
                    </div>
                </div>
            </div>

            {isLoading && !data ? (
                <div className="px-6 py-10 text-center text-sm text-slate-500">Loading integrity check...</div>
            ) : null}

            {errorMessage ? (
                <div className="border-t border-slate-100 px-6 py-6 text-sm text-rose-700">
                    {errorMessage}
                </div>
            ) : null}

            {data ? (
                <>
                    <div className="grid gap-4 px-6 py-6 md:grid-cols-2 xl:grid-cols-3">
                        {counters.map((counter) => {
                            const rawValue = Number(data[counter.key] ?? 0)
                            const tone = rawValue > 0
                                ? 'border-rose-200 bg-rose-50 text-rose-900'
                                : 'border-slate-200 bg-white text-slate-950'

                            return (
                                <div key={counter.key} className={`rounded-[24px] border px-5 py-4 shadow-sm ${tone}`}>
                                    <p className="text-xs font-semibold uppercase tracking-[0.2em] opacity-70">{counter.label}</p>
                                    <p className="mt-3 text-2xl font-semibold">{formatCount(rawValue)}</p>
                                </div>
                            )
                        })}
                    </div>

                    <div className="grid gap-4 border-t border-slate-100 bg-slate-50/70 px-6 py-5 md:grid-cols-2 xl:grid-cols-4">
                        <div className="rounded-[24px] border border-slate-200 bg-white px-4 py-4 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Executed</p>
                            <p className="mt-3 text-sm font-semibold text-slate-950">{formatDateTime(data.executedAtUtc)}</p>
                        </div>
                        <div className="rounded-[24px] border border-slate-200 bg-white px-4 py-4 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Total Flags</p>
                            <p className="mt-3 text-sm font-semibold text-slate-950">{formatCount(data.totalIssueCount)}</p>
                        </div>
                        <div className="rounded-[24px] border border-slate-200 bg-white px-4 py-4 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Debit/Credit Parity</p>
                            <p className="mt-3 text-sm font-semibold text-slate-950">{data.debitCreditParityStatus}</p>
                        </div>
                        <div className="rounded-[24px] border border-slate-200 bg-white px-4 py-4 shadow-sm">
                            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">Scope</p>
                            <p className="mt-3 text-sm font-semibold text-slate-950">{data.scopeDescription}</p>
                        </div>
                    </div>

                    {data.debitCreditParityNote ? (
                        <div className="border-t border-slate-100 px-6 py-5 text-sm text-slate-600">
                            {data.debitCreditParityNote}
                        </div>
                    ) : null}

                    <div className="border-t border-slate-100 px-6 py-5">
                        <div className="flex items-center gap-2 text-sm font-semibold text-slate-900">
                            <ActivitySquare className="h-4 w-4" />
                            Sample Issues
                        </div>

                        {data.issues.length === 0 ? (
                            <div className="mt-4 rounded-[24px] border border-dashed border-emerald-200 bg-emerald-50 px-5 py-8 text-sm text-emerald-900">
                                No sample inconsistencies were returned for the selected scope.
                            </div>
                        ) : (
                            <div className="mt-4 overflow-x-auto">
                                <table className="min-w-full divide-y divide-slate-200 text-sm">
                                    <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        <tr>
                                            <th className="px-4 py-3">Severity</th>
                                            <th className="px-4 py-3">Type</th>
                                            <th className="px-4 py-3">Reference</th>
                                            <th className="px-4 py-3">Summary</th>
                                            <th className="px-4 py-3">Expected</th>
                                            <th className="px-4 py-3">Actual</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-slate-100 bg-white">
                                        {data.issues.map((issue) => (
                                            <tr key={`${issue.issueType}-${issue.referenceId ?? 'none'}-${issue.summary}`} className="align-top">
                                                <td className="px-4 py-4">
                                                    <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${issue.severity === 'ERROR' ? 'border-rose-200 bg-rose-50 text-rose-900' : 'border-amber-200 bg-amber-50 text-amber-900'}`}>
                                                        {issue.severity}
                                                    </span>
                                                </td>
                                                <td className="px-4 py-4 font-medium text-slate-900">{issue.issueType}</td>
                                                <td className="px-4 py-4 text-slate-600">
                                                    <div>{issue.referenceType}</div>
                                                    <div className="mt-1 break-all text-xs">{issue.referenceId ?? 'n/a'}</div>
                                                    <div className="mt-1 text-xs">{formatDateTime(issue.occurredAtUtc ?? undefined)}</div>
                                                </td>
                                                <td className="px-4 py-4 text-slate-600">{issue.summary}</td>
                                                <td className="px-4 py-4 text-slate-700">
                                                    {issue.expectedAmount == null ? 'n/a' : formatCurrency(issue.expectedAmount, issue.currency || 'EGP')}
                                                </td>
                                                <td className="px-4 py-4 text-slate-700">
                                                    {issue.actualAmount == null ? 'n/a' : formatCurrency(issue.actualAmount, issue.currency || 'EGP')}
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        )}
                    </div>
                </>
            ) : null}
        </section>
    )
}
