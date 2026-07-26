import { CalendarDays, PencilLine, Power, ShieldCheck, ShieldAlert } from 'lucide-react'
import type { CommissionRule } from './commissionService'

interface CommissionTableProps {
    rules: CommissionRule[]
    isLoading: boolean
    activeActionRuleId?: string | null
    getAppliedRuleLabel: (rule: CommissionRule) => string
    getDeactivationState: (rule: CommissionRule) => { disabled: boolean; reason?: string }
    onEdit: (rule: CommissionRule) => void
    onDeactivate: (rule: CommissionRule) => void
    onCreateFirstRule: () => void
    resolveScopeName: (rule: CommissionRule) => string
}

function formatPercent(rate: number): string {
    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format(rate * 100)}%`
}

function formatTimestamp(value?: string | null): string {
    if (!value) {
        return 'Unavailable'
    }

    const parsed = new Date(value)
    if (Number.isNaN(parsed.getTime())) {
        return 'Unavailable'
    }

    return parsed.toLocaleString()
}

function getScopeTone(scopeType: CommissionRule['scopeType']): string {
    switch (scopeType) {
        case 'Vendor':
            return 'border-cyan-200 bg-cyan-50 text-cyan-800'
        case 'Category':
            return 'border-violet-200 bg-violet-50 text-violet-800'
        default:
            return 'border-sky-200 bg-sky-50 text-sky-800'
    }
}

function TableSkeleton() {
    return (
        <div className="space-y-3 p-6">
            {Array.from({ length: 5 }).map((_, index) => (
                <div
                    key={`commission-skeleton-row-${index}`}
                    className="grid animate-pulse gap-3 rounded-[28px] border border-slate-200 bg-slate-50/80 px-4 py-4 md:grid-cols-[1.1fr_1.4fr_0.8fr_0.6fr_0.8fr_1fr_0.8fr]"
                >
                    {Array.from({ length: 7 }).map((__, cellIndex) => (
                        <div key={`commission-skeleton-cell-${index}-${cellIndex}`} className="h-5 rounded-full bg-slate-200" />
                    ))}
                </div>
            ))}
        </div>
    )
}

export default function CommissionTable({
    rules,
    isLoading,
    activeActionRuleId,
    getAppliedRuleLabel,
    getDeactivationState,
    onEdit,
    onDeactivate,
    onCreateFirstRule,
    resolveScopeName,
}: CommissionTableProps) {
    if (isLoading) {
        return (
            <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
                <div className="flex items-center justify-between border-b border-slate-100 px-6 py-5">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">
                            Rules Table
                        </p>
                        <h2 className="mt-2 text-xl font-semibold text-slate-950">
                            Loading commission rules
                        </h2>
                    </div>
                </div>
                <TableSkeleton />
            </section>
        )
    }

    return (
        <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
            <div className="flex flex-col gap-3 border-b border-slate-100 px-6 py-5 sm:flex-row sm:items-center sm:justify-between">
                <div>
                    <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-500">
                        Rules Table
                    </p>
                    <h2 className="mt-2 text-xl font-semibold text-slate-950">
                        All configured commission rules
                    </h2>
                    <p className="mt-2 text-sm text-slate-500">
                        Active rules are highlighted, while inactive rules remain visible for auditability.
                    </p>
                </div>
                <div className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-800">
                    <ShieldCheck className="h-4 w-4" />
                    {rules.filter((rule) => rule.isActive).length} active rules
                </div>
            </div>

            {rules.length === 0 ? (
                <div className="px-6 py-14 text-center">
                    <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100 text-slate-500">
                        <CalendarDays className="h-6 w-6" />
                    </div>
                    <h3 className="mt-5 text-lg font-semibold text-slate-950">
                        No commission rules configured
                    </h3>
                    <p className="mt-2 text-sm leading-6 text-slate-500">
                        Create a global default first, then layer category and vendor overrides as needed.
                    </p>
                    <button
                        type="button"
                        className="mt-6 inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800"
                        onClick={onCreateFirstRule}
                    >
                        <ShieldCheck className="h-4 w-4" />
                        Create your first rule
                    </button>
                </div>
            ) : (
                <div className="overflow-x-auto">
                    <table className="min-w-full divide-y divide-slate-200">
                        <thead className="bg-slate-50/90">
                            <tr className="text-left text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                <th className="px-6 py-4">Scope Type</th>
                                <th className="px-6 py-4">Scope Name</th>
                                <th className="px-6 py-4 text-right">Rate</th>
                                <th className="px-6 py-4 text-right">Priority</th>
                                <th className="px-6 py-4">Status</th>
                                <th className="px-6 py-4">Created</th>
                                <th className="px-6 py-4 text-right">Actions</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                            {rules.map((rule) => {
                                const isBusy = activeActionRuleId === rule.id
                                const deactivationState = getDeactivationState(rule)

                                return (
                                    <tr
                                        key={rule.id}
                                        className={[
                                            rule.isActive
                                                ? 'bg-emerald-50/55 shadow-[inset_4px_0_0_0_rgba(16,185,129,0.95)]'
                                                : 'bg-slate-50/40 text-slate-500',
                                            'transition-colors',
                                        ].join(' ')}
                                    >
                                        <td className="px-6 py-4">
                                            <div className="space-y-2">
                                                <span className={`inline-flex rounded-full border px-3 py-1 text-xs font-semibold ${getScopeTone(rule.scopeType)}`}>
                                                    {rule.scopeType}
                                                </span>
                                                {rule.scopeType === 'Global' ? (
                                                    <div className="text-[11px] font-semibold uppercase tracking-[0.18em] text-sky-700">
                                                        Platform default
                                                    </div>
                                                ) : null}
                                            </div>
                                        </td>
                                        <td className="px-6 py-4">
                                            <div className="max-w-[280px]">
                                                <p className="truncate text-sm font-semibold text-slate-900">
                                                    {resolveScopeName(rule)}
                                                </p>
                                                <p className="mt-1 text-xs font-medium text-slate-600">
                                                    {getAppliedRuleLabel(rule)}
                                                </p>
                                                <p className="mt-1 truncate font-mono text-[11px] text-slate-500">
                                                    {rule.scopeId ?? rule.id}
                                                </p>
                                            </div>
                                        </td>
                                        <td className="px-6 py-4 text-right text-sm font-semibold text-slate-900">
                                            {formatPercent(rule.rate)}
                                        </td>
                                        <td className="px-6 py-4 text-right text-sm text-slate-700">
                                            {rule.priority}
                                        </td>
                                        <td className="px-6 py-4">
                                            <span
                                                className={[
                                                    'inline-flex rounded-full px-3 py-1 text-xs font-semibold',
                                                    rule.isActive
                                                        ? 'bg-emerald-100 text-emerald-800'
                                                        : 'bg-slate-100 text-slate-600',
                                                ].join(' ')}
                                            >
                                                {rule.isActive ? 'Active' : 'Inactive'}
                                            </span>
                                        </td>
                                        <td className="px-6 py-4 text-sm text-slate-600">
                                            {formatTimestamp(rule.createdAt)}
                                        </td>
                                        <td className="px-6 py-4">
                                            <div className="space-y-2">
                                                <div className="flex items-center justify-end gap-2">
                                                    <button
                                                        type="button"
                                                        aria-label={`Edit ${rule.scopeType} commission rule`}
                                                        className="inline-flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-3 py-2 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50"
                                                        onClick={() => onEdit(rule)}
                                                    >
                                                        <PencilLine className="h-4 w-4" />
                                                        Edit
                                                    </button>
                                                    <button
                                                        type="button"
                                                        aria-label={`Deactivate ${rule.scopeType} commission rule`}
                                                        title={deactivationState.reason}
                                                        className="inline-flex items-center gap-2 rounded-2xl border border-rose-200 bg-rose-50 px-3 py-2 text-sm font-semibold text-rose-700 transition hover:border-rose-300 hover:bg-rose-100 disabled:cursor-not-allowed disabled:opacity-60"
                                                        disabled={isBusy || !rule.isActive || deactivationState.disabled}
                                                        onClick={() => onDeactivate(rule)}
                                                    >
                                                        <Power className="h-4 w-4" />
                                                        {isBusy ? 'Working...' : 'Deactivate'}
                                                    </button>
                                                </div>
                                                {deactivationState.disabled ? (
                                                    <div className="flex items-start justify-end gap-2 text-right text-[11px] font-medium text-amber-700">
                                                        <ShieldAlert className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                                                        <span>{deactivationState.reason}</span>
                                                    </div>
                                                ) : null}
                                            </div>
                                        </td>
                                    </tr>
                                )
                            })}
                        </tbody>
                    </table>
                </div>
            )}
        </section>
    )
}
