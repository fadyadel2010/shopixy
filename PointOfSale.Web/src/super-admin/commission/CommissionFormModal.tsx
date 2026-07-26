import { useEffect, useState, type FormEvent } from 'react'
import { Building2, FolderTree, Globe2, Loader2, Save, X } from 'lucide-react'
import type {
    CommissionCategoryOption,
    CommissionRule,
    CommissionRuleFormInput,
    CommissionScopeType,
    CommissionVendorOption,
} from './commissionService'

interface CommissionFormModalProps {
    isOpen: boolean
    isSubmitting: boolean
    rule?: CommissionRule | null
    defaultScopeType?: CommissionScopeType
    categories: CommissionCategoryOption[]
    vendors: CommissionVendorOption[]
    onClose: () => void
    onSubmit: (input: CommissionRuleFormInput) => Promise<void> | void
}

type FormErrors = Partial<Record<'scopeId' | 'ratePercent' | 'priority', string>>

function formatCategoryLabel(category: CommissionCategoryOption): string {
    const prefix = category.level > 0 ? `${'-- '.repeat(category.level)}` : ''
    const emoji = category.iconEmoji ? `${category.iconEmoji} ` : ''
    return `${prefix}${emoji}${category.name}`
}

function parseRatePercent(rate: number): string {
    return Number.isInteger(rate * 100)
        ? String(rate * 100)
        : String(Number((rate * 100).toFixed(2)))
}

export default function CommissionFormModal({
    isOpen,
    isSubmitting,
    rule,
    defaultScopeType = 'Category',
    categories,
    vendors,
    onClose,
    onSubmit,
}: CommissionFormModalProps) {
    const [scopeType, setScopeType] = useState<CommissionScopeType>(defaultScopeType)
    const [scopeId, setScopeId] = useState<string>('')
    const [ratePercent, setRatePercent] = useState<string>('')
    const [priority, setPriority] = useState<string>('0')
    const [errors, setErrors] = useState<FormErrors>({})

    useEffect(() => {
        if (!isOpen) {
            return
        }

        setScopeType(rule?.scopeType ?? defaultScopeType)
        setScopeId(rule?.scopeId ?? '')
        setRatePercent(rule ? parseRatePercent(rule.rate) : '')
        setPriority(rule ? String(rule.priority) : '0')
        setErrors({})
    }, [defaultScopeType, isOpen, rule])

    useEffect(() => {
        if (!isOpen) {
            return
        }

        function handleEscape(event: KeyboardEvent) {
            if (event.key === 'Escape' && !isSubmitting) {
                onClose()
            }
        }

        window.addEventListener('keydown', handleEscape)
        return () => window.removeEventListener('keydown', handleEscape)
    }, [isOpen, isSubmitting, onClose])

    if (!isOpen) {
        return null
    }

    const isGlobal = scopeType === 'Global'
    const scopeLabel = scopeType === 'Category' ? 'Category' : 'Vendor'
    const isEditing = Boolean(rule)

    async function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault()

        const nextErrors: FormErrors = {}
        const parsedRate = Number(ratePercent)
        const parsedPriority = Number(priority)

        if (Number.isNaN(parsedRate) || parsedRate < 0 || parsedRate > 100) {
            nextErrors.ratePercent = 'Rate must be between 0 and 100.'
        }

        if (!Number.isInteger(parsedPriority)) {
            nextErrors.priority = 'Priority must be a whole number.'
        }

        if (!isGlobal && !scopeId) {
            nextErrors.scopeId = `${scopeLabel} is required for this rule.`
        }

        setErrors(nextErrors)

        if (Object.keys(nextErrors).length > 0) {
            return
        }

        await onSubmit({
            scopeType,
            scopeId: isGlobal ? null : scopeId,
            ratePercent: parsedRate,
            priority: parsedPriority,
        })
    }

    return (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/55 px-4 py-6 backdrop-blur-sm">
            <div className="relative w-full max-w-2xl overflow-hidden rounded-[32px] border border-white/10 bg-white shadow-[0_40px_120px_-52px_rgba(15,23,42,0.75)]">
                <div className="relative overflow-hidden border-b border-slate-100 px-6 py-5 sm:px-8">
                    <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(16,185,129,0.12),_transparent_30%),radial-gradient(circle_at_bottom_right,_rgba(14,165,233,0.12),_transparent_28%)]" />
                    <div className="relative flex items-start justify-between gap-4">
                        <div>
                            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-emerald-600">
                                {isEditing ? 'Edit Rule' : 'Create Rule'}
                            </p>
                            <h2 className="mt-2 text-2xl font-semibold text-slate-950">
                                {isEditing ? 'Update commission rule' : 'Create a commission rule'}
                            </h2>
                            <p className="mt-2 text-sm leading-6 text-slate-500">
                                Vendor overrides category, category overrides global, and priority only breaks ties inside the same scope.
                            </p>
                        </div>

                        <button
                            type="button"
                            aria-label="Close commission rule form"
                            className="inline-flex h-11 w-11 items-center justify-center rounded-2xl border border-slate-200 bg-white text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                            disabled={isSubmitting}
                            onClick={onClose}
                        >
                            <X className="h-5 w-5" />
                        </button>
                    </div>
                </div>

                <form className="space-y-6 px-6 py-6 sm:px-8" onSubmit={handleSubmit}>
                    <div className="grid gap-5 md:grid-cols-2">
                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-slate-800">
                                Scope Type
                            </label>
                            <div className="relative">
                                <select
                                    value={scopeType}
                                    onChange={(event) => {
                                        const nextScopeType = event.target.value as CommissionScopeType
                                        setScopeType(nextScopeType)
                                        setScopeId('')
                                    }}
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                >
                                    <option value="Global">Global</option>
                                    <option value="Category">Category</option>
                                    <option value="Vendor">Vendor</option>
                                </select>
                            </div>
                            <div className="flex items-center gap-2 text-xs text-slate-500">
                                {scopeType === 'Global' ? <Globe2 className="h-4 w-4" /> : null}
                                {scopeType === 'Category' ? <FolderTree className="h-4 w-4" /> : null}
                                {scopeType === 'Vendor' ? <Building2 className="h-4 w-4" /> : null}
                                <span>
                                    {scopeType === 'Global'
                                        ? 'Default platform rate when no override matches.'
                                        : scopeType === 'Category'
                                            ? 'Applies to all vendors in the selected CommerceHub category.'
                                            : 'Applies to one vendor regardless of category priority.'}
                                </span>
                            </div>
                        </div>

                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-slate-800">
                                Scope
                            </label>
                            <select
                                value={scopeId}
                                disabled={isGlobal}
                                onChange={(event) => setScopeId(event.target.value)}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100 disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400"
                            >
                                <option value="">
                                    {isGlobal
                                        ? 'Global rules do not use a scope target'
                                        : `Select a ${scopeLabel.toLowerCase()}`}
                                </option>
                                {(scopeType === 'Category' ? categories : vendors).map((option) => (
                                    <option key={option.id} value={option.id}>
                                        {'level' in option ? formatCategoryLabel(option) : option.name}
                                    </option>
                                ))}
                            </select>
                            {errors.scopeId ? (
                                <p className="text-sm text-rose-600">{errors.scopeId}</p>
                            ) : (
                                <p className="text-xs text-slate-500">
                                    {isGlobal
                                        ? 'No target required.'
                                        : 'Priority ties are evaluated only against rules in this same scope.'}
                                </p>
                            )}
                        </div>
                    </div>

                    <div className="grid gap-5 md:grid-cols-2">
                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-slate-800">
                                Rate (%)
                            </label>
                            <div className="relative">
                                <input
                                    type="number"
                                    min="0"
                                    max="100"
                                    step="0.01"
                                    inputMode="decimal"
                                    value={ratePercent}
                                    onChange={(event) => setRatePercent(event.target.value)}
                                    placeholder="5"
                                    className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 pr-12 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                                />
                                <span className="pointer-events-none absolute inset-y-0 right-4 flex items-center text-sm font-semibold text-slate-500">
                                    %
                                </span>
                            </div>
                            {errors.ratePercent ? (
                                <p className="text-sm text-rose-600">{errors.ratePercent}</p>
                            ) : (
                                <p className="text-xs text-slate-500">
                                    Enter a percentage value between 0% and 100%.
                                </p>
                            )}
                        </div>

                        <div className="space-y-2">
                            <label className="text-sm font-semibold text-slate-800">
                                Priority
                            </label>
                            <input
                                type="number"
                                step="1"
                                value={priority}
                                onChange={(event) => setPriority(event.target.value)}
                                placeholder="0"
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-emerald-300 focus:ring-4 focus:ring-emerald-100"
                            />
                            {errors.priority ? (
                                <p className="text-sm text-rose-600">{errors.priority}</p>
                            ) : (
                                <p className="text-xs text-slate-500">
                                    Higher priority wins only among rules with the same scope and scope target.
                                </p>
                            )}
                        </div>
                    </div>

                    <div className="rounded-[28px] border border-slate-200 bg-slate-50 px-5 py-4">
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                            Rule behavior
                        </p>
                        <p className="mt-2 text-sm leading-6 text-slate-600">
                            This rule will not modify historical orders. New checkouts snapshot the resolved rate and source at order time.
                        </p>
                    </div>

                    <div className="flex flex-col-reverse gap-3 border-t border-slate-100 pt-5 sm:flex-row sm:justify-end">
                        <button
                            type="button"
                            className="inline-flex items-center justify-center rounded-2xl border border-slate-200 bg-white px-5 py-3 text-sm font-semibold text-slate-700 transition hover:border-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                            disabled={isSubmitting}
                            onClick={onClose}
                        >
                            Cancel
                        </button>
                        <button
                            type="submit"
                            className="inline-flex items-center justify-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                            disabled={isSubmitting}
                        >
                            {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                            {isSubmitting ? 'Saving...' : isEditing ? 'Save changes' : 'Create rule'}
                        </button>
                    </div>
                </form>
            </div>
        </div>
    )
}
