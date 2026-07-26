import { useMemo, useState } from 'react'
import { Loader2, TrendingUp } from 'lucide-react'
import {
    calculateRequiredPriceForTargetEarnings,
    roundCurrencyAmount,
} from './commissionCalculator'
import { useCommissionPreview } from './useCommissionPreview'

interface CommissionPreviewPanelProps {
    vendorId?: string | null
    categoryId?: string | null
    categoryName?: string | null
    price: number
    priceLabel: string
    currencyCode?: string
    onApplySuggestedPrice?: (price: number) => void
}

function formatMoney(value: number, currencyCode: string): string {
    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
    }).format(value)} ${currencyCode}`
}

function formatRate(rate: number | null): string {
    if (rate === null) {
        return '-'
    }

    return `${new Intl.NumberFormat('en-US', {
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
    }).format(rate * 100)}%`
}

function getAppliedRuleLabel(source: string | null, categoryName?: string | null): string {
    if (source === 'Category') {
        return `Applied Rule: Category (${categoryName || 'Selected category'})`
    }

    if (source === 'Vendor') {
        return 'Applied Rule: Vendor override'
    }

    if (source === 'Global') {
        return 'Applied Rule: Global default'
    }

    if (source === 'Legacy') {
        return 'Applied Rule: Legacy fallback'
    }

    return 'Applied Rule: Awaiting resolution'
}

export default function CommissionPreviewPanel({
    vendorId,
    categoryId,
    categoryName,
    price,
    priceLabel,
    currencyCode = 'EGP',
    onApplySuggestedPrice,
}: CommissionPreviewPanelProps) {
    const {
        rate,
        source,
        ruleId,
        priority,
        commissionAmount,
        vendorReceives,
        loading,
        error,
        usedLegacyFallback,
        usingEstimate,
    } = useCommissionPreview({
        vendorId,
        categoryId,
        price,
    })
    const [keepTargetEarnings, setKeepTargetEarnings] = useState(false)
    const [targetEarningsInput, setTargetEarningsInput] = useState('')

    const targetEarnings = useMemo(() => {
        if (!targetEarningsInput.trim()) {
            return null
        }

        const parsed = Number(targetEarningsInput)
        return Number.isFinite(parsed) ? roundCurrencyAmount(parsed) : null
    }, [targetEarningsInput])

    const targetPreview = useMemo(() => {
        if (!keepTargetEarnings || rate === null || targetEarnings === null) {
            return null
        }

        return calculateRequiredPriceForTargetEarnings(targetEarnings, rate)
    }, [keepTargetEarnings, rate, targetEarnings])

    if (!categoryId) {
        return (
            <div className="mt-4 rounded-2xl border border-dashed border-slate-300 bg-slate-50 px-4 py-4 text-sm text-slate-600">
                Select a category to see commission details
            </div>
        )
    }

    return (
        <section className="mt-4 overflow-hidden rounded-2xl border border-emerald-200 bg-gradient-to-br from-emerald-50 via-white to-slate-50 shadow-sm">
            <div className="border-b border-emerald-100 px-4 py-4 sm:px-5">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-emerald-700">
                            Commission Breakdown
                        </p>
                        <p className="mt-1 text-sm text-slate-600">
                            Preview exactly what CommerceHub will deduct before you publish.
                        </p>
                    </div>

                    {loading ? (
                        <div className="inline-flex items-center gap-2 rounded-full border border-sky-200 bg-sky-50 px-3 py-1.5 text-xs font-semibold text-sky-800">
                            <Loader2 className="h-3.5 w-3.5 animate-spin" />
                            Resolving commission...
                        </div>
                    ) : null}
                </div>
            </div>

            <div className="space-y-4 px-4 py-4 sm:px-5 sm:py-5">
                {(error || usedLegacyFallback) ? (
                    <div
                        className={`rounded-xl px-4 py-3 text-sm ${
                            error
                                ? 'border border-amber-200 bg-amber-50 text-amber-900'
                                : 'border border-sky-200 bg-sky-50 text-sky-900'
                        }`}
                    >
                        {error || 'No active commission rule matched this category, so the preview is using the legacy fallback rate.'}
                    </div>
                ) : null}

                <div className="rounded-2xl border border-slate-200 bg-white p-4">
                    <div className="space-y-3 text-sm text-slate-700">
                        <div className="flex items-center justify-between gap-4">
                            <span>{priceLabel}</span>
                            <span className="font-semibold text-slate-900">
                                {formatMoney(price, currencyCode)}
                            </span>
                        </div>

                        <div className="flex items-center justify-between gap-4">
                            <span>Commission Rate</span>
                            <span className="font-semibold text-slate-900">
                                {formatRate(rate)}
                            </span>
                        </div>

                        <div className="flex items-center justify-between gap-4 text-red-600">
                            <span>
                                Commission ({formatRate(rate)})
                            </span>
                            <span className="font-semibold">
                                -{formatMoney(commissionAmount, currencyCode)}
                            </span>
                        </div>

                        <div className="border-t border-dashed border-slate-200 pt-3">
                            <div className="flex items-center justify-between gap-4 text-base">
                                <span className="font-semibold text-slate-900">
                                    You will receive
                                </span>
                                <span className="text-lg font-bold text-emerald-600">
                                    {formatMoney(vendorReceives, currencyCode)}
                                </span>
                            </div>
                        </div>
                    </div>
                </div>

                <div className="grid gap-3 lg:grid-cols-[1.2fr_0.8fr]">
                    <div className="rounded-2xl border border-slate-200 bg-white p-4">
                        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                            Rule Resolution
                        </p>
                        <p className="mt-2 text-sm font-semibold text-slate-900">
                            {getAppliedRuleLabel(source, categoryName)}
                        </p>
                        <div className="mt-3 grid gap-3 text-sm text-slate-600 sm:grid-cols-2">
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    Source
                                </p>
                                <p className="mt-1 font-medium text-slate-900">
                                    {source || 'Pending'}
                                </p>
                            </div>
                            <div>
                                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    Priority
                                </p>
                                <p className="mt-1 font-medium text-slate-900">
                                    {priority ?? '-'}
                                </p>
                            </div>
                        </div>
                        {ruleId ? (
                            <div className="mt-3 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2">
                                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-slate-500">
                                    Rule ID
                                </p>
                                <p className="mt-1 break-all font-mono text-xs text-slate-700">
                                    {ruleId}
                                </p>
                            </div>
                        ) : null}
                        {usingEstimate ? (
                            <p className="mt-3 text-xs font-medium text-amber-800">
                                Showing the latest available estimate until live commission resolution succeeds.
                            </p>
                        ) : null}
                    </div>

                    <div className="rounded-2xl border border-slate-200 bg-white p-4">
                        <label className="flex items-start gap-3">
                            <input
                                type="checkbox"
                                checked={keepTargetEarnings}
                                onChange={(event) => setKeepTargetEarnings(event.target.checked)}
                                className="mt-0.5 h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500"
                            />
                            <span>
                                <span className="block text-sm font-semibold text-slate-900">
                                    Adjust price to maintain my target earnings
                                </span>
                                <span className="mt-1 block text-xs leading-5 text-slate-500">
                                    Use the resolved rate to see what CommerceHub price preserves your payout.
                                </span>
                            </span>
                        </label>

                        {keepTargetEarnings ? (
                            <div className="mt-4 space-y-3">
                                <div>
                                    <label className="mb-1 block text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                                        Desired earnings
                                    </label>
                                    <input
                                        type="number"
                                        min="0"
                                        step="0.01"
                                        value={targetEarningsInput}
                                        onChange={(event) => setTargetEarningsInput(event.target.value)}
                                        placeholder="0.00"
                                        className="w-full rounded-xl border border-slate-300 px-3 py-2 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-4 focus:ring-emerald-100"
                                    />
                                </div>

                                {targetPreview ? (
                                    <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3">
                                        <div className="flex items-center gap-2 text-sm font-semibold text-emerald-900">
                                            <TrendingUp className="h-4 w-4" />
                                            Required CommerceHub price
                                        </div>
                                        <p className="mt-2 text-xl font-bold text-emerald-700">
                                            {formatMoney(targetPreview.requiredPrice, currencyCode)}
                                        </p>
                                        {onApplySuggestedPrice ? (
                                            <button
                                                type="button"
                                                onClick={() => onApplySuggestedPrice(targetPreview.requiredPrice)}
                                                className="mt-3 inline-flex items-center rounded-xl border border-emerald-300 bg-white px-3 py-2 text-xs font-semibold text-emerald-800 transition hover:bg-emerald-100"
                                            >
                                                Use this as Hub Price
                                            </button>
                                        ) : null}
                                    </div>
                                ) : (
                                    <p className="text-xs leading-5 text-slate-500">
                                        Enter the amount you want to receive, and we will calculate the required selling price.
                                    </p>
                                )}
                            </div>
                        ) : null}
                    </div>
                </div>
            </div>
        </section>
    )
}
