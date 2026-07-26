import { useState } from 'react'
import { useTranslation } from 'react-i18next'

export default function VariantBulkActions({
    selectedCount,
    onApply,
    generationBlocked
}) {
    const { t } = useTranslation()
    const tx = (key, fallback) => {
        const value = t(key)
        return value === key ? fallback : value
    }
    const [setPrice, setSetPrice] = useState('')
    const [percentChange, setPercentChange] = useState('')

    const noSelection = selectedCount === 0

    return (
        <div className="space-y-4 rounded-2xl border border-gray-200 bg-white p-4 shadow-sm">
            <div>
                <h4 className="text-sm font-semibold text-gray-900">
                    {tx('variants.bulkActions', 'Bulk Actions')}
                </h4>
                <p className="text-sm text-gray-500">
                    {tx('variants.selectedVariants', 'Selected variants')}: {selectedCount}
                </p>
                {generationBlocked && (
                    <p className="mt-2 text-sm text-amber-700">
                        {tx('variants.combinationLimitWarning', 'Maximum 100 combinations allowed.')}
                    </p>
                )}
                <p className="mt-2 text-xs text-gray-500">
                    {tx('variants.bulkActionsHint', 'Select rows in the matrix first, then apply price or status updates in bulk.')}
                </p>
            </div>

            <div className="grid gap-4 lg:grid-cols-2 xl:grid-cols-3">
                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.setPrice', 'Set POS Price')}</label>
                    <input
                        type="number"
                        step="0.01"
                        value={setPrice}
                        onChange={(e) => setSetPrice(e.target.value)}
                        className="input"
                        placeholder="0.00"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'set-price', value: setPrice })
                            setSetPrice('')
                        }}
                        disabled={noSelection || !setPrice}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {tx('variants.applyToSelected', 'Apply to Selected')}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.increaseByPercent', 'Increase Price %')}</label>
                    <input
                        type="number"
                        step="0.01"
                        value={percentChange}
                        onChange={(e) => setPercentChange(e.target.value)}
                        className="input"
                        placeholder="10"
                    />
                    <button
                        type="button"
                        onClick={() => {
                            onApply({ type: 'increase-percent', value: percentChange })
                            setPercentChange('')
                        }}
                        disabled={noSelection || !percentChange}
                        className="w-full rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-white disabled:cursor-not-allowed disabled:opacity-50"
                    >
                        {tx('variants.applyIncrease', 'Increase Selected')}
                    </button>
                </div>

                <div className="space-y-2 rounded-xl border border-gray-200 bg-gray-50 p-3">
                    <label className="label">{tx('variants.variantStatus', 'Variant Status')}</label>
                    <div className="grid grid-cols-2 gap-2">
                        <button
                            type="button"
                            onClick={() => onApply({ type: 'set-active', value: true })}
                            disabled={noSelection}
                            className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-700 transition hover:bg-emerald-100 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {tx('common.enable', 'Enable')}
                        </button>
                        <button
                            type="button"
                            onClick={() => onApply({ type: 'set-active', value: false })}
                            disabled={noSelection}
                            className="rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-700 transition hover:bg-gray-100 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                            {tx('common.disable', 'Disable')}
                        </button>
                    </div>
                </div>
            </div>

        </div>
    )
}
