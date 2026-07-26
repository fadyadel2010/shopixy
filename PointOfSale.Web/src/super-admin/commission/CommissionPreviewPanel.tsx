import { useState, type FormEvent } from 'react'
import { Building2, FolderTree, Loader2, Radar, ShieldCheck, Sparkles } from 'lucide-react'
import type {
    CommissionCategoryOption,
    CommissionPreviewResult,
    CommissionVendorOption,
} from './commissionService'

interface CommissionPreviewPanelProps {
    categories: CommissionCategoryOption[]
    vendors: CommissionVendorOption[]
    errorMessage?: string | null
    getAppliedRuleLabel: (result: CommissionPreviewResult) => string
    isLoadingLookups: boolean
    isSubmitting: boolean
    result?: CommissionPreviewResult | null
    onPreview: (vendorId?: string | null, categoryId?: string | null) => Promise<void> | void
}

function formatPercent(rate: number): string {
    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format(rate * 100)}%`
}

function formatCategoryLabel(category: CommissionCategoryOption): string {
    const prefix = category.level > 0 ? `${'-- '.repeat(category.level)}` : ''
    const emoji = category.iconEmoji ? `${category.iconEmoji} ` : ''
    return `${prefix}${emoji}${category.name}`
}

function PreviewSkeleton() {
    return (
        <div className="space-y-4">
            {Array.from({ length: 3 }).map((_, index) => (
                <div key={`preview-skeleton-${index}`} className="h-12 animate-pulse rounded-2xl bg-slate-200" />
            ))}
        </div>
    )
}

export default function CommissionPreviewPanel({
    categories,
    vendors,
    errorMessage,
    getAppliedRuleLabel,
    isLoadingLookups,
    isSubmitting,
    result,
    onPreview,
}: CommissionPreviewPanelProps) {
    const [vendorId, setVendorId] = useState<string>('')
    const [categoryId, setCategoryId] = useState<string>('')

    async function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault()
        await onPreview(vendorId || null, categoryId || null)
    }

    return (
        <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-[0_24px_70px_-48px_rgba(15,23,42,0.45)]">
            <div className="relative overflow-hidden border-b border-slate-100 px-6 py-5">
                <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,_rgba(14,165,233,0.15),_transparent_28%),radial-gradient(circle_at_bottom_right,_rgba(16,185,129,0.12),_transparent_26%)]" />
                <div className="relative">
                    <div className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-4 py-2 text-sm font-semibold text-sky-800">
                        <Radar className="h-4 w-4" />
                        Resolve Preview Tool
                    </div>
                    <h2 className="mt-4 text-2xl font-semibold text-slate-950">
                        Preview resolved commission before orders are created
                    </h2>
                    <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-500">
                        Use a vendor, a category, or both to see exactly which rule wins and why.
                    </p>
                </div>
            </div>

            <div className="grid gap-6 px-6 py-6 lg:grid-cols-[1.2fr_1fr]">
                <form className="space-y-4" onSubmit={handleSubmit}>
                    <div className="space-y-2">
                        <label className="text-sm font-semibold text-slate-800">
                            Vendor
                        </label>
                        {isLoadingLookups ? (
                            <PreviewSkeleton />
                        ) : (
                            <select
                                value={vendorId}
                                onChange={(event) => setVendorId(event.target.value)}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                            >
                                <option value="">Any vendor</option>
                                {vendors.map((vendor) => (
                                    <option key={vendor.id} value={vendor.id}>
                                        {vendor.name}
                                    </option>
                                ))}
                            </select>
                        )}
                    </div>

                    <div className="space-y-2">
                        <label className="text-sm font-semibold text-slate-800">
                            Category
                        </label>
                        {isLoadingLookups ? (
                            <PreviewSkeleton />
                        ) : (
                            <select
                                value={categoryId}
                                onChange={(event) => setCategoryId(event.target.value)}
                                className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm font-medium text-slate-900 shadow-sm outline-none transition focus:border-sky-300 focus:ring-4 focus:ring-sky-100"
                            >
                                <option value="">Any category</option>
                                {categories.map((category) => (
                                    <option key={category.id} value={category.id}>
                                        {formatCategoryLabel(category)}
                                    </option>
                                ))}
                            </select>
                        )}
                    </div>

                    <div className="rounded-[28px] border border-slate-200 bg-slate-50 px-5 py-4">
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                            Precedence reminder
                        </p>
                        <p className="mt-2 text-sm leading-6 text-slate-600">
                            Vendor overrides category, category overrides global, and legacy fallback applies only when no active rule matches.
                        </p>
                    </div>

                    <button
                        type="submit"
                        className="inline-flex items-center gap-2 rounded-2xl bg-slate-950 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-slate-900/15 transition hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-60"
                        disabled={isLoadingLookups || isSubmitting}
                    >
                        {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                        {isSubmitting ? 'Previewing...' : 'Preview Commission'}
                    </button>

                    {errorMessage ? (
                        <div className="rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800">
                            <p>{errorMessage}</p>
                            <button
                                type="submit"
                                className="mt-3 inline-flex items-center rounded-xl border border-rose-300 bg-white px-3 py-2 text-xs font-semibold text-rose-700 transition hover:bg-rose-100"
                            >
                                Retry Preview
                            </button>
                        </div>
                    ) : null}
                </form>

                <div className="rounded-[28px] border border-slate-200 bg-slate-50/70 p-5">
                    <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-500">
                        Preview Result
                    </p>

                    {result ? (
                        <div className="mt-4 space-y-4">
                            <div className="rounded-[24px] border border-emerald-200 bg-emerald-50 p-4">
                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-emerald-700">
                                    Commission Rate
                                </p>
                                <p className="mt-2 text-3xl font-semibold text-emerald-900">
                                    {formatPercent(result.rate)}
                                </p>
                                <p className="mt-2 text-sm font-medium text-emerald-900/85">
                                    Vendor will receive {formatPercent(1 - result.rate)} of the product subtotal.
                                </p>
                            </div>

                            <div className="rounded-2xl border border-sky-200 bg-sky-50 px-4 py-3 text-sm font-semibold text-sky-900">
                                {getAppliedRuleLabel(result)}
                            </div>

                            <div className="grid gap-3 sm:grid-cols-2">
                                <div className="rounded-2xl border border-white/70 bg-white px-4 py-3 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        Source
                                    </p>
                                    <p className="mt-2 flex items-center gap-2 text-sm font-semibold text-slate-900">
                                        {result.source === 'Vendor' ? <Building2 className="h-4 w-4 text-cyan-600" /> : null}
                                        {result.source === 'Category' ? <FolderTree className="h-4 w-4 text-violet-600" /> : null}
                                        {result.source === 'Global' ? <ShieldCheck className="h-4 w-4 text-emerald-600" /> : null}
                                        {result.source}
                                    </p>
                                </div>
                                <div className="rounded-2xl border border-white/70 bg-white px-4 py-3 shadow-sm">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        Priority
                                    </p>
                                    <p className="mt-2 text-sm font-semibold text-slate-900">
                                        {result.priority ?? 'N/A'}
                                    </p>
                                </div>
                                <div className="rounded-2xl border border-white/70 bg-white px-4 py-3 shadow-sm sm:col-span-2">
                                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        Rule ID
                                    </p>
                                    <p className="mt-2 break-all font-mono text-xs text-slate-700">
                                        {result.ruleId ?? 'Legacy fallback'}
                                    </p>
                                </div>
                            </div>

                            <div className="rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-600">
                                Commission resolution applies to product subtotal only. Shipping is excluded from the commission base.
                            </div>

                            {result.usedLegacyFallback ? (
                                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                                    No active commission rule matched this preview, so the backend fell back to the legacy configured rate.
                                </div>
                            ) : null}
                        </div>
                    ) : (
                        <div className="mt-4 rounded-[24px] border border-dashed border-slate-300 bg-white/80 px-5 py-8 text-center">
                            <p className="text-sm font-semibold text-slate-800">
                                No preview generated yet
                            </p>
                            <p className="mt-2 text-sm leading-6 text-slate-500">
                                Select optional vendor and category inputs, then run a preview to inspect the exact resolved rate.
                            </p>
                        </div>
                    )}
                </div>
            </div>
        </section>
    )
}
